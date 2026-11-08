namespace Trinity.Batwing;

internal readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Mix(Rgba a, Rgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgba((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
    }

    public Rgba Dim(float f) => new((byte)(R * f), (byte)(G * f), (byte)(B * f), A);
}

internal static class Palette
{
    private static readonly Rgba[] Phosphors =
    {
        new(96, 204, 244),
        new(118, 255, 136),
        new(255, 186, 56),
        new(232, 240, 236),
    };

    public static Rgba Hud { get; private set; } = Phosphors[0];

    public static Rgba Text { get; private set; } = Phosphors[0].Dim(0.86f);

    public static Rgba Dim { get; private set; } = Phosphors[0].Dim(0.55f);

    public static Rgba Faint { get; private set; } = Phosphors[0].Dim(0.3f);

    public static readonly Rgba Caution = new(255, 186, 56);
    public static readonly Rgba Warn = new(255, 84, 70);
    private static readonly Rgba ScreenBase = new(5, 7, 8, 236);
    private const float ScreenTint = 0.04f;

    public static Rgba Screen { get; private set; } = Rgba.Mix(ScreenBase, Phosphors[0] with { A = 236 }, ScreenTint);
    public static readonly Rgba Bezel = new(22, 25, 27, 250);
    public static readonly Rgba BezelEdge = new(58, 64, 68);
    public static readonly Rgba Black = new(0, 0, 0);
    public static readonly Rgba White = new(255, 255, 255);

    public static Rgba CautionNow => Opt.HudColour.Key == "amber" ? new Rgba(240, 244, 240) : Caution;

    private static int _index = -1;

    public static void Update()
    {
        int index = Math.Clamp(Opt.HudColour.Index, 0, Phosphors.Length - 1);
        if (index == _index)
            return;

        _index = index;
        Rgba p = Phosphors[index];
        Hud = p;
        Text = p.Dim(0.86f);
        Dim = p.Dim(0.55f);
        Faint = p.Dim(0.3f);
        Screen = Rgba.Mix(ScreenBase, p with { A = 236 }, ScreenTint);
    }
}
