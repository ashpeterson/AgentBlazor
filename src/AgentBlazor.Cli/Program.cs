using Spectre.Console.Cli;
using AgentBlazor.Cli.Commands;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("agentblazor");
    config.SetApplicationVersion("1.0.0");

    config.AddCommand<InitCommand>("init")
        .WithDescription("Generate AGENT.md from a Blazor solution")
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
});

return await app.RunAsync(args);
