# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]

### Added
- Offline server that replaces the shut-down Photon game server, running inside the game process.
- Login with any account name. Accounts and characters are saved as JSON in `OfflineSaves/`.
- Character list, creation (including the appearance editor) and deletion.
- Entering the world, scene loading, position saving and autosave.
- Wayshrine travel and discovery, and setting a home wayshrine.
- NPC and harvestable spawns rebuilt from the developers' scene markers and streamed by distance.
- Chat and offline commands (`/help`, `/pos`, `/tele`, `/scene`, `/wayshrine`, `/wayshrines`, `/time`, `/save`).
- Steamworks calls made safe so the game still works when it is launched without Steam.
- Build, release and data-extraction tooling.
