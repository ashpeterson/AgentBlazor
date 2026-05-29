# AgentBlazor 0.2.5 - CLI v1 Analyze

AgentBlazor `0.2.5` ships the first v1 slice of the CLI: `agentblazor analyze`.

It scans an existing Blazor solution, finds routes and service methods, asks an LLM for grounded workflow suggestions, validates those suggestions against the static model, and writes a markdown report. It does not modify your application code.

Install:

```bash
dotnet tool install --global AgentBlazor.Cli
```

Run:

```bash
agentblazor analyze ./MySolution.sln --host MyBlazorApp
```

Static-only, no LLM call:

```bash
agentblazor analyze ./MySolution.sln --host MyBlazorApp --static-only
```

Example from eShopOnBlazor:

```text
Host: eShopOnBlazor
Routes: 5
Services: 1
Actions: 0 confirmed, 7 discovered
Workflow suggestions: 3 accepted, 0 rejected
```

The report now filters helper/object-method noise, uses `AgentBlazor action adoption` instead of ambiguous action coverage, and includes explicit `RequiresApproval = true` guidance for mutating workflow suggestions.

Links:

- CLI announcement: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/cli-v1-analyze-announcement.md
- Advanced CLI guide: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/advanced/cli.md
- Release notes: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/releases/0.2.5.md
