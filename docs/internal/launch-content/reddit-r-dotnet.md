# r/dotnet Draft

Title:

Launching AgentBlazor on June 9. Looking for 3 beta testers for a Blazor-native agent workflow package.

Body:

I’m shipping `AgentBlazor` on June 9 and I need 3 outside developers to try the install path before launch.

What it is:

- a NuGet package for Blazor
- adds a chat surface to a real route
- calls explicit capability methods
- supports approval-gated actions
- updates the UI deterministically after the prompt

Current public package:

```bash
dotnet add package AgentBlazor --version 0.2.0-preview.3
```

Current public demo:

- support inbox route
- show open tickets
- explain queue risk
- draft a reply
- escalate blockers first

Repo:

`https://github.com/ashpeterson/AgentBlazor`

I’m specifically looking for people willing to spend 20-30 minutes on one of these:

1. install it into a fresh Blazor app
2. try the support-inbox demo and tell me where the story is still unclear
3. try the CLI path on an existing Blazor app if you already have one

I do not need vague product feedback right now. I need install friction, broken assumptions, and "this sentence still makes no sense to a stranger."

If you want to help, reply here or DM me.
