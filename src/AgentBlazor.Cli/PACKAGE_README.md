# AgentBlazor.Cli

Advanced scaffold and validation tool for wiring AgentBlazor into existing Blazor apps.

Install:

```bash
dotnet tool install --global AgentBlazor.Cli
```

If you prefer a pinned install:

```bash
dotnet tool install --global AgentBlazor.Cli --version 0.2.18
```

Example:

```bash
agentblazor init ./MySolution.slnx --host MyBlazorApp
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --approve
```

Read-only analysis:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

`analyze` writes `.agentblazor/analysis.md` and does not modify application code. It uses OpenAI for workflow suggestions, or you can pass `--static-only` to generate a static report without an LLM call.

If no OpenAI key is configured and the terminal is interactive, `analyze` prompts for a key and uses it for that run only. The key is not written to disk.

Version `0.2.5` and later include a Windows/Roslyn fallback for MSBuildWorkspace load failures. If a machine has conflicting Visual Studio/MSBuild assemblies, `analyze` falls back to static source-file analysis instead of failing before the report is generated.

Version `0.2.7` and later add `--scan-scope solution` for multi-tenant and modular solutions where sibling projects live in the same `.slnx` but are not referenced by the Blazor host project.

Version `0.2.8` hardens solution-scope workflow suggestions when multiple projects expose the same service and method names.

Version `0.2.9` excludes test projects and test asset folders from default solution-scope analysis.

Version `0.2.10` prioritizes safe read-only workflow suggestions, labels suggestion risk, and requires approval for mutating command suggestions.

Version `0.2.11` filters internal chat persistence, state store, runner, scheduler, and tenant store services from workflow suggestions and recommended next steps.

Version `0.2.12` improves real-project solution scans by linking injected interfaces to implementation services, filtering infrastructure/identity/storage/http plumbing, and clearly reporting when all discovered services were filtered out.

Version `0.2.13` improves framework-style Blazor app analysis by explaining component-driven routing, filtering manager/state/auth/token/http plumbing, and using validated LLM workflow suggestions instead of appending static action guesses when LLM suggestions are available.

Version `0.2.14` improves report usability by putting top workflow recommendations and install blockers before the detailed inventory, adding route quality notes, and classifying services by likely agent fit.

Version `0.2.15` was superseded by `0.2.16`.

Version `0.2.16` removes the `0.2.15` map-layer workflow framing and filters UI layer applier, renderer, styling, and map-layer infrastructure from workflow suggestion inputs.

Version `0.2.17` improves workflow relevance by demoting pure read-only data/view suggestions from top recommendations when process-oriented workflow candidates exist.

Version `0.2.18` improves top recommendation quality by demoting raw integration, admin/sensitive, data-access, and infrastructure suggestions so reports prioritize real business/process workflows instead of plumbing.

Reports put top workflow recommendations and install blockers before the detailed inventory, filter helper/framework noise, classify remaining services by likely agent fit, show AgentBlazor action adoption, and call out `RequiresApproval = true` guidance for workflow suggestions that reference mutating methods.

The CLI is an advanced path. The default install story is still `dotnet add package AgentBlazor` plus manual runtime wiring.

Docs:

- Repository: https://github.com/ashpeterson/AgentBlazor
- Advanced CLI guide: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/advanced/cli.md
- 0.2.18 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.18.md
- 0.2.17 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.17.md
- 0.2.16 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.16.md
- 0.2.15 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.15.md
- 0.2.14 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.14.md
- 0.2.13 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.13.md
- 0.2.12 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.12.md
- 0.2.11 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.11.md
- 0.2.10 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.10.md
- 0.2.5 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.5.md
- 0.2.3 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.3.md
- 0.2.2 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.2.md
- 0.2.1 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.1.md
- 0.2.0 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.0.md
