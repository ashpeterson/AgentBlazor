# AgentBlazor Component Discovery Analysis

**Date**: February 2026
**Purpose**: Comprehensive analysis of component discovery, metadata extraction, and recommendations for improvement based on industry best practices.

---

## Executive Summary

This document analyzes how AgentBlazor discovers components, extracts field/column metadata, and reports state to the LLM planner. Based on extensive research into AG-UI Protocol, Playwright/Selenium patterns, Microsoft.Extensions.AI, CAAP (Context-Aware Action Planning), and LLM UI grounding techniques, we identify critical gaps and provide actionable recommendations.

---

## Table of Contents

1. [Current Architecture](#1-current-architecture)
2. [Industry Best Practices Research](#2-industry-best-practices-research)
3. [Gap Analysis](#3-gap-analysis)
4. [Recommendations](#4-recommendations)
5. [Implementation Priority](#5-implementation-priority)

---

## 1. Current Architecture

### 1.1 Component Registration Flow

```
┌─────────────────────────────────────────────────────────────┐
│ Component Renders → OnInitialized()                         │
│     ↓                                                        │
│ IAgentComponentRegistry.Register(this)                      │
│     ↓                                                        │
│ IComponentRouteRegistry.Register(componentId, currentPath)  │
│     ↓                                                        │
│ Component ready for agent actions                           │
└─────────────────────────────────────────────────────────────┘
```

**Key Files:**
- `AgentControllableComponentBase.cs` (lines 44-56) - Registration lifecycle
- `InMemoryAgentComponentRegistry.cs` - Thread-safe storage
- `IAgentControllable.cs` - Core interface

### 1.2 Capability Announcement

Each component implements `GetCapability()` returning:

```csharp
ComponentCapability {
    ComponentId: string,
    Description: string,
    Actions: [
        ComponentActionCapability {
            ActionId: string,
            Description: string,
            RequiresApproval: bool,
            InputSchema: string (JSON Schema)
        }
    ]
}
```

**Source of Truth:** `AgentComponentCapabilityProfile.cs` defines all shipped capabilities.

### 1.3 State Reporting

Each component implements `GetCurrentState()` returning a `ComponentState` dictionary.

**AgentDataGrid State** (lines 137-174):
```csharp
{
    "rowCount": 150,
    "columns": ["Name", "Risk", "Status"],      // ✓ Discovered via reflection
    "columnAliases": { "Risk Level": "Risk" },  // ✓ Optional mapping
    "valueMappings": { "Risk": { "High": 70 }}, // ✓ Optional semantic mapping
    "sortColumn": "Name",
    "sortDirection": "asc",
    "filterColumn": null,
    "filterOperator": null,
    "filterValue": null,
    "currentPageIndex": 0,
    "pageSize": 10,
    "focusedRowKey": null
}
```

**AgentForm State** (lines 74-83):
```csharp
{
    "fieldCount": 5,    // ✗ Only count, not names
    "isValid": true     // ✗ No field details
}
```

### 1.4 Column/Field Resolution

`ComponentActionArgumentResolver.cs` resolves LLM-generated names to canonical names:

1. **Exact Match** - Direct property name match
2. **Alias Lookup** - Optional `ColumnAliases` parameter
3. **Token Discovery** - Splits identifiers into tokens, scores overlap

**Semantic Value Resolution:**
- App-provided `ValueMappings` (per-column)
- Built-in fallbacks: `high` → 70, `medium` → 50, `low` → 30
- Auto-promotes `eq` → `gte` for threshold semantics

---

## 2. Industry Best Practices Research

### 2.1 AG-UI Protocol State Management

**Source:** [AG-UI State Management](https://docs.ag-ui.com/concepts/state)

Key patterns:
- **State Schema Definition**: JSON-based schemas with type and description
- **Predictive State Config**: Maps state fields to tool arguments for streaming
- **State Injection**: Current state automatically injected as system messages
- **Snapshot + Delta**: Full snapshots for baseline, JSON Patch for increments

```python
# AG-UI State Schema Example
state_schema = {
    "recipe": {"type": "object", "description": "The current recipe"},
    "ingredients": {"type": "array", "description": "List of ingredients"}
}

predict_state_config = {
    "recipe": {"tool": "update_recipe", "tool_argument": "recipe"}
}
```

**Insight:** AG-UI explicitly defines what state fields exist and their types upfront.

### 2.2 Playwright Locator Strategies

**Source:** [Playwright Locators](https://playwright.dev/docs/locators)

Priority order for element discovery:
1. **Role locators** (`getByRole`) - Accessibility-first
2. **Test IDs** (`getByTestId`) - Most resilient
3. **Text content** (`getByText`)
4. **Label** (`getByLabel`)
5. **CSS/XPath** (last resort)

**Insight:** Test IDs and semantic roles are most reliable for automation.

### 2.3 LLM UI Grounding (CAAP, SoM)

**Sources:**
- [CAAP Paper](https://arxiv.org/html/2406.06947v2) - 94.4% accuracy with few-shot + CoT
- [Set-of-Mark](https://arxiv.org/abs/2310.11441) - Visual grounding with numeric IDs

Key techniques:
- **Element Enumeration**: Assign unique IDs to each interactable element
- **Coordinates + Descriptions**: Provide both location and semantic info
- **Few-shot Demonstrations**: Show action sequences from similar tasks

```json
// CAAP Action Format
{
    "action_1": {
        "name": "click_element",
        "arg": {"element_id": "btn_submit"},
        "reason": "Submit the form after validation"
    }
}
```

### 2.4 Syncfusion/Telerik DataGrid Patterns

**Sources:**
- [Syncfusion Columns](https://blazor.syncfusion.com/documentation/datagrid/columns)
- [Telerik Grid Binding](https://docs.telerik.com/blazor-ui/components/grid/columns/bound)

Column metadata patterns:
- **Data Annotations**: `[Display]`, `[DisplayFormat]`, `[Required]`
- **Dynamic Binding**: `ExpandoObject`, `DynamicObject` support
- **Field Property**: Maps to data source with type inference
- **Auto-generation**: Reflects on model properties

### 2.5 Microsoft.Extensions.AI Structured Output

**Source:** [Structured Output Quickstart](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/structured-output)

```csharp
// Type-safe structured output
var response = await chatClient.GetResponseAsync<SentimentResult>(prompt);

// Schema from type
ChatResponseFormat.ForJsonSchema<PersonInfo>(
    schemaName: "PersonInfo",
    schemaDescription: "Information about a person"
);
```

**Insight:** .NET supports native JSON schema generation from types.

### 2.6 Function Calling Schema Generation

**Source:** [Schema Generation for LLM Function Calling](https://medium.com/@wangxj03/schema-generation-for-llm-function-calling-5ab29cecbd49)

Python pattern using Pydantic:
```python
from pydantic import BaseModel, Field

class FilterAction(BaseModel):
    column: str = Field(description="Column to filter")
    operator: str = Field(description="Filter operator")
    value: Any = Field(description="Filter value")

# Auto-generates JSON schema from model
```

**Insight:** Type annotations + descriptions auto-generate rich schemas.

---

## 3. Gap Analysis

### 3.1 Critical Gaps

| Gap | Current State | Impact | Priority |
|-----|--------------|--------|----------|
| **Form fields not in state** | Only `fieldCount`, `isValid` | Agent cannot see form structure | **HIGH** |
| **No field types** | All treated as strings | Agent cannot validate input types | **HIGH** |
| **No validation rules** | Not exposed | Agent cannot enforce constraints | **MEDIUM** |
| **No column types** | DataGrid columns are strings | Wrong operator selection | **MEDIUM** |
| **No enum/allowed values** | Only via `ValueMappings` | Guessing valid values | **MEDIUM** |
| **No current route** | Not in planner context | Navigation confusion | **HIGH** |
| **No error state** | Components don't report errors | Agent cannot detect failures | **MEDIUM** |
| **No loading state** | No async status | Race conditions | **LOW** |

### 3.2 Comparison with AG-UI

| Feature | AG-UI | AgentBlazor | Gap |
|---------|-------|-------------|-----|
| State schema definition | ✓ Explicit | ✗ Implicit | Need schema |
| Predictive state updates | ✓ Streaming | ✗ None | Could add |
| State injection | ✓ System message | ✓ Context | OK |
| Bidirectional sync | ✓ Full | ✗ Read-only | Enhancement |
| Type safety | ✓ Pydantic/C# types | Partial | Need types |

### 3.3 Comparison with Playwright

| Feature | Playwright | AgentBlazor | Gap |
|---------|------------|-------------|-----|
| Role-based discovery | ✓ getByRole | ✗ None | Add ARIA roles |
| Test ID support | ✓ getByTestId | ~ AgentId | OK |
| Auto-waiting | ✓ Built-in | ✗ None | Add readiness |
| Locator quality | ✓ Scored | ✗ None | Add confidence |

---

## 4. Recommendations

### 4.1 Expose Form Field Metadata

**File:** `AgentForm.razor` `GetCurrentState()`

```csharp
public override ComponentState GetCurrentState()
{
    EnsureEditContext();
    var values = CaptureValues(Model);
    var fieldMetadata = GetFieldMetadata(Model);

    return new ComponentState
    {
        ["fieldCount"] = values.Count,
        ["isValid"] = _editContext!.Validate(),
        // NEW: Expose field names
        ["fields"] = values.Keys.ToArray(),
        // NEW: Expose current values
        ["fieldValues"] = values,
        // NEW: Expose field types and validation
        ["fieldMetadata"] = fieldMetadata
    };
}

private static Dictionary<string, FieldMetadata> GetFieldMetadata(object model)
{
    var result = new Dictionary<string, FieldMetadata>(StringComparer.OrdinalIgnoreCase);
    foreach (var prop in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!prop.CanRead || !prop.CanWrite) continue;

        result[prop.Name] = new FieldMetadata
        {
            Type = GetFriendlyTypeName(prop.PropertyType),
            IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null,
            MaxLength = prop.GetCustomAttribute<MaxLengthAttribute>()?.Length,
            MinLength = prop.GetCustomAttribute<MinLengthAttribute>()?.Length,
            Pattern = prop.GetCustomAttribute<RegularExpressionAttribute>()?.Pattern,
            AllowedValues = GetEnumValues(prop.PropertyType)
        };
    }
    return result;
}
```

### 4.2 Expose Column Types in DataGrid

**File:** `AgentDataGrid.razor` `GetCurrentState()`

```csharp
// In GetCurrentState(), add:
["columnTypes"] = GetColumnTypes(),

private Dictionary<string, string> GetColumnTypes()
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var prop in typeof(TItem).GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        result[prop.Name] = GetFriendlyTypeName(prop.PropertyType);
    }
    return result;
}

private static string GetFriendlyTypeName(Type type)
{
    if (type == typeof(string)) return "string";
    if (type == typeof(int) || type == typeof(long)) return "integer";
    if (type == typeof(decimal) || type == typeof(double) || type == typeof(float)) return "number";
    if (type == typeof(bool)) return "boolean";
    if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "datetime";
    if (type.IsEnum) return "enum";
    if (Nullable.GetUnderlyingType(type) is { } underlying)
        return GetFriendlyTypeName(underlying) + "?";
    return "object";
}
```

### 4.3 Add Current Route to Planner Context

**File:** `ActionPlanRequest` and `DeterministicAgentRuntime`

```csharp
// In ActionPlanRequest, add:
public string? CurrentRoute { get; init; }

// In DeterministicAgentRuntime.RunTurnAsync(), populate:
var planRequest = new ActionPlanRequest
{
    // ... existing
    CurrentRoute = GetCurrentRoute() // from NavigationManager or similar
};
```

**File:** `StructuredActionPlanner.BuildSystemPrompt()`

```csharp
// Add section:
if (!string.IsNullOrWhiteSpace(request.CurrentRoute))
{
    sb.AppendLine("# CURRENT LOCATION");
    sb.AppendLine($"User is currently on: **{request.CurrentRoute}**");
    sb.AppendLine();
}
```

### 4.4 Add Operator Compatibility by Type

**File:** `AgentComponentCapabilityProfile.cs`

```csharp
public static IReadOnlyDictionary<string, string[]> OperatorsByType { get; } = new Dictionary<string, string[]>
{
    ["string"] = ["eq", "neq", "contains", "startswith", "endswith", "isnull", "notnull"],
    ["integer"] = ["eq", "neq", "gt", "gte", "lt", "lte", "isnull", "notnull"],
    ["number"] = ["eq", "neq", "gt", "gte", "lt", "lte", "isnull", "notnull"],
    ["boolean"] = ["eq", "neq", "isnull", "notnull"],
    ["datetime"] = ["eq", "neq", "gt", "gte", "lt", "lte", "isnull", "notnull"],
    ["enum"] = ["eq", "neq", "in", "notin", "isnull", "notnull"]
};
```

Include in planner prompt when listing columns.

### 4.5 Add Few-Shot Examples from User's Domain

**File:** `StructuredActionPlanner.cs` or new `IPlannerExampleProvider`

Allow apps to inject domain-specific examples:

```csharp
public interface IPlannerExampleProvider
{
    IReadOnlyList<PlannerExample> GetExamples();
}

public record PlannerExample(
    string UserMessage,
    string Context,
    string ExpectedJson
);

// In BuildSystemPrompt, inject custom examples:
var customExamples = _exampleProvider?.GetExamples() ?? [];
foreach (var example in customExamples.Take(3))
{
    sb.AppendLine($"## Custom Example: {example.UserMessage}");
    // ... format like existing examples
}
```

### 4.6 Add Component Readiness/Error State

**File:** `IAgentControllable.cs`

```csharp
public interface IAgentControllable
{
    // Existing members...

    // NEW: Component health
    ComponentHealth GetHealth();
}

public record ComponentHealth(
    bool IsReady,
    bool IsLoading,
    string? ErrorMessage
);
```

### 4.7 Use Native Structured Output

**File:** `StructuredActionPlanner.cs`

Consider using `ChatClientStructuredOutputExtensions.GetResponseAsync<T>()`:

```csharp
// Define response type
public sealed class PlannerResponse
{
    public string? Reasoning { get; set; }
    public List<PlannerStep>? Steps { get; set; }
    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
}

// Use typed structured output (if provider supports it)
var response = await _chatClient.GetResponseAsync<PlannerResponse>(
    messages,
    new ChatOptions { Temperature = 0.0f },
    cancellationToken);
```

This guarantees JSON schema compliance at the API level.

---

## 5. Implementation Priority

### Phase 1: High Impact, Low Effort (Week 1)

1. **Expose form fields in state** - Simple change to `GetCurrentState()`
2. **Add current route to context** - Pass through existing NavigationManager
3. **Add column types to DataGrid state** - Reflection already exists

### Phase 2: Medium Impact (Week 2)

4. **Add field validation metadata** - Extract from data annotations
5. **Add operator compatibility guidance** - Static mapping in profile
6. **Add component health/error state** - New interface method

### Phase 3: Advanced (Week 3+)

7. **Domain-specific example injection** - New provider interface
8. **Native structured output** - Depends on provider support
9. **Predictive state updates** - AG-UI-style streaming

---

## 6. Research Sources

### Protocols & Frameworks
- [AG-UI Protocol Documentation](https://docs.ag-ui.com/)
- [AG-UI State Management](https://docs.ag-ui.com/concepts/state)
- [Microsoft Agent Framework AG-UI](https://learn.microsoft.com/en-us/agent-framework/integrations/ag-ui/state-management)

### LLM UI Agents
- [CAAP: Context-Aware Action Planning](https://arxiv.org/html/2406.06947v2)
- [Set-of-Mark Prompting](https://arxiv.org/abs/2310.11441)
- [GUI-Actor Visual Grounding](https://arxiv.org/pdf/2506.03143)

### Automation Testing
- [Playwright Locators](https://playwright.dev/docs/locators)
- [Microsoft UI Automation](https://en.wikipedia.org/wiki/Microsoft_UI_Automation)
- [Accessibility Tree](https://benmyers.dev/blog/accessibility-tree/)

### .NET & Blazor
- [Microsoft.Extensions.AI Structured Output](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/structured-output)
- [Blazor Component Discovery APIs](https://github.com/dotnet/aspnetcore/issues/49756)
- [Syncfusion DataGrid Columns](https://blazor.syncfusion.com/documentation/datagrid/columns)
- [Telerik Grid Binding](https://docs.telerik.com/blazor-ui/components/grid/columns/bound)

### Schema Generation
- [Schema Generation for LLM Function Calling](https://medium.com/@wangxj03/schema-generation-for-llm-function-calling-5ab29cecbd49)
- [ToolRegistry Protocol-Agnostic Library](https://arxiv.org/html/2507.10593v1)

---

## Appendix: Current File Locations

| Component | File Path |
|-----------|-----------|
| Component Base | `src/AgentBlazor.Components/Wrappers/AgentControllableComponentBase.cs` |
| DataGrid | `src/AgentBlazor.Components/Wrappers/AgentDataGrid.razor` |
| Form | `src/AgentBlazor.Components/Wrappers/AgentForm.razor` |
| Capability Profile | `src/AgentBlazor.Core/Components/AgentComponentCapabilityProfile.cs` |
| Argument Resolver | `src/AgentBlazor.Core/Runtime/Components/ComponentActionArgumentResolver.cs` |
| Planner | `src/AgentBlazor.Core/Runtime/Planning/StructuredActionPlanner.cs` |
| Runtime | `src/AgentBlazor.Core/Runtime/Planning/DeterministicAgentRuntime.cs` |
| Route Registry | `src/AgentBlazor.Core/Runtime/Routing/InMemoryRouteRegistry.cs` |
