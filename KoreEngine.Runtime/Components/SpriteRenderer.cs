using SDL3;

namespace KoreEngine
{
    public enum FilteringMode
    {
        Nearest = 0,
        PixelArt = 1,
        Linear = 2
    }

    public class SpriteRenderer : Component
    {
        public IntPtr Texture;
        public string TexturePath = "";

        public int Frame = -1;                    // -1 = texture entière ; >= 0 : frame de la grille FrameSize (ligne par ligne)
        public Color Color = Color.White;         // teinte et opacité (0-255)

        /// <summary>Nombre de colonnes de la grille de frames (largeur de texture / FrameSize.X).</summary>
        public int Columns
        {
            get
            {
                if (Texture == IntPtr.Zero) return 1;
                SDL.GetTextureSize(Texture, out float tw, out _);
                return Math.Max(1, (int)(tw / Math.Max(1f, FrameSize.X)));
            }
        }


        // Taille d'une frame sur la texture (en pixels)
        public Vector2 FrameSize = new Vector2(16, 16);

        Vector2 Size = Vector2.One; // Taille en mètres (pour le rendu)

        /// <summary>Matériau (.kmat) appliqué au sprite ; vide = rendu normal.</summary>
        public string MaterialPath = "";

        /// <summary>Surcharges des paramètres du matériau pour cet objet : "Nom=v1/v2|Nom2=v" (vide = valeurs du matériau).</summary>
        public string MaterialParams = "";

        // Cache des surcharges décodées et des valeurs prêtes pour le GPU (reconstruit si le texte ou le matériau change)
        Dictionary<string, float[]> overrides = new();
        string? overridesParsedFrom;
        Material? uniformsMaterial;
        string? uniformsOverrides;
        float[]? uniforms;

        public bool FlipX = false;
        public bool FlipY = false;

        public override IEnumerable<InspectorField> GetInspectorFields()
        {
            yield return new TextureField { Label = "Texture", Get = () => (Texture, TexturePath), Set = (tex, path) => { Texture = tex; TexturePath = path; } };

            yield return new ColorField { Label = "Color", Get = () => Color, Set = c => Color = c };

            yield return new Bool2Field { Label = "Flip", GetFirst = () => FlipX, GetSecond = () => FlipY, Set = (x, y) => { FlipX = x; FlipY = y; } };

            yield return new Vector2Field { Label = "Frame Size (px)", Get = () => FrameSize, Set = v => FrameSize = v, Min = 1, Max = float.MaxValue };

            yield return new IntField { Label = "Frame", Get = () => Frame, Set = v => Frame = v, Min = -1, Max = int.MaxValue };

            yield return new AssetPathField { Label = "Material", Extensions = new[] { ".kmat" }, Get = () => MaterialPath, Set = v => MaterialPath = v };

        var material = string.IsNullOrEmpty(MaterialPath) ? null : MaterialCache.Get(MaterialPath);
        if (material == null) yield break;

        foreach (var param in material.Params)
            foreach (var field in ParamFields(material, param))
                yield return field;

        if (!string.IsNullOrEmpty(MaterialParams))
            yield return new ButtonField { Label = "Reset material params", Action = () => MaterialParams = "", Tooltip = "Revient aux valeurs du matériau" };
        }

        // ---- Paramètres de shader ------------------------------------------------------------------------

        void EnsureOverrides()
        {
            if (overridesParsedFrom == MaterialParams) return;
            overrides = ShaderParams.ParseOverrides(MaterialParams);
            overridesParsedFrom = MaterialParams;
        }

        float[] GetParamValue(Material material, ShaderParam param)
        {
            EnsureOverrides();
            return overrides.TryGetValue(param.Name, out var v) ? (float[])v.Clone() : material.GetDefault(param);
        }

        void SetParamValue(ShaderParam param, float[] value)
        {
            EnsureOverrides();
            overrides[param.Name] = value;
            MaterialParams = ShaderParams.FormatOverrides(overrides);
            overridesParsedFrom = MaterialParams;
        }

        IEnumerable<InspectorField> ParamFields(Material material, ShaderParam param)
        {
            switch (param.Type)
            {
                case ShaderParamType.Float:
                    yield return new FloatField
                    {
                        Label = param.Name,
                        Get = () => GetParamValue(material, param)[0],
                        Set = v => { var a = GetParamValue(material, param); a[0] = v; SetParamValue(param, a); },
                        Min = param.HasRange ? param.Min : 0f,
                        Max = param.HasRange ? param.Max : 0f,
                        Slider = param.HasRange
                    };
                    break;

                case ShaderParamType.Float2:
                    yield return new Vector2Field
                    {
                        Label = param.Name,
                        Get = () => { var a = GetParamValue(material, param); return new Vector2(a[0], a[1]); },
                        Set = v => SetParamValue(param, new[] { v.X, v.Y, 0f, 0f })
                    };
                    break;

                case ShaderParamType.Float3:
                case ShaderParamType.Float4:
                    string[] axes = { "x", "y", "z", "w" };
                    for (int i = 0; i < param.Components; i++)
                    {
                        int index = i;
                        yield return new FloatField
                        {
                            Label = param.Name + "." + axes[index],
                            Get = () => GetParamValue(material, param)[index],
                            Set = v => { var a = GetParamValue(material, param); a[index] = v; SetParamValue(param, a); }
                        };
                    }
                    break;

                case ShaderParamType.Color:
                    yield return new ColorField
                    {
                        Label = param.Name,
                        Get = () => { var a = GetParamValue(material, param); return new Color((int)a[0], (int)a[1], (int)a[2], (int)a[3]); },
                        Set = c => SetParamValue(param, new float[] { c.R, c.G, c.B, c.A })
                    };
                    break;
            }
        }

        /// <summary>Valeurs GPU du matériau + surcharges de cet objet (null si le shader n'a pas de paramètres).</summary>
        float[]? BuildUniforms(Material material)
        {
            if (material.Params.Count == 0) return null;

            if (uniforms == null || uniformsMaterial != material || uniformsOverrides != MaterialParams)
            {
                EnsureOverrides();
                uniforms = ShaderParams.Pack(material.Params,
                    p => overrides.TryGetValue(p.Name, out var v) ? v : material.GetDefault(p));
                uniformsMaterial = material;
                uniformsOverrides = MaterialParams;
            }
            return uniforms;
        }

        public override void Render(Renderer renderer, Camera? camera)
        {
            if (Texture == IntPtr.Zero) return;

            // Position et échelle monde (en MÈTRES)
            var pos = gameObject.transform.WorldPosition;
            var scale = gameObject.transform.WorldScale;

            // Dimensions finales en mètres
            float w = Size.X * scale.X;
            float h = Size.Y * scale.Y;

            // Calcul du centre en mètres
            float x = pos.X;
            float y = pos.Y;

            double angle = -gameObject.transform.WorldRotation;

            var material = string.IsNullOrEmpty(MaterialPath) ? null : MaterialCache.Get(MaterialPath);
            var shader = material?.Shader;
            if (shader != null)
            {
                var data = BuildUniforms(material!);
                if (data != null) shader.SetUniforms(data);
                renderer.SetShader(shader);
                renderer.ClearSdlError();
            }

            if (Frame >= 0)
            {
                int fw = Math.Max(1, (int)FrameSize.X), fh = Math.Max(1, (int)FrameSize.Y);
                int cols = Columns;
                var src = new Rectangle((Frame % cols) * fw, (Frame / cols) * fh, fw, fh);

                if (camera != null) renderer.DrawTexturePart(Texture, src, x, y, w, h, Color, FlipX, FlipY, angle, camera);
                else renderer.DrawTexturePart(Texture, src, x, y, w, h, Color, FlipX, FlipY, angle);
            }
            else
            {
                if (camera != null)
                    renderer.DrawTexture(Texture, x, y, w, h, Color, FlipX, FlipY, angle, camera);
                else
                    renderer.DrawTexture(Texture, x, y, w, h, Color, FlipX, FlipY, angle);
            }

            if (shader != null)
            {
                renderer.Flush();   // SDL lit les uniforms au flush : on exécute ce draw avant que le sprite suivant change les valeurs
                renderer.ReportSdlError($"Draw avec {MaterialPath}");
                renderer.SetShader(null);
            }
        }
    }
}