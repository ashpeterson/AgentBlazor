# GitHub Packages Private Preview

Last updated: 2026-04-13

Use this flow when you want to publish `AgentBlazor` and `AgentBlazor.Cli` privately and install them in a separate Blazor app as a test user.

## What This Gives You

- the package stays off `nuget.org`
- publishing is done from GitHub Actions
- a test user can install the runtime package and CLI tool from the GitHub Packages NuGet feed

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
5. Enter a prerelease version such as `0.1.0-preview.3`.

What the workflow does:

- restore, build, test, and run Playwright
- pack the AgentBlazor package set
- run the local consumer smoke test script, including CLI and startup validation
- push `AgentBlazor` and `AgentBlazor.Cli` `.nupkg` files to GitHub Packages
- upload both `.nupkg` files as workflow artifacts

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

Install the runtime package:

```powershell
dotnet add package AgentBlazor --prerelease --source github-agentblazor
```

Install the CLI tool from the same feed:

```powershell
dotnet tool install --global AgentBlazor.Cli --prerelease --add-source "https://nuget.pkg.github.com/ashpeterson/index.json"
```

Verify the CLI tool resolves to the preview package version:

```powershell
agentblazor --version
```

Expected for the current preview:

```text
0.1.0-preview.3
```

Then run the clean-app validation sequence:

```powershell
agentblazor init
agentblazor scaffold --diff
agentblazor scaffold --approve
dotnet restore
dotnet build
agentblazor doctor
agentblazor validate
```

## Notes

- This is the right path for private preview testing.
- Published-feed validation is currently pending. Local preflight on 2026-04-13 found no `NUGET_API_KEY`, `GITHUB_TOKEN`, `GH_TOKEN`, or logged-in `gh` session on the validation machine, so the next verification requires dispatching the GitHub Actions workflow or providing authenticated feed credentials.
- Once real-project validation is complete, move to `nuget.org` for normal public installation.
- The package currently targets `net10.0`.
- The current preview story should lead with the workflow-first app-layer positioning, not the removed planner/runtime architecture.
