using KoreEngine.Core;

namespace KoreEngine.Components
{
    public class AnimationDefinition
    {
        public string Name = "new_anim";
        public int SizeX = 16, SizeY = 16;
        public int Row = 0;
        public int StartCol = 0;
        public int FrameCount = 1;
        public float FrameDuration = 0.1f;
        public bool Looping = true;
    }

    public enum AnimationCondition
    {
        Always,
        IsGrounded,
        IsNotGrounded,
        VelocityXAbsGreaterThan,
        VelocityXAbsLessThan,
        VelocityYGreaterThan,
        VelocityYLessThan,
    }

    public class AnimationTransition
    {
        public string From = "Idle";
        public string To = "Idle";
        public AnimationCondition Condition = AnimationCondition.Always;
        public float Threshold = 0f;
    }

    public class Animator : Component
    {
        public AnimationStateMachine AnimationStateMachine = new();
        public List<AnimationDefinition> Definitions = new();
        public List<AnimationTransition> Transitions = new();

        SpriteRenderer? Target;
        PhysicsBody? body;

        readonly List<Animation> _animations = new();

        public override void Start()
        {
            body = Owner.GetComponent<PhysicsBody>();
            Target = Owner.GetComponent<SpriteRenderer>();
            Apply();
        }

        public void Apply()
        {
            if (AnimationStateMachine == null) return;

            _animations.Clear();
            foreach (var def in Definitions)
            {
                var anim = Animation.FromStrip(
                    def.Name, def.SizeX, def.SizeY,
                    def.Row, def.StartCol, def.FrameCount,
                    def.FrameDuration, def.Looping);
                _animations.Add(anim);
                AnimationStateMachine.Add(anim);
            }

            foreach (var t in Transitions)
            {
                var captured = t;
                Func<bool> cond = captured.Condition switch
                {
                    AnimationCondition.IsGrounded => () => body?.IsGrounded ?? false,
                    AnimationCondition.IsNotGrounded => () => !(body?.IsGrounded ?? true),
                    AnimationCondition.VelocityXAbsGreaterThan => () => MathF.Abs(body?.Velocity.X ?? 0) > captured.Threshold,
                    AnimationCondition.VelocityXAbsLessThan => () => MathF.Abs(body?.Velocity.X ?? 0) <= captured.Threshold,
                    AnimationCondition.VelocityYGreaterThan => () => (body?.Velocity.Y ?? 0) > captured.Threshold,
                    AnimationCondition.VelocityYLessThan => () => (body?.Velocity.Y ?? 0) < captured.Threshold,
                    _ => () => true,
                };
                AnimationStateMachine.AddTransition(captured.From, captured.To, cond);
            }
        }

        public override IEnumerable<string> Serialize()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var d in Definitions)
                yield return $"Def: {d.Name},{d.SizeX},{d.SizeY},{d.Row},{d.StartCol},{d.FrameCount},{d.FrameDuration.ToString(inv)},{d.Looping.ToString().ToLower()}";
            foreach (var t in Transitions)
                yield return $"Trans: {t.From},{t.To},{t.Condition},{t.Threshold.ToString(inv)}";
        }

        public override void Deserialize(List<string> extraLines)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Definitions.Clear();
            Transitions.Clear();

            foreach (var line in extraLines)
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();
                var p = value.Split(',');

                if (key == "Def" && p.Length >= 8)
                {
                    Definitions.Add(new AnimationDefinition
                    {
                        Name = p[0].Trim(),
                        SizeX = int.Parse(p[1]),
                        SizeY = int.Parse(p[2]),
                        Row = int.Parse(p[3]),
                        StartCol = int.Parse(p[4]),
                        FrameCount = int.Parse(p[5]),
                        FrameDuration = float.Parse(p[6], inv),
                        Looping = p[7].Trim() == "true"
                    });
                }
                else if (key == "Trans" && p.Length >= 4)
                {
                    Transitions.Add(new AnimationTransition
                    {
                        From = p[0].Trim(),
                        To = p[1].Trim(),
                        Condition = Enum.Parse<AnimationCondition>(p[2].Trim()),
                        Threshold = float.Parse(p[3], inv)
                    });
                }
            }
        }

        public override void Update(float dt)
        {
            AnimationStateMachine?.Update(dt);
        }

        // ---------------------------------------------------------------
        // Inspector
        // ---------------------------------------------------------------

        static readonly string[] ConditionNames = Enum.GetNames<AnimationCondition>();

        public override IEnumerable<InspectorField> GetInspectorFields()
        {
            yield return new ComponentRefField
            {
                Label = "Target",
                ComponentType = typeof(SpriteRenderer),
                Get = () => Target,
                Set = c => Target = c as SpriteRenderer
            };

            yield return new ListField
            {
                Label = "Animations",
                Count = () => Definitions.Count,
                ItemHeader = i => $"Animation {i}: {Definitions[i].Name}",
                ItemFields = DefinitionFields,
                AddItem = () => Definitions.Add(new AnimationDefinition()),
                RemoveItem = i => Definitions.RemoveAt(i),
                ItemContextActions = i => new (string, Action)[]
                {
                    ("Set as default", () => AnimationStateMachine?.SetDefault(Definitions[i].Name))
                }
            };

            yield return new ListField
            {
                Label = "Transitions",
                Count = () => Transitions.Count,
                ItemHeader = i => $"Transition {i}: {Transitions[i].From} → {Transitions[i].To}",
                ItemFields = TransitionFields,
                AddItem = () => Transitions.Add(new AnimationTransition()),
                RemoveItem = i => Transitions.RemoveAt(i)
            };

            yield return new ActionField
            {
                Label = "Apply",
                Action = () =>
                {
                    body = Owner.GetComponent<PhysicsBody>();
                    Apply();
                },
                Tooltip = "Recharge animations et transitions dans la state machine."
            };
        }

        IEnumerable<InspectorField> DefinitionFields(int i)
        {
            var d = Definitions[i];

            yield return new StringField { Label = "Name", Get = () => d.Name, Set = v => d.Name = v };
            yield return new IntField { Label = "Size X", Get = () => d.SizeX, Set = v => d.SizeX = v, Min = 1, Max = 512 };
            yield return new IntField { Label = "Size Y", Get = () => d.SizeY, Set = v => d.SizeY = v, Min = 1, Max = 512 };
            yield return new IntField { Label = "Row", Get = () => d.Row, Set = v => d.Row = v, Min = 0, Max = 64 };
            yield return new IntField { Label = "Start Col", Get = () => d.StartCol, Set = v => d.StartCol = v, Min = 0, Max = 64 };
            yield return new IntField { Label = "Frames", Get = () => d.FrameCount, Set = v => d.FrameCount = v, Min = 1, Max = 64 };
            yield return new FloatField { Label = "Duration", Get = () => d.FrameDuration, Set = v => d.FrameDuration = v, Speed = 0.01f, Min = 0.01f, Max = 2f };
            yield return new BoolField { Label = "Loop", Get = () => d.Looping, Set = v => d.Looping = v };
        }

        IEnumerable<InspectorField> TransitionFields(int i)
        {
            var t = Transitions[i];

            yield return new StringField { Label = "From", Get = () => t.From, Set = v => t.From = v };
            yield return new StringField { Label = "To", Get = () => t.To, Set = v => t.To = v };
            yield return new EnumField
            {
                Label = "Condition",
                Names = ConditionNames,
                Get = () => (int)t.Condition,
                Set = idx => t.Condition = (AnimationCondition)idx
            };

            bool needsThreshold = t.Condition is
                AnimationCondition.VelocityXAbsGreaterThan or
                AnimationCondition.VelocityXAbsLessThan or
                AnimationCondition.VelocityYGreaterThan or
                AnimationCondition.VelocityYLessThan;

            if (needsThreshold)
                yield return new FloatField { Label = "Threshold", Get = () => t.Threshold, Set = v => t.Threshold = v, Speed = 0.5f };
        }
    }
}
