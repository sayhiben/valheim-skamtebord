using UnityEngine;

namespace Skamtebord;

internal sealed class SkateHud : MonoBehaviour
{
    private GUIStyle title, text, small, speedStyle, radioStyle;
    internal Rect LastPanelRect { get; private set; }

    private void OnGUI()
    {
        var plugin = SkamtebordPlugin.Instance;
        var rider = BoardRider.Local;
        if (!plugin || !plugin.Settings.ShowHud.Value || !rider || !rider.Riding || !BoardRider.InputAllowed()) return;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1, .77f, .35f) } };
            speedStyle = new GUIStyle(title) { fontSize = 22, alignment = TextAnchor.MiddleRight };
            text = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, normal = { textColor = Color.white } };
            small = new GUIStyle(text) { fontSize = 13, normal = { textColor = new Color(.84f, .88f, .88f) } };
            radioStyle = new GUIStyle(small) { wordWrap = false, clipping = TextClipping.Clip };
        }
        var s = plugin.Settings;
        // Keep text readable at 1440p/4K; the old scale capped at 1.5 and anchored
        // directly over health. Fit the full panel when playing in a small window.
        float scale = Mathf.Max(1f, Screen.height / 1080f) * Mathf.Clamp(s.HudScale.Value, .75f, 1.5f);
        scale = Mathf.Min(scale, (Screen.width - 32f) / 350f, (Screen.height - 32f) / 232f);
        const float width = 350, height = 232;
        Vector2 position = s.HudPosition.Value;
        float x = 16 + Mathf.Clamp01(position.x) * Mathf.Max(0, Screen.width - 32 - width * scale);
        float y = 16 + Mathf.Clamp01(position.y) * Mathf.Max(0, Screen.height - 32 - height * scale);
        LastPanelRect = new Rect(x, y, width * scale, height * scale);
        var previous = GUI.matrix;
        var color = GUI.color;
        try
        {
            GUI.matrix = Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.color = new Color(.035f, .055f, .06f, .9f);
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(14, 10, 190, 29), "SKAMTEBORD", title);
            GUI.Label(new Rect(201, 5, 134, 36), $"{rider.Speed * 3.6f:0} km/h", speedStyle);
            string combo = rider.Combo.PendingScore > 0
                ? $"{rider.Combo.PendingScore:N0} pts  ×{rider.Combo.Multiplier}  {rider.Combo.ComboLabel}"
                : "Find a line. Keep rolling.";
            GUI.Label(new Rect(14, 43, 322, 40), combo, text);
            GUI.Label(new Rect(14, 83, 322, 25), $"Level {rider.Progression.Level}  ·  {rider.Progression.LifetimePoints:N0} XP", text);
            string status = rider.Sprinting ? "Tucked sprint · 6 stamina / second"
                : Time.time < rider.StatusUntil ? rider.Status : "Land, then roll for 2 seconds to bank.";
            GUI.Label(new Rect(14, 110, 322, 36), status, small);
            GUI.Label(new Rect(14, 148, 322, 22), $"{ZInput.instance.GetBoundKeyString("Run")}: sprint  ·  Jump: ollie  ·  {s.Mount.Value}: off", small);
            GUI.Label(new Rect(14, 173, 322, 22), $"{s.Shuvit.Value} shuvit · {s.Kickflip.Value} flip · {s.Heelflip.Value} heel · {s.Grab.Value} grab · {s.Spin.Value} 360", small);
            string radio = plugin.Radio && plugin.Radio.IsEnabled
                ? (string.IsNullOrEmpty(plugin.Radio.NowPlaying) ? plugin.Radio.Status : plugin.Radio.NowPlaying) : "Radio off";
            GUI.Label(new Rect(14, 198, 242, 22), new GUIContent("♫ " + radio, radio), radioStyle);
            GUI.Label(new Rect(261, 198, 75, 22), $"{s.RadioToggle.Value} / {s.RadioNext.Value}", radioStyle);
        }
        finally { GUI.matrix = previous; GUI.color = color; }
    }
}
