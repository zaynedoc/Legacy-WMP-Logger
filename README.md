# WMPL Wrap

**A local listening-history companion for Windows Media Player Legacy**

**Current version:** `v1.1.0` · [Releases](https://github.com/zaynedoc/WMPL-Wrap/releases) · [Privacy](PRIVACY.md) · [MIT License](LICENSE)

WMPL Wrap turns Windows Media Player's cumulative play counts into a personal
listening history. It captures local snapshots, compares them over time, and presents
your top songs, albums, artists, and latest listening changes in a native Windows app.

## What it does

- Captures read-only snapshots of the local Windows Media Player Legacy library
- Calculates listens from play-count increases between snapshots
- Matches recurring tracks by local source URL, so tag edits keep their history
- Shows overview, snapshot history, top-song, top-album, and top-artist views
- Can schedule one daily local snapshot through Windows Task Scheduler
- Lets users manually check GitHub Releases for a newer signed version

## How the history works

Windows Media Player does not keep a dated play history; it exposes a cumulative
`UserPlayCount` for each library item. Your first snapshot establishes a local
baseline. Each later report compares two snapshots:

```text
listens in a period = max(0, end.UserPlayCount - start.UserPlayCount)
```

WMPL Wrap can optionally include the first snapshot's existing counts ("baseline") in reports that
include the baseline date. This makes a new installation useful immediately, while
clearly treating those counts as baseline data rather than retroactively dated listens.

## Data recording

Snapshots are JSON files in `data/snapshots` (or the directory provided with `--data`).
Each one stores its capture time, the WMP total count, and the metadata needed to label
results:

- Source URL
- Title
- Artist
- Album
- Duration

The `data` directory is ignored by Git. Artwork is read locally—from WMP artwork
references, nearby cover files, or embedded artwork—and is never looked up online.
See [PRIVACY.md](PRIVACY.md) for the full local-data notice.

## Run the desktop app

### Development

Requires the .NET 10 SDK and Windows Media Player Legacy. Verify the build by running:

```powershell
dotnet run --project src/WmplWrap.Desktop
```

### Standalone build

Create a self-contained, single-file desktop build:

```powershell
.\scripts\publish-desktop.ps1
```

The result is `publish\desktop\WmplWrap.Desktop.exe`. It does not need VS Code,
PowerShell, or a separately installed .NET runtime. An unsigned build is useful for
local packaging checks; however Windows security features can block it from launching.

### Signed public release

The public release workflow publishes, signs, timestamps, and verifies the final EXE in
one command. The signing certificate's private key stays in Azure; it is never stored in
this repository.

```powershell
winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
.\scripts\publish-desktop.ps1 -Sign
```

The first signed release can open a browser for Azure authentication. The default profile
is `wmplwrapdesktop` / `wmplwrapdesktop-public` in East US.

Fork maintainers can use their own Azure Artifact Signing account and profile:

```powershell
.\scripts\publish-desktop.ps1 -Sign `
  -CodeSigningAccountName "myappsigning" `
  -CertificateProfileName "myapp-public" `
  -Endpoint "https://cus.codesigning.azure.net"
```

## Snapshot logger commands

The command-line logger requires the .NET 10 SDK or runtime and Windows Media Player's
legacy COM library. Run these from the repository root:

```powershell
dotnet run --project src/WmplWrap -- snapshot
dotnet run --project src/WmplWrap -- status
dotnet run --project src/WmplWrap -- report --from 2026-09-01 --to 2026-09-30 --top 20
```

`snapshot` is safe to run more than once per day. `status` reports the first and latest
local snapshot. `--from` and `--to` accept Eastern calendar dates (`yyyy-MM-dd`) or
ISO-8601 timestamps; ranges are inclusive and the report prints the snapshots it used.

## Automatic daily snapshots

First publish the command-line logger. This replaces the local publish output; it does
not create duplicate scheduled tasks.

```powershell
dotnet publish src/WmplWrap -c Release -o publish
.\scripts\setup-scheduled-snapshot.ps1 -PublishDirectory .\publish
```

The setup script creates one local task, **WMPL Wrap Daily Snapshot**, scheduled for
12:05 AM. `StartWhenAvailable` lets Windows catch up after the next sign-in or startup
if the PC was off at midnight.

To stop automatic snapshots without deleting recorded history:

```powershell
Unregister-ScheduledTask -TaskName "WMPL Wrap Daily Snapshot" -Confirm:$false
```

## Project notes

- Cumulative counts from before the first snapshot cannot be timestamped retroactively.
- More snapshots create a more useful history; daily collection is a good default.
- Windows Media Player documents `UserPlayCount` (also called `PlayCount`) as a
  library-only value. WMPL Wrap uses its `getAll()` API to enumerate items. See
  [UserPlayCount](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmp/userplaycount-attribute)
  and [getAll](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmp/wmplibiwmpmediacollection-iwmpmediacollection-getall--vb-and-c).
