namespace WindowsOptimizer.Core;

/// <summary>
/// Disables a list of scheduled tasks. Tasks this Windows doesn't have are skipped;
/// Undo enables again only the tasks that were enabled before the first Apply.
/// </summary>
public sealed class ScheduledTaskTweak : TweakBase
{
    private const string Disabled = "Disabled";

    private readonly string[] _tasks;

    /// <param name="tasks">Full task paths, such as <c>\Microsoft\Windows\Feedback\Siuf\DmClient</c>.</param>
    public ScheduledTaskTweak(
        TweakContext ctx, string id, string name, string description,
        TweakCategory category, RiskLevel risk, params string[] tasks)
        : base(ctx, id, name, description, category, risk)
    {
        _tasks = tasks;
        NeedsAdmin = true;
    }

    public override TweakStatus GetStatus()
    {
        var states = ReadStates();
        if (states.Count == 0) return TweakStatus.Unknown;

        var disabled = states.Values.Count(s => s == Disabled);
        return disabled == states.Count ? TweakStatus.Applied
            : disabled == 0 ? TweakStatus.NotApplied
            : TweakStatus.Partial;
    }

    public override string? Fingerprint()
    {
        var states = ReadStates();
        return states.Count == 0 ? null : string.Join("; ", states.Select(s => $"{s.Key}={s.Value}"));
    }

    public override IReadOnlyList<PreviewLine> Preview() =>
        ReadStates().Select(s => new PreviewLine(s.Key, s.Value, Disabled)).ToList();

    public override TweakResult Apply()
    {
        var states = ReadStates();
        if (states.Count == 0) return TweakResult.Fail("None of these scheduled tasks exist on this PC.");

        // Keep the first snapshot: applying twice must never forget which tasks were enabled.
        if (Ctx.State.Load(Id) is null)
        {
            var snapshot = new TweakSnapshot { TweakId = Id };
            foreach (var (task, state) in states) snapshot.Data["task:" + task] = state;
            Ctx.State.Save(snapshot);
        }

        var result = Ctx.Runner.RunPowerShell(ForEachTask(states.Keys, "Disable-ScheduledTask"));
        return result.Success
            ? TweakResult.Ok($"{states.Count} scheduled task(s) disabled.")
            : TweakResult.Fail("Could not disable the tasks: " + result.Text);
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null) return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        var enabledBefore = snapshot.Data
            .Where(e => e.Key.StartsWith("task:", StringComparison.Ordinal) && e.Value != Disabled)
            .Select(e => e.Key[5..])
            .ToList();

        if (enabledBefore.Count > 0)
        {
            var result = Ctx.Runner.RunPowerShell(ForEachTask(enabledBefore, "Enable-ScheduledTask"));
            if (!result.Success) return TweakResult.Fail("Could not enable the tasks again: " + result.Text);
        }

        Ctx.State.Delete(Id);
        return TweakResult.Ok("Scheduled tasks restored.");
    }

    /// <summary>State of each task that exists, as Get-ScheduledTask reports it (Ready, Disabled, Running...).</summary>
    private Dictionary<string, string> ReadStates()
    {
        var result = Ctx.Runner.RunPowerShell(ForEachTask(_tasks,
            "$x = Get-ScheduledTask -ErrorAction SilentlyContinue", "; if ($x) { $t + '|' + $x.State }"));

        // The exit code is ignored on purpose: PowerShell exits with 1 when the last task looked up doesn't exist,
        // even though the lines for the other tasks were printed.
        var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bar = line.LastIndexOf('|');
            if (bar > 0) states[line[..bar]] = line[(bar + 1)..];
        }
        return states;
    }

    /// <summary>A script that splits each task path into folder and name and passes them to <paramref name="command"/>.</summary>
    private static string ForEachTask(IEnumerable<string> tasks, string command, string after = "| Out-Null")
    {
        var list = string.Join(",", tasks.Select(t => "'" + t.Replace("'", "''") + "'"));
        return $$"""
            foreach ($t in @({{list}})) {
                $i = $t.LastIndexOf('\')
                {{command}} -TaskPath $t.Substring(0, $i + 1) -TaskName $t.Substring($i + 1) {{after}}
            }
            """;
    }
}
