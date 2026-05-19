# Why AgentBlazor Exists

AgentBlazor is for the case where a Blazor screen already has real business state, real actions, and real guardrails, but the user still has to click through the flow manually.

The pitch is narrow on purpose:

- one Blazor route
- one chat surface
- one capability class
- one deterministic workflow result on screen

The point is not "chat for everything." The point is giving a user a safer way to drive a workflow that already exists in the app.

The launch demo uses a support queue because the behavior is obvious:

- show open tickets from this week
- explain why they need attention
- draft a reply
- escalate blocked tickets first

That sequence is a better proof than an abstract enterprise workflow because a developer can see the page move and understand why it moved.

What AgentBlazor gives you:

- a Blazor-native chat surface
- route-scoped workflow registration
- explicit capability methods
- approval-gated actions
- deterministic UI changes after the prompt

What it does not try to do at launch:

- replace the rest of your app
- invent a new workflow engine
- make the CLI the first-user story

The first install path is now the normal one:

```bash
dotnet add package AgentBlazor --version 0.2.0-preview.3
```

Then wire `AddAgentBlazor(...)`, map `MapAgentBlazorEndpoints()`, add one capability class, and mount one chat surface.

The launch target is simple:

- one package install that works
- one support-inbox demo that makes sense
- one landing path that shows CLI -> code -> working UI

That is enough for v1.
