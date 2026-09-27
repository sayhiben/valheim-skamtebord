using System;
using System.Collections.Generic;
using System.IO;

namespace Skamtebord.Radio
{
    /// <summary>Filesystem and shuffle logic kept independent of Unity.</summary>
    internal static class RadioPlaylist
    {
        internal const int MaximumTracks = 2048;
        private const int MaximumDirectoryEntries = 8192;

        internal static string ResolveDirectory(string pluginDirectory, string configuredDirectory)
        {
            // Windows environment expansion truncates at NUL, so validate before expanding.
            if (configuredDirectory != null && configuredDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ArgumentException("The radio directory contains invalid path characters.", nameof(configuredDirectory));
            var directory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(pluginDirectory, "radio-mp3s")
                : Environment.ExpandEnvironmentVariables(configuredDirectory);
            return Path.GetFullPath(Path.IsPathRooted(directory)
                ? directory
                : Path.Combine(pluginDirectory, directory));
        }

        internal static List<string> Scan(string directory, out bool truncated)
        {
            var tracks = new List<string>();
            var inspected = 0;
            truncated = false;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (++inspected > MaximumDirectoryEntries || tracks.Count >= MaximumTracks)
                {
                    truncated = true;
                    break;
                }

                if (string.Equals(Path.GetExtension(file), ".mp3", StringComparison.OrdinalIgnoreCase))
                    tracks.Add(Path.GetFullPath(file));
            }
            return tracks;
        }

        internal static string ToFileUri(string path)
        {
            // Uri escapes spaces, Unicode, literal # and % correctly, unlike "file://" + path.
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        internal static void Shuffle(List<string> tracks, Random random, string previousTrack)
        {
            for (var index = tracks.Count - 1; index > 0; index--)
            {
                var other = random.Next(index + 1);
                var swap = tracks[index];
                tracks[index] = tracks[other];
                tracks[other] = swap;
            }

            if (tracks.Count > 1 && string.Equals(tracks[0], previousTrack, StringComparison.Ordinal))
            {
                var swap = tracks[0];
                tracks[0] = tracks[1];
                tracks[1] = swap;
            }
        }
    }
}
