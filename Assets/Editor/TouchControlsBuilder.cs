using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.EditorTools
{
    /// <summary>
    /// Phone sticks on the HUD (the original's PW_TouchInterface textures): a floating run stick in the bottom left,
    /// the hook stick on the right above the shop bar. Built under the shop so the open shop panel covers them.
    /// </summary>
    public static class TouchControlsBuilder
    {
        const string Dir = "Assets/Art/Extracted/Assets/Touch/";
        const string StickBackground = Dir + "VirtualJoystick_Background.png";
        const string StickThumb = Dir + "VirtualJoystick_Thumb.png";
        const string HookIcon = Dir + "JoystikHook.png";

        const float MoveSize = 320f, MoveThumb = 140f;
        const float HookSize = 260f, HookThumb = 110f, HookZonePadding = 30f;

        public static TouchControls Build(Transform canvas)
        {
            var root = ShopUiBuilder.Rect(canvas, "TouchControls", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var controls = root.gameObject.AddComponent<TouchControls>();

            // run: the whole bottom-left corner catches the finger, the stick comes to it
            var moveZone = ShopUiBuilder.Rect(root, "MoveStick", Vector2.zero, Vector2.zero, new Vector2(680f, 600f));
            controls.moveStick = Stick(moveZone, MoveSize, StickBackground, new Color(1f, 1f, 1f, 0.45f), MoveThumb, new Color(1f, 1f, 1f, 0.85f));
            controls.moveStick.floating = true;

            // hook: fixed, over the shop bar (24 px margin + 128 px bar)
            var hookZone = ShopUiBuilder.Rect(root, "HookStick", new Vector2(1f, 0f), new Vector2(-50f, 180f),
                new Vector2(HookSize + 2f * HookZonePadding, HookSize + 2f * HookZonePadding));
            controls.hookStick = Stick(hookZone, HookSize, HookIcon, Color.white, HookThumb, new Color(1f, 1f, 1f, 0.75f));
            controls.hookStick.deadZone = 0.25f;

            // like the ability icon: the icon's own sprite as the mask, so the fill covers exactly the circle
            var icon = controls.hookStick.background.GetComponent<Image>();
            var cooldown = ShopUiBuilder.Img(ShopUiBuilder.Rect(controls.hookStick.background, "Cooldown", new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(HookSize, HookSize)), new Color(0f, 0f, 0f, 0.65f));
            cooldown.sprite = icon.sprite;
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Radial360;
            cooldown.fillOrigin = (int)Image.Origin360.Top;
            cooldown.fillClockwise = false;
            cooldown.raycastTarget = false;
            cooldown.enabled = false;
            cooldown.transform.SetSiblingIndex(0); // under the thumb
            controls.hookCooldown = cooldown;
            return controls;
        }

        /// <summary>Transparent touch zone with the stick's background centred in it and the thumb in the background's centre.</summary>
        static VirtualJoystick Stick(RectTransform zone, float size, string backgroundPath, Color backgroundColor, float thumbSize, Color thumbColor)
        {
            ShopUiBuilder.Img(zone, Color.clear); // invisible, but catches the touches
            var stick = zone.gameObject.AddComponent<VirtualJoystick>();

            stick.background = ShopUiBuilder.Rect(zone, "Background", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var bg = ShopUiBuilder.Img(stick.background, backgroundColor);
            bg.sprite = AbilityBarBuilder.Sprite(backgroundPath);
            bg.raycastTarget = false;

            stick.thumb = ShopUiBuilder.Rect(stick.background, "Thumb", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(thumbSize, thumbSize));
            var thumb = ShopUiBuilder.Img(stick.thumb, thumbColor);
            thumb.sprite = AbilityBarBuilder.Sprite(StickThumb);
            thumb.raycastTarget = false;
            return stick;
        }
    }
}
