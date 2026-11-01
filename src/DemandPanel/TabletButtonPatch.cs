using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DemandPanel
{
    // CreateWindow ends with the whole tablet inactive, and text built and measured under an inactive
    // parent gets the wrong width (the game's own TabletUI.MeasureWidth warns about it). OpenUI
    // activates the tablet first, so the button is built there. It runs on every opening, which Build
    // handles with its one-attempt-per-tablet check. The private field "frame" is injected by its
    // literal name.
    [HarmonyPatch(typeof(NegociationUIManager), "OpenUI")]
    internal static class TabletButtonPatch
    {
        private static void Postfix(NegotiationTabletFrame ___frame)
        {
            try
            {
                TabletButton.Build(___frame);
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Tablet button failed: " + e.Message);
            }
        }
    }

    // Inside namespace DemandPanel this name hides the game's global TabletButton component. Code here
    // never names the game type; it only uses the value TabletUI.SmallButton returns, through var.
    internal static class TabletButton
    {
        private const string ButtonName = "DemandPanelBtn";
        private const float Gap = 8f;
        private const float MinButtonWidth = 120f;
        private const float ButtonPadding = 16f;
        private const float NarrowButtonWidth = 96f;
        private const float LabelInset = 8f;
        private const float LabelMinFontSize = 10f;

        private static GameObject button;

        // The last tablet frame Build looked at. A new scene creates a new frame object.
        private static NegotiationTabletFrame triedFrame;

        // Shows or hides the button if it still exists. A scene change destroys it, which the Unity
        // null check reports, and then nothing happens until the next Build.
        public static void SetVisible(bool visible)
        {
            try
            {
                if (button == null)
                {
                    return;
                }
                if (button.activeSelf != visible)
                {
                    button.SetActive(visible);
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Tablet button visibility failed: " + e.Message);
            }
        }

        public static bool IsAlive => button != null;

        // The postfix only fires when the tablet opens after the mod is enabled. When the mod is
        // enabled while the tablet is already open, the Driver calls this to build from it
        // (uiObject is a public field). Build itself ignores a tablet that is not active.
        public static void TryBuildFromScene()
        {
            try
            {
                if (button != null)
                {
                    return;
                }
                NegociationUIManager manager = NegociationUIManager.Instance;
                if (manager == null || manager.uiObject == null)
                {
                    return;
                }
                Build(manager.uiObject.GetComponent<NegotiationTabletFrame>());
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Tablet button failed: " + e.Message);
            }
        }

        public static void Remove()
        {
            try
            {
                if (button != null)
                {
                    UnityEngine.Object.Destroy(button);
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Tablet button removal failed: " + e.Message);
            }
            button = null;
            // After a disable and enable in the same scene the tablet is the same object, so it must be
            // allowed to build again.
            triedFrame = null;
        }

        internal static void Build(NegotiationTabletFrame frame)
        {
            if (button != null)
            {
                return;
            }
            if (frame == null || frame.panel == null || frame.closeButton == null || frame.resizeButton == null)
            {
                return;
            }
            // Not marked as tried, so the next opening builds it.
            if (!frame.panel.gameObject.activeInHierarchy)
            {
                return;
            }

            // One attempt per tablet. When no spot is free, the Driver's once-per-second retry would
            // otherwise measure and log the same result again for the same frame.
            if (ReferenceEquals(frame, triedFrame))
            {
                return;
            }
            triedFrame = frame;

            // A second enable can find the button of the first one.
            Transform existing = frame.panel.Find(ButtonName);
            if (existing != null)
            {
                button = existing.gameObject;
                return;
            }

            RectTransform panel = frame.panel;
            // The layout of the prefab may not be computed yet when Start runs.
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            if (!TryGetBounds(panel, (RectTransform)frame.resizeButton.transform, out Rect resize)
                || !TryGetBounds(panel, (RectTransform)frame.closeButton.transform, out Rect close))
            {
                return;
            }
            Rect panelRect = panel.rect;
            Rect bargain = default;
            bool hasBargain = frame.bargainButton != null
                && TryGetBounds(panel, (RectTransform)frame.bargainButton.transform, out bargain);

            float height = Mathf.Clamp(resize.height, 24f, 40f);
            float y = resize.yMin + (resize.height - height) * 0.5f;
            string label = Loc.T("btn_open_panel");
            float measured;
            var measuringButton = TabletUI.SmallButton(frame, panel, 0f, 0f, MinButtonWidth, height, label, null);
            if (measuringButton == null)
            {
                return;
            }
            try
            {
                // The panel is active, so the button is too. The game helper resolves Noto_Sans for the
                // selected locale, or returns null.
                var localeFont = CustomUIMachineryStyle.Font();
                if (localeFont != null)
                {
                    measuringButton.label.font = localeFont;
                }
                measuringButton.label.enableAutoSizing = false;
                measuringButton.label.fontSize = 16f;
                measured = TabletUI.MeasureWidth(measuringButton.label, label);
            }
            finally
            {
                UnityEngine.Object.Destroy(measuringButton.gameObject);
            }

            // The label sits 4 px inside each edge of the button (TabletUI.BuildButton).
            float wantedWidth = Mathf.Max(MinButtonWidth, measured + LabelInset + ButtonPadding);

            // Full width first. When the free gap is smaller the button shrinks, and the label's own
            // size range and ellipsis keep the text inside it.
            float buttonWidth = 0f;
            float x = 0f;
            bool placed = false;
            foreach (float width in new[] { wantedWidth, MinButtonWidth, NarrowButtonWidth })
            {
                if (width > wantedWidth)
                {
                    continue;
                }
                // Left of the leftmost of the two corner controls first, right of the rightmost second.
                float leftX = Mathf.Min(resize.xMin, close.xMin) - Gap - width;
                float rightX = Mathf.Max(resize.xMax, close.xMax) + Gap;
                if (Fits(leftX, y, width, height, panelRect, resize, close, hasBargain, bargain))
                {
                    x = leftX;
                }
                else if (Fits(rightX, y, width, height, panelRect, resize, close, hasBargain, bargain))
                {
                    x = rightX;
                }
                else
                {
                    continue;
                }
                buttonWidth = width;
                placed = true;
                break;
            }
            if (!placed)
            {
                ModEntry.Log.Log("No free spot next to the tablet controls, button not added");
                return;
            }

            // Coordinates here are measured from the panel's top left corner, downwards, which is what
            // TabletUI.NewRect and SmallButton use (WildNetworkUI.SetRect).
            var created = TabletUI.SmallButton(frame, panel, x, y, buttonWidth, height, label, Driver.TogglePanel);
            if (created == null)
            {
                return;
            }
            created.label.fontSizeMin = LabelMinFontSize;
            created.label.overflowMode = TextOverflowModes.Ellipsis;
            created.gameObject.name = ButtonName;
            created.gameObject.SetActive(false);
            button = created.gameObject;
        }

        // Bounds of a control in the panel's own space, measured from the panel's top left corner with y
        // growing downwards. The panel's scale and the zoom of the tablet cancel out because the world
        // corners are converted back into panel space.
        private static bool TryGetBounds(RectTransform panel, RectTransform target, out Rect bounds)
        {
            bounds = default;
            if (panel == null || target == null)
            {
                return false;
            }
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Rect panelRect = panel.rect;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            foreach (Vector3 corner in corners)
            {
                Vector3 local = panel.InverseTransformPoint(corner);
                float px = local.x - panelRect.xMin;
                float py = panelRect.yMax - local.y;
                minX = Mathf.Min(minX, px);
                maxX = Mathf.Max(maxX, px);
                minY = Mathf.Min(minY, py);
                maxY = Mathf.Max(maxY, py);
            }
            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return bounds.width > 0f && bounds.height > 0f;
        }

        private static bool Fits(float x, float y, float width, float height, Rect panelRect, Rect resize, Rect close, bool hasBargain, Rect bargain)
        {
            var candidate = new Rect(x, y, width, height);
            if (candidate.xMin < 0f || candidate.xMax > panelRect.width || candidate.yMin < 0f || candidate.yMax > panelRect.height)
            {
                return false;
            }
            return !candidate.Overlaps(resize)
                && !candidate.Overlaps(close)
                && !(hasBargain && candidate.Overlaps(bargain));
        }
    }
}
