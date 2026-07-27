$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = @(& git @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $details = ($output | Out-String).Trim()
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE.`n$details"
    }

    return $output
}

function Get-PluginManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Revision
    )

    $json = (Invoke-Git -Arguments @("show", "${Revision}:plugin.json")) -join [Environment]::NewLine
    return $json | ConvertFrom-Json
}

function Get-ManifestFingerprint {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Manifest
    )

    $rows = @(
        $Manifest.PSObject.Properties |
            Where-Object { $_.Name -ne "Version" } |
            Sort-Object Name |
            ForEach-Object {
                $value = $_.Value | ConvertTo-Json -Compress -Depth 20
                "$($_.Name)=$value"
            }
    )

    return $rows -join "`n"
}

if ($env:GITHUB_EVENT_NAME -ne "pull_request") {
    Write-Host "Docs check skipped: not a pull_request event."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_EVENT_PATH) -or -not (Test-Path $env:GITHUB_EVENT_PATH)) {
    throw "GITHUB_EVENT_PATH is missing or does not exist."
}

$event = Get-Content $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
$baseSha = [string]$event.pull_request.base.sha
$headSha = [string]$event.pull_request.head.sha

if ($baseSha -notmatch '^[0-9a-fA-F]{40}$' -or $headSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Could not determine valid base/head SHA values from the pull request event."
}

Invoke-Git -Arguments @("fetch", "--no-tags", "origin", $baseSha, $headSha) | Out-Null

$changedFiles = @(
    Invoke-Git -Arguments @(
        "diff",
        "--name-only",
        "--diff-filter=ACMR",
        $baseSha,
        $headSha
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

if ($changedFiles.Count -eq 0) {
    Write-Host "Docs check passed: no changed files detected."
    exit 0
}

Write-Host "Changed files:"
$changedFiles | ForEach-Object { Write-Host " - $_" }

$readmeChanged = $changedFiles -contains "README.md"

$userFacingPatterns = @(
    '^ActionCommandBuilder\.cs$',
    '^AutoCompleter\.cs$',
    '^CommandInputGuard\.cs$',
    '^CommandProfile\.cs$',
    '^Flow\.Launcher\.Plugin\.QuickSSH\.csproj$',
    '^Main\.cs$',
    '^Profile\.cs$',
    '^ProfileImportService\.cs$',
    '^ProfileSerializer\.cs$',
    '^ProfileStorage\.cs$',
    '^ProfileWizard\.cs$',
    '^RemoteKeyInstallBuilder\.cs$',
    '^SearchMatcher\.cs$',
    '^ShellLaunchPlan\.cs$',
    '^SshCommandBuilder\.cs$',
    '^SshConfigParser\.cs$',
    '^SshKeyEntry\.cs$',
    '^SshProfile\.cs$',
    '^Utils\.cs$',
    '^Images\/',
    '^Languages\/',
    '^plugin\.json$'
)

$userFacingChangedFiles = @(
    @(
        foreach ($file in $changedFiles) {
            foreach ($pattern in $userFacingPatterns) {
                if ($file -match $pattern) {
                    $file
                    break
                }
            }
        }
    ) | Sort-Object -Unique
)

# A plugin.json change that modifies only Version does not require README changes.
if ($userFacingChangedFiles -contains "plugin.json") {
    $pluginBase = Get-PluginManifest -Revision $baseSha
    $pluginHead = Get-PluginManifest -Revision $headSha

    if ((Get-ManifestFingerprint -Manifest $pluginBase) -eq
        (Get-ManifestFingerprint -Manifest $pluginHead)) {
        Write-Host "plugin.json change is version-only — exempted from the docs gate."
        $userFacingChangedFiles = @(
            $userFacingChangedFiles | Where-Object { $_ -ne "plugin.json" }
        )
    }
}

if ($userFacingChangedFiles.Count -eq 0) {
    Write-Host "Docs check passed: no tracked user-facing files changed."
    Write-Host "README_REQUIRED=NO"
    exit 0
}

Write-Host "Tracked user-facing files changed:"
$userFacingChangedFiles | ForEach-Object { Write-Host " - $_" }

if (-not $readmeChanged) {
    throw "README.md was not updated even though tracked user-facing files changed."
}

Invoke-Git -Arguments @("cat-file", "-e", "${headSha}:README.md") | Out-Null

Write-Host "Docs check passed: README.md was updated."
Write-Host "README_REQUIRED=YES"
Write-Host "README_CHANGED=YES"
Write-Host "USER_FACING_FILE_COUNT=$($userFacingChangedFiles.Count)"
exit 0
