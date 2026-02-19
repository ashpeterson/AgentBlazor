# Compatibility Matrix

Last verified: 2026-02-18  
Scope: MudBlazor-first AgentBlazor runtime, hosting, demo, and test projects.

## Tested Baseline

| Area | Pinned/Tested Version | Source of Truth |
|---|---:|---|
| .NET SDK | `10.0.200-preview.0.26103.119` | `global.json` |
| Target Framework | `net10.0` | `*.csproj` |
| MudBlazor | `8.15.0` | `Directory.Packages.props` |
| Microsoft Agent Framework (core) | `Microsoft.Agents.AI` `1.0.0-preview.260209.1` | `Directory.Packages.props` |
| Microsoft Agent Framework (OpenAI adapter) | `Microsoft.Agents.AI.OpenAI` `1.0.0-preview.260209.1` | `Directory.Packages.props` |
| Microsoft Agent Framework (AG-UI hosting) | `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` `1.0.0-preview.260209.1` | `Directory.Packages.props` |
| Azure OpenAI SDK | `Azure.AI.OpenAI` `2.8.0-beta.1` | `Directory.Packages.props` |
| ASP.NET Core Components package | `Microsoft.AspNetCore.Components.Web` `10.0.2` | `Directory.Packages.props` |
| Test SDK | `Microsoft.NET.Test.Sdk` `17.14.1` | `Directory.Packages.props` |
| xUnit | `xunit` `2.9.3`, `xunit.runner.visualstudio` `3.1.4` | `Directory.Packages.props` |
| Coverage collector | `coverlet.collector` `6.0.4` | `Directory.Packages.props` |

## Protocol and Source Alignment

| Reference | Location |
|---|---|
| AG-UI docs | https://docs.ag-ui.com/ |
| AG-UI source (local) | `C:\Git\repos\ag-ui` |
| Microsoft Agent Framework docs | https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp |
| Microsoft Agent Framework source (local) | `C:\Git\Grouptree\agent-framework` |
| MudBlazor source (local) | `C:\Git\repos\MudBlazor` |
| MudBlazor license (MIT) | `C:\Git\repos\MudBlazor\LICENSE` |

## Version Pinning Strategy

1. All NuGet versions are pinned centrally in `Directory.Packages.props`.
2. Projects must not define inline `Version` attributes on `<PackageReference ... />`.
3. Lock files (`packages.lock.json`) are enabled through `Directory.Build.props`.
4. CI runs `dotnet restore --locked-mode` to ensure dependency graph reproducibility.

## CI Validation

CI workflow: `.github/workflows/ci.yml`

Validation gates:
1. Enforce central pinning (fail if inline package versions are found).
2. Restore in locked mode (`dotnet restore AgentBlazor.slnx --locked-mode`).
3. Build + test in `Release` with `--no-restore`.

## Updating Versions Safely

1. Update versions only in `Directory.Packages.props` (and `global.json` if SDK changes).
2. Run `dotnet restore AgentBlazor.slnx` to refresh `packages.lock.json` files.
3. Run `dotnet build AgentBlazor.slnx -nologo` and `dotnet test AgentBlazor.slnx -nologo`.
4. Update this matrix and relevant notes in `docs/spec-references.md`.
