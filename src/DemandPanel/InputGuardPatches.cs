using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DemandPanel
{
    // The game finds the item under the mouse with a pixel raycast that skips every uGUI object that
    // is not a game node, so a press on the panel also reaches the game item underneath. Each prefix
    // skips the press handler when the pointer is over the panel and the press is the handler's own
    // mouse button. A press that only carries a modifier key still runs, so the handler's state
    // stays consistent.
    //
    // The release paths are not patched, so a skipped press must not leave a target behind:
    // the quick transfer handlers only clear currentItem on a release that finds an item, so a
    // target left over from an earlier press would still match the item under the panel. Each
    // skipping prefix therefore clears it, and the double click handler is reset.
    internal static class InputGuard
    {
        public static bool ShouldSkip(KeyCode mouseKey, ISet<KeyCode> keysPressed)
        {
            try
            {
                return keysPressed != null && keysPressed.Contains(mouseKey) && PanelView.IsPointerOverPanel;
            }
            catch
            {
                // A guard that throws would break the game's own input, so fail open.
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(ItemQuickTransferAltHandler1), "OnEventPress")]
    internal static class QuickTransferStoragePressGuard
    {
        private static bool Prefix(ItemQuickTransferAltHandler1 __instance, ISet<KeyCode> keysPressed, ref GameItem ___currentItem)
        {
            if (!InputGuard.ShouldSkip(__instance.key, keysPressed))
            {
                return true;
            }
            ___currentItem = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemQuickTransferAltHandler), "OnEventPress")]
    internal static class QuickTransferShowcasePressGuard
    {
        private static bool Prefix(ItemQuickTransferAltHandler __instance, ISet<KeyCode> keysPressed, ref GameItem ___currentItem)
        {
            if (!InputGuard.ShouldSkip(__instance.key, keysPressed))
            {
                return true;
            }
            ___currentItem = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemQuickTransferHandler), "OnEventPress")]
    internal static class QuickTransferInventoryPressGuard
    {
        private static bool Prefix(ItemQuickTransferHandler __instance, ISet<KeyCode> keysPressed, ref GameItem ___currentItem)
        {
            if (!InputGuard.ShouldSkip(__instance.key, keysPressed))
            {
                return true;
            }
            ___currentItem = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "OnEventPress")]
    internal static class DoubleClickPressGuard
    {
        private static bool Prefix(ItemMouseDoubleClickHandler __instance, ISet<KeyCode> keysPressed)
        {
            if (!InputGuard.ShouldSkip(__instance.key, keysPressed))
            {
                return true;
            }
            // OnEventRelease never clears lastClickedItem after a single click, so a click made before
            // the panel opened would still be replayed through the panel. UpdateReset runs OnEventReset.
            __instance.UpdateReset();
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "OnEventPress")]
    internal static class MouseDragPressGuard
    {
        // While a game item is already being dragged this press method also handles rotate and flip
        // keys, so it must keep running. When it is not dragging, OnEventReset only clears the
        // handler's fields (ItemMouseDragHandler.cs:333-349), and calling it drops a press target left
        // by an earlier press whose release was never delivered (InputActionManager can stop
        // dispatching to lower priority handlers within a frame), which OnEventMouseMove could
        // otherwise turn into a drag of the item under the panel.
        private static bool Prefix(ItemMouseDragHandler __instance, ISet<KeyCode> keysPressed)
        {
            if (__instance.IsDraggingItem || !InputGuard.ShouldSkip(__instance.key, keysPressed))
            {
                return true;
            }
            __instance.UpdateReset();
            return false;
        }
    }
}
