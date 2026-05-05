# AgentBlazor Starter

This sample is a reference app, not the onboarding path.

Use the public quickstart first:

- [docs/quickstart.md](/home/ashdev/workspace/AgentBlazor/docs/quickstart.md)

Then use this starter when you want to inspect a slightly fuller route-scoped workflow example with:

- one workflow agent
- one capability class
- one service-backed state model
- one approval boundary
- one embedded chat surface

Run it:

```powershell
dotnet run --project samples/AgentBlazor.Starter/AgentBlazor.Starter.csproj
```

Open:

- `/`
- `/ops-review`

Inside this repo, the starter defaults to local source-project references so the sample can build before packages are published.

To validate the published package path from inside the repo:

```powershell
dotnet run --project samples/AgentBlazor.Starter/AgentBlazor.Starter.csproj -p:UseLocalAgentBlazorSource=false -p:AgentBlazorPackageVersion=0.1.0-preview.11
```
