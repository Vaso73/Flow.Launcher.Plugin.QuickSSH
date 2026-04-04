$ErrorActionPreference = "Stop"

if ($env:GITHUB_EVENT_NAME -ne "pull_request") {
    Write-Host "Docs check skipped: not a pull_request event."
    exit 0
}

$event = Get-Content $env:GITHUB_EVENT_PATH | ConvertFrom-Json
$baseSha = $event.pull_request.base.sha
$headSha = $event.pull_request.head.sha

if ([string]::IsNullOrWhiteSpace($baseSha) -or [string]::IsNullOrWhiteSpace($headSha)) {
    Write-Error "Could not determine base/head SHA from pull request event."
    exit 1
}

git fetch --no-tags origin $baseSha $headSha | Out-Null

$changedFiles = @(git diff --name-only $baseSha $headSha)

if ($changedFiles.Count -eq 0) {
    Write-Host "No changed files detected."
    exit 0
}

Write-Host "Changed files:"
$changedFiles | ForEach-Object { Write-Host " - $_" }

$readmeChanged = $changedFiles -contains "README.md"

$userFacingPatterns = @(
    '^Main\.cs$',
    '^AutoCompleter\.cs$',
    '^SshConfigParser\.cs$',
    '^SshCommandBuilder\.cs$',
    '^Profile\.cs$',
    '^Languages\/',
    '^plugin\.json$'
)

$userFacingChangedFiles = @()

foreach ($file in $changedFiles) {
    foreach ($pattern in $userFacingPatterns) {
        if ($file -match $pattern) {
            $userFacingChangedFiles += $file
            break
        }
    }
}

$userFacingChangedFiles = $userFacingChangedFiles | Sort-Object -Unique

# Exempt plugin.json when only the Version field changed (version-only release bump)
if ($userFacingChangedFiles -contains "plugin.json") {
    $pluginBase = git show "${baseSha}:plugin.json" 2>$null | ConvertFrom-Json
    $pluginHead = git show "${headSha}:plugin.json" 2>$null | ConvertFrom-Json

    if ($null -ne $pluginBase -and $null -ne $pluginHead) {
        $baseHash = @{}
        $pluginBase.PSObject.Properties | Where-Object { $_.Name -ne "Version" } | ForEach-Object { $baseHash[$_.Name] = $_.Value }

        $headHash = @{}
        $pluginHead.PSObject.Properties | Where-Object { $_.Name -ne "Version" } | ForEach-Object { $headHash[$_.Name] = $_.Value }

        $isVersionOnly = ($baseHash.Count -eq $headHash.Count)
        if ($isVersionOnly) {
            foreach ($key in $baseHash.Keys) {
                if (-not $headHash.ContainsKey($key) -or ($headHash[$key] | ConvertTo-Json -Compress -Depth 10) -ne ($baseHash[$key] | ConvertTo-Json -Compress -Depth 10)) {
                    $isVersionOnly = $false
                    break
                }
            }
        }

        if ($isVersionOnly) {
            Write-Host "plugin.json change is version-only bump — exempted from docs gate."
            $userFacingChangedFiles = @($userFacingChangedFiles | Where-Object { $_ -ne "plugin.json" })
        }
    }
}

if ($userFacingChangedFiles.Count -eq 0) {
    Write-Host "Docs check passed: no tracked user-facing files changed."
    exit 0
}

Write-Host "Tracked user-facing files changed:"
$userFacingChangedFiles | ForEach-Object { Write-Host " - $_" }

if (-not $readmeChanged) {
    Write-Error "README.md was not updated even though tracked user-facing files changed."
    exit 1
}

Write-Host "Docs check passed: README.md was updated."
exit 0
