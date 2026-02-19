# Specification References

Last verified: 2026-02-18

## External Specifications and Framework Sources

- AG-UI Concepts: Events  
  https://docs.ag-ui.com/concepts/events
- AG-UI JavaScript SDK Events  
  https://docs.ag-ui.com/sdk/js/events
- AG-UI core event schema source (`EventType`, `STATE_DELTA`, `TOOL_CALL_RESULT`)  
  https://github.com/ag-ui-protocol/ag-ui/blob/main/sdks/typescript/packages/core/src/events.ts
- AG-UI Protocol Documentation Home  
  https://docs.ag-ui.com/
- Microsoft Agent Framework Overview  
  https://learn.microsoft.com/agent-framework/overview/agent-framework-overview
- Microsoft Agent Framework AG-UI integration tutorial (OpenAI Responses API)  
  https://learn.microsoft.com/en-us/agent-framework/tutorials/agents-with-openai-responses-api
- Microsoft Agent Framework (source repository)  
  https://github.com/microsoft/agent-framework
- MudBlazor (source repository)  
  https://github.com/MudBlazor/MudBlazor
- MudBlazor license (MIT)  
  https://github.com/MudBlazor/MudBlazor/blob/dev/LICENSE
- `AIAgent` source (dotnet)  
  https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs
- `ChatClientAgent` source (dotnet)  
  https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs
- `MapAGUI` endpoint source (dotnet)  
  https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Hosting.AGUI.AspNetCore/AGUIEndpointRouteBuilderExtensions.cs
- `RunAgentInput` AG-UI request model source (dotnet)  
  https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.AGUI/Shared/RunAgentInput.cs

## Local Project Sources Used

- Plan source of truth: `plan.md`
- Compatibility/version source of truth: `docs/compatibility-matrix.md`
- MudBlazor taxonomy contract and versioning rules: `docs/mudblazor-capability-taxonomy.md`
- Tier packaging + entitlement mapping: `docs/pricing-tiers.md`
- Core runtime contracts and framework tool wiring: `src/AgentBlazor.Core/Runtime`
- MudBlazor capability profile source: `src/AgentBlazor.Core/Components/AgentComponentV1CapabilityProfile.cs`
- Mud tier boundary source: `src/AgentBlazor.Core/Components/AgentComponentTierBoundaries.cs`
- Service registration and DI wiring: `src/AgentBlazor.Core/Services`
- Licensing tier contract source: `src/AgentBlazor.Licensing`
- Provider registration and `IChatClient` wiring: `src/AgentBlazor.ProviderAdapters`
- AG-UI hosting endpoint: `src/AgentBlazor.Hosting`
- Demo host wiring: `demo/AgentBlazor.Demo/Program.cs`
- Demo AgentDataGrid flow page: `demo/AgentBlazor.Demo/Components/Pages/AgentDataGridAgentDemo.razor`
- Demo AgentDataGrid executor/state adapter: `demo/AgentBlazor.Demo/Services/AgentDataGridDemoState.cs`
- Demo AgentDialog+AgentForm flow page: `demo/AgentBlazor.Demo/Components/Pages/AgentDialogFormAgentDemo.razor`
- Demo AgentDialog+AgentForm executor/state adapter: `demo/AgentBlazor.Demo/Services/AgentDialogFormDemoState.cs`
- Demo mixed routing page: `demo/AgentBlazor.Demo/Components/Pages/MudAgentRoutingDemo.razor`
- Integration tests (runtime + provider + hosting): `tests/AgentBlazor.IntegrationTests`
- Hosted AG-UI approval tests: `tests/AgentBlazor.IntegrationTests/AgUiHostingIntegrationTests.cs`
- Mud execution-matrix tests (policy/approval/mixed tools): `tests/AgentBlazor.IntegrationTests/AgentRuntimeIntegrationTests.cs`
- AG-UI protocol source tree referenced by project brief: `C:\Git\repos\ag-ui`
- AG-UI event schema source used for payload alignment: `C:\Git\repos\ag-ui\sdks\typescript\packages\core\src\events.ts`
- Microsoft Agent Framework source tree referenced by project brief: `C:\Git\Grouptree\agent-framework`
- Microsoft AG-UI hosting source inspected: `C:\Git\Grouptree\agent-framework\dotnet\src\Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`
- MudBlazor source tree referenced by project brief: `C:\Git\repos\MudBlazor`
- MudBlazor MIT license file inspected: `C:\Git\repos\MudBlazor\LICENSE`
- Central package pinning file: `Directory.Packages.props`
- Restore lock strategy file: `Directory.Build.props`
- SDK pinning file: `global.json`
- CI pinned-version validation workflow: `.github/workflows/ci.yml`

## Compatibility Notes

- AG-UI endpoint payload and stream generation are handled by Microsoft Agent Framework AG-UI ASP.NET Core integration (`AddAGUI` + `MapAGUI`) via `MapAgentBlazorAgUiRun(...)`.
- Runtime execution is framework-native through Microsoft Agent Framework `ChatClientAgent` and framework tools (`AIFunction`), with no custom keyword planner path or custom AG-UI stream contract in core runtime.
- Agent action-policy enforcement is applied before framework tool registration using registration allow lists (`AllowedComponents` + `AllowedActions`).
- Runtime and hosted AG-UI paths share the same policy evaluation (`ComponentActionPolicyEvaluation`) so blocked Mud actions are filtered consistently and surfaced via diagnostics/logging.
- Runtime and hosted AG-UI paths share the same approval evaluation (`ComponentActionApprovalPolicy`) so `RequiresApproval` Mud actions are gated consistently from framework invocation context.
- Runtime and hosted AG-UI paths also apply entitlement filtering for Mud actions using `AgentComponentTierBoundaries` + `IAgentBlazorEntitlementService` before framework tool registration.
- `WithToolsFromAssembly(...)` is implemented as framework tool registration using reflection-based `AIFunctionFactory.Create(MethodInfo, ...)` over explicitly registered assemblies.
- Product direction is MudBlazor-first: AgentBlazor targets orchestration/agentic control over MudBlazor components.
