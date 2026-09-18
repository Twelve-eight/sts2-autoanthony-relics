# Steam Workshop Upload Guide - Anthony Algorithm - Relics (东尼算法 - 遗物)

Staged 2026-09-12 (night fix round). Everything is staged; only Steam credentials/2FA are needed to publish.

## What's staged (this folder)

- `content/AutoAnthonyRelics/` - the mod payload exactly as the game loads it:
  `AutoAnthonyRelics.dll`, `AutoAnthonyRelics.pck`, `AutoAnthonyRelics.json` (+ pdb).
- `preview.png` - 512x512 preview (4x4 gem fan on dark gradient).
- `workshop_upload.vdf` - steamcmd build script. `publishedfileid` is EMPTY = creates a NEW
  item on first run; after creation, write the returned id into it for future updates.
- `workshop-push.ps1` - universal pusher (uses `STEAM_ACCOUNT` / `STEAM_PASSWORD` env vars,
  optional `-GuardCode`).

## Publish (new item, first run)

Option A - interactive (prompts for password + Steam Guard):

```
cd "G:\omp works\.tooling\steamcmd"
.\steamcmd.exe +login YOUR_STEAM_LOGIN +workshop_build_item "G:\omp works\sts2-autoanthony-relics\workshop\workshop_upload.vdf" +quit
```

Option B - env-var pusher (from any shell with the env set):

```
powershell -File "G:\omp works\sts2-autoanthony-relics\workshop\workshop-push.ps1" -Vdf "G:\omp works\sts2-autoanthony-relics\workshop\workshop_upload.vdf"
```

Notes:
- The account must own Slay the Spire 2 (app 2868840) and satisfy Steam community posting
  requirements; Steam Guard code is prompted/emailed on first use, then cached in
  `G:\omp works\.tooling\steamcmd\config\`.
- After the first publish, note the printed `Published file id` and put it into
  `workshop_upload.vdf` -> `publishedfileid` so later runs UPDATE instead of duplicating.

## Before publishing - manual verification checklist (do these in-game first)

1. Fresh run: rewards/chests/shops/events offer generated relics; description text (EN + 中文) renders.
2. Hold one relic across a save/load - same relic, same behavior.
3. WITH Qurious installed (v0.5.2+): both mod families appear in the reward pool (mixed), and
   Qurious's migration did NOT touch `AutoAnthonyRelics.cfg` (check `mod_configs/`).
4. Console: `relic add AUTOANTHONYRELICS-ANTHONY_RELIC005` adds slot 6.

## Update flow

Bump `version` in `mod/AutoAnthonyRelics.json` -> rebuild Release -> re-stage `content/` ->
edit `changenote` in the VDF -> rerun the steamcmd command.
