# Recoverable Capability Errors

AgentBlazor sends `CapabilityResult` back to the model after a tool call. If an action cannot run, return a result the model can recover from instead of throwing raw exceptions or burying instructions in prose.

## Convention

- `Summary`: say why the current action did not complete.
- `NextActions`: tell the model exactly what to do next.
- `Warnings`: use only for non-fatal caveats.
- `Outputs`: include compact machine-readable metadata such as `errorCode`, `parameterName`, and `expectedShape`.
- `NeedsClarification`: use only when the user must provide missing information.

## Invalid Arguments

Use `InvalidArguments` when the model supplied values but the requested operation is not valid.

```csharp
[AgentAction("Find orders in a date range")]
public CapabilityResult FindOrders(DateOnly startDate, DateOnly endDate)
{
    if (endDate < startDate)
    {
        return CapabilityResult
            .InvalidArguments("The end date must be on or after the start date.")
            .WithOutput("errorCode", "invalid_date_range")
            .WithOutput("parameterName", "endDate")
            .WithOutput("expectedShape", "date on or after startDate")
            .WithNextAction("Retry with an endDate that is on or after startDate.");
    }

    return CapabilityResult.Success("Found matching orders.");
}
```

## Missing User Input

Use `MissingArgument` when the user needs to provide a required value.

```csharp
return CapabilityResult.MissingArgument(
    parameterName: "ticketId",
    expectedShape: "a ticket ID such as TCK-1042",
    actionId: "support_inbox.draft_ticket_reply");
```

This sets `RequiresClarification`, so the chat surface can ask the user for the missing value.

## Invalid Shape

Use `InvalidArgumentShape` when the model can repair the call without asking the user.

```csharp
return CapabilityResult.InvalidArgumentShape(
    parameterName: "ticketId",
    expectedShape: "a string such as TCK-1042",
    actualShape: "object",
    actionId: "support_inbox.draft_ticket_reply");
```

The runtime also returns this automatically when a reflected `[AgentAction]` receives an argument that cannot be bound to the method parameter type.

## Recoverable Failures

Use `RecoverableFailure` for validation-style failures from inside the method body.

```csharp
return CapabilityResult
    .RecoverableFailure("The ticket is blocked until billing evidence is attached.")
    .WithOutput("errorCode", "missing_billing_evidence")
    .WithOutput("ticketId", ticketId)
    .WithNextAction("Ask for billing evidence before drafting the reply again.");
```

Unexpected exceptions are treated as terse failures by the runtime. Do not rely on exception text as the model-facing recovery path.

## Demo Reference

The hosted runtime-probe demo includes a simple structured-error path without approval or generated-UI noise:

```text
Run the structured error date range probe
```

The reflection registry rejects the call before invoking the method because the required `startDate` parameter is missing. The chat receives a structured `missing_argument` result with `parameterName=startDate`, an `expectedShape`, a clarification question, and a `NextActions` recovery hint. This is the reference pattern for binding-layer failures that should be recoverable by the model or clarified with the user instead of surfacing raw exception text.

For in-method validation, the same runtime-probe capability returns `errorCode=invalid_date_range` when `endDate` is earlier than `startDate`. Use that shape when your method body can validate supplied arguments but the requested operation is not valid.

## Runtime Wrapping

The reflection registry wraps common binding failures before the method runs:

- missing required parameter
- invalid scalar shape
- invalid enum value
- invalid array/object shape
- required `null`
- missing runtime context

The normalized execution plan carries `Summary`, `Warnings`, `NextActions`, and `Outputs` into the chat UI. The semantic capability tool result also returns the full serialized `CapabilityResult`, so the model sees the recovery hints.
