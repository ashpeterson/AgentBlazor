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
    dotnet restore $projectPath -nologo --source $packageOutputDir --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }
}

Invoke-Step "Writing smoke test component usage" {
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

Write-Host ""
Write-Host "Smoke test passed for $PackageId $PackageVersion." -ForegroundColor Green

if ($KeepScratch) {
    Write-Host "Scratch app kept at: $scratchRoot"
}
else {
    Remove-Item -Recurse -Force $scratchRoot
    Write-Host "Scratch app removed: $scratchRoot"
}
