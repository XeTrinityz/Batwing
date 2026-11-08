using System.Numerics;
using System.Text.RegularExpressions;
using BmSDK;
using BmSDK.BmGame;

namespace Trinity.Batwing;

internal static class Districts
{
    public readonly record struct Cell(string Name, Vector3 Min, Vector3 Max);

    private const double RefreshSeconds = 2.0;

    // Main district boxes, taken from the game's district volumes. The game only keeps the volumes near
    // Batman loaded, so at Batwing speed we fly out of them and need this to know where we are.
    private static readonly Cell[] Map =
    {
        new("CityX_01", new(-106496f, -88064f, -253952f), new(-94208f, -75776f, 253952f)),
        new("CityX_02", new(-94208f, -69632f, -253952f), new(-81920f, -57344f, 253952f)),
        new("CityX_03", new(-94208f, -57344f, -253952f), new(-81920f, -16202f, 253952f)),
        new("CityX_04", new(-106496f, -88064f, -253952f), new(-94208f, -75776f, 253952f)),
        new("CityX_05", new(-106496f, -75776f, -253952f), new(-94208f, -63488f, 253952f)),
        new("CityX_06", new(-106496f, -63488f, -253952f), new(-94208f, -51200f, 253952f)),
        new("CityX_07", new(-106496f, -51200f, -253952f), new(-94208f, -3326f, 253952f)),
        new("CityX_08", new(-118784f, -94208f, -253952f), new(-106496f, -81920f, 253952f)),
        new("CityX_09", new(-118784f, -81920f, -253952f), new(-106496f, -69632f, 253952f)),
        new("CityX_10", new(-118784f, -69632f, -256f), new(-106496f, -57344f, 253952f)),
        new("CityX_11", new(-118784f, -57344f, -3648f), new(-106496f, -45056f, 253952f)),
        new("CityX_12", new(-118784f, -45056f, -253952f), new(-106496f, -3326f, 253952f)),
        new("CityX_13", new(-131072f, -106496f, -253952f), new(-118784f, -88064f, 253952f)),
        new("CityX_14", new(-131072f, -88064f, -253952f), new(-118784f, -75776f, 253952f)),
        new("CityX_15", new(-131072f, -75776f, -253952f), new(-118784f, -62144f, 253952f)),
        new("CityX_16", new(-131072f, -62144f, -253952f), new(-118784f, -51200f, 253952f)),
        new("CityX_17", new(-131072f, -51200f, -253952f), new(-118784f, -32768f, 253952f)),
        new("CityX_18", new(-150472f, -81920f, -253952f), new(-131072f, -69632f, 253952f)),
        new("CityX_19", new(-239093f, -69632f, -253952f), new(-131072f, -57344f, 253952f)),
        new("CityX_20", new(-223245f, -57344f, -253952f), new(-131072f, -35328f, 253952f)),
        new("CityX_28", new(-118784f, -106496f, -253952f), new(-106496f, -94208f, 253952f)),
        new("CityX_98", new(-158720f, -141248f, -253952f), new(-131072f, -81920f, 253952f)),
        new("CityY_01", new(-57344f, -151552f, -253952f), new(-45056f, -112640f, 253952f)),
        new("CityY_02", new(-57344f, -112640f, -253952f), new(-45056f, -100352f, 253952f)),
        new("CityY_03", new(-69632f, -157696f, -253952f), new(-57344f, -118784f, 253952f)),
        new("CityY_04", new(-69632f, -118784f, -253952f), new(-57344f, -106496f, 253952f)),
        new("CityY_05", new(-69632f, -106496f, -253952f), new(-57344f, -94208f, 253952f)),
        new("CityY_06", new(-69632f, -94208f, -253952f), new(-57344f, -81920f, 253952f)),
        new("CityY_07", new(-81920f, -157696f, -253952f), new(-69632f, -124928f, 253952f)),
        new("CityY_08", new(-81920f, -124928f, -253952f), new(-69632f, -112640f, 253952f)),
        new("CityY_09", new(-81920f, -112640f, -253952f), new(-69632f, -100352f, 253952f)),
        new("CityY_10", new(-81920f, -100352f, -253952f), new(-69632f, -88064f, 253952f)),
        new("CityY_11", new(-81920f, -88064f, -253952f), new(-69632f, -75776f, 253952f)),
        new("CityY_12", new(-94208f, -155648f, -253952f), new(-81920f, -118784f, 253952f)),
        new("CityY_13", new(-94208f, -118784f, -253952f), new(-81920f, -106496f, 253952f)),
        new("CityY_14", new(-94208f, -106496f, -253952f), new(-81920f, -94208f, 253952f)),
        new("CityY_15", new(-94208f, -94208f, -253952f), new(-81920f, -81920f, 253952f)),
        new("CityY_16", new(-106496f, -139264f, -253952f), new(-94208f, -112640f, 253952f)),
        new("CityY_17", new(-106496f, -112640f, -253952f), new(-94208f, -100352f, 253952f)),
        new("CityY_18", new(-106496f, -100352f, -253952f), new(-94208f, -88064f, 253952f)),
        new("CityY_19", new(-131072f, -141248f, -253952f), new(-106496f, -118784f, 253952f)),
        new("CityY_20", new(-131072f, -118784f, -253952f), new(-106496f, -106496f, 253952f)),
        new("CityZ_00", new(-45056f, -112640f, -253952f), new(-4480f, -94208f, 253952f)),
        new("CityZ_01", new(-45056f, -94208f, -253952f), new(-26624f, -81920f, 253952f)),
        new("CityZ_02", new(-45056f, -81920f, -253952f), new(-14266f, -69632f, 253952f)),
        new("CityZ_03", new(-45056f, -69632f, -253952f), new(-15872f, -57344f, 253952f)),
        new("CityZ_04", new(-45056f, -57344f, -253952f), new(-24576f, -45056f, 253952f)),
        new("CityZ_05", new(-45056f, -45056f, -253952f), new(-24576f, -34048f, 253952f)),
        new("CityZ_06", new(-57344f, -100352f, -253952f), new(-45056f, -88064f, 253952f)),
        new("CityZ_07", new(-57344f, -88064f, -253952f), new(-45056f, -75776f, 253952f)),
        new("CityZ_08", new(-57344f, -75776f, -253952f), new(-45056f, -63488f, 253952f)),
        new("CityZ_09", new(-57344f, -63488f, -253952f), new(-45056f, -51200f, 253952f)),
        new("CityZ_10", new(-57344f, -51200f, 1536f), new(-45056f, -38912f, 253952f)),
        new("CityZ_11", new(-57344f, -38912f, -253952f), new(-45056f, -22528f, 253952f)),
        new("CityZ_12", new(-69632f, -81920f, -253952f), new(-57344f, -69632f, 253952f)),
        new("CityZ_13", new(-69632f, -69632f, -253952f), new(-57344f, -57344f, 253952f)),
        new("CityZ_14", new(-69632f, -57344f, -253952f), new(-57344f, -45056f, 253952f)),
        new("CityZ_15", new(-69632f, -45056f, -253952f), new(-57344f, -24064f, 253952f)),
        new("CityZ_16", new(-81920f, -75776f, -253952f), new(-69632f, -63488f, 253952f)),
        new("CityZ_17", new(-81920f, -63488f, -253952f), new(-69632f, -51200f, 253952f)),
        new("CityZ_18", new(-81920f, -39040f, -253952f), new(-69632f, -26624f, 253952f)),
        new("CityZ_19", new(-81920f, -51200f, -253952f), new(-69632f, -39040f, 253952f)),
        new("CityZ_20", new(-45056f, -34048f, -253952f), new(-24576f, -128f, 253952f)),
        new("CityZ_21", new(-26624f, -94208f, -253952f), new(-14336f, -81920f, 253952f)),
    };

    private static readonly Dictionary<string, Cell> _map = Map.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    // Boxes of loaded volumes not in the map, the small special areas inside districts.
    private static readonly Dictionary<string, Cell> _seen = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, FName> _names = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(RLevelVolume Volume, string Name)> _volumes = new();
    private static Every _refresh;

    private static readonly Regex Main = new(@"^City[XYZ]_\d\d$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsDistrict(string name) => Main.IsMatch(name);

    public static string? At(Vector3 point)
    {
        Refresh();
        RLevelVolume? best = null;
        string? name = null;
        foreach ((RLevelVolume v, string n) in _volumes)
        {
            if (v is { IsValid: true } && v.EncompassesPoint(point) && (best == null || v.Priority > best.Priority))
            {
                best = v;
                name = n;
            }
        }

        return name;
    }

    public static string? Under(Vector3 point) => Scan(point) ?? Mapped(point);

    public static string? Scan(Vector3 point)
    {
        string? at = At(point);
        if (at != null)
            return at;

        RLevelVolume? best = null;
        string? name = null;
        foreach ((RLevelVolume v, string n) in _volumes)
        {
            if (!v.IsValid || !Box(n, out Cell c) || !Inside(c, point))
                continue;

            float z = Math.Clamp(point.Z, c.Min.Z + 50f, MathF.Max(c.Min.Z + 50f, c.Max.Z - 50f));
            if (v.EncompassesPoint(new Vector3(point.X, point.Y, z)) && (best == null || v.Priority > best.Priority))
            {
                best = v;
                name = n;
            }
        }

        return name;
    }

    // Boxes overlap where districts meet, so the smallest box holding the point is the best guess.
    public static string? Mapped(Vector3 point)
    {
        string? best = null;
        float bestArea = float.MaxValue;
        foreach (Cell c in Map)
        {
            if (!Inside(c, point))
                continue;

            float area = (c.Max.X - c.Min.X) * (c.Max.Y - c.Min.Y);
            if (area < bestArea)
            {
                bestArea = area;
                best = c.Name;
            }
        }

        return best;
    }

    // The loaded main district whose box is closest to the point, flat distance.
    public static string? NearestLoaded(Vector3 point)
    {
        Refresh();
        string? best = null;
        float bestDist = float.MaxValue;
        foreach ((RLevelVolume v, string n) in _volumes)
        {
            if (!v.IsValid || !IsDistrict(n) || !Box(n, out Cell c))
                continue;

            float dx = MathF.Max(0f, MathF.Max(c.Min.X - point.X, point.X - c.Max.X));
            float dy = MathF.Max(0f, MathF.Max(c.Min.Y - point.Y, point.Y - c.Max.Y));
            float d = dx * dx + dy * dy;
            if (d < bestDist)
            {
                bestDist = d;
                best = n;
            }
        }

        return best;
    }

    private static bool Box(string name, out Cell c) => _map.TryGetValue(name, out c) || _seen.TryGetValue(name, out c);

    private static bool Inside(Cell c, Vector3 p) => p.X >= c.Min.X && p.X <= c.Max.X && p.Y >= c.Min.Y && p.Y <= c.Max.Y;

    public static FName NameOf(string level)
    {
        if (!_names.TryGetValue(level, out FName name))
            _names[level] = name = new FName(level);
        return name;
    }

    public static RLevelVolume? Volume(string level)
    {
        Refresh();
        foreach ((RLevelVolume v, string name) in _volumes)
        {
            if (v is { IsValid: true } && name.Equals(level, StringComparison.OrdinalIgnoreCase))
                return v;
        }

        return null;
    }

    public static bool Outline(out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        foreach (Cell c in Map)
        {
            min = Vector3.Min(min, c.Min);
            max = Vector3.Max(max, c.Max);
        }

        return true;
    }

    public static void Forget()
    {
        _seen.Clear();
        _volumes.Clear();
        _refresh.Force();
    }

    private static void Refresh()
    {
        if (!_refresh.Elapsed(RefreshSeconds))
            return;

        try
        {
            _volumes.Clear();
            foreach (RLevelVolume v in World.All<RLevelVolume>())
            {
                string name = v.Level.ToString();
                if (name.Length == 0 || name == "None" || !name.StartsWith("City", StringComparison.OrdinalIgnoreCase))
                    continue;

                _volumes.Add((v, name));
                if (_map.ContainsKey(name) || _seen.ContainsKey(name))
                    continue;

                v.GetComponentsBoundingBox(out GameObject.FBox box);
                if (box.IsValid != 0)
                    _seen[name] = new Cell(name, box.Min, box.Max);
            }
        }
        catch (Exception ex)
        {
            Log.Throttled("districts", "Districts: failed to read district volumes: " + ex.Message);
        }
    }
}
