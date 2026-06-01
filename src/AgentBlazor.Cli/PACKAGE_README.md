# AgentBlazor.Cli

Advanced scaffold and validation tool for wiring AgentBlazor into existing Blazor apps.

Install:

```bash
dotnet tool install --global AgentBlazor.Cli
```

If you prefer a pinned install:

```bash
dotnet tool install --global AgentBlazor.Cli --version 0.2.6
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

Version `0.2.6` and later add `--scan-scope solution` for multi-tenant and modular solutions where sibling projects live in the same `.slnx` but are not referenced by the Blazor host project.

Reports filter helper/framework noise, show AgentBlazor action adoption, and call out `RequiresApproval = true` guidance for workflow suggestions that reference mutating methods.

The CLI is an advanced path. The default install story is still `dotnet add package AgentBlazor` plus manual runtime wiring.

Docs:

- Repository: https://github.com/ashpeterson/AgentBlazor
- Advanced CLI guide: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/advanced/cli.md
- 0.2.6 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.6.md
- 0.2.5 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.5.md
- 0.2.3 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.3.md
- 0.2.2 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.2.md
- 0.2.1 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.1.md
- 0.2.0 release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.0.md
