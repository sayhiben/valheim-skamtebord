using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace Skamtebord.Radio
{
    /// <summary>Local MP3 playback, temporarily muting game music while audible.</summary>
    public sealed class SkateRadio : MonoBehaviour
    {
        private readonly System.Random _random = new System.Random();
        private readonly GameMusicMute _gameMusic = new GameMusicMute();
        private List<string> _tracks = new List<string>();
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<string> _directory;
        private ConfigEntry<float> _volume;
        private ManualLogSource _logger;
        private string _pluginDirectory;
        private GameObject _audioObject;
        private AudioSource _source;
        private AudioClip _clip;
        private UnityWebRequest _request;
        private Coroutine _loadRoutine;
        private int _nextTrack;
        private bool _initialized;
        private bool _skating;
        private bool _loading;
        private bool _exhausted;
        private bool _hasPlayed;
        private volatile bool _restartRequested;
        private float _playbackGraceUntil;
        private string _previousTrack;
        private string _playingPath;

        public string NowPlaying { get; private set; } = string.Empty;
        public string Status { get; private set; } = "Ready";
        public bool IsEnabled => _initialized && _enabled.Value;
        private bool CanPlay => _initialized && _skating && IsEnabled && isActiveAndEnabled;

        public void Initialize(string pluginDirectory, ConfigFile config, ManualLogSource logger)
        {
            if (_initialized)
                throw new InvalidOperationException("SkateRadio has already been initialized.");
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pluginDirectory = Path.GetFullPath(pluginDirectory);
            var defaultDirectory = Path.Combine(_pluginDirectory, "radio-mp3s");
            _enabled = config.Bind("Radio", "Enabled", true,
                "Play your local MP3 files while skating. No music files are included.");
            _directory = config.Bind("Radio", "Directory", "radio-mp3s",
                "Folder containing MP3 files; subfolders are ignored. Relative paths use the mod folder. " +
                "Remount the board to discover newly added files. URLs are not supported.");
            _volume = config.Bind("Radio", "Volume", 0.55f,
                new ConfigDescription("Skating radio volume, independent of the game's music volume.",
                    new AcceptableValueRange<float>(0f, 1f)));
            _enabled.SettingChanged += RequestRestart;
            _directory.SettingChanged += RequestRestart;

            _audioObject = new GameObject("Skamtebord Radio");
            _audioObject.transform.SetParent(transform, false);
            _source = _audioObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.dopplerLevel = 0f;
            _source.volume = 0f;
            _initialized = true;

            try
            {
                Directory.CreateDirectory(defaultDirectory);
            }
            catch (Exception error) when (IsFileError(error))
            {
                _logger.LogWarning("Could not create the default skating radio folder: " + error.Message);
            }
        }

        public void SetSkating(bool skating)
        {
            if (!_initialized || _skating == skating)
                return;
            _skating = skating;
            Restart();
        }

        public void Toggle()
        {
            if (!_initialized)
                return;
            _enabled.Value = !_enabled.Value;
            // Input comes from the owning plugin; apply its change immediately on the Unity thread.
            _restartRequested = false;
            Restart();
        }

        public void NextTrack()
        {
            if (!CanPlay)
                return;
            StopPlayback();
            if (_exhausted)
                RebuildPlaylist();
            BeginNextTrack();
        }

        private void RequestRestart(object sender, EventArgs args)
        {
            // Config managers may raise this off-thread; all Unity work stays in Update.
            _restartRequested = true;
        }

        private void Update()
        {
            if (!_initialized)
                return;
            if (_restartRequested)
            {
                _restartRequested = false;
                Restart();
            }
            if (!CanPlay)
                return;

            _source.volume = Mathf.MoveTowards(_source.volume, Mathf.Clamp01(_volume.Value),
                Time.unscaledDeltaTime * 3f);
            UpdateGameMusicMute();
            if (_loading || _exhausted || AudioListener.pause)
                return;
            if (_source.isPlaying)
            {
                _hasPlayed = true;
                return;
            }

            var decoderFailed = _clip != null && _clip.loadState == AudioDataLoadState.Failed;
            if (!decoderFailed && !_hasPlayed && Time.realtimeSinceStartup < _playbackGraceUntil)
                return;
            if (_clip != null && (decoderFailed || !_hasPlayed))
            {
                _logger.LogWarning("Skipping radio track '" + Path.GetFileName(_playingPath) +
                    "': audio playback did not start or its decoder failed.");
                RemoveFailedTrack(_playingPath);
                ReleaseClip();
            }
            BeginNextTrack();
        }

        // Catch clip completion, external pause/mute and a replaced scene music
        // source even when Update exits early for loading, exhaustion or a pause.
        private void LateUpdate() => UpdateGameMusicMute();

        private void UpdateGameMusicMute() => _gameMusic.SetMuted(CanPlay && _source
            && _source.isPlaying && !_source.mute && _source.volume > 0f && !AudioListener.pause);

        private void Restart()
        {
            StopPlayback();
            if (!CanPlay)
                return;
            RebuildPlaylist();
            BeginNextTrack();
        }

        private void RebuildPlaylist()
        {
            _tracks.Clear();
            _nextTrack = 0;
            _exhausted = false;
            try
            {
                var directory = RadioPlaylist.ResolveDirectory(_pluginDirectory, _directory.Value);
                bool truncated;
                _tracks = RadioPlaylist.Scan(directory, out truncated);
                RadioPlaylist.Shuffle(_tracks, _random, _previousTrack);
                if (truncated)
                    _logger.LogWarning("Radio folder scan limit reached; use a smaller MP3 folder.");
                if (_tracks.Count == 0)
                {
                    Status = "No MP3s · check Radio/Directory";
                    _logger.LogInfo("No MP3 files in skating radio folder: " + directory);
                }
                else
                {
                    Status = "Loading radio…";
                    _logger.LogInfo("Skating radio found " + _tracks.Count + " MP3 files in " + directory);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                Status = "Cannot read Radio/Directory";
                _logger.LogWarning("Could not scan skating radio folder: " + error.Message);
            }
            _exhausted = _tracks.Count == 0;
        }

        private void BeginNextTrack()
        {
            if (!CanPlay || _loading || _exhausted)
                return;
            ReleaseClip();
            _loading = true;
            _loadRoutine = StartCoroutine(LoadNextTrack());
        }

        private IEnumerator LoadNextTrack()
        {
            // Ensure StartCoroutine returns its handle before any synchronous failure can finish us.
            yield return null;
            try
            {
                while (CanPlay && _tracks.Count > 0)
                {
                    if (_nextTrack >= _tracks.Count)
                    {
                        RadioPlaylist.Shuffle(_tracks, _random, _previousTrack);
                        _nextTrack = 0;
                    }
                    var path = _tracks[_nextTrack++];
                    var operation = TryBeginRequest(path);
                    if (operation != null)
                        yield return operation;

                    var loaded = ReadCompletedClip(path);
                    if (loaded != null && TryPlayClip(loaded, path))
                        yield break;

                    ReleaseRequest();
                    // An unreadable file is tried once per folder scan, not once every frame/song.
                    RemoveFailedTrack(path);
                    yield return null;
                }
                _exhausted = true;
                Status = "No playable MP3s · Next rescans";
                _logger.LogInfo("Skating radio stopped: no playable MP3 files remain. " +
                    "Remount, change the folder, or press next track to scan again.");
            }
            finally
            {
                ReleaseRequest();
                _loading = false;
                _loadRoutine = null;
            }
        }

        private bool TryPlayClip(AudioClip loaded, string path)
        {
            try
            {
                _clip = loaded;
                ReleaseRequest();
                _source.clip = _clip;
                _source.volume = 0f;
                _source.Play();
                _hasPlayed = _source.isPlaying;
                _playbackGraceUntil = Time.realtimeSinceStartup + 5f;
                _playingPath = path;
                _previousTrack = path;
                NowPlaying = Path.GetFileNameWithoutExtension(path);
                Status = "Playing";
                return true;
            }
            catch (Exception error)
            {
                _logger.LogWarning("Skipping radio track '" + Path.GetFileName(path) + "': " + error.Message);
                ReleaseClip();
                return false;
            }
        }

        private void RemoveFailedTrack(string path)
        {
            var index = _tracks.IndexOf(path);
            if (index >= 0)
            {
                _tracks.RemoveAt(index);
                if (index < _nextTrack)
                    _nextTrack--;
            }
            _exhausted = _tracks.Count == 0;
        }

        private UnityWebRequestAsyncOperation TryBeginRequest(string path)
        {
            try
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("The MP3 file no longer exists.", path);
                _request = UnityWebRequestMultimedia.GetAudioClip(RadioPlaylist.ToFileUri(path), AudioType.MPEG);
                // Decode streaming audio instead of retaining a full decompressed song in memory.
                ((DownloadHandlerAudioClip)_request.downloadHandler).streamAudio = true;
                _request.timeout = 30;
                return _request.SendWebRequest();
            }
            catch (Exception error)
            {
                _logger.LogWarning("Skipping radio track '" + Path.GetFileName(path) + "': " + error.Message);
                ReleaseRequest();
                return null;
            }
        }

        private AudioClip ReadCompletedClip(string path)
        {
            if (_request == null)
                return null;
            try
            {
                if (_request.result != UnityWebRequest.Result.Success)
                    throw new IOException(_request.error ?? "Audio loading failed.");
                var clip = DownloadHandlerAudioClip.GetContent(_request);
                if (clip == null || clip.length <= 0f || clip.loadState == AudioDataLoadState.Failed)
                    throw new IOException("The MP3 decoder did not produce a playable clip.");
                return clip;
            }
            catch (Exception error)
            {
                _logger.LogWarning("Skipping radio track '" + Path.GetFileName(path) + "': " + error.Message);
                return null;
            }
        }

        private void StopPlayback()
        {
            if (_loadRoutine != null)
                StopCoroutine(_loadRoutine);
            _loadRoutine = null;
            _loading = false;
            ReleaseRequest();
            ReleaseClip();
        }

        private void ReleaseRequest()
        {
            var request = _request;
            _request = null;
            if (request == null)
                return;
            try
            {
                if (!request.isDone)
                    request.Abort();
                var handler = request.downloadHandler as DownloadHandlerAudioClip;
                // A failed or cancelled decoder can still have allocated an AudioClip.
                var pendingClip = handler != null && handler.isDone ? handler.audioClip : null;
                if (pendingClip != null && pendingClip != _clip)
                    Destroy(pendingClip);
            }
            catch (Exception error)
            {
                // Some decoder failures cannot expose their clip; disposing the request must still
                // happen, and a bad media file must never interrupt a player's dismount.
                _logger?.LogDebug("Radio decoder cleanup: " + error.Message);
            }
            finally
            {
                request.Dispose();
            }
        }

        private void ReleaseClip()
        {
            if (_source != null)
            {
                _source.Stop();
                _source.clip = null;
            }
            _gameMusic.Dispose();
            if (_clip != null)
                Destroy(_clip);
            _clip = null;
            _playingPath = null;
            _hasPlayed = false;
            _playbackGraceUntil = 0f;
            NowPlaying = string.Empty;
        }

        private void OnDisable()
        {
            StopPlayback();
        }

        private void OnEnable()
        {
            if (_initialized && _skating)
                Restart();
        }

        private void OnDestroy()
        {
            if (_enabled != null)
                _enabled.SettingChanged -= RequestRestart;
            if (_directory != null)
                _directory.SettingChanged -= RequestRestart;
            StopPlayback();
            if (_audioObject != null)
                Destroy(_audioObject);
            _initialized = false;
        }

        private static bool IsFileError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is NotSupportedException ||
                error is System.Security.SecurityException;
        }
    }
}
