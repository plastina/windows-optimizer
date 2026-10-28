using Microsoft.Win32;

namespace WindowsOptimizer.Core;

public enum RegHive { CurrentUser, LocalMachine, Users }

public enum RegKind { DWord, String }

public readonly record struct RegValue(RegKind Kind, object Data)
{
    public static RegValue DWord(int value) => new(RegKind.DWord, value);
    public static RegValue Str(string value) => new(RegKind.String, value);

    public override string ToString() => Kind == RegKind.String ? $"\"{Data}\"" : Data.ToString() ?? "";
}

/// <summary>Registry operations the tweaks need, behind an interface so they can be tested without touching the machine.</summary>
public interface IRegistryAccess
{
    bool KeyExists(RegHive hive, string subKey);
    RegValue? GetValue(RegHive hive, string subKey, string name);
    void SetValue(RegHive hive, string subKey, string name, RegValue value);
    void DeleteValue(RegHive hive, string subKey, string name);
    void DeleteKeyIfEmpty(RegHive hive, string subKey);
    void DeleteKeyTree(RegHive hive, string subKey);
    void ClearValues(RegHive hive, string subKey);

    /// <summary>Names of the values in a key (empty when the key does not exist).</summary>
    IReadOnlyList<string> GetValueNames(RegHive hive, string subKey);

    /// <summary>Names of the sub-keys of a key (empty when the key does not exist).</summary>
    IReadOnlyList<string> GetSubKeyNames(RegHive hive, string subKey);

    byte[]? GetBinary(RegHive hive, string subKey, string name);
    void SetBinary(RegHive hive, string subKey, string name, byte[] data);
}

public sealed class WindowsRegistry : IRegistryAccess
{
    private static RegistryKey Root(RegHive hive) =>
        hive switch
        {
            RegHive.CurrentUser => Registry.CurrentUser,
            RegHive.Users => Registry.Users,
            _ => Registry.LocalMachine,
        };

    public bool KeyExists(RegHive hive, string subKey)
    {
        using var key = Root(hive).OpenSubKey(subKey);
        return key is not null;
    }

    public RegValue? GetValue(RegHive hive, string subKey, string name)
    {
        using var key = Root(hive).OpenSubKey(subKey);
        if (key is null) return null;

        var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (data is null) return null;

        return key.GetValueKind(name) switch
        {
            RegistryValueKind.DWord => RegValue.DWord(Convert.ToInt32(data)),
            RegistryValueKind.String or RegistryValueKind.ExpandString => RegValue.Str((string)data),
            _ => null,
        };
    }

    public void SetValue(RegHive hive, string subKey, string name, RegValue value)
    {
        using var key = Root(hive).CreateSubKey(subKey, writable: true);
        key.SetValue(
            name,
            value.Data,
            value.Kind == RegKind.DWord ? RegistryValueKind.DWord : RegistryValueKind.String);
    }

    public void DeleteValue(RegHive hive, string subKey, string name)
    {
        using var key = Root(hive).OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKeyIfEmpty(RegHive hive, string subKey)
    {
        bool empty;
        using (var key = Root(hive).OpenSubKey(subKey))
        {
            if (key is null) return;
            empty = key.SubKeyCount == 0 && key.ValueCount == 0;
        }

        if (empty) Root(hive).DeleteSubKey(subKey, throwOnMissingSubKey: false);
    }

    public void DeleteKeyTree(RegHive hive, string subKey) =>
        Root(hive).DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);

    public void ClearValues(RegHive hive, string subKey)
    {
        using var key = Root(hive).OpenSubKey(subKey, writable: true);
        if (key is null) return;
        foreach (var name in key.GetValueNames()) key.DeleteValue(name, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetValueNames(RegHive hive, string subKey)
    {
        using var key = Root(hive).OpenSubKey(subKey);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string subKey)
    {
        using var key = Root(hive).OpenSubKey(subKey);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public byte[]? GetBinary(RegHive hive, string subKey, string name)
    {
        using var key = Root(hive).OpenSubKey(subKey);
        if (key is null || key.GetValue(name) is null || key.GetValueKind(name) != RegistryValueKind.Binary) return null;
        return (byte[])key.GetValue(name)!;
    }

    public void SetBinary(RegHive hive, string subKey, string name, byte[] data)
    {
        using var key = Root(hive).CreateSubKey(subKey, writable: true);
        key.SetValue(name, data, RegistryValueKind.Binary);
    }
}
