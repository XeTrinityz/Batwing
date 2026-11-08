namespace Trinity.Batwing;

internal static class Deck
{
    public const float RepeatDelay = 0.38f;
    public const float RepeatRate = 0.06f;
    private const float ConfirmSeconds = 6f;

    private static readonly Keys[] RepeatKeys = { Keys.Left, Keys.Right, Keys.Up, Keys.Down };
    private static readonly float[] _held = { -1f, -1f, -1f, -1f };
    private static readonly Dictionary<Page, int> _rowMemory = new();
    private static nint _inputPc;
    private static float _confirmLeft;

    public static bool IsOpen { get; private set; }

    public static int PageIndex { get; private set; }

    public static int RowIndex { get; private set; }

    public static int Level { get; private set; } = 1;

    public static Bind? Binding { get; private set; }

    public static bool BindRefused { get; private set; }

    public static Act? Confirming { get; private set; }

    public static float ConfirmLeft => Math.Clamp(_confirmLeft / ConfirmSeconds, 0f, 1f);

    public static Option? Flashed { get; private set; }

    public static double FlashAt { get; private set; }

    public static double OpenedAt { get; private set; }

    public static double PageChangedAt { get; private set; }

    public static Page Current => Opt.Pages[Math.Clamp(PageIndex, 0, Opt.Pages.Count - 1)];

    public static Option? Selected => Current.Options.Count == 0 ? null : Current.Options[Math.Clamp(RowIndex, 0, Current.Options.Count - 1)];

    public static void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public static void Open()
    {
        if (IsOpen || !World.InGame || World.Paused)
            return;
        if (!Shared.ScreenFree)
        {
            Readout.Toast("DECK", "ANOTHER MENU IS OPEN", false);
            return;
        }

        IsOpen = true;
        PageIndex = Math.Clamp(PageIndex, 0, Opt.Pages.Count - 1);
        RowIndex = _rowMemory.TryGetValue(Current, out int row) ? Math.Clamp(row, 0, Math.Max(0, Current.Options.Count - 1)) : 0;
        Level = 1;
        Binding = null;
        Confirming = null;
        BindRefused = false;
        OpenedAt = Gfx.Time;
        PageChangedAt = Gfx.Time;
        Shared.MenuOwner = Shared.OwnerName;
        HoldInput();
    }

    public static void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        Binding = null;
        Confirming = null;
        Array.Fill(_held, -1f);
        Pad.DeckClosed();
        if (Shared.MenuOwner == Shared.OwnerName)
            Shared.MenuOwner = null;
        ReleaseInput();
    }

    private static void HoldInput()
    {
        Safe.Run("deck movement", () =>
        {
            if (World.Pc is { } pc)
            {
                pc.IgnoreMoveInput(true);
                _inputPc = pc.Ptr;
            }
        });
        Safe.Run("deck keys", () => GameKeys.Hold("deck", GameKeys.DeckKeys));
    }

    private static void ReleaseInput()
    {
        Safe.Run("deck keys", () => GameKeys.Release("deck"));
        Safe.Run("deck movement", () =>
        {
            if (_inputPc != 0 && World.Pc is { } pc && pc.Ptr == _inputPc)
                pc.IgnoreMoveInput(false);
            _inputPc = 0;
        });
    }

    public static void Tick(float dt)
    {
        if (!IsOpen)
            return;

        if (Shared.MenuOwner != Shared.OwnerName || !World.InGame)
        {
            Close();
            return;
        }

        if (Confirming != null)
        {
            _confirmLeft -= dt;
            if (_confirmLeft <= 0f)
                Confirming = null;
        }

        if (Binding != null)
            return;

        for (int i = 0; i < RepeatKeys.Length; i++)
        {
            if (!Keyboard.IsDown(RepeatKeys[i]) || !Native.GameInFront())
            {
                _held[i] = -1f;
                continue;
            }

            if (_held[i] < 0f)
            {
                _held[i] = 0f;
                continue;
            }

            _held[i] += dt;
            if (_held[i] >= RepeatDelay)
            {
                _held[i] -= RepeatRate;
                Press(RepeatKeys[i], true);
            }
        }
    }

    public static void Press(Keys key, bool repeat)
    {
        if (!IsOpen)
            return;

        if (Binding is { } binding)
        {
            if (repeat)
                return;
            if (key is Keys.Back or Keys.Escape)
            {
                Binding = null;
                return;
            }

            if (Array.IndexOf(GameKeys.DeckKeys, key) >= 0 || key is Keys.LButton or Keys.RButton or Keys.MButton || (binding != Opt.DeckKey && key == Opt.DeckKey.Key) || (binding != Opt.CallKey && key == Opt.CallKey.Key) || (binding != Opt.RemoteKey && key == Opt.RemoteKey.Key)
                || (binding != Opt.EscortKey && key == Opt.EscortKey.Key) || (binding != Opt.StrikeKey && key == Opt.StrikeKey.Key))
            {
                BindRefused = true;
                return;
            }

            BindRefused = false;
            if (binding.Take(key))
                Changed(binding);
            Binding = null;
            return;
        }

        if (key != Keys.Enter)
            Confirming = null;

        if (key == Opt.DeckKey.Key && !repeat)
        {
            Close();
            return;
        }

        switch (key)
        {
            case Keys.Up:
                if (Level == 0)
                    TurnPage(-1);
                else
                    MoveRow(-1);
                break;
            case Keys.Down:
                if (Level == 0)
                    TurnPage(1);
                else
                    MoveRow(1);
                break;
            case Keys.Left:
                if (Level == 1 && Selected is { } l && l.Step(-1))
                    Changed(l);
                break;
            case Keys.Right:
                if (Level == 0)
                    Level = 1;
                else if (Selected is { } r && r.Step(1))
                    Changed(r);
                break;
            case Keys.Enter:
                if (repeat)
                    break;
                if (Level == 0)
                    Level = 1;
                else if (Selected is { } o)
                    Activate(o);
                break;
            case Keys.Back:
                if (repeat)
                    break;
                if (Level == 1)
                    Level = 0;
                else
                    Close();
                break;
            case Keys.PageUp:
                TurnPage(-1);
                break;
            case Keys.PageDown:
            case Keys.Tab:
                TurnPage(1);
                break;
            case Keys.Home:
                if (Level == 1)
                    RowIndex = 0;
                break;
            case Keys.End:
                if (Level == 1)
                    RowIndex = Math.Max(0, Current.Options.Count - 1);
                break;
            case Keys.Delete:
                if (repeat || Level != 1 || Selected is not { } d || d is Act || d.IsDefault)
                    break;
                d.Reset();
                Changed(d);
                break;
        }
    }

    private static void TurnPage(int dir)
    {
        _rowMemory[Current] = RowIndex;
        PageIndex = (PageIndex + dir + Opt.Pages.Count) % Opt.Pages.Count;
        RowIndex = _rowMemory.TryGetValue(Current, out int row) ? Math.Clamp(row, 0, Math.Max(0, Current.Options.Count - 1)) : 0;
        PageChangedAt = Gfx.Time;
    }

    private static void MoveRow(int dir)
    {
        int n = Current.Options.Count;
        if (n == 0)
            return;
        RowIndex = (RowIndex + dir + n) % n;
    }

    private static void Activate(Option o)
    {
        switch (o)
        {
            case Act act:
                if (act.Confirm && Confirming != act)
                {
                    Confirming = act;
                    _confirmLeft = ConfirmSeconds;
                    return;
                }

                Confirming = null;
                Safe.Run(act.Id, () => act.Press());
                Flash(act);
                break;
            case Bind bind:
                Binding = bind;
                BindRefused = false;
                break;
            default:
                if (o.Press())
                    Changed(o);
                break;
        }
    }

    private static void Changed(Option o)
    {
        Safe.Run(o.Id + " change", () => o.Changed?.Invoke());
        Settings.MarkDirty();
        Flash(o);
    }

    private static void Flash(Option o)
    {
        Flashed = o;
        FlashAt = Gfx.Time;
    }
}
