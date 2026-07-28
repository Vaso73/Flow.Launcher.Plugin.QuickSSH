[CmdletBinding()]
param(
    [string]$PreviousVersion,
    [string]$ReleaseLabel,
    [string]$CurrentVersion
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function ConvertTo-QuickSshVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw "$Name must use strict major.minor.patch SemVer without prerelease or build metadata: '$Value'."
    }

    return [pscustomobject]@{
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
        Text = $Value
    }
}

function Get-QuickSshNextVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PreviousVersion,

        [Parameter(Mandatory = $true)]
        [string]$ReleaseLabel
    )

    $previous = ConvertTo-QuickSshVersion -Value $PreviousVersion -Name 'PreviousVersion'

    switch ($ReleaseLabel) {
        'release:patch' { return "$($previous.Major).$($previous.Minor).$($previous.Patch + 1)" }
        'release:minor' { return "$($previous.Major).$($previous.Minor + 1).0" }
        'release:major' { return "$($previous.Major + 1).0.0" }
        'skip-release' { return $previous.Text }
        default { throw "Unsupported release label '$ReleaseLabel'." }
    }
}

function Assert-QuickSshReleaseReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PreviousVersion,

        [Parameter(Mandatory = $true)]
        [string]$ReleaseLabel,

        [Parameter(Mandatory = $true)]
        [string]$CurrentVersion
    )

    $null = ConvertTo-QuickSshVersion -Value $CurrentVersion -Name 'CurrentVersion'
    $expected = Get-QuickSshNextVersion -PreviousVersion $PreviousVersion -ReleaseLabel $ReleaseLabel

    if ($CurrentVersion -ne $expected) {
        throw "Release label '$ReleaseLabel' requires plugin version '$expected' after '$PreviousVersion', but plugin.json contains '$CurrentVersion'."
    }

    return [pscustomobject]@{
        PreviousVersion = $PreviousVersion
        CurrentVersion = $CurrentVersion
        ExpectedVersion = $expected
        ReleaseLabel = $ReleaseLabel
        SkipRelease = ($ReleaseLabel -eq 'skip-release')
        Tag = "v$CurrentVersion"
    }
}

if ($PSBoundParameters.ContainsKey('PreviousVersion') -or
    $PSBoundParameters.ContainsKey('ReleaseLabel') -or
    $PSBoundParameters.ContainsKey('CurrentVersion')) {
    if (-not $PSBoundParameters.ContainsKey('PreviousVersion') -or
        -not $PSBoundParameters.ContainsKey('ReleaseLabel') -or
        -not $PSBoundParameters.ContainsKey('CurrentVersion')) {
        throw 'PreviousVersion, ReleaseLabel, and CurrentVersion must be supplied together.'
    }

    Assert-QuickSshReleaseReady `
        -PreviousVersion $PreviousVersion `
        -ReleaseLabel $ReleaseLabel `
        -CurrentVersion $CurrentVersion |
        ConvertTo-Json -Compress
}
