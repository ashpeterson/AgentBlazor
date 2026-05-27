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

The report includes:

- discovered routes and pages
- existing `[AgentCapability]` / `[AgentAction]` methods
- developer-facing services and public methods
- LLM workflow suggestions validated against the static analysis model
- install-readiness checks
- recommended next steps

Run static analysis only when you do not want an LLM call:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp --static-only
```

## Analyze Provider Configuration

For OpenAI, set an API key before running `analyze`:

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

If no provider is configured, `analyze` exits before scanning and tells you which environment variables to set. Use `--static-only` to avoid provider configuration entirely.

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
