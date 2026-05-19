# Beta Tester Runbook

This is the Week 5 operator runbook for the June 9 launch path.

## Target

Get 3 outside developers to:

1. install `AgentBlazor`
2. run one route
3. submit one prompt
4. report friction in a structured way

## Ask

Use this short message:

> I’m shipping AgentBlazor on June 9 and need 20 to 30 minutes of install feedback from a Blazor developer. The current test is narrow: install the NuGet package, wire one support-inbox route, and tell me where the docs or product stop making sense. Would you be willing to try it?

## What To Send

Send only these links:

- repo root README
- `docs/beta-testing.md`
- support-inbox live demo route
- direct issue forms:
  - `/issues/new?template=install-friction.yml`
  - `/issues/new?template=beta-feedback.yml`

Do not send internal roadmap docs.

## Success Criteria

A beta run counts only if the tester confirms:

1. package install result
2. build result
3. whether the chat surface opened
4. whether one prompt produced a visible page change
5. whether the approval boundary made sense

## Triage Rules

Fix before freeze:

- package install failures
- broken docs commands
- missing assets or startup failures
- chat surface not opening
- prompt flow that does not visibly change the page
- approval flow confusion caused by product wording

Do not expand scope before freeze for:

- new features
- new providers
- new components
- pricing/pro work
- non-launch-path architecture cleanup

## Tracking

For each tester capture:

- name or handle
- date contacted
- app type tested
- SDK version
- pass/fail on install
- pass/fail on build
- pass/fail on prompt result
- pass/fail on approval understanding
- first confusion point
- first blocker
- issue link

## Fast Operator Reply

If a tester asks what to do, answer with this exact path:

1. create a fresh Blazor app
2. run `dotnet add package AgentBlazor --version 0.2.0-preview.3`
3. follow `docs/quickstart.md`
4. test the support-inbox shape only
5. submit feedback through one of the issue forms

## Freeze Rule

Once 3 outside developers have tested the package, only fix launch blockers.
