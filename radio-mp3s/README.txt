Put your personal MP3 tracks here in the installed mod's radio-mp3s directory.
Set Radio/Directory in BepInEx/config/com.skamtebord.valheim.cfg to use another folder.
F8 toggles the radio; F9 skips a track. Remount to rescan files.
Only files directly in this directory are scanned. No music is bundled or shared.
Audible MP3 playback temporarily mutes game music; stopping restores it.
Sound effects and saved music-volume settings are not changed.

Development: scripts/qa.ps1 uses this source checkout's radio-mp3s folder.
An r2modman profile has a separate configuration: point Radio/Directory here with
an absolute path to share this playlist, or put tracks beside its installed DLL.
All MP3 files (including mixed-case extensions) are gitignored across the repo.
