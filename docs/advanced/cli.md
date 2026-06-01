# Advanced CLI

The CLI is an advanced setup path for existing Blazor apps.

Use it when:

- you want scaffold help instead of wiring `Program.cs` and the host shell yourself
- you are integrating AgentBlazor into an existing solution
- you want `doctor` and `validate` checks after scaffold

Current run order:

```bash
dotnet tool install --global AgentBlazor.Cli
agentblazor init ./MySolution.slnx --host MyBlazorApp
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --approve
dotnet restore ./MySolution.slnx --force-evaluate
dotnet build ./MySolution.slnx --no-restore -nologo
agentblazor doctor ./MySolution.slnx --host MyBlazorApp
agentblazor validate ./MySolution.slnx --host MyBlazorApp
```

## Analyze An Existing App

`agentblazor analyze` is the read-only v1 onboarding command. It scans a Blazor solution, writes a markdown report, and does not change application code.

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

By default the report is written to `.agentblazor/analysis.md` next to the solution or project. Use `--output` to write somewhere else:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp --output ./agentblazor-analysis.md
```

`--host` identifies the Blazor startup project used for host-shape and install-readiness checks. It does not mean "only scan this project." By default `analyze` scans the host project plus its transitive project references.

For multi-tenant or modular solutions where tenant projects live as sibling projects in the same `.slnx` but are not referenced by the host project, use full-solution scope:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp --scan-scope solution
```

Readiness checks still apply to the host project only, because `Program.cs`, shell assets, endpoint mapping, and chat placement belong to the runnable app. Routes, services, capabilities, and candidate actions are scanned from every project in the solution when `--scan-scope solution` is used.

The report includes:

- discovered routes and pages
- existing `[AgentCapability]` / `[AgentAction]` methods
- developer-facing services and public methods, with framework/helper noise filtered out
- LLM workflow suggestions validated against the static analysis model
- approval guidance for suggestions that reference mutating methods
- install-readiness checks
- recommended next steps

The summary uses "AgentBlazor action adoption" to show how many actions are already confirmed with AgentBlazor attributes versus how many candidate actions were discovered but are not exposed yet.

Run static analysis only when you do not want an LLM call:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp --static-only
```

## Analyze Provider Configuration

For OpenAI, run `analyze` directly. If no key is configured and the terminal is interactive, the CLI asks for one and uses it for that run only:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

The key is not written to disk. For repeat runs, CI, or non-interactive shells, set an environment variable:

```bash
export OPENAI_API_KEY="<openai-api-key>"
export AGENTBLAZOR_ANALYZE_MODEL="gpt-4o-mini"
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

PowerShell:

```powershell
$env:OPENAI_API_KEY = "<openai-api-key>"
$env:AGENTBLAZOR_ANALYZE_MODEL = "gpt-4o-mini"
agentblazor analyze .\MySolution.slnx --host MyBlazorApp
```

Supported OpenAI environment variables:

- `OPENAI_API_KEY` or `OpenAI__ApiKey`
- `AGENTBLAZOR_ANALYZE_MODEL`, `OPENAI_MODEL`, or `OpenAI__Model`

For Azure OpenAI:

```bash
export AGENTBLAZOR_ANALYZE_PROVIDER="azure-openai"
export AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT="<deployment-name>"
export AZURE_OPENAI_API_KEY="<azure-openai-api-key>"
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

If no provider is configured and the command cannot prompt, `analyze` exits before scanning and tells you which environment variables to set. Use `--static-only` to avoid provider configuration entirely.

## Windows MSBuild Fallback

`analyze` normally loads solutions through Roslyn `MSBuildWorkspace`. On machines where Visual Studio/MSBuild assemblies conflict, this can fail before project analysis starts. Version `0.2.4` and later fall back to static source-file analysis for known MSBuildWorkspace failures, including the `Microsoft.Build.Shared.XMakeElements` type-initializer error.

You normally do not need to do anything. To force the fallback for diagnostics:

```powershell
$env:AGENTBLAZOR_STATIC_WORKSPACE = "1"
agentblazor analyze .\MySolution.slnx --host MyBlazorApp --output .\analysis.md
Remove-Item Env:AGENTBLAZOR_STATIC_WORKSPACE
```

## Analyze Token Cost

`agentblazor analyze` sends a compact summary of routes, capabilities, services, and public methods to the configured model. It does not send full source files.

Typical run size on the current synthetic targets is roughly:

- simple app: under 10k input tokens
- realistic app: 10k-30k input tokens
- larger real app: depends on route/service count; use `--static-only` first if you want to inspect what will be summarized

With `gpt-4o-mini`, OpenAI currently lists text pricing at `$0.15 / 1M input tokens` and `$0.60 / 1M output tokens`. At those rates, a 30k input / 2k output analysis is about `$0.006` before taxes, exchange rates, or provider-specific charges. Check the provider pricing page before relying on this number for budgeting.

Do not make this the first-user story. The default path is still:

1. install the package
2. register `AddAgentBlazor(...)`
3. map `MapAgentBlazorEndpoints()`
4. add one capability class
5. render one chat surface
