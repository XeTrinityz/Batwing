using System.Numerics;
using System.Text;
using BmSDK;
using BmSDK.Engine;

namespace Trinity.Batwing;

// Keeps the city loaded round Batman while he moves at Batwing speed. The game only loads the districts
// round the district volume Batman is in, so this enters the loaded volume nearest to where he'll be in a
// moment. When he has outrun them that steps to the edge one, which loads its neighbours, which are closer,
// and so on until he's back inside. Asking for districts by name loaded too much at once and filled the
// object table, which is a hard crash, so only already loaded volumes are ever entered.
internal sealed class Streamer
{
    private const float LeadTime = 1.5f;
    private const float LeadFrom = 1500f;

    private Every _step, _agree, _collect, _holdSaid, _waitSaid;
    private string? _movedTo;

    // The district last entered.
    public string? Cell { get; private set; }

    public void Reset()
    {
        Cell = null;
        _movedTo = null;
        _step.Force();
    }

    // Runs every flight step. It only does the work a few times a second.
    public void Step(Vector3 at, Vector3 velocity)
    {
        if (Cell is { } cell && _agree.Elapsed(1.0))
        {
            Safe.Run("streaming agree", () => Agree(cell));
            Safe.Run("streaming watch", () => Watch(cell));
        }

        if (_step.Elapsed(0.3))
            Lead(at, velocity);
    }

    public void Lead(Vector3 at, Vector3 velocity)
    {
        // Streaming calls from inside a draw can land in the middle of the game's own streaming work. While
        // paused nothing moves, so nothing needs loading.
        if (App.TickFromDraw || World.Paused)
            return;

        int room = ObjectTable.Headroom;
        bool tight = room < ObjectTable.Low;
        bool full = room < ObjectTable.Full;
        if (tight && _collect.Elapsed(5.0) && World.WorldInfo is { } wi)
        {
            Log.Info("Streaming: object table nearly full (" + ObjectTable.Describe() + "), asking for a garbage collection.");
            Safe.Run("streaming collect", () => wi.ForceGarbageCollection(full));
        }

        // Entering a district loads it and the ones next to it. With next to no room left that's what fills
        // the table, so the walk stops until the collection has made room.
        if (full)
        {
            if (_holdSaid.Elapsed(15.0))
            {
                Log.Info("Streaming: no room to load more of the city (" + ObjectTable.Describe() + "), holding the districts where they are.");
                Toasts.Notice("GAME NEAR ITS OBJECT LIMIT // CITY LOADING ON HOLD", false);
            }

            return;
        }

        Vector3 target = at;
        if (!tight && velocity.LengthSquared() > LeadFrom * LeadFrom)
            target += velocity * LeadTime;

        string? cell = Districts.Scan(target) ?? Districts.NearestLoaded(target);
        if (cell == null || string.Equals(cell, Cell, StringComparison.OrdinalIgnoreCase))
            return;

        // Only remembered once entered, so a held off entry is tried again next time.
        if (!Safe.Try("streaming enter", () => Ground.Enter(cell), true))
            return;

        Cell = cell;
        _movedTo = null;
        if (!Log.Diagnostics)
            return;

        string? under = Districts.Scan(at);
        Log.Detail("Streaming: over " + cell + (under != null && !under.Equals(cell, StringComparison.OrdinalIgnoreCase)
            ? " (leading, under us is " + under + ")"
            : Districts.Scan(target) == null ? " (nearest loaded, walking towards " + (Districts.Mapped(target) ?? "unknown") + ")" : "")
            + ", " + ObjectTable.Describe() + ".");
    }

    // The district loaded but the game left it switched off after an earlier request. Asked for again.
    private static void Agree(string cell)
    {
        if (App.TickFromDraw || World.WorldInfo is not { } wi)
            return;

        int changed = 0;
        TArray<LevelStreaming> all = wi.StreamingLevels;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is not { IsValid: true } ls || ls.LoadedLevel is not { IsValid: true })
                continue;
            if (!ls.PackageName.ToString().Equals(cell, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ls.bShouldBeLoaded && ls.bShouldBeVisible)
                continue;

            ls.bShouldBeLoaded = true;
            ls.bShouldBeVisible = true;
            changed++;
        }

        if (changed > 0)
            Log.Info("Streaming: " + cell + " already loaded from an earlier request, requested again.");
    }

    private void Watch(string cell)
    {
        if (!Log.Diagnostics || World.Ri is not { } ri)
            return;

        string now = ri.CurrentLevelVolume is { IsValid: true } v ? v.Level.ToString() : "none";
        if (now.Equals(cell, StringComparison.OrdinalIgnoreCase) || now == _movedTo)
            return;

        _movedTo = now;
        Log.Detail("Streaming: entered " + cell + " but the game's current district is now " + now + ".");
    }

    // The district under the point is still on its way in, or the game says the levels round Batman are.
    public static bool StillLoading(Vector3 at)
    {
        bool around = World.Ri is { } ri && Safe.Try("levels around player", () => ri.AreLevelsLoadedAroundPlayer(0f), fallback: true);
        if (!around)
            return true;

        return Districts.Under(at) is { } cell && !Safe.Try("district shown", () => Shown(cell), fallback: true);
    }

    private static bool Shown(string cell)
    {
        if (World.WorldInfo is not { } wi)
            return false;

        TArray<LevelStreaming> all = wi.StreamingLevels;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is { IsValid: true } ls && ls.bIsVisible && ls.LoadedLevel is { IsValid: true }
                && ls.PackageName.ToString().Equals(cell, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // A log line on what the streaming is doing round a spot that won't load.
    public void Waiting(Vector3 at, float ground, bool force)
    {
        if (Cell is not { } cell)
            return;

        Safe.Run("streaming agree", () => Agree(cell));
        if (force || _waitSaid.Elapsed(3.0))
            Safe.Run("streaming wait", () => Log.Info("Streaming: waiting over " + cell + " at " + Geo.Position(at) + ", ground "
                + (ground >= Probe.Reach ? "out of reach" : Fmt.Metres(ground) + " below") + ", " + Describe(cell) + "."));
    }

    private static string Describe(string cell)
    {
        var sb = new StringBuilder();
        if (World.Ri is { } ri)
        {
            FName name = Districts.NameOf(cell);
            sb.Append("loaded ").Append(Safe.Try("level loaded", () => ri.IsLevelLoaded(name, true)) ? "yes" : "no")
              .Append(", visible ").Append(Safe.Try("level visible", () => ri.IsLevelVisible(name, true)) ? "yes" : "no")
              .Append(", current ").Append(ri.CurrentLevel.ToString());
        }

        if (World.WorldInfo is { } wi)
        {
            TArray<LevelStreaming> all = wi.StreamingLevels;
            int shown = 0;
            for (int i = 0; i < all.Count && shown < 12; i++)
            {
                if (all[i] is not { IsValid: true } ls)
                    continue;
                string pkg = ls.PackageName.ToString();
                if (!pkg.StartsWith(cell, StringComparison.OrdinalIgnoreCase))
                    continue;

                sb.Append(shown == 0 ? "; requests " : ", ").Append(pkg).Append(" [")
                  .Append(ls.bShouldBeLoaded ? 'L' : '-').Append(ls.bShouldBeVisible ? 'V' : '-')
                  .Append(ls.bHasLoadRequestPending ? 'P' : '-').Append(ls.bIsVisible ? 'S' : '-')
                  .Append(ls.LoadedLevel is { IsValid: true } ? 'D' : '-').Append(']');
                shown++;
            }

            if (shown == 0)
                sb.Append("; no engine request for it at all");
        }

        return sb.ToString();
    }
}

// What lies under a point, measured straight down.
internal readonly record struct Probe(float Distance, Vector3 Point, Vector3 Normal)
{
    public const float Reach = 30000f;

    public bool Known => Distance < Reach;

    public static Probe Down(Actor tracer, Vector3 from)
    {
        if (Ground.Ray(tracer, from, from - new Vector3(0f, 0f, Reach), out Vector3 hit, out Vector3 n))
            return new Probe(from.Z - hit.Z, hit, n);
        return new Probe(Reach, from - new Vector3(0f, 0f, Reach), Vector3.UnitZ);
    }
}
