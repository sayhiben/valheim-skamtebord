# Installation guide

Download **[Skamtebord-0.3.1.zip](https://github.com/sayhiben/valheim-skamtebord/releases/download/v0.3.1/Skamtebord-0.3.1.zip)** from the repository's [Releases page](https://github.com/sayhiben/valheim-skamtebord/releases). Choose this asset under **Assets**; GitHub's **Source code** archives do not include a compiled DLL.

Tested on Windows with Steam **Valheim 1.0.16**, **BepInExPack_Valheim 5.4.2350**, and **Jotunn 2.30.1**. Newer dependency versions may work but are outside this release's recorded validation. No developer tools are needed to play. For multiplayer, every player and the server need the mod and dependencies; two-client behavior has not yet been verified.

## r2modman

1. Close Valheim. Open r2modman, choose **Valheim**, and select a profile. A separate profile makes it easy to try the mod alongside only its required dependencies.
2. In **Online**, install **BepInExPack_Valheim** by **denikson**, choosing version **5.4.2350**. Install **Jotunn** by **ValheimModding**, choosing **2.30.1**. Local imports do not install dependencies automatically.
3. In **Settings**, search for **local mod**. Click **Import local mod** / **Install local mod**, choose the downloaded ZIP without extracting it, and confirm. If metadata is requested, enter **sayhiben**, **Skamtebord**, **0.3.1** for author, name, and version.
4. Open **Installed** and confirm Skamtebord and both dependencies are enabled. Click **Start modded** whenever you want to play this profile.
5. Enter a world. Open your inventory and look for the **Skamtebord** recipe. Craft it with **8 Wood, 4 Resin, and 2 Leather scraps**; it needs no crafting station. Keep it in your inventory and press **B** on solid ground to mount.

Use **W** to push, **A/D** to steer, **S** to brake, and **Space** to ollie. **Shift+W** adds sprint speed with a crouched stance, respecting your hold/toggle sprint setting. Tricks unlock with skating XP; see the [control table](../README.md#first-ride).

## Manual installation on Windows

Use this route for a direct Steam-folder installation. For an r2modman profile, use the import steps above so the manager tracks the mod.

1. Close Valheim. In Steam, right-click **Valheim → Manage → Browse local files**. This opens the folder containing `valheim.exe`.
2. Download **5.4.2350** from the **Versions** list on the [BepInExPack Valheim page](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). Extract it elsewhere, then copy the **contents of `BepInExPack_Valheim`** into the game folder. `BepInEx`, `winhttp.dll`, and `doorstop_config.ini` should sit beside `valheim.exe`.
3. Download **2.30.1** from the **Versions** list on the [Jotunn page](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/). Put its `plugins/Jotunn.dll` in `Valheim/BepInEx/plugins/Jotunn/`.
4. Extract **Skamtebord-0.3.1.zip**. Copy its **`BepInEx` folder** into the game folder and allow the folders to merge. This installs `Valheim/BepInEx/plugins/Skamtebord/Skamtebord.dll` and its adjacent `radio-mp3s` folder. Do not add another outer `Skamtebord-0.3.1` directory.
5. Start Valheim through Steam and craft the board as described above. The first launch creates `BepInEx/config/com.skamtebord.valheim.cfg`.

The release contains only the Skamtebord DLL and documentation. BepInEx and Jotunn must be installed separately. These manual instructions cover Windows; other platforms need their loader's platform-specific setup and have not been tested here.

## Your music and configuration

For r2modman, use **Settings → Browse profile folder**. For manual installs, open the game folder. Under `BepInEx/plugins`, locate **`Skamtebord.dll`**; the default **`radio-mp3s`** directory is immediately beside that DLL. A mod manager can rename or nest the containing folder, so use the DLL as your reference.

Place your own `.mp3` files directly in `radio-mp3s`. Remount the board to rescan. Subfolders are ignored. **F8** toggles the radio and **F9** skips tracks. Game music is muted while an MP3 plays and restored when playback stops; sound effects and saved music volume are unchanged.

You can keep music in a permanent folder outside the mod installation. After the first launch, close the game and edit the existing `[Radio]` section of `BepInEx/config/com.skamtebord.valheim.cfg`, for example:

```ini
[Radio]
Enabled = true
Directory = D:\Music\Skating
Volume = 0.55
```

Use your own folder path without quotation marks. Relative paths are resolved beside the installed DLL. The same configuration file contains key bindings and HUD position/scale. No music is bundled or sent to other players.

## Updating

1. Close Valheim. Keep a copy of `BepInEx/config/com.skamtebord.valheim.cfg` and any MP3s stored inside the mod folder. A music directory outside the installation avoids mod-manager replacements affecting your tracks.
2. With r2modman, import the new release ZIP into the same profile using the same author/name. For manual installs, copy the new release's `BepInEx` folder over the previous one. Keep your existing configuration and personal music.
3. Ensure there is only **one active `Skamtebord.dll`** under `BepInEx/plugins`. If your earlier development build was named **Local-Skamtebord**, you can replace its DLL with the one in the release ZIP instead of importing a second copy; its existing radio folder and configuration remain in place. The manager's displayed version may still show the old value after a manual DLL replacement.
4. Start modded again. Old default trick keys migrate to **J/K/L/U/I** on first load; customized bindings are retained.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No recipe or B does nothing | Use **Start modded** in the correct profile. Check that Skamtebord, BepInExPack, and Jotunn are enabled. Read `BepInEx/LogOutput.log` for `Skamtebord 0.3.1 loaded` or a missing-dependency error. |
| Recipe exists but mounting fails | Craft the board, keep it in your inventory, close menus, and stand on solid ground. |
| No music | Check the HUD status and the configured `Radio/Directory`; put MP3s directly in that folder, set volume above zero, press F8 if disabled, and remount. The source checkout's music folder is separate from an installed mod's default folder. |
| Old version still loads | Close the game, check the selected profile, and find duplicate `Skamtebord.dll` files. Keep one active copy. |
| HUD overlaps another mod | Adjust `[Interface]` `HudPosition` and `HudScale` in the configuration. |

If reporting a problem, include your game/mod versions and the relevant lines from `BepInEx/LogOutput.log`. See [test coverage and remaining limitations](testing.md).

## Optional download verification

Download the adjacent **Skamtebord-0.3.1.zip.sha256** release asset. Run `Get-FileHash .\Skamtebord-0.3.1.zip -Algorithm SHA256` in PowerShell and compare its hash with that text file.

References: [r2modman local imports](https://github.com/ebkr/r2modmanPlus/wiki/Installing-mods-locally), [local-import dependency behavior](https://wiki.thunderstore.io/mods/mod-not-visible), and the dependency installation pages linked above.
