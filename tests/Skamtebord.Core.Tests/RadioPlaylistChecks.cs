using Skamtebord.Radio;

internal static class RadioPlaylistChecks
{
    internal static void FileUris()
    {
        using var folder = new TemporaryRadioFolder();
        foreach (var name in new[] { "road trip.mp3", "Håkon 音楽.mp3", "song #1.mp3", "100%20 wheels.mp3" })
        {
            var path = folder.File(name);
            var uri = new Uri(RadioPlaylist.ToFileUri(path));
            Require(uri.IsFile, "A local MP3 became a remote URI");
            Require(uri.LocalPath == path, "Filename did not survive URI encoding: " + name);
            Require(uri.Query.Length == 0 && uri.Fragment.Length == 0, "A filename became a URI query or fragment");
        }
    }

    internal static void FileFiltering()
    {
        using var folder = new TemporaryRadioFolder();
        var expected = new HashSet<string>
        {
            folder.File("one.mp3"), folder.File("two.MP3"), folder.File("three.Mp3")
        };
        folder.File("notes.txt");
        folder.File("almost.mp3.wav");
        folder.File("nested/hidden.mp3");
        var tracks = RadioPlaylist.Scan(folder.Root, out var truncated);
        Require(!truncated && tracks.Count == expected.Count && expected.SetEquals(tracks),
            "Scan must include exactly the MP3 files in the top folder");
    }

    internal static void FolderResolution()
    {
        using var folder = new TemporaryRadioFolder();
        var expected = Path.Combine(folder.Root, "radio-mp3s");
        Require(RadioPlaylist.ResolveDirectory(folder.Root, "") == expected);
        Require(RadioPlaylist.ResolveDirectory(folder.Root, "   ") == expected);
        Require(RadioPlaylist.ResolveDirectory(folder.Root, "radio-mp3s") == expected);
        Require(RadioPlaylist.ResolveDirectory(folder.Root, expected) == expected);
    }

    internal static void InvalidFolders()
    {
        using var folder = new TemporaryRadioFolder();
        Expect<DirectoryNotFoundException>(() => RadioPlaylist.Scan(Path.Combine(folder.Root, "missing"), out _));
        Expect<ArgumentException>(() => RadioPlaylist.Scan(folder.Root + "\0invalid", out _));
        Expect<ArgumentException>(() => RadioPlaylist.ResolveDirectory(folder.Root, "\0invalid"));
        var empty = RadioPlaylist.Scan(folder.Root, out var truncated);
        Require(empty.Count == 0 && !truncated, "An empty folder should yield an empty playlist");
    }

    internal static void Shuffling()
    {
        var random = new Random(418);
        var tracks = new List<string> { "one.mp3", "two.mp3", "three.mp3", "four.mp3" };
        var expected = new HashSet<string>(tracks);
        for (var pass = 0; pass < 100; pass++)
        {
            var previous = tracks[0];
            RadioPlaylist.Shuffle(tracks, random, previous);
            Require(tracks.Count == expected.Count && expected.SetEquals(tracks), "Shuffle lost or duplicated a track");
            Require(tracks[0] != previous, "The previous song was immediately repeated");
        }

        var single = new List<string> { "only.mp3" };
        RadioPlaylist.Shuffle(single, random, "only.mp3");
        Require(single.Count == 1 && single[0] == "only.mp3", "A single-track playlist must remain playable");
        var empty = new List<string>();
        RadioPlaylist.Shuffle(empty, random, "previous.mp3");
        Require(empty.Count == 0);
    }

    private static void Require(bool condition, string message = "Radio assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class TemporaryRadioFolder : IDisposable
    {
        private readonly List<string> files = new();
        private readonly List<string> directories = new();
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Skamtebord.Radio.Tests-" + Guid.NewGuid().ToString("N"));

        internal TemporaryRadioFolder() => Directory.CreateDirectory(Root);

        internal string File(string relativePath)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relativePath));
            Require(path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "Test file must remain in its temporary folder");
            var directory = Path.GetDirectoryName(path)!;
            if (directory != Root && !directories.Contains(directory))
            {
                Directory.CreateDirectory(directory);
                directories.Add(directory);
            }
            System.IO.File.WriteAllText(path, "");
            files.Add(path);
            return path;
        }

        public void Dispose()
        {
            // Delete only the exact files created by this fixture; never recursively delete a path.
            foreach (var file in files) System.IO.File.Delete(file);
            foreach (var directory in directories.AsEnumerable().Reverse()) Directory.Delete(directory);
            Directory.Delete(Root);
        }
    }
}
