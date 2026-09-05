using System.IO;
using System.Text.Json;

namespace NetMeter;

public sealed class LimitsStore
{
    private readonly string _path;

    public LimitsStore(string? path = null)
    {
        _path = path ?? StoragePaths.LimitsPath;
    }

    public Dictionary<string, LimitsConfig> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, LimitsConfig>();
            return JsonSerializer.Deserialize<Dictionary<string, LimitsConfig>>(StoragePaths.ReadIfSafe(_path))
                ?? new Dictionary<string, LimitsConfig>();
        }
        catch
        {
            // Preserve the corrupted file for inspection instead of silently
            // discarding the user's configuration
            try
            {
                if (File.Exists(_path))
                    File.Move(_path, _path + ".bad", overwrite: true);
            }
            catch
            {
            }
            return new Dictionary<string, LimitsConfig>();
        }
    }

    public void Save(IReadOnlyDictionary<string, LimitsConfig> limits)
    {
        try
        {
            var json = JsonSerializer.Serialize(limits, new JsonSerializerOptions { WriteIndented = true });
            StoragePaths.AtomicWrite(_path, json);
        }
        catch (Exception ex)
        {
            DiagLog.Error($"LimitsStore.Save: {ex.Message}");
        }
    }
}