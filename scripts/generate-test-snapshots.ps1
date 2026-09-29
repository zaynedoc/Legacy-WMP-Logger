# DESTRUCTIVE TEST-DATA SCRIPT
#
# This script replaces every JSON file in data\snapshots with generated test history.
# BACK UP YOUR DATA BEFORE RUNNING. As an additional safeguard, it copies the current
# snapshots to data\backups\pre-test-snapshots-<timestamp> before making any changes.
# It does not alter desktop-settings.json and it is intentionally not exposed in the app UI.

[CmdletBinding()]
param(
    [ValidateRange(8, 2000)]
    [int]$SnapshotCount = 360,

    [int]$Seed = 20260929,

    [string]$DataDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'data')
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$expectedDataDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'data'))
$resolvedDataDirectory = [System.IO.Path]::GetFullPath($DataDirectory)
if (-not [string]::Equals($resolvedDataDirectory, $expectedDataDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "For safety, test snapshots can only be generated in '$expectedDataDirectory'."
}

$snapshotDirectory = Join-Path $resolvedDataDirectory 'snapshots'
if (-not (Test-Path -LiteralPath $snapshotDirectory -PathType Container)) {
    throw "Snapshot directory '$snapshotDirectory' does not exist. Capture a real snapshot first."
}

$existingSnapshots = @(Get-ChildItem -LiteralPath $snapshotDirectory -Filter '*.json' -File | Sort-Object Name)
if ($existingSnapshots.Count -eq 0) {
    throw 'No real snapshots are available to derive test tracks from. Capture a real snapshot first.'
}

$sourceSnapshot = Get-Content -LiteralPath $existingSnapshots[-1].FullName -Raw | ConvertFrom-Json
$sourceTracks = @($sourceSnapshot.Tracks)
if ($sourceTracks.Count -eq 0) {
    throw 'The newest snapshot has no tracks, so test data cannot be generated.'
}

$backupDirectory = Join-Path (Join-Path $resolvedDataDirectory 'backups') ("pre-test-snapshots-{0}" -f (Get-Date -Format 'yyyyMMddTHHmmss'))
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
Copy-Item -LiteralPath $existingSnapshots.FullName -Destination $backupDirectory -Force

# The curve concentrates more generated captures near today while retaining a useful five-year tail.
$random = [System.Random]::new($Seed)
$nowUtc = [DateTime]::UtcNow
$startUtc = $nowUtc.AddYears(-5)
$spanDays = ($nowUtc - $startUtc).TotalDays
$captureTimes = [System.Collections.Generic.List[DateTime]]::new()
$seenTicks = [System.Collections.Generic.HashSet[long]]::new()

while ($captureTimes.Count -lt $SnapshotCount) {
    $ageDays = [Math]::Pow($random.NextDouble(), 2.35) * $spanDays
    $candidate = $nowUtc.AddDays(-$ageDays).AddSeconds(-$random.Next(0, 86400))
    if ($candidate -lt $startUtc) { $candidate = $startUtc.AddSeconds($random.Next(0, 86400)) }
    $candidate = [DateTime]::new($candidate.Year, $candidate.Month, $candidate.Day, $candidate.Hour, $candidate.Minute, $candidate.Second, [DateTimeKind]::Utc)
    if ($seenTicks.Add($candidate.Ticks)) { $captureTimes.Add($candidate) }
}

# Guarantee a recent endpoint, making the generated data immediately useful in week/month views.
$captureTimes.Sort()
$captureTimes[$captureTimes.Count - 1] = $nowUtc.AddMinutes(-1)

$trackStates = foreach ($track in $sourceTracks) {
    $appearsLater = $random.NextDouble() -lt 0.28
    $firstSnapshot = if ($appearsLater) {
        [Math]::Min($SnapshotCount - 1, [Math]::Max(1, [int]($SnapshotCount * (1 - [Math]::Pow($random.NextDouble(), 1.8)))))
    }
    else { 0 }

    [pscustomobject]@{
        Track = $track
        FirstSnapshot = $firstSnapshot
        PlayCount = [long]0
    }
}

# Back up before removing material, then replace only the known JSON snapshot files.
$existingSnapshots | Remove-Item -Force

for ($index = 0; $index -lt $captureTimes.Count; $index++) {
    $tracks = foreach ($state in $trackStates) {
        if ($index -lt $state.FirstSnapshot) { continue }

        if ($index -eq $state.FirstSnapshot) {
            # Later arrivals receive a larger initial total to exercise first-seen reporting.
            $maximumInitial = if ($state.FirstSnapshot -eq 0) { 12 } else { 36 }
            $state.PlayCount = $random.Next(0, $maximumInitial + 1)
        }
        else {
            $roll = $random.NextDouble()
            if ($roll -lt 0.10) { $state.PlayCount += $random.Next(1, 4) }
            elseif ($roll -lt 0.115) { $state.PlayCount += $random.Next(4, 11) }
        }

        $track = $state.Track
        [ordered]@{
            Id = [string]$track.Id
            SourceUrl = [string]$track.SourceUrl
            Title = [string]$track.Title
            Artist = [string]$track.Artist
            Album = [string]$track.Album
            Duration = [string]$track.Duration
            PlayCount = [long]$state.PlayCount
            AlbumArtUrl = [string]$track.AlbumArtUrl
        }
    }

    $capturedAt = $captureTimes[$index].ToUniversalTime()
    $snapshot = [ordered]@{
        SchemaVersion = 1
        CapturedAtUtc = $capturedAt.ToString('O', [System.Globalization.CultureInfo]::InvariantCulture)
        TimeZoneId = 'Eastern Standard Time'
        Tracks = @($tracks)
    }
    $fileName = $capturedAt.ToString('yyyyMMddTHHmmssfffZ', [System.Globalization.CultureInfo]::InvariantCulture) + '.json'
    $snapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $snapshotDirectory $fileName) -Encoding utf8
}

Write-Host "Generated $SnapshotCount test snapshots from $($sourceTracks.Count) local tracks."
Write-Host "Original snapshots were backed up to: $backupDirectory"
