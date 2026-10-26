using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsOptimizer.Core;

/// <summary>What a tweak found before changing it, so Undo can put it back.</summary>
public sealed class TweakSnapshot
{
    public string TweakId { get; set; } = "";
    public DateTimeOffset TakenAt { get; set; } = DateTimeOffset.Now;
    public List<RegistrySnapshotEntry> Registry { get; set; } = new();

    /// <summary>Free-form values for tweaks that change more than the registry (power plans, DNS, services...).</summary>
    public Dictionary<string, string> Data { get; set; } = new();
}

public sealed class RegistrySnapshotEntry
{
    public RegHive Hive { get; set; }
    public string SubKey { get; set; } = "";
    public string Name { get; set; } = "";
    public bool KeyExisted { get; set; }
    public bool ValueExisted { get; set; }
    public RegKind Kind { get; set; }
    public string? Text { get; set; }
    public int Number { get; set; }

    public RegValue? ToValue() =>
        !ValueExisted ? null
        : Kind == RegKind.String ? RegValue.Str(Text ?? "")
        : RegValue.DWord(Number);
}

/// <summary>One JSON file per tweak. A file exists only while the tweak is applied by this app.</summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public StateStore(string directory) => _directory = directory;

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WindowsOptimizer");

    private string PathFor(string id) => Path.Combine(_directory, id + ".json");

    public bool Exists(string id) => File.Exists(PathFor(id));

    public TweakSnapshot? Load(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<TweakSnapshot>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(TweakSnapshot snapshot)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(snapshot.TweakId);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, Json));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }
}
