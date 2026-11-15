using System.Text;

namespace Trinity.Batwing;

// settings.toml. The deck writes it a moment after a change, and edits made to the file by hand are read
// back in while the game runs.
internal static class Settings
{
    private const string FileName = "settings.toml";
    private const float SaveDelay = 0.75f;
    private const double PollSeconds = 1.0;

    private static DateTime _stamp = DateTime.MinValue;
    private static bool _dirty;
    private static float _saveLeft;
    private static Every _poll;
    private static string _lastError = "";

    private static string PathOf => Path.Combine(App.Dir, FileName);

    public static void MarkDirty()
    {
        _dirty = true;
        _saveLeft = SaveDelay;
    }

    public static void Tick(float dt)
    {
        if (_dirty)
        {
            _saveLeft -= dt;
            if (_saveLeft <= 0f)
                Flush();
            return;
        }

        if (_poll.Elapsed(PollSeconds))
            Load(first: false);
    }

    public static void Flush()
    {
        if (!_dirty)
            return;

        _dirty = false;
        try
        {
            string temp = PathOf + ".tmp";
            File.WriteAllText(temp, Compose());
            File.Move(temp, PathOf, overwrite: true);
            _stamp = File.GetLastWriteTimeUtc(PathOf);
        }
        catch (Exception ex)
        {
            Log.Info("Couldn't save " + FileName + ": " + ex.Message);
        }
    }

    public static void Load(bool first)
    {
        string path = PathOf;
        if (!File.Exists(path))
        {
            if (first)
            {
                MarkDirty();
                Flush();
                Log.Info("Made " + FileName + ".");
            }

            return;
        }

        DateTime stamp = File.GetLastWriteTimeUtc(path);
        if (!first && stamp == _stamp)
            return;

        TomlTable root;
        try
        {
            root = Tomlyn.TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path))!;
        }
        catch (Exception ex)
        {
            if (ex.Message != _lastError)
            {
                _lastError = ex.Message;
                Log.Info(FileName + " failed to load, keeping the current settings: " + ex.Message);
            }

            return;
        }

        _lastError = "";
        _stamp = stamp;
        int changed = 0;
        foreach (Option o in Opt.All)
        {
            if (!o.Saved)
                continue;

            string before = o.Value;
            o.Read(Section(root, o.Page.Key));
            if (o.Value == before)
                continue;

            changed++;
            if (!first)
                Safe.Run(o.Id + " change", () => o.Changed?.Invoke());
        }

        if (!first && changed > 0)
            Log.Info("Settings reloaded from " + FileName + " (" + changed + " changed).");
    }

    private static string Compose()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Batwing settings. The deck keeps this file up to date, and edits made here apply while you play.");
        sb.AppendLine("# Switches are true or false, dials are numbers, picks are names, keys are key names (F4, Y, Insert, MButton...).");
        foreach (Page page in Opt.Pages)
        {
            sb.AppendLine();
            sb.AppendLine("[" + page.Key + "]");
            foreach (Option o in page.Options)
            {
                if (o.Toml is not { } value)
                    continue;
                sb.AppendLine("# " + o.Label + ": " + o.Takes);
                sb.AppendLine(o.Id + " = " + value);
            }
        }

        return sb.ToString();
    }

    private static TomlTable Section(TomlTable root, string name) =>
        root.TryGetValue(name, out object? raw) && raw is TomlTable table ? table : new TomlTable();
}
