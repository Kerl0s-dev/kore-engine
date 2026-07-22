using KoreEngine.Core;

public class Animation
{
    public string Name;
    public AnimationFrame[] Frames;
    public bool Loop = true;

    public Animation(string name, AnimationFrame[] frames, bool loop = true)
    {
        Name = name;
        Frames = frames;
        Loop = loop;
    }

    // Helpers pour créer rapidement une animation depuis une spritesheet uniforme
    public static Animation FromStrip(
        string name,
        int frameWidth, int frameHeight,
        int row, int startCol, int frameCount,
        float frameDuration,
        bool loop = true)
    {
        var frames = new AnimationFrame[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            frames[i] = new AnimationFrame
            {
                SourceRect = new Rectangle(
                    (startCol + i) * frameWidth,
                    row * frameHeight,
                    frameWidth,
                    frameHeight),
                Duration = frameDuration
            };
        }
        return new Animation(name, frames, loop);
    }
}