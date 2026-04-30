# AgentBlazor.Cli

Advanced scaffold and validation tool for wiring AgentBlazor into existing Blazor apps.

Install:

```bash
dotnet tool install --global AgentBlazor.Cli --prerelease
```

If you want the exact current preview:

```bash
dotnet tool install --global AgentBlazor.Cli --version 0.1.0-preview.10
```

Example:

```bash
agentblazor init ./MySolution.slnx --host MyBlazorApp
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --approve
```

The CLI is an advanced path. The default install story is still `dotnet add package AgentBlazor` plus manual runtime wiring.

Docs:

- Repository: https://github.com/ashpeterson/AgentBlazor
- Advanced CLI guide: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/advanced/cli.md
