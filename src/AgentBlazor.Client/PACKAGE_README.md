# AgentBlazor.Client

Browser-safe AgentBlazor chat components for Blazor WebAssembly and hosted WebAssembly apps.

Install:

```bash
dotnet add package AgentBlazor.Client --prerelease
```

If you want the exact current preview:

```bash
dotnet add package AgentBlazor.Client --version 0.1.0-preview.10
```

Server project:

```csharp
app.MapAgentBlazorEndpoints();
app.MapAgentBlazorRemoteChat();
```

Client project:

```razor
@using AgentBlazor.Client.Chat

<AgentRemoteChatWidget Endpoint="/agentblazor/chat/run" Title="Assistant" />
```

The package also includes `AgentRemoteChatSurface`, `AgentRemoteChatPanel`, and `AgentRemoteChatBar`.

Docs:

- Repository: https://github.com/ashpeterson/AgentBlazor
- Quickstart: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/quickstart.md
