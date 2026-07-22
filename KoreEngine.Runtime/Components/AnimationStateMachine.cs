public class AnimationStateMachine
{
    record Transition(string From, string To, Func<bool> Condition);

    Dictionary<string, Animation> animations = new();
    List<Transition> transitions = new();

    string current = "";
    int currentFrame;
    float timer;
    bool finished;

    public string CurrentState => current;
    public bool IsFinished => finished;
    public AnimationFrame CurrentFrame
    {
        get
        {
            if (string.IsNullOrEmpty(current) || !animations.ContainsKey(current))
                return default;
            return animations[current].Frames[currentFrame];
        }
    }

    public AnimationStateMachine Add(Animation animation)
    {
        animations[animation.Name] = animation;
        return this;
    }

    public AnimationStateMachine AddTransition(string from, string to, Func<bool> condition)
    {
        transitions.Add(new Transition(from, to, condition));
        return this;
    }

    public AnimationStateMachine SetDefault(string name)
    {
        current = name;
        currentFrame = 0;
        timer = 0;
        finished = false;
        return this;
    }

    public void Update(float dt)
    {
        if (string.IsNullOrEmpty(current) || !animations.ContainsKey(current)) return;

        // Vérifier les transitions
        foreach (var t in transitions)
        {
            if (t.From == current && t.Condition())
            {
                GoTo(t.To);
                break;
            }
        }

        var anim = animations[current];

        // Avancer le timer
        timer += dt;
        if (timer >= anim.Frames[currentFrame].Duration)
        {
            timer = 0;
            currentFrame++;

            if (currentFrame >= anim.Frames.Length)
            {
                if (anim.Loop)
                    currentFrame = 0;
                else
                {
                    currentFrame = anim.Frames.Length - 1;
                    finished = true;
                }
            }
        }
    }

    public void GoTo(string name)
    {
        if (current == name) return;
        current = name;
        currentFrame = 0;
        timer = 0;
        finished = false;
    }
}