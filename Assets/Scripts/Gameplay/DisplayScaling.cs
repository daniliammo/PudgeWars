using System.Text.RegularExpressions;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// GNOME fractional scaling hands X11 apps a virtual screen bigger than the monitor (2560x1440 at 125% →
    /// 4096x2304), so the player rendered 2.5x the pixels and the compositor shrank them back. On startup asks
    /// Mutter for the monitor's real mode and renders at it in borderless fullscreen. The mode's refresh rate is
    /// also read in the editor, where Screen.currentResolution always reports 60 Hz.
    /// </summary>
    public static class DisplayScaling
    {
        /// <summary>Monitor's physical mode reported by the compositor; zero if unknown.</summary>
        public static Vector2Int NativeSize { get; private set; }

        /// <summary>Monitor's refresh rate reported by the compositor, Hz; zero if unknown.</summary>
        public static float NativeRefreshRate { get; private set; }

        static readonly Regex CurrentMode = new(@"\('[^']*', (\d+), (\d+), ([\d.]+), [\d.]+, \[[^\]]*\], \{[^}]*'is-current': <true>");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            QueryMutter(Screen.currentResolution.width, Screen.currentResolution.height);
#endif
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            if (NativeSize == Vector2Int.zero || Screen.fullScreenMode != FullScreenMode.FullScreenWindow) return;
            if (Screen.width == NativeSize.x && Screen.height == NativeSize.y) return;
            Debug.Log($"[DisplayScaling] desktop {Screen.currentResolution.width}x{Screen.currentResolution.height}, monitor {NativeSize.x}x{NativeSize.y} — rendering at monitor size");
            Screen.SetResolution(NativeSize.x, NativeSize.y, FullScreenMode.FullScreenWindow, Screen.currentResolution.refreshRateRatio);
#endif
        }

        /// <summary>
        /// Fills NativeSize/NativeRefreshRate from the current mode of the monitor whose aspect matches the desktop
        /// (gdbus → Mutter DisplayConfig).
        /// </summary>
        static void QueryMutter(int desktopWidth, int desktopHeight)
        {
            NativeSize = Vector2Int.zero;
            NativeRefreshRate = 0f;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("gdbus",
                    "call --session --dest org.gnome.Mutter.DisplayConfig --object-path /org/gnome/Mutter/DisplayConfig --method org.gnome.Mutter.DisplayConfig.GetCurrentState")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null) return;
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(1500) || process.ExitCode != 0) return;

                float aspect = desktopWidth / (float)desktopHeight;
                foreach (Match m in CurrentMode.Matches(output.Result))
                {
                    var size = new Vector2Int(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
                    if (Mathf.Abs(size.x / (float)size.y - aspect) >= 0.02f || size.x <= NativeSize.x) continue;
                    NativeSize = size;
                    NativeRefreshRate = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (System.Exception e)
            {
                Debug.Log($"[DisplayScaling] Mutter query failed: {e.Message}");
            }
        }
    }
}
