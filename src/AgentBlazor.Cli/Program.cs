using System.Reflection;
using Spectre.Console.Cli;
using AgentBlazor.Cli.Commands;

var app = new CommandApp();
var version = typeof(ScaffoldCommand).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion
    ?? typeof(ScaffoldCommand).Assembly.GetName().Version?.ToString()
    ?? "0.2.1";
version = version.Split('+', 2)[0];

app.Configure(config =>
{
    config.SetApplicationName("agentblazor");
    config.SetApplicationVersion(version);

    config.AddCommand<InitCommand>("init")
        .WithDescription("Initialize AgentBlazor for an existing Blazor app and show the next installer steps")
        .WithExample("init")
        .WithExample("init", "./MySolution.sln")
        .WithExample("init", "--description", "My app description");

    config.AddCommand<AnalyzeCommand>("analyze")
        .WithDescription("Analyze an existing Blazor app and write a read-only markdown report")
        .WithExample("analyze")
        .WithExample("analyze", "./MySolution.slnx", "--host", "MyBlazorApp")
        .WithExample("analyze", "./MySolution.slnx", "--output", ".agentblazor/analysis.md");

    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Regenerate AGENT.md if code has changed")
        .WithExample("update")
        .WithExample("update", "--force");

    config.AddCommand<WatchCommand>("watch")
        .WithDescription("Watch for file changes and auto-regenerate")
        .WithExample("watch")
        .WithExample("watch", "--debounce", "1000");

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Inspect an existing Blazor app for baseline AgentBlazor wiring")
        .WithExample("doctor")
        .WithExample("doctor", "./MySolution.slnx", "--host", "MyBlazorApp");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Validate an AgentBlazor install using readiness checks plus scaffold audit data when available")
        .WithExample("validate")
        .WithExample("validate", "./MySolution.slnx", "--host", "MyBlazorApp");

    config.AddCommand<ScaffoldCommand>("scaffold")
        .WithDescription("Preview the baseline AgentBlazor install edits for an existing Blazor app")
        .WithExample("scaffold")
        .WithExample("scaffold", "./MySolution.slnx", "--host", "MyBlazorApp")
        .WithExample("scaffold", "./MySolution.slnx", "--host", "MyBlazorApp", "--provider", "openai", "--diff")
        .WithExample("scaffold", "./MySolution.slnx", "--host", "MyBlazorApp", "--provider", "openai", "--approve");
});

return await app.RunAsync(args);
