using System.Linq;
using PudgeWars.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.EditorTools
{
    /// <summary>uGUI ability bar at the bottom centre of the HUD (slot 0: the hook, key Q).</summary>
    public static class AbilityBarBuilder
    {
        const string HookIcon = "Assets/_Project/Art/UI/Abilities/hook.jpeg";
        const float SlotSize = 112f, Gap = 12f, Padding = 14f;

        public static AbilityBar Build(Transform canvas, Font font)
        {
            var abilities = new[] { (name: "Hook", key: "Q", icon: Sprite(HookIcon)) };

            var bar = canvas.gameObject.AddComponent<AbilityBar>();
            var width = 2f * Padding + abilities.Length * SlotSize + (abilities.Length - 1) * Gap;
            var panel = ShopUiBuilder.Rect(canvas, "AbilityBar", new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(width, SlotSize + 2f * Padding));
            ShopUiBuilder.Img(panel, ShopUiBuilder.Panel);

            bar.frameColor = ShopUiBuilder.Frame;
            bar.slots = new AbilitySlotView[abilities.Length];
            for (int i = 0; i < abilities.Length; i++)
            {
                var (name, key, icon) = abilities[i];
                var slotRt = ShopUiBuilder.Rect(panel, "Ability_" + name, new Vector2(0f, 0.5f), new Vector2(Padding + i * (SlotSize + Gap), 0f), new Vector2(SlotSize, SlotSize));
                var view = slotRt.gameObject.AddComponent<AbilitySlotView>();
                view.frame = ShopUiBuilder.Img(slotRt, ShopUiBuilder.Frame);

                var inner = new Vector2(SlotSize - 8f, SlotSize - 8f);
                view.icon = ShopUiBuilder.Img(ShopUiBuilder.Rect(slotRt, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, inner), Color.white);
                view.icon.sprite = icon;

                // the icon's own sprite as the mask so the radial fill covers exactly the picture
                view.cooldownOverlay = ShopUiBuilder.Img(ShopUiBuilder.Rect(slotRt, "Cooldown", new Vector2(0.5f, 0.5f), Vector2.zero, inner), new Color(0f, 0f, 0f, 0.65f));
                view.cooldownOverlay.sprite = icon;
                view.cooldownOverlay.type = Image.Type.Filled;
                view.cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
                view.cooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
                view.cooldownOverlay.fillClockwise = false;
                view.cooldownOverlay.raycastTarget = false;
                view.cooldownOverlay.enabled = false;

                view.cooldownLabel = ShopUiBuilder.Txt(slotRt, "CooldownTime", font, 30, "", TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, inner);
                view.cooldownLabel.fontStyle = FontStyle.Bold;
                view.cooldownLabel.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);

                view.keyLabel = ShopUiBuilder.Txt(slotRt, "Key", font, 26, key, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(8f, -4f), new Vector2(40f, 34f));
                view.keyLabel.fontStyle = FontStyle.Bold;
                view.keyLabel.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);

                view.button = slotRt.gameObject.AddComponent<Button>();
                view.button.targetGraphic = view.icon;
                bar.slots[i] = view;
            }
            return bar;
        }

        static Sprite Sprite(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti != null && (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single
                               || ti.textureShape != TextureImporterShape.Texture2D))
            {
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) Debug.LogError("[AbilityBarBuilder] no sprite in " + path);
            return sprite;
        }
    }
}
