using System.Collections.Generic;
using HarmonyLib;

namespace DemandPanel
{
    // The native handler retains highlight nodes between drags, including destroyed page nodes.
    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class DragHighlightPatches
    {
        internal static void Prefix(List<TreeNodeRender> ___highlightedMayNodes)
        {
            ___highlightedMayNodes.RemoveAll(node => node == null || node.image == null);
        }

        internal static void Postfix(List<TreeNodeRender> ___highlightedMayNodes)
        {
            ___highlightedMayNodes.Clear();
        }
    }

    // AbortDrag also touches the cached images before completing its reset.
    [HarmonyPatch(typeof(ItemMouseDragHandler), "AbortDrag")]
    internal static class AbortDragHighlightPatches
    {
        private static void Prefix(List<TreeNodeRender> ___highlightedMayNodes)
        {
            DragHighlightPatches.Prefix(___highlightedMayNodes);
        }

        private static void Postfix(List<TreeNodeRender> ___highlightedMayNodes)
        {
            DragHighlightPatches.Postfix(___highlightedMayNodes);
        }
    }
}
