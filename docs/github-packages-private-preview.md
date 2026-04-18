# GitHub Packages Private Preview

Last updated: 2026-04-18

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

Current source private-preview version:

- `0.1.0-preview.8`
- source commit `79cf68df3c448868d1e90a845d3629da20cb5672`

Latest published-feed validated private-preview version:

- `0.1.0-preview.8`
- workflow run `24597951350`
- source commit `79cf68df3c448868d1e90a845d3629da20cb5672`
- package feed: `https://nuget.pkg.github.com/ashpeterson/index.json`

How to run it:

1. Open the repository in GitHub.
2. Go to `Actions`.
3. Open `publish-github-packages-preview`.
4. Click `Run workflow`.
5. Enter a prerelease version such as `0.1.0-preview.8`.

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

Install the runtime package using the exact version you are validating:

```powershell
dotnet add package AgentBlazor --version 0.1.0-preview.8 --source github-agentblazor
```

Install the CLI tool from the same feed and exact version:

```powershell
dotnet tool install --global AgentBlazor.Cli --version 0.1.0-preview.8 --add-source "https://nuget.pkg.github.com/ashpeterson/index.json"
```

Verify the CLI tool resolves to the preview package version:

```powershell
agentblazor --version
```

Expected for the current private-preview package:

```text
0.1.0-preview.8
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
- `0.1.0-preview.8` has been validated from GitHub Packages against a clean Blazor Web App and the external real-world CSP/nonce-aware app `damienbod/BlazorSecurityNet10`.
- `0.1.0-preview.8` includes the SDK roll-forward, package-lock, and explicit web-app runtime framework updates needed for newer .NET 10 preview SDK environments.
- Use the same exact version for `AgentBlazor` and `AgentBlazor.Cli`. Scaffolded workflow files use `AgentBlazor.App` APIs such as `CapabilityResult`; those will not compile if the runtime package is stale or mismatched with the CLI.
- Published-feed validation for `0.1.0-preview.8` used isolated NuGet config/cache/tool paths, `agentblazor init`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, `dotnet build`, `doctor`, `validate`, runtime HTTP smokes with a placeholder `OpenAI__ApiKey`, and all-surface Playwright prompt validation against the external real-world app. Clean-app workdir: `/tmp/agentblazor-published-preview8-clean-20260418053801`. Real-app workdir: `/tmp/agentblazor-published-preview8-damienbod-20260418053944/BlazorSecurityNet10/BlazorApp`. Published-feed all-surface workflow run: `24598484039`.
- To reproduce published-feed external chat validation from this repo, run `npm --prefix tests/e2e run test:external-chat-widget` with `AGENTBLAZOR_PACKAGE_SOURCE_MODE=published`, `AGENTBLAZOR_PACKAGE_VERSION=0.1.0-preview.8`, GitHub Packages credentials, `AGENTBLAZOR_EXTERNAL_REPO=https://github.com/damienbod/BlazorSecurityNet10.git`, `AGENTBLAZOR_EXTERNAL_PROJECT=BlazorApp/BlazorApp.csproj`, `AGENTBLAZOR_EXTERNAL_PROVIDER_MODE=deterministic`, and `AGENTBLAZOR_EXTERNAL_CHAT_SURFACES=widget,surface,panel,bar`.
- For deterministic runtime smoke, prefer `dotnet run --no-launch-profile --urls http://127.0.0.1:5288` or another known free port; otherwise app launch profiles can bind a different port than the one being probed.
- Earlier `0.1.0-preview.2` GitHub Packages validation found a stale immutable runtime package, `0.1.0-preview.3` later exposed a real-app dependency-range issue, the `0.1.0-preview.4` feed version was already occupied by an older immutable build, `0.1.0-preview.5` was too strict for apps that already depend on Microsoft Agents `1.1.0`, and `0.1.0-preview.6` did not preserve CSP nonces for scaffolded script/link assets. Use `0.1.0-preview.8` or later for private-preview testing.
- Earlier `0.1.0-preview.3` clean-app validation passed, but real-app runtime smoke exposed an open dependency-range issue where NuGet could float Microsoft Agents packages to an incompatible API. Use `0.1.0-preview.8` or later for private-preview testing.
- Real-app tester checklist: `docs/private-preview-validation.md`.
- Once real-project validation is complete, move to `nuget.org` for normal public installation.
- The package currently targets `net10.0`.
- The current preview story should lead with the workflow-first app-layer positioning, not the removed planner/runtime architecture.
