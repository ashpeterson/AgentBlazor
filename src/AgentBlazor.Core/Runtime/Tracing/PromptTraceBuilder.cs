using System.Diagnostics;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Execution;
using AgentBlazor.Options;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Tracing;

/// <summary>
/// Fluent builder for constructing PromptTrace instances through the pipeline.
/// Thread-safe for use within a single request context.
/// </summary>
internal sealed class PromptTraceBuilder
{
    private readonly PromptTracingOptions _options;
    private readonly Stopwatch _totalStopwatch;
    private readonly Stopwatch _stageStopwatch;
    private readonly string _traceId;

    private PromptTraceEntry? _entry;
    private PromptTraceClassification? _classification;
    private PromptTraceResolution? _resolution;
    private PromptTracePlanning? _planning;
    private PromptTraceExecution? _execution;
    private PromptTraceResponse? _response;

    /// <summary>
    /// Creates a new trace builder.
    /// </summary>
    /// <param name="options">Tracing options.</param>
    public PromptTraceBuilder(IOptions<PromptTracingOptions>? options = null)
    {
        _options = options?.Value ?? new PromptTracingOptions();
        _traceId = GenerateTraceId();
        _totalStopwatch = Stopwatch.StartNew();
        _stageStopwatch = new Stopwatch();
    }

    /// <summary>
    /// Gets whether tracing is enabled.
    /// </summary>
    public bool IsEnabled => _options.Enabled;

    /// <summary>
    /// Gets the trace ID for this builder.
    /// </summary>
    public string TraceId => _traceId;

    /// <summary>
    /// Gets the total elapsed time since the builder was created.
    /// </summary>
    public TimeSpan TotalElapsed => _totalStopwatch.Elapsed;

    /// <summary>
    /// Records the entry point of the prompt.
    /// </summary>
    public PromptTraceBuilder RecordEntry(
        AgentTurnRequest request,
        string? agentName = null)
    {
        if (!IsEnabled) return this;

        _entry = new PromptTraceEntry
        {
            TraceId = _traceId,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = request.GetEffectiveSessionId(),
            UserId = request.GetEffectiveUserId(),
            UserMessage = TruncateContent(request.UserMessage),
            Context = request.Context?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase),
            AgentName = agentName
        };

        return this;
    }

    /// <summary>
    /// Records the entry point with explicit values.
    /// </summary>
    public PromptTraceBuilder RecordEntry(
        string userMessage,
        string? sessionId = null,
        string? userId = null,
        string? agentName = null,
        IDictionary<string, string>? context = null)
    {
        if (!IsEnabled) return this;

        _entry = new PromptTraceEntry
        {
            TraceId = _traceId,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            UserId = userId,
            UserMessage = TruncateContent(userMessage),
            Context = context?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase),
            AgentName = agentName
        };

        return this;
    }

    /// <summary>
    /// Starts timing a stage.
    /// </summary>
    public PromptTraceBuilder StartStage()
    {
        if (!IsEnabled) return this;
        _stageStopwatch.Restart();
        return this;
    }

    /// <summary>
    /// Gets the elapsed time for the current stage.
    /// </summary>
    public TimeSpan StageElapsed => _stageStopwatch.Elapsed;

    /// <summary>
    /// Records intent classification with explicit values.
    /// </summary>
    public PromptTraceBuilder RecordClassification(
        string method,
        string primaryIntent,
        float confidence,
        string? targetComponentId = null,
        string? targetActionId = null,
        IReadOnlyDictionary<string, object?>? extractedEntities = null)
    {
        if (!IsEnabled) return this;

        _classification = new PromptTraceClassification
        {
            Method = method,
            PrimaryIntent = primaryIntent,
            Confidence = confidence,
            TargetComponentId = targetComponentId,
            TargetActionId = targetActionId,
            ExtractedEntities = _options.CaptureExtractedEntities ? extractedEntities : null,
            Duration = _stageStopwatch.Elapsed
        };

        return this;
    }

    /// <summary>
    /// Records intent resolution with explicit values.
    /// </summary>
    public PromptTraceBuilder RecordResolution(
        string componentId,
        string actionId,
        string reason,
        IReadOnlyDictionary<string, object?>? arguments = null,
        bool requiresApproval = false)
    {
        if (!IsEnabled) return this;

        _resolution = new PromptTraceResolution
        {
            ComponentId = componentId,
            ActionId = actionId,
            Arguments = _options.CaptureActionArguments ? arguments : null,
            Reason = reason,
            RequiresApproval = requiresApproval,
            Duration = _stageStopwatch.Elapsed
        };

        return this;
    }

    /// <summary>
    /// Records action planning results.
    /// </summary>
    public PromptTraceBuilder RecordPlanning(
        IReadOnlyList<PlannedComponentAction> plannedActions,
        int toolCount = 0)
    {
        if (!IsEnabled) return this;

        var tracedActions = plannedActions.Select(action => new PromptTracePlannedAction
        {
            ComponentId = action.ComponentId,
            ActionId = action.ActionId,
            Reason = action.Reason,
            Arguments = _options.CaptureActionArguments ? action.Arguments : null
        }).ToArray();

        _planning = new PromptTracePlanning
        {
            PlannedActions = tracedActions,
            Duration = _stageStopwatch.Elapsed,
            ToolCount = toolCount
        };

        return this;
    }

    /// <summary>
    /// Records planning from a normalized execution plan.
    /// </summary>
    public PromptTraceBuilder RecordPlanning(
        AgentExecutionPlan executionPlan,
        int toolCount = 0)
    {
        if (!IsEnabled) return this;

        var tracedActions = executionPlan.Steps.Select(step => new PromptTracePlannedAction
        {
            ComponentId = step.TargetId,
            ActionId = step.ActionId,
            Reason = step.Message ?? $"{step.Kind} step",
            Arguments = _options.CaptureActionArguments ? step.Arguments : null
        }).ToArray();

        _planning = new PromptTracePlanning
        {
            PlannedActions = tracedActions,
            Duration = _stageStopwatch.Elapsed,
            ToolCount = toolCount
        };

        return this;
    }

    /// <summary>
    /// Records execution results.
    /// </summary>
    public PromptTraceBuilder RecordExecution(
        IReadOnlyList<ComponentActionExecutionResult> results,
        IReadOnlyList<TimeSpan>? individualDurations = null)
    {
        if (!IsEnabled) return this;

        var tracedResults = results.Select((result, index) => new PromptTraceExecutionResult
        {
            ComponentId = result.ComponentId,
            ActionId = result.ActionId,
            Succeeded = result.Succeeded,
            Outcome = result.Outcome,
            Message = result.Message,
            Duration = individualDurations is not null && index < individualDurations.Count
                ? individualDurations[index]
                : TimeSpan.Zero
        }).ToArray();

        _execution = new PromptTraceExecution
        {
            Results = tracedResults,
            TotalDuration = _stageStopwatch.Elapsed,
            SuccessCount = results.Count(r => r.Succeeded),
            FailureCount = results.Count(r => !r.Succeeded)
        };

        return this;
    }

    /// <summary>
    /// Records execution results from a normalized execution plan.
    /// </summary>
    public PromptTraceBuilder RecordExecution(AgentExecutionPlan executionPlan)
    {
        if (!IsEnabled) return this;

        var tracedResults = executionPlan.Steps.Select(step => new PromptTraceExecutionResult
        {
            ComponentId = step.TargetId,
            ActionId = step.ActionId,
            Succeeded = step.Status is AgentExecutionStepStatus.Completed,
            Outcome = MapOutcome(step.Status),
            Message = step.Message ?? string.Empty,
            Duration = TimeSpan.Zero
        }).ToArray();

        _execution = new PromptTraceExecution
        {
            Results = tracedResults,
            TotalDuration = _stageStopwatch.Elapsed,
            SuccessCount = tracedResults.Count(static result => result.Succeeded),
            FailureCount = tracedResults.Count(static result => !result.Succeeded)
        };

        return this;
    }

    /// <summary>
    /// Records the final response and completes the trace.
    /// </summary>
    public PromptTraceBuilder RecordResponse(
        string responseText,
        PromptTraceOutcome outcome,
        string? errorMessage = null)
    {
        if (!IsEnabled) return this;

        _totalStopwatch.Stop();

        _response = new PromptTraceResponse
        {
            ResponseText = TruncateContent(responseText),
            Outcome = outcome,
            TotalDuration = _totalStopwatch.Elapsed,
            ErrorMessage = errorMessage
        };

        return this;
    }

    /// <summary>
    /// Records a successful response.
    /// </summary>
    public PromptTraceBuilder RecordSuccess(string responseText)
    {
        var outcome = _execution?.FailureCount > 0
            ? PromptTraceOutcome.PartialSuccess
            : PromptTraceOutcome.Succeeded;

        return RecordResponse(responseText, outcome);
    }

    /// <summary>
    /// Records a failed response.
    /// </summary>
    public PromptTraceBuilder RecordFailure(string errorMessage)
    {
        return RecordResponse(errorMessage, PromptTraceOutcome.Failed, errorMessage);
    }

    /// <summary>
    /// Records a canceled response.
    /// </summary>
    public PromptTraceBuilder RecordCanceled()
    {
        return RecordResponse("Request was canceled", PromptTraceOutcome.Canceled);
    }

    /// <summary>
    /// Builds the complete PromptTrace.
    /// </summary>
    public PromptTrace? Build()
    {
        if (!IsEnabled || _entry is null) return null;

        // Ensure response is recorded if not already
        if (_response is null)
        {
            _totalStopwatch.Stop();
            _response = new PromptTraceResponse
            {
                ResponseText = "[Trace incomplete]",
                Outcome = PromptTraceOutcome.Error,
                TotalDuration = _totalStopwatch.Elapsed,
                ErrorMessage = "Trace was built without recording a response"
            };
        }

        return new PromptTrace
        {
            TraceId = _traceId,
            Entry = _entry,
            Classification = _classification,
            Resolution = _resolution,
            Planning = _planning,
            Execution = _execution,
            Response = _response
        };
    }

    private string TruncateContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        if (!_options.CaptureFullContent)
            return content.Length > 100 ? $"{content[..100]}..." : content;

        return content.Length > _options.MaxContentLength
            ? $"{content[.._options.MaxContentLength]}...[truncated]"
            : content;
    }

    private static string GenerateTraceId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var guid = Guid.NewGuid().ToString("N")[..12];
        return $"trace_{timestamp}_{guid}";
    }

    private static ActionOutcome MapOutcome(AgentExecutionStepStatus status)
        => status switch
        {
            AgentExecutionStepStatus.Completed => ActionOutcome.Applied,
            AgentExecutionStepStatus.ApprovalRequired => ActionOutcome.Blocked,
            AgentExecutionStepStatus.NeedsClarification => ActionOutcome.NeedsClarification,
            AgentExecutionStepStatus.Blocked => ActionOutcome.Blocked,
            AgentExecutionStepStatus.Failed => ActionOutcome.Failed,
            _ => ActionOutcome.Queued
        };
}
