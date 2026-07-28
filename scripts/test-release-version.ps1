$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'resolve-release-version.ps1')

$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Actual,

        [Parameter(Mandatory = $true)]
        [object]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Actual -ne $Expected) {
        $failures.Add("$Name expected '$Expected' but got '$Actual'.")
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    try {
        & $Action
        $failures.Add("$Name did not throw.")
    }
    catch {
        Write-Host "PASS expected failure: $Name"
    }
}


Assert-Equal `
    -Actual (Get-QuickSshLatestTaggedVersion -Tags @('v3.6.0')) `
    -Expected '3.6.0' `
    -Name 'single tag remains a complete scalar value'

Assert-Equal `
    -Actual (Get-QuickSshLatestTaggedVersion -Tags @('v3.7.2', 'v4.0.0', 'v3.10.0')) `
    -Expected '4.0.0' `
    -Name 'latest tag uses semantic ordering'

Assert-Equal `
    -Actual (Get-QuickSshLatestTaggedVersion -Tags @('preview', 'v3.6.1-beta', 'v3.6.0')) `
    -Expected '3.6.0' `
    -Name 'non-strict tags are ignored'

Assert-Throws `
    -Name 'no strict SemVer tag' `
    -Action {
        Get-QuickSshLatestTaggedVersion -Tags @('preview', 'v3.6.1-beta')
    }

Assert-Equal `
    -Actual (Get-QuickSshNextVersion -PreviousVersion '3.6.0' -ReleaseLabel 'release:patch') `
    -Expected '3.6.1' `
    -Name 'patch bump'

Assert-Equal `
    -Actual (Get-QuickSshNextVersion -PreviousVersion '3.6.0' -ReleaseLabel 'release:minor') `
    -Expected '3.7.0' `
    -Name 'minor bump'

Assert-Equal `
    -Actual (Get-QuickSshNextVersion -PreviousVersion '3.6.0' -ReleaseLabel 'release:major') `
    -Expected '4.0.0' `
    -Name 'major bump'

Assert-Equal `
    -Actual (Get-QuickSshNextVersion -PreviousVersion '3.6.0' -ReleaseLabel 'skip-release') `
    -Expected '3.6.0' `
    -Name 'skip release'

$ready = Assert-QuickSshReleaseReady `
    -PreviousVersion '3.6.0' `
    -ReleaseLabel 'release:patch' `
    -CurrentVersion '3.6.1'

Assert-Equal -Actual $ready.Tag -Expected 'v3.6.1' -Name 'release tag'
Assert-Equal -Actual $ready.SkipRelease -Expected $false -Name 'release flag'

Assert-Throws `
    -Name 'unchanged release version' `
    -Action {
        Assert-QuickSshReleaseReady `
            -PreviousVersion '3.6.0' `
            -ReleaseLabel 'release:patch' `
            -CurrentVersion '3.6.0'
    }

Assert-Throws `
    -Name 'skipped patch version' `
    -Action {
        Assert-QuickSshReleaseReady `
            -PreviousVersion '3.6.0' `
            -ReleaseLabel 'release:patch' `
            -CurrentVersion '3.6.2'
    }

Assert-Throws `
    -Name 'unsupported label' `
    -Action {
        Get-QuickSshNextVersion `
            -PreviousVersion '3.6.0' `
            -ReleaseLabel 'release:banana'
    }

Assert-Throws `
    -Name 'invalid previous SemVer' `
    -Action {
        Get-QuickSshNextVersion `
            -PreviousVersion 'v3.6.0' `
            -ReleaseLabel 'release:patch'
    }

Assert-Throws `
    -Name 'invalid current SemVer' `
    -Action {
        Assert-QuickSshReleaseReady `
            -PreviousVersion '3.6.0' `
            -ReleaseLabel 'release:patch' `
            -CurrentVersion '3.6.1-beta'
    }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'RELEASE_VERSION_TESTS=PASS'
exit 0
