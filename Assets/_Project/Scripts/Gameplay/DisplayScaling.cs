using System.Text.RegularExpressions;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// GNOME fractional scaling hands X11 apps a virtual screen bigger than the monitor (2560x1440 at 125% →
    /// 4096x2304), so the player rendered 2.5x the pixels and the compositor shrank them back. On startup asks
    /// Mutter for the monitor's real mode and renders at it in borderless fullscreen.
    /// </summary>
    public static class DisplayScaling
    {
        /// <summary>Monitor's physical mode reported by the compositor; zero if unknown.</summary>
        public static Vector2Int NativeSize { get; private set; }

        static readonly Regex CurrentMode = new(@"\('[^']*', (\d+), (\d+), [\d.]+, [\d.]+, \[[^\]]*\], \{[^}]*'is-current': <true>");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            NativeSize = QueryMutter(Screen.currentResolution.width, Screen.currentResolution.height);
            if (NativeSize == Vector2Int.zero || Screen.fullScreenMode != FullScreenMode.FullScreenWindow) return;
            if (Screen.width == NativeSize.x && Screen.height == NativeSize.y) return;
            Debug.Log($"[DisplayScaling] desktop {Screen.currentResolution.width}x{Screen.currentResolution.height}, monitor {NativeSize.x}x{NativeSize.y} — rendering at monitor size");
            Screen.SetResolution(NativeSize.x, NativeSize.y, FullScreenMode.FullScreenWindow, Screen.currentResolution.refreshRateRatio);
#endif
        }

        /// <summary>Current mode of the monitor whose aspect matches the desktop (gdbus → Mutter DisplayConfig).</summary>
        static Vector2Int QueryMutter(int desktopWidth, int desktopHeight)
        {
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
                if (process == null) return Vector2Int.zero;
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(1500) || process.ExitCode != 0) return Vector2Int.zero;

                float aspect = desktopWidth / (float)desktopHeight;
                var best = Vector2Int.zero;
                foreach (Match m in CurrentMode.Matches(output.Result))
                {
                    var size = new Vector2Int(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
                    if (Mathf.Abs(size.x / (float)size.y - aspect) < 0.02f && size.x > best.x) best = size;
                }
                return best;
            }
            catch (System.Exception e)
            {
                Debug.Log($"[DisplayScaling] Mutter query failed: {e.Message}");
                return Vector2Int.zero;
            }
        }
    }
}
