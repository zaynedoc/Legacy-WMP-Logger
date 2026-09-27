# WMPL Wrap: Snapshot logger

### Basically Spotify Wrap / YouTube Music Recap, but for Windows Media Player Legacy!

------

WMPL does not expose a play history; it exposes a cumulative `UserPlayCount` for
each library item. This project turns those cumulative values into period listens
by taking read-only snapshots of the Windows Media Player library.

The first snapshot is a **baseline**, not a recap, due to a lack of timestampping.
Every sequential report thereafter compares the same track in two snapshots:

```
listens in a period = max(0, end.UserPlayCount - start.UserPlayCount)
```

##### Note: The `max` is deliberate. If WMP or its library is reset and a counter gets smaller, treating the difference as new listens would invent data. The report instead flags the counter reset.

This is important, as you may notice that "Last week/month/year" option on the
desktop app will not be inclusive of your baseline snapshot data, only data after.

## What is recorded

Snapshots are JSON files in `data/snapshots` (or the directory supplied by
`--data`). They contain a timestamp, the WMP total count, and just enough
metadata to label a result: source URL, title, artist, album, and duration.
The scanner reads WMP through its COM library API; it never writes media tags,
play counts, or the WMP library.

Tracks that first appear after the start snapshot are omitted from that period's
totals. Their pre-existing WMP count cannot be reliably assigned to the period;
they start contributing once a later snapshot establishes their baseline.

## Desktop app

The native WPF desktop app is separate from the PowerShell/CLI workflow:

```powershell
dotnet run --project src/WmplWrap.Desktop
```

It provides a Windows 7-inspired library view with the latest observed play-count
changes and a Top 5 selector for all time, the past week, month, or year. The
selector stays selected for the life of the app session. All-time shows WMP's
current cumulative counts; date ranges use only observed snapshot deltas and
therefore remain empty until enough baseline history exists.

Artwork is read locally only: first from WMP's saved `WM/AlbumCoverURL` when it
is a local file, then from common cover files beside the source track (such as
`Folder.jpg` or `cover.jpg`), then from embedded audio-file artwork. The app
never looks artwork up online and never writes into the Music folder. Use the
**Capture snapshot** button to create a new local snapshot; otherwise opening
the app is read-only.

## Terminal

Requires the .NET 10 SDK or runtime and Windows Media Player's legacy COM
library. From this directory:

```powershell
dotnet run --project src/WmplWrap -- snapshot
dotnet run --project src/WmplWrap -- status
dotnet run --project src/WmplWrap -- report --from 2026-09-01 --to 2026-09-30 --top 20
```

`--from` and `--to` accept `yyyy-MM-dd` (Eastern calendar dates) or an ISO-8601
timestamp. A date range is inclusive at both ends. The result prints the actual
snapshot timestamps used, so it never implies precision the data does not have.

For a quick end-of-day scan before a demo, run `snapshot` again. Multiple
snapshots in a day are safe.

For automatic snapshots, from project folder, publish a release:

```powershell
dotnet publish src/WmplWrap -c Release -o publish
```

Then review and run:
```powershell
.\scripts\setup-scheduled-snapshot.ps1 -PublishDirectory .\publish
```
It creates one Windows Task Scheduler task at 12:05 AM in the computer's local time.
On an Eastern-time PC that means Eastern time and tracks daylight saving time. 
It also uses `StartWhenAvailable`, so a missed midnight run is caught up after the 
next sign-in/startup. The script is not run by this project automatically.

To opt out without deleting previous records:
```powershell
Unregister-ScheduledTask -TaskName "WMPL Wrap Daily Snapshot" -Confirm:$false
```

## Notes

- The first baseline needs a few days of collection before a meaningful recent
  recap exists.
- Cumulative counts before the first baseline cannot be dated retroactively.
- WMP documents `UserPlayCount` (also called `PlayCount`) as a library-only
  value. Its library `getAll()` API is used to enumerate items. See Microsoft:
  [UserPlayCount](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmp/userplaycount-attribute)
  and [getAll](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmp/wmplibiwmpmediacollection-iwmpmediacollection-getall--vb-and-c).
