using BepInEx.Configuration;
using System.Linq;
using UnityEngine;

namespace Skamtebord;

internal sealed class SkateSettings
{
    internal readonly ConfigEntry<KeyboardShortcut> Mount, Shuvit, Kickflip, Heelflip, Grab, Spin, RadioToggle, RadioNext;
    internal readonly ConfigEntry<float> PushAcceleration, PushTopSpeed, MaximumSpeed, TurnSpeed, BrakeStrength, JumpSpeed;
    internal readonly ConfigEntry<float> SprintAcceleration, SprintTopSpeed;
    internal readonly ConfigEntry<bool> AthleticsExperience, ShowHud;
    internal readonly ConfigEntry<float> HudScale;
    internal readonly ConfigEntry<Vector2> HudPosition;

    internal SkateSettings(ConfigFile config)
    {
        Mount = Key("Mount", KeyCode.B, "Mount/dismount a skateboard in your inventory. Hotbar use also toggles it.");
        Shuvit = Key("Shuvit", KeyCode.J, "Air trick; requires Skamtebord level 3.");
        Kickflip = Key("Kickflip", KeyCode.K, "Air trick; requires level 8.");
        Heelflip = Key("Heelflip", KeyCode.L, "Air trick; requires level 12.");
        Grab = Key("Grab", KeyCode.U, "Air trick; requires level 18.");
        Spin = Key("ThreeSixty", KeyCode.I, "Air trick; requires level 25.");
        RadioToggle = Key("RadioToggle", KeyCode.F8, "Toggle skating radio.");
        RadioNext = Key("RadioNext", KeyCode.F9, "Skip track. Remount to rescan the MP3 directory.");
        var layout = config.Bind("Internal", "ControlLayoutVersion", 0, "One-time migration of legacy default trick keys; custom bindings are preserved.");
        if (layout.Value < 1)
        {
            Migrate(Shuvit, KeyCode.Q, KeyCode.J); Migrate(Kickflip, KeyCode.E, KeyCode.K);
            Migrate(Heelflip, KeyCode.R, KeyCode.L); Migrate(Grab, KeyCode.F, KeyCode.U); Migrate(Spin, KeyCode.C, KeyCode.I);
            layout.Value = 1;
        }
        PushAcceleration = Number("PushAcceleration", 5.5f, 1f, 15f, "Forward acceleration while pushing, m/s². Pushing uses stamina.");
        PushTopSpeed = Number("PushTopSpeed", 9f, 3f, 15f, "Pushing stops adding speed above this, m/s; gravity can go faster.");
        SprintAcceleration = Number("SprintAcceleration", 8f, 1f, 20f, "Acceleration while holding forward and Valheim's sprint input, m/s². Uses 6 stamina per second and a tucked stance.");
        SprintTopSpeed = Number("SprintTopSpeed", 14f, 9f, 25f, "Sprint skating top speed, m/s. Does not overwrite faster downhill momentum.");
        MaximumSpeed = Number("MaximumSpeed", 25f, 10f, 40f, "Soft downhill speed limit, m/s.");
        TurnSpeed = Number("TurnSpeed", 105f, 30f, 180f, "Steering degrees per second; decreases at speed.");
        BrakeStrength = Number("BrakeStrength", 10f, 2f, 25f, "Braking deceleration, m/s².");
        JumpSpeed = Number("JumpSpeed", 5.2f, 3f, 8f, "Ollie upward impulse added to current velocity, m/s. Time it near a ramp lip for more air; 0.12 seconds of takeoff grace.");
        AthleticsExperience = config.Bind("Progression", "AthleticsExperience", false, "Optional small Jump skill reward: 0.05 per bank, at most once every 30 seconds. Vanilla ollie Jump XP is suppressed.");
        ShowHud = config.Bind("Interface", "ShowHud", true, "Show speed, combo, skill and controls while skating.");
        HudScale = config.Bind("Interface", "HudScale", 1f, new ConfigDescription("Skating panel size, independent of Valheim UI scale.", new AcceptableValueRange<float>(.75f, 1.5f)));
        HudPosition = config.Bind("Interface", "HudPosition", new Vector2(1f, .52f), "Position within the usable screen: X 0=left, 1=right; Y 0=top, 1=bottom. Default is middle-right, clear of health, stamina and the minimap.");
        ConfigEntry<KeyboardShortcut> Key(string name, KeyCode key, string description) => config.Bind("Controls", name, new KeyboardShortcut(key), description);
        ConfigEntry<float> Number(string name, float value, float min, float max, string description) => config.Bind("Physics", name, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        void Migrate(ConfigEntry<KeyboardShortcut> entry, KeyCode previous, KeyCode next)
        {
            if (entry.Value.MainKey == previous && !entry.Value.Modifiers.Any()) entry.Value = new KeyboardShortcut(next);
        }
    }
}
