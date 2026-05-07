using AgentBlazor.Execution;

namespace AgentBlazor.Components.Chat;

internal static class ExecutionStepNarrativeFormatter
{
    public static IReadOnlyList<string> BuildResultTexts(AgentExecutionStep step)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(step.Message))
        {
            lines.Add(step.Message!);
        }
        else
        {
            switch (step.Status)
            {
                case AgentExecutionStepStatus.ApprovalRequired:
                    lines.Add("Awaiting approval before execution.");
                    break;
                case AgentExecutionStepStatus.Queued:
                    lines.Add("Execution was queued and is waiting for a matching UI component.");
                    break;
                case AgentExecutionStepStatus.Blocked:
                    lines.Add("Execution was blocked.");
                    break;
                case AgentExecutionStepStatus.Failed:
                    lines.Add("Execution failed.");
                    break;
                case AgentExecutionStepStatus.NeedsClarification:
                    lines.Add("More information is required before execution can continue.");
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(step.PolicyDecision.Reason) &&
            step.Status is AgentExecutionStepStatus.ApprovalRequired or AgentExecutionStepStatus.Blocked or AgentExecutionStepStatus.Failed)
        {
            lines.Add($"Policy: {step.PolicyDecision.Reason}");
        }

        if (step.Warnings is { Count: > 0 })
        {
            foreach (var warning in step.Warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning)))
            {
                lines.Add($"Warning: {warning}");
            }
        }

        if (step.NextActions is { Count: > 0 })
        {
            foreach (var nextAction in step.NextActions.Where(static action => !string.IsNullOrWhiteSpace(action)))
            {
                lines.Add($"Next: {nextAction}");
            }
        }

        if (step.Outputs is { Count: > 0 })
        {
            foreach (var output in step.Outputs.Take(4))
            {
                lines.Add($"Output: {output.Key}={FormatOutputValue(output.Value)}");
            }
        }

        return lines;
    }

    private static string FormatOutputValue(object? value)
    {
        return value switch
        {
            null => "(null)",
            string text when string.IsNullOrWhiteSpace(text) => "\"\"",
            string text => text,
            IReadOnlyCollection<string> strings => string.Join(", ", strings.Take(4)),
            IEnumerable<string> strings => string.Join(", ", strings.Take(4)),
            System.Collections.IEnumerable sequence when value is not string =>
                string.Join(", ", sequence.Cast<object?>().Take(4).Select(FormatScalarValue)),
            _ => FormatScalarValue(value)
        };
    }

    private static string FormatScalarValue(object? value) =>
        value switch
        {
            null => "(null)",
            DateTimeOffset dto => dto.ToString("O"),
            DateTime dt => dt.ToString("O"),
            _ => value.ToString() ?? string.Empty
        };
}
