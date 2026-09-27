using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

internal static class RadioSmoke
{
    private static IEnumerator WaitForPlayback(AudioSource source)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while ((!source.isPlaying || source.volume <= 0f) && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForEndOfFrame();
    }

    internal static IEnumerator Run(string root, Action<bool, string> check, Action<string> log)
    {
        var type = AccessTools.TypeByName("Skamtebord.Radio.SkateRadio");
        var radio = UnityEngine.Object.FindFirstObjectByType(type);
        var source = (AudioSource)AccessTools.Field(type,"_source").GetValue(radio);
        var music = (AudioSource)AccessTools.Field(typeof(MusicMan), "m_musicSource").GetValue(MusicMan.instance);
        bool originalMute = music.mute;
        float originalMasterVolume = MusicMan.m_masterMusicVolume;
        float savedMusicVolume = PlatformPrefs.GetFloat("MusicVolume", 1f);
        float listenerVolume = AudioListener.volume;
        var volume = (ConfigEntry<float>)AccessTools.Field(type, "_volume").GetValue(radio);
        var directory = (ConfigEntry<string>)AccessTools.Field(type, "_directory").GetValue(radio);
        float radioVolume = volume.Value;
        string radioDirectory = directory.Value;
        var component = (Behaviour)radio;
        int count = ((IList)AccessTools.Field(type,"_tracks").GetValue(radio)).Count;
        check(count > 0, "radio discovers MP3s from the configured QA folder");
        check((bool)AccessTools.Property(type,"IsEnabled").GetValue(radio), "radio is enabled");
        log($"Discovered {count} tracks; listener volume={AudioListener.volume}, paused={AudioListener.pause}, output rate={AudioSettings.outputSampleRate}");
        var played = new HashSet<string>();
        var samples = new float[1024];
        for (int index = 0; index < Mathf.Min(count, 8); index++)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while ((!source.clip || !source.isPlaying) && Time.realtimeSinceStartup < deadline) yield return null;
            check(source.clip && source.isPlaying, $"track {index+1} decodes and starts playback");
            int initialSample = source.timeSamples;
            float peak = 0;
            while (Time.realtimeSinceStartup < deadline && (peak < .00001f || source.timeSamples <= initialSample))
            {
                source.GetOutputData(samples,0);
                foreach (float sample in samples) peak = Mathf.Max(peak,Mathf.Abs(sample));
                yield return null;
            }
            check(source.timeSamples > initialSample && peak > .00001f && source.volume > 0,
                $"track {index+1} advances and produces non-silent audio samples (peak={peak:F5})");
            yield return new WaitForEndOfFrame();
            check(music.mute, $"track {index+1} temporarily mutes Valheim music");
            played.Add((string)AccessTools.Field(type,"_playingPath").GetValue(radio));
            if (index + 1 < Mathf.Min(count,8)) AccessTools.Method(type,"NextTrack").Invoke(radio,null);
        }
        check(played.Count == Mathf.Min(count,8), "playlist visits distinct tracks before repeating");
        AccessTools.Method(type,"Toggle").Invoke(radio,null);
        check(!source.isPlaying, "radio toggle stops playback");
        check(music.mute == originalMute, "radio toggle immediately restores the previous game music mute state");
        AccessTools.Method(type,"Toggle").Invoke(radio,null);
        yield return WaitForPlayback(source);
        check(source.isPlaying, "radio toggle resumes playback");
        check(music.mute, "resumed radio mutes game music again");
        check(MusicMan.m_masterMusicVolume == originalMasterVolume
            && PlatformPrefs.GetFloat("MusicVolume", 1f) == savedMusicVolume
            && AudioListener.volume == listenerVolume, "radio muting leaves music preferences and master audio volume unchanged");

        try
        {
            // Simulate a music-slider change without writing the user's saved settings.
            float changedVolume = originalMasterVolume < .5f ? .73f : .31f;
            MusicMan.m_masterMusicVolume = changedVolume;
            yield return new WaitForEndOfFrame();
            check(music.mute, "game music remains muted after a live volume adjustment");
            AccessTools.Method(type,"Toggle").Invoke(radio,null);
            check(music.mute == originalMute && MusicMan.m_masterMusicVolume == changedVolume,
                "ending playback preserves a music-volume change made during the ride");
            MusicMan.m_masterMusicVolume = originalMasterVolume;

            music.mute = true;
            AccessTools.Method(type,"Toggle").Invoke(radio,null);
            yield return WaitForPlayback(source);
            AccessTools.Method(type,"Toggle").Invoke(radio,null);
            check(music.mute, "a pre-existing game music mute is preserved");
            music.mute = originalMute;
            AccessTools.Method(type,"Toggle").Invoke(radio,null);
            yield return WaitForPlayback(source);

            volume.Value = 0f;
            yield return new WaitForSecondsRealtime(.5f);
            check(source.isPlaying && music.mute == originalMute, "zero-volume radio releases the game music mute");
            volume.Value = radioVolume;
            yield return WaitForPlayback(source);
            check(music.mute, "raising radio volume mutes game music again");

            component.enabled = false;
            check(!source.isPlaying && music.mute == originalMute, "disabling the radio component restores game music immediately");
            component.enabled = true;
            yield return WaitForPlayback(source);
            check(source.isPlaying && music.mute, "re-enabled radio resumes playback and game music muting");

            string empty = Path.Combine(root, "empty-radio");
            Directory.CreateDirectory(empty);
            directory.Value = empty;
            yield return new WaitForSecondsRealtime(.3f);
            check(!source.isPlaying && music.mute == originalMute, "an empty playlist leaves game music available");

            string corrupt = Path.Combine(root, "corrupt-radio");
            Directory.CreateDirectory(corrupt);
            File.WriteAllText(Path.Combine(corrupt, "invalid.mp3"), "QA invalid audio fixture");
            directory.Value = corrupt;
            yield return null;
            float failureDeadline = Time.realtimeSinceStartup + 15f;
            while (!(bool)AccessTools.Field(type, "_exhausted").GetValue(radio)
                && Time.realtimeSinceStartup < failureDeadline) yield return null;
            check((bool)AccessTools.Field(type, "_exhausted").GetValue(radio) && !source.isPlaying && music.mute == originalMute,
                "a failed MP3 decode leaves game music available");
        }
        finally
        {
            component.enabled = true;
            volume.Value = radioVolume;
            directory.Value = radioDirectory;
            MusicMan.m_masterMusicVolume = originalMasterVolume;
        }
        yield return WaitForPlayback(source);
        check(source.isPlaying && music.mute, "restoring the valid playlist resumes radio and music muting");
    }
}
