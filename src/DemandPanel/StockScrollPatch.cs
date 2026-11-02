using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DemandPanel
{
    [HarmonyPatch(typeof(TreeNodeRender), "OnScroll")]
    internal static class StockScrollPatch
    {
        private static bool Prefix(TreeNodeRender __instance, PointerEventData eventData)
        {
            var viewport = __instance.FindParentElement<NativePagedViewport>();
            if (viewport == null || eventData == null || !viewport.Scroll(eventData.scrollDelta,
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return true;
            eventData.Use();
            CustomEventHandler.ClearDoubleClickTarget();
            return false;
        }
    }
}
