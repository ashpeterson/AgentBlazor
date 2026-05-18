# X / LinkedIn Thread Draft

Post 1:

I’m launching `AgentBlazor` on June 9.

It adds a real agent workflow surface to a Blazor route:

- install package
- wire one capability class
- mount one chat surface
- let the prompt drive the page

Post 2:

The important constraint is that it is not trying to replace the app UI.

The launch path is:

- one route
- one workflow
- one visible approval boundary
- one deterministic result on screen

Post 3:

The public demo is now a support queue, not an abstract workflow:

- show open tickets from this week
- explain why they need attention
- draft a reply
- escalate blocked tickets

That is a much better proof than a generic orchestration screen.

Post 4:

Install path:

```bash
dotnet add package AgentBlazor --version 0.2.0-preview.1
```

CLI exists, but it is the advanced path, not the headline.

Post 5:

If you build Blazor apps and want to try the package before launch, I’m looking for a few beta testers in the last two weeks of May.

Repo:

`https://github.com/ashpeterson/AgentBlazor`
