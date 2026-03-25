param(
    [string]$PackageVersion = "",
    [string]$PackageId = "AgentBlazor",
    [string]$ProjectName = "package-smoke",
    [switch]$Pack,
    [switch]$KeepScratch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageProject = Join-Path (Join-Path (Join-Path $repoRoot "src") "AgentBlazor.Components") "AgentBlazor.Components.csproj"
$packageOutputDir = Join-Path (Join-Path (Join-Path $repoRoot "src") "AgentBlazor.Components") (Join-Path "bin" "Release")
$scratchRoot = Join-Path (Join-Path $repoRoot ".tmp") $ProjectName

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

if ($Pack -and [string]::IsNullOrWhiteSpace($PackageVersion)) {
    throw "PackageVersion is required when -Pack is used."
}

if ($Pack) {
    Invoke-Step "Packing $PackageId $PackageVersion" {
        dotnet pack $packageProject -nologo -c Release /p:UseSharedCompilation=false "/p:PackageVersion=$PackageVersion"
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet pack failed."
        }
    }
}

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = Resolve-PackageVersion -OutputDir $packageOutputDir -Id $PackageId
}

Invoke-Step "Resetting scratch app" {
    if (Test-Path $scratchRoot) {
        Remove-Item -Recurse -Force $scratchRoot
    }
}

Invoke-Step "Creating scratch Blazor app" {
    dotnet new blazor -o $scratchRoot --no-https
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet new blazor failed."
    }
}

$importsPath = Join-Path (Join-Path $scratchRoot "Components") "_Imports.razor"
$homePath = Join-Path (Join-Path (Join-Path $scratchRoot "Components") "Pages") "Home.razor"
$projectPath = Join-Path $scratchRoot "$ProjectName.csproj"
$programPath = Join-Path $scratchRoot "Program.cs"

Invoke-Step "Writing local package reference" {
    $projectContent = Get-Content -Path $projectPath -Raw
    $packageReference = @"
  <ItemGroup>
    <PackageReference Include="$PackageId" VersionOverride="$PackageVersion" />
  </ItemGroup>

"@

    $updatedProject = $projectContent.Replace("</Project>", "$packageReference</Project>")
    Set-Content -Path $projectPath -Value $updatedProject
}

Invoke-Step "Restoring against local package source" {
    dotnet restore $projectPath -nologo --force-evaluate --source $packageOutputDir --source "https://api.nuget.org/v3/index.json"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }
}

Invoke-Step "Writing smoke test component usage" {
    $programContent = Get-Content -Path $programPath -Raw
    if ($programContent -notmatch "using AgentBlazor;") {
        $programContent = "using AgentBlazor;`r`n" + $programContent
    }

    if ($programContent -notmatch "using MudBlazor.Services;") {
        $programContent = "using MudBlazor.Services;`r`n" + $programContent
    }

    if ($programContent -notmatch [regex]::Escape("builder.Services.AddAgentBlazor();")) {
        $programContent = $programContent.Replace(
            "builder.Services.AddRazorComponents()`r`n    .AddInteractiveServerComponents();",
            "builder.Services.AddRazorComponents()`r`n    .AddInteractiveServerComponents();`r`nbuilder.Services.AddMudServices();`r`nbuilder.Services.AddAgentBlazor();")
    }

    Set-Content -Path $programPath -Value $programContent

    $existingImports = Get-Content -Path $importsPath -Raw
    $requiredImports = @(
        "@using AgentBlazor.Components",
        "@using MudBlazor"
    )

    foreach ($import in $requiredImports) {
        if ($existingImports -notmatch [regex]::Escape($import)) {
            if (-not $existingImports.EndsWith("`r`n")) {
                $existingImports += "`r`n"
            }

            $existingImports += "$import`r`n"
        }
    }

    Set-Content -Path $importsPath -Value $existingImports

    @'
@page "/"

<PageTitle>Home</PageTitle>

<h1>Package smoke test</h1>

<AgentTabs AgentId="smoke-tabs">
    <MudTabPanel Text="Overview">
        <p>Local NuGet package resolved correctly.</p>
    </MudTabPanel>
    <MudTabPanel Text="Details">
        <p>AgentBlazor component compiled in a clean consumer app.</p>
    </MudTabPanel>
</AgentTabs>
'@ | Set-Content -Path $homePath
}

Invoke-Step "Building scratch app against local package" {
    dotnet build $projectPath -nologo --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed."
    }
}

Invoke-Step "Running scratch app startup check" {
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
        if ($response.StatusCode -ne 200 -or $response.Content -notmatch "smoke-tabs") {
            throw "Scratch app did not render the expected AgentTabs markup."
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }
}

Write-Host ""
Write-Host "Smoke test passed for $PackageId $PackageVersion." -ForegroundColor Green

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
