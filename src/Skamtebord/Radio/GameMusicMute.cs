using System;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.Radio;

// Mute the music output without changing saved volume, track selection or fades.
internal sealed class GameMusicMute : IDisposable
{
    private static readonly AccessTools.FieldRef<MusicMan, AudioSource> MusicSource =
        AccessTools.FieldRefAccess<MusicMan, AudioSource>("m_musicSource");
    private AudioSource mutedSource;
    private bool previousMute;

    internal void SetMuted(bool mute)
    {
        var source = mute && MusicMan.instance ? MusicSource(MusicMan.instance) : null;
        if (mutedSource != source)
        {
            Dispose();
            if (source)
            {
                mutedSource = source;
                previousMute = source.mute;
            }
        }
        if (mutedSource) mutedSource.mute = true;
    }

    public void Dispose()
    {
        if (mutedSource) mutedSource.mute = previousMute;
        mutedSource = null;
    }
}
