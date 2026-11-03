# FlagDeck

A fast, themeable FastFlag manager for Roblox on Windows.

- Instant search across the full Roblox flag list (fetched from the community FFlag tracker, cached locally, refreshed automatically or on demand)
- Presets: enable/disable, reorder (later wins), merge, duplicate, share as a one-line code, import Bloxstrap-style JSON
- Writes `ClientAppSettings.json` to Bloxstrap, Voidstrap, Fishstrap, any other strap with a `Modifications` folder, and/or plain Roblox, and re-applies if it gets wiped
- Global hotkeys: toggle a preset, cycle presets, toggle/cycle a single flag value
- Themes: Dark, Light, Midnight, Ember. Tray icon, optional start with Windows
- Updates: flag list and app each set to Auto or Manual

## Build the installer
**CI (no local setup):** push to GitHub, tag `v1.0.0`. The workflow builds `FlagDeck-Setup.exe` + `.sha256` and attaches them to the release.
**Local:** install .NET 8 SDK and Inno Setup 6, then run `.\build.ps1`.

Before shipping, set `AppUpdater.Repo` in `src/Services.cs` to `your-user/FlagDeck`.

## How applying works
Roblox reads flags once at launch. FlagDeck writes the merged result of all enabled presets
(top to bottom, later wins) to each target's `ClientAppSettings.json`. Hotkeys change presets
and rewrite the file instantly; restart Roblox to load the new values.
Close the bootstrapper's own FastFlag editor while using FlagDeck so they don't overwrite each other.
Roblox only honors flags it allows, so some values may be ignored by the client.

## Deliberately not included
Live memory patching and memory offsets. That means writing into the running Roblox process,
which its anti-tamper treats as cheating and is a common cause of account bans.

## Code signing (reduces SmartScreen warnings)
Add these GitHub repo secrets and the workflow signs both `FlagDeck.exe` and the installer automatically
(Azure Artifact Signing, formerly Trusted Signing, "Public Trust" profile):
`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TRUSTED_SIGNING_ENDPOINT`,
`AZURE_CODE_SIGNING_NAME`, `AZURE_CERT_PROFILE_NAME`.
Without the secrets the build still works but produces unsigned files.
A signature does not give instant SmartScreen trust; reputation builds over downloads.
