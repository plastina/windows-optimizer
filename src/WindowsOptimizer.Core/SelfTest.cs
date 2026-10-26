using System.Text;

namespace WindowsOptimizer.Core;

public enum SelfTestOutcome { Pass, Fail, Skip }

public sealed record SelfTestResult(string Id, string Name, SelfTestOutcome Outcome, string Detail);

/// <summary>
/// Applies every tweak for real, checks the state, then undoes it and checks the state went back.
/// Meant for a throwaway machine (Windows Sandbox or a VM), never for the user's own PC.
/// </summary>
public static class SelfTest
{
    public static IReadOnlyList<SelfTestResult> Run(
        IEnumerable<ITweak> tweaks, ISet<string>? skip = null, Action<string>? log = null)
    {
        var results = new List<SelfTestResult>();
        foreach (var tweak in tweaks)
        {
            var result = skip?.Contains(tweak.Id) == true
                ? new SelfTestResult(tweak.Id, tweak.Name, SelfTestOutcome.Skip, "Skipped on purpose.")
                : !tweak.IsAvailable()
                    ? new SelfTestResult(tweak.Id, tweak.Name, SelfTestOutcome.Skip, "Not available on this machine.")
                    : RunOne(tweak);

            log?.Invoke($"{result.Outcome,-4} {result.Id}: {result.Detail}");
            results.Add(result);
        }
        return results;
    }

    public static string ToMarkdown(IReadOnlyList<SelfTestResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("# Windows Optimizer self-test").AppendLine();
        text.AppendLine($"Machine: {Environment.MachineName} — {Environment.OSVersion.VersionString}").AppendLine();
        text.AppendLine(
            $"Result: {results.Count(r => r.Outcome == SelfTestOutcome.Pass)} passed, " +
            $"{results.Count(r => r.Outcome == SelfTestOutcome.Fail)} failed, " +
            $"{results.Count(r => r.Outcome == SelfTestOutcome.Skip)} skipped.").AppendLine();
        text.AppendLine("| Result | Tweak | Detail |");
        text.AppendLine("|---|---|---|");
        foreach (var r in results)
            text.AppendLine($"| {r.Outcome} | `{r.Id}` | {r.Detail.Replace("|", "\\|").ReplaceLineEndings(" ")} |");
        return text.ToString();
    }

    private static SelfTestResult RunOne(ITweak tweak)
    {
        SelfTestResult Done(SelfTestOutcome outcome, string detail) => new(tweak.Id, tweak.Name, outcome, detail);

        try
        {
            var before = tweak.GetStatus();
            var changes = tweak.Preview().Count;

            var apply = tweak.Apply();
            if (!apply.Success) return Done(SelfTestOutcome.Fail, $"Apply failed: {apply.Message}");

            var applied = tweak.GetStatus();
            if (tweak.Kind == TweakKind.Toggle && applied is not (TweakStatus.Applied or TweakStatus.Unknown))
                return Done(SelfTestOutcome.Fail, $"After applying, the status is {applied}, expected Applied.");

            if (!tweak.CanUndo())
                return Done(SelfTestOutcome.Pass, $"Ran ({apply.Message}). Nothing to undo.");

            var undo = tweak.Undo();
            if (!undo.Success) return Done(SelfTestOutcome.Fail, $"Undo failed: {undo.Message}");

            var after = tweak.GetStatus();
            if (before != TweakStatus.Unknown && after != before)
                return Done(SelfTestOutcome.Fail, $"After undoing, the status is {after}; it was {before}.");

            return Done(SelfTestOutcome.Pass,
                $"Before {before}, applied {applied}, after undo {after}. {changes} line(s) in \"what changes\".");
        }
        catch (Exception ex)
        {
            return Done(SelfTestOutcome.Fail, $"Exception: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
