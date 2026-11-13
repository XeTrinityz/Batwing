using System.Numerics;
using System.Text;
using BmSDK;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private Every _agree;
    private Every _streamLog;

    private static int Agree(string cell)
    {
        if (Fallback.Running || World.WorldInfo is not { } wi)
            return 0;

        int changed = 0;
        TArray<LevelStreaming> all = wi.StreamingLevels;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is not { IsValid: true } ls || ls.LoadedLevel is not { IsValid: true })
                continue;
            if (!(ls.PackageName.ToString() ?? "").Equals(cell, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ls.bShouldBeLoaded && ls.bShouldBeVisible)
                continue;

            ls.bShouldBeLoaded = true;
            ls.bShouldBeVisible = true;
            changed++;
        }

        if (changed > 0)
            Log.Info(Title + ": " + cell + " already loaded from an earlier request, requested again.");
        return changed;
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
                string pkg = ls.PackageName.ToString() ?? "";
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

    private static bool Shown(string cell)
    {
        if (World.WorldInfo is not { } wi)
            return false;

        TArray<LevelStreaming> all = wi.StreamingLevels;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is { IsValid: true } ls && ls.bIsVisible && ls.LoadedLevel is { IsValid: true }
                && (ls.PackageName.ToString() ?? "").Equals(cell, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private const float LeadTime = 1.5f;
    private const float LeadFrom = 1500f;
    private const int LowRoom = 90000;

    private Every _collect;

    // Enters the loaded district volume nearest to where we'll be in a moment. Only volumes near Batman are
    // loaded, so when we outrun them this steps to the edge one, which loads its neighbours, which are closer,
    // and so on until we're back inside. Asking for districts by name loaded too much at once and filled
    // the game's object table, which is a hard crash.
    private void Lead()
    {
        // Streaming calls from inside a draw can land in the middle of the game's own streaming work.
        if (Fallback.Running)
            return;

        // The game dies when its 785,000 object table fills, and fast flight streams districts in quicker
        // than it collects the old ones. Low on room, stop leading and ask for a collection.
        int room = ObjectTable.Headroom;
        bool tight = room < LowRoom;
        if (tight && _collect.Elapsed(5.0) && World.WorldInfo is { } wi)
        {
            Log.Info(Title + ": object table nearly full (" + ObjectTable.Describe() + "), asking for a garbage collection.");
            Safe.Run("batwing collect", () => wi.ForceGarbageCollection(false));
        }

        Vector3 target = _fm.Position;
        if (!tight && _fm.Velocity.LengthSquared() > LeadFrom * LeadFrom)
            target += _fm.Velocity * LeadTime;

        string? cell = Districts.Scan(target) ?? Districts.NearestLoaded(target);
        if (cell == null || string.Equals(cell, _cell, StringComparison.OrdinalIgnoreCase))
            return;

        // Only remember the cell once it's entered, so a held-off entry is tried again next frame.
        if (!Safe.Try("batwing district", () => Ground.Enter(cell), true))
            return;

        _cell = cell;
        Log.Detail(Title + ": over " + cell + (Districts.Scan(_fm.Position) is { } under && !under.Equals(cell, StringComparison.OrdinalIgnoreCase)
            ? " (leading, under us is " + under + ")."
            : Districts.Scan(target) == null ? " (nearest loaded, walking towards " + (Districts.Mapped(target) ?? "unknown") + ")." : "."));
    }

    private void KeepLoading()
    {
        if (_cell is { } cell && _agree.Elapsed(1.0))
            Safe.Run("batwing streaming", () => Agree(cell));
    }

    private void WaitLog(string? cell, bool force)
    {
        if (cell == null)
            return;

        Safe.Run("batwing streaming", () => Agree(cell));
        if (force || _streamLog.Elapsed(3.0))
        {
            Safe.Run("batwing streaming", () => Log.Info(Title + ": waiting over " + cell + " at " + Geo.Position(_fm.Position) + ", ground "
                + (_ground >= GroundReach ? "out of reach" : Fmt.Whole(_ground / Metre) + " m below") + ", " + Describe(cell) + "."));
        }
    }
}
