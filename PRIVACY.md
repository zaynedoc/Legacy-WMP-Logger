# Privacy Notice

Effective date: September 28, 2026

WMPL Wrap is a local Windows Media Player Legacy listening-history tool. This
notice describes the behavior of the desktop app and snapshot logger in this
repository.

## Data the app reads

When you capture a snapshot, WMPL Wrap reads information already available in
your local Windows Media Player Legacy library, including track title, artist,
album, duration, cumulative play count, artwork references, and source URLs.
Source URLs can include paths to media files on your computer.

## Local storage

Snapshots and desktop preferences are saved locally in the app's data directory.
The desktop app displays that location in its footer. The data is used only to
calculate your listening history and render the app's reports. You can remove
the local data directory to delete those saved snapshots and preferences.

## No telemetry or data uploads

WMPL Wrap does not include analytics, advertising, crash-reporting, user
accounts, cloud sync, or a service that uploads your library or listening data.
It does not send snapshot data to the developer or to third parties.

## Optional Discord Rich Presence

Discord Rich Presence is disabled by default. If you enable it, WMPL Wrap reads the
current Windows Media Player title, artist, album, playback state, and local playback
position to produce a Discord status. It sends the title, artist, paused state, and
selected Discord image asset keys to the Discord desktop client running on your computer.
The Discord client may then display that activity according to your Discord account and
privacy settings.

WMPL Wrap does not send the media file, local file path, snapshot history, or album-art
image file to Discord. Album-art mappings, your chosen Discord Application ID, and the
feature settings are stored only in WMPL Wrap's local settings file. Discord Developer
Portal image assets are managed by you and are subject to Discord's own practices.

The **View on GitHub** button opens GitHub in your browser. The optional
**Check for updates** button makes a request to GitHub only when you select it,
to read the latest public release's version and link; it does not send library
or listening data. Any use of GitHub is governed by GitHub's own privacy
practices. The optional Azure Artifact Signing release script is a
developer-only publishing tool; it is not part of the desktop app's
listening-data collection.

## Changes to this notice

If WMPL Wrap's data practices change, this notice will be updated in the source
repository alongside the relevant release.

## Contact

For questions or to report a privacy concern, open an issue in the
[WMPL Wrap repository](https://github.com/zaynedoc/WMPL-Wrap).
