# Advanced CLI

The CLI is an advanced setup path for existing Blazor apps.

Use it when:

- you want scaffold help instead of wiring `Program.cs` and the host shell yourself
- you are integrating AgentBlazor into an existing solution
- you want `doctor` and `validate` checks after scaffold

Current run order:

```bash
dotnet tool install --global AgentBlazor.Cli --version 0.1.0-preview.9 --add-source https://nuget.pkg.github.com/ashpeterson/index.json
agentblazor init ./MySolution.slnx --host MyBlazorApp
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --approve
dotnet restore ./MySolution.slnx --force-evaluate
dotnet build ./MySolution.slnx --no-restore -nologo
agentblazor doctor ./MySolution.slnx --host MyBlazorApp
agentblazor validate ./MySolution.slnx --host MyBlazorApp
```

Do not make this the first-user story. The default path is still:

1. install the package
2. register `AddAgentBlazor(...)`
3. map `MapAgentBlazorEndpoints()`
4. add one capability class
5. render one chat surface
