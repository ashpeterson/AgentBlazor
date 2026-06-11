param(
    [string]$PackageVersion = "",
    [string]$ProjectName = "PackageSmoke",
    [switch]$Pack,
    [switch]$KeepScratch,
    [string]$OpenAIApiKey = "",
    [string]$OpenAIModel = "gpt-4o-mini"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$scratchRoot = Join-Path (Join-Path $repoRoot ".tmp") $ProjectName
$appRoot = Join-Path $scratchRoot $ProjectName
$solutionPath = Join-Path $scratchRoot "$ProjectName.sln"
$projectPath = Join-Path $appRoot "$ProjectName.csproj"
$toolPath = Join-Path $scratchRoot "toolbin"
$localFeed = Join-Path $scratchRoot "local-feed"
$nugetConfigPath = Join-Path $scratchRoot "NuGet.Config"
$directoryPackagesPropsPath = Join-Path $scratchRoot "Directory.Packages.props"
$programPath = Join-Path $appRoot "Program.cs"
$appRazorPath = Join-Path (Join-Path $appRoot "Components") "App.razor"
$importsPath = Join-Path (Join-Path $appRoot "Components") "_Imports.razor"
$mainLayoutPath = Join-Path (Join-Path (Join-Path $appRoot "Components") "Layout") "MainLayout.razor"
$homePath = Join-Path (Join-Path (Join-Path $appRoot "Components") "Pages") "Home.razor"
$servicesDir = Join-Path $appRoot "Services"
$workflowsDir = Join-Path $appRoot "Workflows"
$appSettingsDevelopmentPath = Join-Path $appRoot "appsettings.Development.json"

$packageDefinitions = @(
    @{
        Id = "AgentBlazor.Licensing"
        Project = Join-Path $repoRoot "src\AgentBlazor.Licensing\AgentBlazor.Licensing.csproj"
        OutputDir = Join-Path $repoRoot "src\AgentBlazor.Licensing\bin\Release"
    },
    @{
        Id = "AgentBlazor.Core"
        Project = Join-Path $repoRoot "src\AgentBlazor.Core\AgentBlazor.Core.csproj"
        OutputDir = Join-Path $repoRoot "src\AgentBlazor.Core\bin\Release"
    },
    @{
        Id = "AgentBlazor.ProviderAdapters"
        Project = Join-Path $repoRoot "src\AgentBlazor.ProviderAdapters\AgentBlazor.ProviderAdapters.csproj"
        OutputDir = Join-Path $repoRoot "src\AgentBlazor.ProviderAdapters\bin\Release"
    },
    @{
        Id = "AgentBlazor.Hosting"
        Project = Join-Path $repoRoot "src\AgentBlazor.Hosting\AgentBlazor.Hosting.csproj"
        OutputDir = Join-Path $repoRoot "src\AgentBlazor.Hosting\bin\Release"
    },
    @{
        Id = "AgentBlazor"
        Project = Join-Path $repoRoot "src\AgentBlazor.Components\AgentBlazor.Components.csproj"
        OutputDir = Join-Path $repoRoot "src\AgentBlazor.Components\bin\Release"
    },
    @{
        Id = "AgentBlazor.Client"
        Project = Join-Path $repoRoot "src\AgentBlazor.Client\AgentBlazor.Client.csproj"
        OutputDir = Join-Path $repoRoot "src\AgentBlazor.Client\bin\Release"
    },
    @{
        Id = "AgentBlazor.EntityFrameworkCore"
        Project = Join-Path $repoRoot "src\AgentBlazor.EntityFrameworkCore\AgentBlazor.EntityFrameworkCore.csproj"
        OutputDir = Join-Path $repoRoot "src\AgentBlazor.EntityFrameworkCore\bin\Release"
    },
    @{
        Id = "AgentBlazor.Cli"
        Project = Join-Path $repoRoot "src\AgentBlazor.Cli\AgentBlazor.Cli.csproj"
        OutputDir = Join-Path $repoRoot "src\AgentBlazor.Cli\bin\Release"
    }
)

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host "==> $Message" -ForegroundColor Cyan
    & $Action
}

function Resolve-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDir,
        [Parameter(Mandatory = $true)]
        [string]$Id
    )

    $packages = Get-ChildItem -Path $OutputDir -Filter "$Id.*.nupkg" -File |
        Where-Object { $_.Name -notlike "*.symbols.nupkg" } |
        Sort-Object LastWriteTimeUtc -Descending

    if ($packages.Count -eq 0) {
        throw "No package matching '$Id.*.nupkg' was found in '$OutputDir'. Run with -Pack or pack the project first."
    }

    $name = $packages[0].BaseName
    if (-not $name.StartsWith("$Id.")) {
        throw "Could not infer package version from '$($packages[0].Name)'."
    }

    return $name.Substring($Id.Length + 1)
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed."
    }
}

function Clear-LocalPackageCache {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $homePath = $env:USERPROFILE
    if ([string]::IsNullOrWhiteSpace($homePath)) {
        $homePath = $env:HOME
    }

    if ([string]::IsNullOrWhiteSpace($homePath)) {
        throw "Unable to resolve the user home directory for NuGet package cache cleanup."
    }

    $packageIds = @(
        "agentblazor",
        "agentblazor.core",
        "agentblazor.hosting",
        "agentblazor.entityframeworkcore",
        "agentblazor.licensing",
        "agentblazor.provideradapters",
        "agentblazor.cli"
    )

    foreach ($packageId in $packageIds) {
        $path = Join-Path $homePath ".nuget/packages/$packageId/$Version"
        if (Test-Path $path) {
            Remove-Item -Recurse -Force $path
        }
    }
}

function Copy-PackageToFeed {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Definition,
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $packagePath = Join-Path $Definition.OutputDir "$($Definition.Id).$Version.nupkg"
    if (-not (Test-Path $packagePath)) {
        throw "Expected package '$packagePath' does not exist. Run with -Pack or build the release packages first."
    }

    Copy-Item -LiteralPath $packagePath -Destination $localFeed -Force

    $symbolPackagePath = Join-Path $Definition.OutputDir "$($Definition.Id).$Version.snupkg"
    if (Test-Path $symbolPackagePath) {
        Copy-Item -LiteralPath $symbolPackagePath -Destination $localFeed -Force
    }
}

function Test-AllPackagesAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    foreach ($definition in $packageDefinitions) {
        $packagePath = Join-Path $definition.OutputDir "$($definition.Id).$Version.nupkg"
        if (-not (Test-Path $packagePath)) {
            return $false
        }
    }

    return $true
}

function Resolve-AgentBlazorToolPath {
    $candidates = @(
        (Join-Path $toolPath "agentblazor"),
        (Join-Path $toolPath "agentblazor.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "AgentBlazor CLI executable was not found in '$toolPath'."
}

function Write-TextFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    Set-Content -Path $Path -Value $Content -NoNewline
}

if ($Pack -and [string]::IsNullOrWhiteSpace($PackageVersion)) {
    throw "PackageVersion is required when -Pack is used."
}

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = Resolve-PackageVersion -OutputDir (Join-Path $repoRoot "src\AgentBlazor.Components\bin\Release") -Id "AgentBlazor"
}

Invoke-Step "Resetting scratch workspace" {
    if (Test-Path $scratchRoot) {
        Remove-Item -Recurse -Force $scratchRoot
    }

    New-Item -ItemType Directory -Force $scratchRoot | Out-Null
    New-Item -ItemType Directory -Force $localFeed | Out-Null
}

Invoke-Step "Preparing local package feed" {
    $shouldPack = $Pack -or -not (Test-AllPackagesAvailable -Version $PackageVersion)

    if ($shouldPack) {
        foreach ($definition in $packageDefinitions) {
            Invoke-DotNet -Arguments @(
                "pack",
                $definition.Project,
                "-nologo",
                "-c", "Release",
                "-o", $localFeed,
                "/p:UseSharedCompilation=false",
                "/p:Version=$PackageVersion",
                "/p:PackageVersion=$PackageVersion",
                "/p:RestoreLockedMode=false",
                "/p:RestoreForceEvaluate=true"
            )

            $packedPackagePath = Join-Path $localFeed "$($definition.Id).$PackageVersion.nupkg"
            if (-not (Test-Path $packedPackagePath)) {
                throw "Expected packed package '$packedPackagePath' does not exist."
            }

            New-Item -ItemType Directory -Force $definition.OutputDir | Out-Null
            Copy-Item -LiteralPath $packedPackagePath -Destination $definition.OutputDir -Force

            $packedSymbolPackagePath = Join-Path $localFeed "$($definition.Id).$PackageVersion.snupkg"
            if (Test-Path $packedSymbolPackagePath) {
                Copy-Item -LiteralPath $packedSymbolPackagePath -Destination $definition.OutputDir -Force
            }
        }
    }
    else {
        foreach ($definition in $packageDefinitions) {
            Copy-PackageToFeed -Definition $definition -Version $PackageVersion
        }
    }
}

Invoke-Step "Clearing cached package versions" {
    Clear-LocalPackageCache -Version $PackageVersion
}

Invoke-Step "Writing isolated NuGet configuration" {
    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-agentblazor" value="$localFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-agentblazor">
      <package pattern="AgentBlazor*" />
      <package pattern="agentblazor*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@

    Write-TextFile -Path $nugetConfigPath -Content $nugetConfig
}

Invoke-Step "Writing local central package management file" {
    $directoryPackagesProps = @"
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="AgentBlazor" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@

    Write-TextFile -Path $directoryPackagesPropsPath -Content $directoryPackagesProps
}

Invoke-Step "Creating scratch Blazor app" {
    Invoke-DotNet -Arguments @("new", "blazor", "-n", $ProjectName, "-o", $appRoot, "--no-https") -WorkingDirectory $scratchRoot
    Invoke-DotNet -Arguments @("new", "sln", "--format", "sln", "-n", $ProjectName, "-o", $scratchRoot) -WorkingDirectory $scratchRoot
    Invoke-DotNet -Arguments @("sln", $solutionPath, "add", $projectPath) -WorkingDirectory $scratchRoot
}

Invoke-Step "Writing package reference" {
    $projectContent = Get-Content -Path $projectPath -Raw
    $packageReference = @"
  <ItemGroup>
    <PackageReference Include="AgentBlazor" />
  </ItemGroup>

"@

    $updatedProject = $projectContent.Replace("</Project>", "$packageReference</Project>")
    Set-Content -Path $projectPath -Value $updatedProject
}

Invoke-Step "Writing smoke-test host files" {
    New-Item -ItemType Directory -Force $servicesDir | Out-Null
    New-Item -ItemType Directory -Force $workflowsDir | Out-Null

    $programContent = @"
using AgentBlazor;
using MudBlazor.Services;
using $ProjectName.Components;
using $ProjectName.Services;
using $ProjectName.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<IMyService, DemoItemService>();

builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"] ?? "$OpenAIModel");

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<MyCapabilities>("assistant", agent =>
        {
            agent.WithDescription("Help users complete their tasks.");
            agent.WithRoutePrefixes("/");
        });
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();

app.Run();
"@

    $appRazorContent = @"
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <ResourcePreloader />
    <link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
    <link rel="stylesheet" href="@Assets["app.css"]" />
    <link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
    <link rel="stylesheet" href="@Assets["$ProjectName.styles.css"]" />
    <ImportMap />
    <link rel="icon" type="image/png" href="favicon.png" />
    <HeadOutlet @rendermode="InteractiveServer" />
</head>

<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
    <script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
    <script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
</body>

</html>
"@

    $importsContent = @"
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using AgentBlazor.Components
@using $ProjectName
@using $ProjectName.Components
@using $ProjectName.Components.Layout
"@

    $mainLayoutContent = @"
@inherits LayoutComponentBase

<AgentBlazorShell>
    <div class="page">
        <div class="sidebar">
            <NavMenu />
        </div>

        <main>
            <div class="top-row px-4">
                <a href="https://learn.microsoft.com/aspnet/core/" target="_blank">About</a>
            </div>

            <article class="content px-4">
                @Body
            </article>
        </main>
    </div>

    <div id="blazor-error-ui" data-nosnippet>
        An unhandled error has occurred.
        <a href="." class="reload">Reload</a>
        <span class="dismiss">x</span>
    </div>
</AgentBlazorShell>
"@

    $homeContent = @"
@page "/"

<PageTitle>Package smoke test</PageTitle>

<h1>Package smoke test</h1>

<p>Package restore, host wiring, and chat surface are active.</p>
"@

    $serviceInterfaceContent = @"
namespace $ProjectName.Services;

public interface IMyService
{
    Task<IReadOnlyList<string>> SearchAsync(string query);
    Task SubmitAsync(Guid orderId);
}
"@

    $serviceContent = @"
namespace $ProjectName.Services;

public sealed class DemoItemService : IMyService
{
    public Task<IReadOnlyList<string>> SearchAsync(string query)
    {
        IReadOnlyList<string> results =
        [
            `$"Result for {query}",
            `$"Another result for {query}"
        ];

        return Task.FromResult(results);
    }

    public Task SubmitAsync(Guid orderId)
    {
        _ = orderId;
        return Task.CompletedTask;
    }
}
"@

    $capabilitiesContent = @"
using AgentBlazor.App;
using AgentBlazor.Attributes;
using $ProjectName.Services;

namespace $ProjectName.Workflows;

[AgentCapability("assistant")]
public sealed class MyCapabilities(IMyService service)
{
    [AgentAction("Search for items")]
    public async Task<CapabilityResult> SearchAsync(
        [AgentParam("Search query")] string query)
    {
        var results = await service.SearchAsync(query);
        return CapabilityResult.Success(`$"Found {results.Count} items.");
    }

    [AgentAction("Submit order", RequiresApproval = true)]
    public async Task<CapabilityResult> SubmitOrderAsync(
        [AgentParam("Order ID")] Guid orderId)
    {
        await service.SubmitAsync(orderId);
        return CapabilityResult.Success("Order submitted.")
            .WithNextActions("View order status", "Create another order");
    }
}
"@

    $apiKey = if ([string]::IsNullOrWhiteSpace($OpenAIApiKey)) { "placeholder-key" } else { $OpenAIApiKey }
    $appSettingsContent = @"
{
  "OpenAI": {
    "ApiKey": "$apiKey",
    "Model": "$OpenAIModel"
  }
}
"@

    Write-TextFile -Path $programPath -Content $programContent
    Write-TextFile -Path $appRazorPath -Content $appRazorContent
    Write-TextFile -Path $importsPath -Content $importsContent
    Write-TextFile -Path $mainLayoutPath -Content $mainLayoutContent
    Write-TextFile -Path $homePath -Content $homeContent
    Write-TextFile -Path (Join-Path $servicesDir "IMyService.cs") -Content $serviceInterfaceContent
    Write-TextFile -Path (Join-Path $servicesDir "DemoItemService.cs") -Content $serviceContent
    Write-TextFile -Path (Join-Path $workflowsDir "MyCapabilities.cs") -Content $capabilitiesContent
    Write-TextFile -Path $appSettingsDevelopmentPath -Content $appSettingsContent
}

Invoke-Step "Restoring and building against the isolated local feed" {
    if (Test-Path (Join-Path $appRoot "packages.lock.json")) {
        Remove-Item -Force (Join-Path $appRoot "packages.lock.json")
    }

    if (Test-Path (Join-Path $appRoot "obj")) {
        Remove-Item -Recurse -Force (Join-Path $appRoot "obj")
    }

    Invoke-DotNet -Arguments @(
        "restore",
        $projectPath,
        "--configfile", $nugetConfigPath,
        "-nologo"
    ) -WorkingDirectory $scratchRoot

    Invoke-DotNet -Arguments @(
        "build",
        $projectPath,
        "-nologo",
        "--no-restore"
    ) -WorkingDirectory $scratchRoot
}

Invoke-Step "Installing the local CLI package" {
    if (Test-Path $toolPath) {
        Remove-Item -Recurse -Force $toolPath
    }

    Invoke-DotNet -Arguments @(
        "tool",
        "install",
        "AgentBlazor.Cli",
        "--version", $PackageVersion,
        "--tool-path", $toolPath,
        "--configfile", $nugetConfigPath
    ) -WorkingDirectory $scratchRoot

    $cliExe = Resolve-AgentBlazorToolPath
    $cliVersion = (& $cliExe --version).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "agentblazor --version failed."
    }

    if ($cliVersion -ne $PackageVersion) {
        throw "Installed AgentBlazor.Cli reported version '$cliVersion', expected '$PackageVersion'."
    }
}

Invoke-Step "Generating AGENT.md with the CLI" {
    $cliExe = Resolve-AgentBlazorToolPath
    & $cliExe init $solutionPath --host $ProjectName --non-interactive
    if ($LASTEXITCODE -ne 0) {
        throw "agentblazor init failed."
    }

    $agentMdPath = Join-Path (Join-Path $scratchRoot ".agentblazor") "AGENT.md"
    $agentMd = Get-Content -Path $agentMdPath -Raw

    if ($agentMd -notmatch "Search" -or $agentMd -notmatch "Submit Order") {
        throw "AGENT.md did not include the expected workflow actions."
    }
}

Invoke-Step "Starting the scratch app" {
    $port = 5187
    $stdoutLog = Join-Path $scratchRoot "agentblazor-smoke.stdout.log"
    $stderrLog = Join-Path $scratchRoot "agentblazor-smoke.stderr.log"

    foreach ($path in @($stdoutLog, $stderrLog)) {
        if (Test-Path $path) {
            Remove-Item -Force $path
        }
    }

    $process = Start-Process dotnet `
        -ArgumentList @("run", "--project", $projectPath, "--no-build", "--urls", "http://127.0.0.1:$port") `
        -WorkingDirectory $scratchRoot `
        -PassThru `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog

    try {
        $ready = $false
        for ($i = 0; $i -lt 60; $i++) {
            Start-Sleep -Milliseconds 500

            $stdout = if (Test-Path $stdoutLog) { Get-Content -Path $stdoutLog -Raw } else { "" }
            $stderr = if (Test-Path $stderrLog) { Get-Content -Path $stderrLog -Raw } else { "" }

            if ($stdout -match "Now listening on:" -or $stderr -match "Now listening on:") {
                $ready = $true
                break
            }

            if ($process.HasExited) {
                break
            }
        }

        if (-not $ready) {
            if (Test-Path $stdoutLog) {
                Get-Content -Path $stdoutLog -Raw | Write-Host
            }

            if (Test-Path $stderrLog) {
                Get-Content -Path $stderrLog -Raw | Write-Host
            }

            throw "Scratch app failed to start."
        }

        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$port/" -UseBasicParsing
        if ($response.StatusCode -ne 200 -or $response.Content -notmatch "Package smoke test") {
            throw "Scratch app did not render the expected home page."
        }

        if (-not [string]::IsNullOrWhiteSpace($OpenAIApiKey)) {
            $runRequest = @{
                threadId = "package-smoke"
                runId = "package-smoke"
                messages = @(
                    @{
                        role = "user"
                        content = "search for widgets"
                    }
                )
                state = @{}
            } | ConvertTo-Json -Depth 5

            $runResponse = Invoke-WebRequest `
                -Uri "http://127.0.0.1:$port/agentblazor/agui/run" `
                -Method Post `
                -ContentType "application/json" `
                -Body $runRequest `
                -UseBasicParsing

            if ($runResponse.StatusCode -ne 200 -or $runResponse.Content -notmatch "search_async") {
                throw "AG-UI run did not execute the expected semantic workflow action."
            }
        }
        else {
            Write-Host "Skipping live AG-UI run because no OpenAI API key was provided." -ForegroundColor Yellow
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }
}

Write-Host ""
Write-Host "Smoke test passed for AgentBlazor $PackageVersion." -ForegroundColor Green

if ($KeepScratch) {
    Write-Host "Scratch app kept at: $scratchRoot"
}
else {
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            if (Test-Path $scratchRoot) {
                Remove-Item -Recurse -Force $scratchRoot
            }

            break
        }
        catch {
            if ($attempt -eq 4) {
                throw
            }

            Start-Sleep -Milliseconds 500
        }
    }

    Write-Host "Scratch app removed: $scratchRoot"
}
