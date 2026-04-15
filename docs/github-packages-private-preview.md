# GitHub Packages Private Preview

Last updated: 2026-04-15

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

Current validated private-preview version:

- `0.1.0-preview.7`
- workflow run `24443709690`
- source commit `5809ddcfda282e5a70bd89649a901e9599d89ac4`
- package feed: `https://nuget.pkg.github.com/ashpeterson/index.json`

How to run it:

1. Open the repository in GitHub.
2. Go to `Actions`.
3. Open `publish-github-packages-preview`.
4. Click `Run workflow`.
5. Enter a prerelease version such as `0.1.0-preview.7`.

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
0.1.0-preview.7
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
- `0.1.0-preview.7` has been validated from GitHub Packages against a clean Blazor Web App and external real-world Blazor apps including `neozhu/CleanArchitectureWithBlazorServer` and `damienbod/BlazorSecurityNet10`.
- Use `0.1.0-preview.7` or later for real-app private-preview testing. `0.1.0-preview.7` aligns AgentBlazor to the Microsoft Agents 1.1 API family and preserves existing CSP nonce attributes when scaffold inserts MudBlazor and AgentBlazor assets.
- Published-feed validation should use a clean Blazor Web App or real app, isolated NuGet cache/tool paths, `agentblazor init`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, `dotnet build`, `doctor`, `validate`, and a runtime HTTP smoke with a placeholder `OpenAI__ApiKey`.
- For deterministic runtime smoke, prefer `dotnet run --no-launch-profile --urls http://127.0.0.1:5288` or another known free port; otherwise app launch profiles can bind a different port than the one being probed.
- Earlier `0.1.0-preview.2` GitHub Packages validation found a stale immutable runtime package, `0.1.0-preview.3` later exposed a real-app dependency-range issue, the `0.1.0-preview.4` feed version was already occupied by an older immutable build, `0.1.0-preview.5` was too strict for apps that already depend on Microsoft Agents `1.1.0`, and `0.1.0-preview.6` did not preserve CSP nonces for scaffolded script/link assets. Use `0.1.0-preview.7` or later for private-preview testing.
- Earlier `0.1.0-preview.3` clean-app validation passed, but real-app runtime smoke exposed an open dependency-range issue where NuGet could float Microsoft Agents packages to an incompatible API. Use `0.1.0-preview.7` instead.
- Real-app tester checklist: `docs/private-preview-validation.md`.
- Once real-project validation is complete, move to `nuget.org` for normal public installation.
- The package currently targets `net10.0`.
- The current preview story should lead with the workflow-first app-layer positioning, not the removed planner/runtime architecture.
