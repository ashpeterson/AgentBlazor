# GitHub Packages Private Preview

Last updated: 2026-03-20

Use this flow when you want to publish `AgentBlazor` privately and install it in a separate Blazor app as a test user.

## What This Gives You

- the package stays off `nuget.org`
- publishing is done from GitHub Actions
- a test user can install the package from the GitHub Packages NuGet feed

Feed URL:

- `https://nuget.pkg.github.com/ashpeterson/index.json`

## Publish From This Repo

Workflow:

- `.github/workflows/publish-github-packages-preview.yml`

How to run it:

1. Open the repository in GitHub.
2. Go to `Actions`.
3. Open `publish-github-packages-preview`.
4. Click `Run workflow`.
5. Enter a prerelease version such as `0.1.0-preview.2`.

What the workflow does:

- restore, build, test, and run Playwright
- pack the `AgentBlazor` NuGet package
- run the local consumer smoke test script
- push the `.nupkg` to GitHub Packages

## Install As A Test User

The test user must have read access to the repository/package.

Authentication for installing private NuGet packages from GitHub Packages uses a GitHub personal access token (classic) with:

- `read:packages`

If the test user does not already have repo access through GitHub, they also need access to the private repository/package itself.

Add the feed:

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/ashpeterson/index.json" `
  --name github-agentblazor `
  --username TEST_GITHUB_USERNAME `
  --password TEST_GITHUB_PAT `
  --store-password-in-clear-text
```

Install the package:

```powershell
dotnet add package AgentBlazor --prerelease --source github-agentblazor
```

## Notes

- This is the right path for private preview testing.
- Once real-project validation is complete, move to `nuget.org` for normal public installation.
- The package currently targets `net10.0`.
- The current preview story should lead with the workflow-first app-layer positioning, not the removed planner/runtime architecture.
