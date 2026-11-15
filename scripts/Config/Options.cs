using System.Globalization;

namespace Trinity.Batwing;

internal sealed record Page(string Key, string Title, string Blurb)
{
    public readonly List<Option> Options = new();
}

internal abstract class Option
{
    public string Id = "";
    public string Label = "";
    public string Help = "";
    public Page Page = null!;

    public Action? Changed;

    public abstract string Value { get; }

    public abstract bool IsDefault { get; }

    // Left and right. False when nothing changed.
    public virtual bool Step(int dir) => false;

    // Enter or A. False when nothing changed.
    public virtual bool Press() => Step(1);

    public abstract void Reset();

    public abstract void Read(TomlTable table);

    // What goes in settings.toml, or null for a row that isn't saved.
    public abstract string? Toml { get; }

    // What settings.toml says this row takes, for the comment over it.
    public virtual string Takes => "";

    public virtual string LabelNow => Label;

    public virtual bool Saved => Toml != null;
}

internal sealed class Switch : Option
{
    public bool On;
    public bool Default;

    public override string Value => On ? "ON" : "OFF";

    public override bool IsDefault => On == Default;

    public override bool Step(int dir)
    {
        On = !On;
        return true;
    }

    public override void Reset() => On = Default;

    public override void Read(TomlTable table) => On = table.TryGetValue(Id, out object? v) && v is bool b ? b : Default;

    public override string Toml => On ? "true" : "false";

    public override string Takes => "true or false";
}

internal sealed class Dial : Option
{
    public float Number;
    public float Default;
    public float Min;
    public float Max;
    public float Stride;
    public Func<float, string> Format = v => v.ToString("0.##", CultureInfo.InvariantCulture);

    public override string Value => Format(Number);

    public override bool IsDefault => MathF.Abs(Number - Default) <= 0.0001f;

    public float Fraction => Max > Min ? Math.Clamp((Number - Min) / (Max - Min), 0f, 1f) : 0f;

    public float DefaultFraction => Max > Min ? Math.Clamp((Default - Min) / (Max - Min), 0f, 1f) : 0f;

    public override bool Step(int dir)
    {
        float next = MathF.Round(MathF.Round(Math.Clamp(Number + dir * Stride, Min, Max) / Stride) * Stride, 3);
        next = Math.Clamp(next, Min, Max);
        if (MathF.Abs(next - Number) < 0.0001f)
            return false;

        Number = next;
        return true;
    }

    public override bool Press() => false;

    public override void Reset() => Number = Default;

    public override void Read(TomlTable table)
    {
        Number = Default;
        if (table.TryGetValue(Id, out object? v))
        {
            if (v is double d)
                Number = (float)d;
            else if (v is long l)
                Number = l;
        }

        Number = Math.Clamp(Number, Min, Max);
    }

    public override string Toml => Number.ToString("0.0##", CultureInfo.InvariantCulture);

    public override string Takes => Format(Min) + " to " + Format(Max);
}

internal sealed class Pick : Option
{
    public int Index;
    public int Default;
    public string[] Keys = Array.Empty<string>();
    public string[] Labels = Array.Empty<string>();

    public override string Value => Labels.Length == 0 ? "" : Labels[Math.Clamp(Index, 0, Labels.Length - 1)];

    public string Key => Keys.Length == 0 ? "" : Keys[Math.Clamp(Index, 0, Keys.Length - 1)];

    public override bool IsDefault => Index == Default;

    public override bool Step(int dir)
    {
        if (Labels.Length < 2)
            return false;

        Index = (Index + dir + Labels.Length) % Labels.Length;
        return true;
    }

    public override void Reset() => Index = Default;

    public override void Read(TomlTable table)
    {
        string saved = table.TryGetValue(Id, out object? v) && v is string s ? s : "";
        int found = Array.FindIndex(Keys, k => k.Equals(saved, StringComparison.OrdinalIgnoreCase));
        Index = found >= 0 ? found : Default;
    }

    public override string Toml => "\"" + Key + "\"";

    public override string Takes => string.Join(", ", Keys);
}

// A keyboard key or mouse button for one action.
internal sealed class Bind : Option
{
    public Keys Key;
    public Keys Default;
    public Act For;

    // Flight controls may take the mouse buttons. The mod's own hotkeys can't, the game needs them on foot.
    public bool Mouse;

    public override string Value => Key == Keys.None ? "NONE" : KeyName(Key);

    public override bool IsDefault => Key == Default;

    public override bool Press() => false;

    public override void Reset() => Key = Default;

    public override void Read(TomlTable table) =>
        Key = table.TryGetValue(Id, out object? v) && v is string s && Enum.TryParse(s, true, out Keys k) ? k : Default;

    public override string Toml => "\"" + Key + "\"";

    public override string Takes => "a key name";

    public static string KeyName(Keys key) => key switch
    {
        Keys.None => "NONE",
        Keys.LButton => "LEFT MOUSE",
        Keys.RButton => "RIGHT MOUSE",
        Keys.MButton => "MIDDLE MOUSE",
        Keys.XButton1 => "MOUSE 4",
        Keys.XButton2 => "MOUSE 5",
        Keys.Back => "BACKSPACE",
        Keys.Return => "ENTER",
        Keys.ControlKey => "CTRL",
        Keys.ShiftKey => "SHIFT",
        Keys.Menu => "ALT",
        Keys.Space => "SPACE",
        Keys.Capital => "CAPS LOCK",
        Keys.Oemtilde => "~",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        Keys.OemPipe => "\\",
        >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "NUM " + ((int)key - (int)Keys.NumPad0).ToString(CultureInfo.InvariantCulture),
        _ => key.ToString().ToUpperInvariant(),
    };
}

// A controller button for one action.
internal sealed class PadBind : Option
{
    public PadButton Button;
    public PadButton Default;
    public Act For;

    public override string Value => Glyphs.Name(Button);

    public override bool IsDefault => Button == Default;

    public override bool Press() => false;

    public override void Reset() => Button = Default;

    public override void Read(TomlTable table) =>
        Button = table.TryGetValue(Id, out object? v) && v is string s && Enum.TryParse(s, true, out PadButton b) && Glyphs.Bindable(b) ? b : Default;

    public override string Toml => "\"" + Button + "\"";

    public override string Takes => "A, B, X, Y, LB, RB, LS, RS, Back, Up, Down, Left, Right";
}

// A row that does something rather than holding a setting.
internal sealed class Command : Option
{
    public Action Run = () => { };

    // The label as it reads right now, like CALL BATWING or BAIL OUT.
    public Func<string>? Live;

    // Shown where a setting shows its value.
    public Func<string>? Note;

    // Asks for a second press before it runs.
    public bool Confirm;

    public override string Value => Note?.Invoke() ?? "";

    public override string LabelNow => Live?.Invoke() ?? Label;

    public override bool IsDefault => true;

    public override bool Press()
    {
        Run();
        return true;
    }

    public override void Reset()
    {
    }

    public override void Read(TomlTable table)
    {
    }

    public override string? Toml => null;
}
