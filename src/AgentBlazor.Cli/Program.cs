using Spectre.Console.Cli;
using AgentBlazor.Cli.Commands;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("agentblazor");
    config.SetApplicationVersion("1.0.0");

    config.AddCommand<InitCommand>("init")
        .WithDescription("Initialize AgentBlazor for an existing Blazor app and show the next installer steps")
        .WithExample("init")
        .WithExample("init", "./MySolution.sln")
        .WithExample("init", "--description", "My app description");

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

    config.AddCommand<ScaffoldCommand>("scaffold")
        .WithDescription("Preview the baseline AgentBlazor install edits for an existing Blazor app")
        .WithExample("scaffold")
        .WithExample("scaffold", "./MySolution.slnx", "--host", "MyBlazorApp")
        .WithExample("scaffold", "./MySolution.slnx", "--host", "MyBlazorApp", "--diff")
        .WithExample("scaffold", "./MySolution.slnx", "--host", "MyBlazorApp", "--approve");
});

return await app.RunAsync(args);
