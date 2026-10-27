namespace KoreEngine;

// Structure représentant une particule individuelle
public struct Particle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public Color CurrentColor;
    public float Size;
    public float LifeTime;     // Durée de vie restante
    public float MaxLifeTime;  // Durée de vie totale initiale
    public bool IsActive;
}

public class ParticleSystem : Component
{
    public IntPtr Texture;
    public string TexturePath = "";
    public int MaxParticles = 100;
    public float EmissionRate = 10f; // Particules par seconde

    public float ParticleLifetime = 2f; // Secondes

    public float ParticleSize = 1f;
    public float ParticleSizeRandomness = 0.2f; // Facteur de randomisation pour la taille

    public Color ParticleColor = Color.White;

    public Vector2 ParticleVelocity = new(0, -1); // Vitesse par défaut
    public float ParticleVelocityRandomness = 0.5f; // Facteur de randomisation pour la vitesse

    // Liste interne des particules
    private List<Particle> _particles;
    private float _emissionTimer = 0f;
    private Random _random = new();

    public ParticleSystem()
    {
        _particles = new List<Particle>(MaxParticles);
        // Pré-allouer les particules pour éviter le garbage collection en jeu
        for (int i = 0; i < MaxParticles; i++)
        {
            _particles.Add(new Particle { IsActive = false });
        }
    }

    public override void Update(float deltaTime)
    {
        // 1. Mise à jour des particules existantes
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            if (!p.IsActive) continue;

            // Réduire la durée de vie
            p.LifeTime -= deltaTime;
            if (p.LifeTime <= 0f)
            {
                p.IsActive = false;
                _particles[i] = p;
                continue;
            }

            // Mettre à jour la position en fonction de la vitesse
            p.Position += p.Velocity * deltaTime;

            // Optionnel : faire varier la couleur ou l'opacité selon la vie restante
            // float lifeRatio = p.LifeTime / p.MaxLifeTime;
            // p.CurrentColor = ...

            _particles[i] = p;
        }

        // 2. Émission de nouvelles particules
        _emissionTimer += deltaTime;
        float emissionInterval = 1f / EmissionRate;

        while (_emissionTimer >= emissionInterval)
        {
            _emissionTimer -= emissionInterval;
            EmitParticle();
        }
    }

    private void EmitParticle()
    {
        // Trouver une particule inactive dans la liste
        int inactiveIndex = _particles.FindIndex(p => !p.IsActive);
        if (inactiveIndex == -1) return; // Plus de place dispo

        // Récupérer la position du GameObject parent
        // Supposons ici qu'on émet depuis (0,0) ou la position du composant
        Vector2 spawnPosition = transform.Position;

        // Appliquer un peu d'aléa sur la vitesse
        float randomVelFactor = 1f + (float)(_random.NextDouble() * 2.0 - 1.0) * ParticleVelocityRandomness;
        Vector2 finalVelocity = ParticleVelocity * randomVelFactor;

        // Appliquer un peu d'aléa sur la taille
        float randomSizeFactor = 1f + (float)(_random.NextDouble() * 2.0 - 1.0) * ParticleSizeRandomness;
        float finalSize = Math.Max(0.1f, ParticleSize * randomSizeFactor);

        // Activer et initialiser la particule
        _particles[inactiveIndex] = new Particle
        {
            Position = spawnPosition,
            Velocity = finalVelocity,
            CurrentColor = ParticleColor,
            Size = finalSize,
            LifeTime = ParticleLifetime,
            MaxLifeTime = ParticleLifetime,
            IsActive = true
        };
    }

    public override void Render(Renderer renderer, Camera? camera)
    {
        foreach (var p in _particles)
        {
            if (!p.IsActive) continue;

            if (Texture != IntPtr.Zero)
            {
                // Si une texture est assignée, on utilise la méthode DrawTexture
                // On centre la texture sur la position (p.Position.X - w/2, etc.) si besoin
                float w = p.Size;
                float h = p.Size;
                renderer.DrawTexture(
                    Texture,
                    p.Position.X - (w/2), // Centre X du GameObject
                    p.Position.Y + (h/2), // Centre Y du GameObject
                    w,
                    h,
                    p.CurrentColor,
                    camera: camera
                );
            }
            else
            {
                // Fallback si pas de texture (dessin de base)
                renderer.DrawRect(p.Position.X, p.Position.Y, p.Size, p.Size, p.CurrentColor, (byte)p.CurrentColor.A);
            }
        }
    }
}