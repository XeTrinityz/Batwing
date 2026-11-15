namespace Trinity.Batwing;

// The settings deck: which page and row are picked and what the keys and buttons do to them. DeckView and
// GauntletView draw it.
internal static class Deck
{
    public const float RepeatDelay = 0.38f;
    public const float RepeatRate = 0.06f;
    private const float ConfirmSeconds = 6f;

    private static readonly Keys[] NavKeys =
    {
        Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Enter, Keys.Back, Keys.Tab, Keys.PageUp, Keys.PageDown, Keys.Delete, Keys.Home, Keys.End,
    };

    private static readonly Keys[] RepeatKeys = { Keys.Left, Keys.Right, Keys.Up, Keys.Down };
    private static readonly PadButton[] RepeatButtons = { PadButton.Left, PadButton.Right, PadButton.Up, PadButton.Down };
    private static readonly float[] _keyHeld = { -1f, -1f, -1f, -1f };
    private static readonly float[] _padHeld = { -1f, -1f, -1f, -1f };
    private static readonly Dictionary<Page, int> _rowMemory = new();
    private static nint _inputPc;
    private static float _confirmLeft;
    private static bool _padWaitRelease;

    public static bool IsOpen { get; private set; }

    public static int PageIndex { get; private set; }

    public static int RowIndex { get; private set; }

    // 0 while picking a page, 1 on its rows.
    public static int Level { get; private set; } = 1;

    // The row waiting for a new key or button.
    public static Option? Binding { get; private set; }

    public static string? Swapped { get; private set; }

    public static Command? Confirming { get; private set; }

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
            Toasts.Show("ANOTHER MENU IS OPEN", false);
            return;
        }

        IsOpen = true;
        PageIndex = Math.Clamp(PageIndex, 0, Opt.Pages.Count - 1);
        RowIndex = _rowMemory.TryGetValue(Current, out int row) ? Math.Clamp(row, 0, Math.Max(0, Current.Options.Count - 1)) : 0;
        Level = 1;
        Binding = null;
        Swapped = null;
        Confirming = null;
        OpenedAt = Gfx.Time;
        PageChangedAt = Gfx.Time;
        _padWaitRelease = true;
        Shared.MenuOwner = Shared.OwnerName;
        Safe.Run("deck movement", () =>
        {
            if (World.Pc is { } pc)
            {
                pc.IgnoreMoveInput(true);
                _inputPc = pc.Ptr;
            }
        });
        GameKeys.Hold("deck", NavKeys, GameKeys.PadNames);
    }

    public static void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        Binding = null;
        Confirming = null;
        Array.Fill(_keyHeld, -1f);
        Array.Fill(_padHeld, -1f);
        if (Shared.MenuOwner == Shared.OwnerName)
            Shared.MenuOwner = null;
        GameKeys.Release("deck");
        Safe.Run("deck movement", () =>
        {
            if (_inputPc != 0 && World.Pc is { } pc && pc.Ptr == _inputPc)
                pc.IgnoreMoveInput(false);
            _inputPc = 0;
        });
        Settings.Flush();
    }

    public static void Tick(float dt)
    {
        if (!IsOpen)
            return;

        if (Shared.MenuOwner != Shared.OwnerName || !World.InGame || World.Paused)
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
            Repeat(ref _keyHeld[i], Keyboard.IsDown(RepeatKeys[i]) && Native.GameInFront, dt, () => Press(RepeatKeys[i], true));
    }

    private static void Repeat(ref float held, bool down, float dt, Action again)
    {
        if (!down)
        {
            held = -1f;
            return;
        }

        if (held < 0f)
        {
            held = 0f;
            return;
        }

        held += dt;
        if (held < RepeatDelay)
            return;

        held -= RepeatRate;
        again();
    }

    // The controller in the deck. A selects, B goes back, the bumpers turn pages and X resets.
    public static void PadFrame(float dt)
    {
        if (!IsOpen || !Pad.Connected)
            return;

        // Whatever opened the deck is still held. Nothing counts until it's let go.
        if (_padWaitRelease)
        {
            if (Pad.Held == PadButton.None && !Pad.MenuDown(PadButton.Up) && !Pad.MenuDown(PadButton.Down) && !Pad.MenuDown(PadButton.Left) && !Pad.MenuDown(PadButton.Right))
                _padWaitRelease = false;
            return;
        }

        if (Binding is PadBind bind)
        {
            // Start cancels, since every other button can be bound.
            foreach (PadButton pressed in Pad.NewPresses())
            {
                if (pressed == PadButton.Start)
                {
                    Binding = null;
                    return;
                }

                if (Glyphs.Bindable(pressed))
                {
                    TakeButton(bind, pressed);
                    return;
                }
            }

            return;
        }

        if (Wheel.Chord != PadButton.None && Pad.Pressed(Wheel.Chord))
        {
            Close();
            return;
        }

        if (Pad.Pressed(PadButton.A))
            Press(Keys.Enter, false);
        else if (Pad.Pressed(PadButton.B))
            Press(Keys.Back, false);
        else if (Pad.Pressed(PadButton.LB))
            Press(Keys.PageUp, false);
        else if (Pad.Pressed(PadButton.RB))
            Press(Keys.PageDown, false);
        else if (Pad.Pressed(PadButton.X))
            Press(Keys.Delete, false);

        if (!IsOpen || Binding != null)
            return;

        for (int i = 0; i < RepeatButtons.Length; i++)
        {
            PadButton b = RepeatButtons[i];
            if (Pad.MenuPressed(b))
            {
                _padHeld[i] = 0f;
                Press(RepeatKeys[i], false);
                continue;
            }

            if (Pad.MenuDown(b))
                Repeat(ref _padHeld[i], true, dt, () => Press(RepeatKeys[i], true));
            else
                _padHeld[i] = -1f;

            if (!IsOpen)
                return;
        }
    }

    public static void Press(Keys key, bool repeat)
    {
        if (!IsOpen)
            return;

        if (Binding is Bind binding)
        {
            if (repeat)
                return;
            if (key is Keys.Back or Keys.Escape)
            {
                Binding = null;
                return;
            }

            TakeKey(binding, key);
            return;
        }

        if (Binding is PadBind)
        {
            if (key is Keys.Back or Keys.Escape)
                Binding = null;
            return;
        }

        if (key != Keys.Enter)
            Confirming = null;

        if (key == Controls.Key(Act.Deck) && !repeat)
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
            case Keys.Enter when !repeat:
                if (Level == 0)
                    Level = 1;
                else if (Selected is { } o)
                    Activate(o);
                break;
            case Keys.Back when !repeat:
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
            case Keys.Home when Level == 1:
                RowIndex = 0;
                break;
            case Keys.End when Level == 1:
                RowIndex = Math.Max(0, Current.Options.Count - 1);
                break;
            case Keys.Delete when !repeat && Level == 1 && Selected is { } d && d is not Command && !d.IsDefault:
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
        if (n > 0)
            RowIndex = (RowIndex + dir + n) % n;
    }

    private static void Activate(Option o)
    {
        switch (o)
        {
            case Command command:
                if (command.Confirm && Confirming != command)
                {
                    Confirming = command;
                    _confirmLeft = ConfirmSeconds;
                    return;
                }

                Confirming = null;
                Safe.Run(command.Id, () => command.Press());
                Flash(command);
                break;
            case Bind or PadBind:
                Binding = o;
                Swapped = null;
                _padWaitRelease = Controls.PadActive;
                break;
            default:
                if (o.Press())
                    Changed(o);
                break;
        }
    }

    // A key already on another action swaps over to the one being bound, so nothing is left without a key.
    private static void TakeKey(Bind bind, Keys key)
    {
        if (Array.IndexOf(NavKeys, key) >= 0 || key is Keys.Escape)
        {
            Swapped = "THE DECK USES THAT KEY";
            return;
        }

        if (!bind.Mouse && key is Keys.LButton or Keys.RButton or Keys.MButton)
        {
            Swapped = "MOUSE BUTTONS ARE FOR FLIGHT KEYS";
            return;
        }

        Binding = null;
        if (key == bind.Key)
            return;

        Swapped = null;
        foreach (Bind other in Opt.KeyBinds.Values)
        {
            if (other == bind || other.Key != key)
                continue;

            // A hotkey can't take a mouse button, so it can't be handed this one's mouse button either.
            if (!other.Mouse && bind.Key is Keys.LButton or Keys.RButton or Keys.MButton)
            {
                Swapped = "THAT KEY IS TAKEN BY " + other.Label;
                Binding = bind;
                return;
            }

            other.Key = bind.Key;
            Changed(other);
            Swapped = other.Label + " MOVED TO " + Bind.KeyName(other.Key);
        }

        bind.Key = key;
        Changed(bind);
    }

    private static void TakeButton(PadBind bind, PadButton button)
    {
        Binding = null;
        if (button == bind.Button)
            return;

        Swapped = null;
        foreach (PadBind other in Opt.PadBinds.Values)
        {
            if (other == bind || other.Button != button)
                continue;

            other.Button = bind.Button;
            Changed(other);
            Swapped = other.Label + " MOVED TO " + Glyphs.Name(other.Button);
        }

        bind.Button = button;
        Changed(bind);
        _padWaitRelease = true;
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
