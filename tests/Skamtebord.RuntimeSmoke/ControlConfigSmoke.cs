using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

internal static class ControlConfigSmoke
{
    internal static void Run(string root, Action<bool, string> check)
    {
        string[] names = { "Shuvit", "Kickflip", "Heelflip", "Grab", "ThreeSixty" };
        KeyCode[] legacy = { KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F, KeyCode.C };
        KeyCode[] next = { KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.U, KeyCode.I };
        var config = new ConfigFile(Path.Combine(root, "legacy-controls.cfg"), false);
        for (int i = 0; i < names.Length; i++) config.Bind("Controls", names[i], new KeyboardShortcut(legacy[i]));
        var ctor = AccessTools.Constructor(AccessTools.TypeByName("Skamtebord.SkateSettings"), new[] { typeof(ConfigFile) });
        ctor.Invoke(new object[] { config });
        for (int i = 0; i < names.Length; i++)
            check(config.Bind("Controls", names[i], new KeyboardShortcut()).Value.MainKey == next[i], names[i] + " legacy default migrates to " + next[i]);
        var custom = new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt);
        var customConfig = new ConfigFile(Path.Combine(root, "custom-controls.cfg"), false);
        var entry = customConfig.Bind("Controls", "Kickflip", custom);
        ctor.Invoke(new object[] { customConfig });
        check(entry.Value.Equals(custom), "migration preserves a custom modified binding");
        entry.Value = new KeyboardShortcut(KeyCode.E);
        ctor.Invoke(new object[] { customConfig });
        check(entry.Value.MainKey == KeyCode.E, "migration runs only once and preserves later rebindings");
        // ZInput's current defaults were read from this installed game's ResetKBMButtons.
        var vanilla = new[] { KeyCode.E, KeyCode.R, KeyCode.Space, KeyCode.LeftControl, KeyCode.LeftShift,
            KeyCode.C, KeyCode.Q, KeyCode.X, KeyCode.F, KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
            KeyCode.Tab, KeyCode.M, KeyCode.Comma, KeyCode.Period, KeyCode.V, KeyCode.F5, KeyCode.G, KeyCode.T };
        check(!next.Intersect(vanilla).Any() && next.Distinct().Count() == 5, "default trick cluster has no Valheim 1.0.16 action collisions");
    }
}
