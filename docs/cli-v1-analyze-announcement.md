# AgentBlazor CLI v1: Read-Only Analysis For Existing Blazor Apps

AgentBlazor `0.2.5` ships the first v1 slice of the CLI: `agentblazor analyze`.

It scans an existing Blazor solution, finds routes and service methods, asks an LLM for grounded workflow suggestions, validates those suggestions against the static model, and writes a markdown report. It does not modify your application code.

## Try It

Install the tool:

```bash
dotnet tool install --global AgentBlazor.Cli
```

Run it from a solution directory:

```bash
agentblazor analyze ./MySolution.sln --host MyBlazorApp
```

If no OpenAI key is configured and your terminal is interactive, the CLI asks for one and uses it for that run only. The key is not written to disk.

To avoid an LLM call:

```bash
agentblazor analyze ./MySolution.sln --host MyBlazorApp --static-only
```

## What The Report Shows

The report includes:

- discovered Blazor routes and pages
- existing `[AgentCapability]` and `[AgentAction]` methods
- developer-facing services and public methods
- LLM workflow suggestions validated against real methods from the app
- install-readiness checks for AgentBlazor wiring
- recommended next steps

The command is intentionally read-only. It tells you where AgentBlazor could fit before asking you to wire anything.

## Example: eShopOnBlazor

Running against `dotnet-architecture/eShopOnBlazor` produced:

```text
Host: eShopOnBlazor
Routes: 5
Services: 1
Actions: 0 confirmed, 7 discovered
Readiness: 2 passed, 3 warnings, 5 missing
Host shape: Standard Blazor Web App
Workflow suggestions: 3 accepted, 0 rejected
```

The report identified `CatalogService` as the developer-facing service and suggested grounded workflows for catalog operations:

```markdown
### Create Catalog Item

This workflow allows users to create a new catalog item with approval.

- Existing methods used:
  - `CatalogService.CreateCatalogItem`
- Approval guidance: this workflow references mutating methods. Mark generated `[AgentAction]` methods with `RequiresApproval = true` unless a human-reviewed policy says the action is safe to run automatically.
- Suggested attribute: `[AgentAction("Create Catalog Item", RequiresApproval = true)]`
```

That is the intended shape: useful onboarding information, explicit approval boundaries for mutating actions, and no code changes made by the tool.

## Windows MSBuild Fallback

Some Windows machines hit Roslyn `MSBuildWorkspace` failures when Visual Studio/MSBuild assemblies conflict. `0.2.5` includes the fallback introduced in `0.2.4`: if solution loading fails with a known MSBuildWorkspace error, `analyze` falls back to static source-file analysis instead of stopping before it can produce a report.

You can force that path for diagnostics:

```powershell
$env:AGENTBLAZOR_STATIC_WORKSPACE = "1"
agentblazor analyze .\MySolution.sln --host MyBlazorApp --output .\analysis.md
Remove-Item Env:AGENTBLAZOR_STATIC_WORKSPACE
```

## Links

- [Advanced CLI guide](advanced/cli.md)
- [0.2.5 release notes](releases/0.2.5.md)
- [CLI v1 validation notes](internal/cli-v1-real-app-validation-2026-05-27.md)
