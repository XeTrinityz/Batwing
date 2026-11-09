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

    public abstract bool Step(int dir);

    public virtual bool Press() => Step(1);

    public abstract void Reset();

    public abstract void Read(TomlTable table);

    public abstract string? Toml { get; }

    public virtual string LabelNow => Label;

    public virtual bool HasValue => true;
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
        float next = MathF.Round(Math.Clamp(Number + dir * Stride, Min, Max) / Stride) * Stride;
        next = MathF.Round(next, 3);
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
}

internal sealed class Pick : Option
{
    public int Index;
    public string[] Keys = Array.Empty<string>();
    public string[] Labels = Array.Empty<string>();

    public override string Value => Labels.Length == 0 ? "" : Labels[Math.Clamp(Index, 0, Labels.Length - 1)];

    public string Key => Keys.Length == 0 ? "" : Keys[Math.Clamp(Index, 0, Keys.Length - 1)];

    public override bool IsDefault => Index == 0;

    public override bool Step(int dir)
    {
        if (Labels.Length < 2)
            return false;

        Index = (Index + dir + Labels.Length) % Labels.Length;
        return true;
    }

    public override void Reset() => Index = 0;

    public override void Read(TomlTable table)
    {
        string saved = table.TryGetValue(Id, out object? v) && v is string s ? s : "";
        Index = Math.Max(0, Array.FindIndex(Keys, k => k.Equals(saved, StringComparison.OrdinalIgnoreCase)));
    }

    public override string Toml => "\"" + Key + "\"";
}

internal sealed class Bind : Option
{
    public Keys Key;
    public Keys Default;

    public override string Value => Key == Keys.None ? "NONE" : KeyName(Key);

    public override bool IsDefault => Key == Default;

    public override bool Step(int dir) => false;

    public override bool Press() => false;

    public bool Take(Keys key)
    {
        if (key == Key)
            return false;
        Key = key;
        return true;
    }

    public override void Reset() => Key = Default;

    public override void Read(TomlTable table) =>
        Key = table.TryGetValue(Id, out object? v) && v is string s && Enum.TryParse(s, true, out Keys k) ? k : Default;

    public override string Toml => "\"" + Key + "\"";

    public static string KeyName(Keys key) => key switch
    {
        Keys.LButton => "MOUSE 1",
        Keys.RButton => "MOUSE 2",
        Keys.MButton => "MOUSE 3",
        Keys.XButton1 => "MOUSE 4",
        Keys.XButton2 => "MOUSE 5",
        Keys.Back => "BACKSPACE",
        Keys.Return => "ENTER",
        Keys.ControlKey => "CTRL",
        Keys.ShiftKey => "SHIFT",
        Keys.Menu => "ALT",
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

internal sealed class Act : Option
{
    public Action Run = () => { };

    public Func<string>? Live;

    public Func<string>? Note;

    public bool Confirm;

    public override string Value => Note?.Invoke() ?? "";

    public override string LabelNow => Live?.Invoke() ?? Label;

    public override bool HasValue => false;

    public override bool IsDefault => true;

    public override bool Step(int dir) => false;

    public override bool Press()
    {
        Run();
        return true;
    }

    public override void Reset() { }

    public override void Read(TomlTable table) { }

    public override string? Toml => null;
}
