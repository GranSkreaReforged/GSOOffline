# Developer guide: build and run the offline server

The "server" is not a separate program. It is a BepInEx plugin that runs inside the game and answers the game's network messages itself. Building it means compiling one DLL against **your own copy** of Gran Skrea Online; running it means starting the game.

## 1. Prerequisites

- Windows, with Gran Skrea Online installed (Steam)
- [.NET SDK](https://dotnet.microsoft.com/download) 9.0.200 or newer
- PowerShell 7 (`pwsh`) for releases; 5.1 is fine for building
- The sibling **GSODevTools** repo (`..\GSODevTools`) for decompiling, data mining and in-game testing

## 2. Get the code

```powershell
git clone <repo-url> GSOOffline
cd GSOOffline
```

## 3. First build

```powershell
.\build.ps1 -InstallBepInEx -Deploy
```

This command:
1. Finds the game in your Steam libraries (or pass `-GameDir 'X:\...\Gran Skrea Online'`). The build only reads it.
2. Builds `GSOOffline.dll` into `artifacts\build\Debug\BepInEx\plugins\GSOOffline\`, next to an `INSTALL.txt`.
3. `-InstallBepInEx`: puts the pinned, hash-checked BepInEx 5.4.23.5 and `steam_appid.txt` into the game.
4. `-Deploy`: copies the built files into the game (`<game>\BepInEx\plugins\GSOOffline\`).

Both switches are conveniences. Without them the game is left alone, and you can review `artifacts\build\Debug\` and copy it in yourself (see `INSTALL.txt`). After the first time, `.\build.ps1 -Deploy` is the usual loop. **Close the game before deploying**, because the running game locks the DLL.

## 4. Run the server (= play the game)

1. Start Gran Skrea Online, from Steam or `GSO.exe`.
2. Log in with any name; the password is ignored.
3. Create a character, then press Done.

Useful files:

| Path | What it is |
|---|---|
| `<game>\BepInEx\LogOutput.log` | Server log. Look for `Unhandled client event` (the to-do list) and errors. |
| `%USERPROFILE%\AppData\LocalLow\Gran Skrea\Gran Skrea\output_log.txt` | Unity's own log (client exceptions) |
| `<game>\OfflineSaves\` | Accounts and characters as JSON |
| `<game>\BepInEx\config\gso.offline.server.cfg` | Settings; see [COMMANDS.md](COMMANDS.md) |

To skip the menus while developing, set `AutoLogin` / `AutoCharacter` in the config, or use the GSODevTools DevBridge.

## 5. Develop and test

1. Read how the client behaves: `..\GSODevTools\tools\decompile.ps1`, then [PROTOCOL.md](PROTOCOL.md).
2. Change code in `src\GSOOffline\` (the file map is in the README).
3. `.\build.ps1 -Deploy`
4. Test through the real client:

   ```powershell
   cd ..\GSODevTools
   .\build.ps1 -Deploy                           # once: deploys the DevBridge plugin
   .\tools\devbridge.ps1 -Enable
   .\tools\devbridge.ps1 -Launch -Commands 'npcs 5' -Filter 'uid='
   .\tools\devbridge.ps1 -Disable -ResetSaves
   ```

   GSODevTools' `docs/DEVBRIDGE.md` lists the test commands.
5. Update `CHANGELOG.md` under `## [Unreleased]`.

Quest glue (which NPC says what, and when) lives in `src\GSOOffline\Data\content.json`. Game data tools are in `..\GSODevTools\tools\datamining\`. Everything is listed in [COMMANDS.md](COMMANDS.md).

## 6. Branches and releases

| Branch | Role |
|---|---|
| `main` | Releases only. Each release is one reviewed pull request from `dev`. |
| `dev` | Integration. Every feature merges here. |
| `feature/<area>/<name>`, `fix/<area>/<name>` | One piece of work, branched from `dev`, e.g. `feature/inventory/stack-split`. |

`<area>` names the system you're changing (`inventory`, `quests`, `combat`, `ui`, `build`, `docs`...). The prefix is `feature/` rather than `dev/` because git can't have a branch `dev` and branches under `dev/` at the same time.

A feature:

```powershell
git switch dev; git pull --ff-only
git switch -c feature/inventory/stack-split
# ...commit as you go...
git switch dev; git pull --ff-only
git merge --no-ff feature/inventory/stack-split
git push
git branch -d feature/inventory/stack-split
```

Feature branches stay local; only `dev` and `main` live on GitHub. Push a feature branch only when you want it backed up or shared (`git push -u origin <branch>`, and `git push origin --delete <branch>` after merging).

`--no-ff` keeps each feature as one merge on `dev`, so `git log --first-parent dev` reads as a list of features. If `dev` moved on and the feature conflicts, resolve it on the feature branch: rebase it onto `dev` while it's local, or merge `dev` into it if it has been pushed. A single small commit (a typo, one doc line) can go straight on `dev`.

### Changes that need a reviewed pull request

These go to `dev` through a pull request that the maintainer reviews and merges on GitHub, instead of a local merge:
- **Could break the game or saves:** the save format (`SaveSystem.cs`, `Json.cs`), login and entering the world, Harmony patches, the message dispatch in `OfflineServer.cs`, plugin startup.
- **Security:** anything that downloads, runs processes, deletes files, touches the network or adds a dependency, and what goes into release zips.
- **Large:** roughly 300+ changed lines of code, 10+ files, or a new subsystem.
- **Core build files:** `build.ps1`, `release.ps1`, `build\GSOBuild.psm1`, `Directory.Build.props`, the `.csproj`, `.gitignore`.

Every release is a pull request too (`dev` into `main`).

The pull request is opened by hand from the branch's final commit: its first line is the PR title and the rest is the description (summary, why, changes, testing, risk). Push the branch, use GitHub's **Compare & pull request** (base `dev`), and copy them in. Merge with **Create a merge commit**, not squash or rebase. Review fixes are new commits on the same branch. After the merge: `git switch dev; git pull --ff-only; git branch -d <branch>`.

### Dev builds and release builds

- **Dev build:** anything `build.ps1` makes, from any branch, Debug or Release configuration. It's for playtesting and never shipped. Its version says where it came from, e.g. `1.0.0-dev+feature-inventory-stack-split.4af9ce7` (`.dirty` if there were uncommitted changes), and the plugin logs it at startup with "(dev build)".
- **Release build:** made only by `release.ps1`, from a clean `dev`. It carries the plain version (`1.0.1`), is tagged, and is packaged into the zips.

### A release

```powershell
git switch dev; git pull --ff-only
.\release.ps1 -Version 0.2.0 -DryRun   # package into dist\ and check it
.\release.ps1 -Version 0.2.0           # bump, date the CHANGELOG, commit "Release v0.2.0" + notes, tag
git push origin dev                    # not the tag yet
# pull request dev -> main: title and description are the release commit's message (git log -1)
# once it's merged:
git push origin v0.2.0
git switch main; git pull --ff-only; git switch dev; git merge --ff-only main; git push
```

The release commit's message is "Release v0.2.0" followed by that version's CHANGELOG section, so the pull request shows the release notes. The zips never contain game files.

## Rules

- Never commit game files or anything decompiled or extracted (those live, git-ignored, in GSODevTools).
- Target framework is .NET 3.5 (Unity 2017.4 Mono), so check that the APIs you use exist there.
- Use the plugin's `Json` class, not `UnityEngine.JsonUtility`, for the plugin's own data.
- License: GPL-3.0-or-later.
