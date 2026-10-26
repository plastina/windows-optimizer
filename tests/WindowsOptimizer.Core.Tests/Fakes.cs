using WindowsOptimizer.Core;

namespace WindowsOptimizer.Core.Tests;

/// <summary>In-memory registry: keys are compared case-insensitively, like the real one.</summary>
public sealed class FakeRegistry : IRegistryAccess
{
    private readonly Dictionary<(RegHive, string), Dictionary<string, RegValue>> _keys = new();

    private static (RegHive, string) Id(RegHive hive, string subKey) => (hive, subKey.ToLowerInvariant());

    public void AddKey(RegHive hive, string subKey) => _keys.TryAdd(Id(hive, subKey), new(StringComparer.OrdinalIgnoreCase));

    public bool KeyExists(RegHive hive, string subKey) => _keys.ContainsKey(Id(hive, subKey));

    public RegValue? GetValue(RegHive hive, string subKey, string name) =>
        _keys.TryGetValue(Id(hive, subKey), out var values) && values.TryGetValue(name, out var v) ? v : null;

    public void SetValue(RegHive hive, string subKey, string name, RegValue value)
    {
        AddKey(hive, subKey);
        _keys[Id(hive, subKey)][name] = value;
    }

    public void DeleteValue(RegHive hive, string subKey, string name)
    {
        if (_keys.TryGetValue(Id(hive, subKey), out var values)) values.Remove(name);
        if (_binary.TryGetValue(Id(hive, subKey), out var bytes)) bytes.Remove(name);
    }

    public void DeleteKeyIfEmpty(RegHive hive, string subKey)
    {
        var id = Id(hive, subKey);
        if (!_keys.TryGetValue(id, out var values) || values.Count > 0) return;
        var hasChildren = _keys.Keys.Any(k => k.Item1 == hive && k.Item2.StartsWith(id.Item2 + "\\", StringComparison.Ordinal));
        if (!hasChildren) _keys.Remove(id);
    }

    public void DeleteKeyTree(RegHive hive, string subKey)
    {
        var id = Id(hive, subKey);
        foreach (var key in _keys.Keys.Where(k => k.Item1 == hive && (k.Item2 == id.Item2 || k.Item2.StartsWith(id.Item2 + "\\", StringComparison.Ordinal))).ToList())
            _keys.Remove(key);
    }

    public void ClearValues(RegHive hive, string subKey)
    {
        if (_keys.TryGetValue(Id(hive, subKey), out var values)) values.Clear();
        _binary.Remove(Id(hive, subKey));
    }

    private readonly Dictionary<(RegHive, string), Dictionary<string, byte[]>> _binary = new();

    public IReadOnlyList<string> GetValueNames(RegHive hive, string subKey)
    {
        var id = Id(hive, subKey);
        var names = new List<string>();
        if (_keys.TryGetValue(id, out var values)) names.AddRange(values.Keys);
        if (_binary.TryGetValue(id, out var bytes)) names.AddRange(bytes.Keys);
        return names;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string subKey)
    {
        var prefix = subKey.ToLowerInvariant() + "\\";
        return _keys.Keys.Concat(_binary.Keys)
            .Where(k => k.Item1 == hive && k.Item2.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k.Item2[prefix.Length..].Split('\\')[0])
            .Distinct()
            .ToList();
    }

    public byte[]? GetBinary(RegHive hive, string subKey, string name) =>
        _binary.TryGetValue(Id(hive, subKey), out var values) && values.TryGetValue(name, out var v) ? v : null;

    public void SetBinary(RegHive hive, string subKey, string name, byte[] data)
    {
        AddKey(hive, subKey);
        var id = Id(hive, subKey);
        if (!_binary.TryGetValue(id, out var values)) _binary[id] = values = new(StringComparer.OrdinalIgnoreCase);
        values[name] = data;
    }
}

/// <summary>Records commands instead of running them.</summary>
public sealed class FakeRunner : ICommandRunner
{
    public List<string> Commands { get; } = new();

    public CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000)
    {
        Commands.Add($"{fileName} {arguments}");
        return new CommandResult(0, "", "");
    }

    public CommandResult RunPowerShell(string script, int timeoutMs = 300_000)
    {
        Commands.Add("powershell " + script);
        return new CommandResult(0, "", "");
    }
}

public sealed class TestEnvironment : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WindowsOptimizerTests", Guid.NewGuid().ToString("N"));

    public TestEnvironment()
    {
        Registry = new FakeRegistry();
        Runner = new FakeRunner();
        Context = new TweakContext(Registry, new StateStore(Path.Combine(_root, "state")), Runner, _root);
    }

    public FakeRegistry Registry { get; }
    public FakeRunner Runner { get; }
    public TweakContext Context { get; }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
