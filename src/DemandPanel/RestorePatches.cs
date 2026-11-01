using System;
using HarmonyLib;

namespace DemandPanel
{
    // Native partial drops split AFTER the grid checks the original item. The resulting new item is
    // untracked and would be stranded in an unsaved window. Reject that commit before it can split,
    // and also recheck ownership of the record in case a game-specific path bypassed the grid gate.
    [HarmonyPatch(typeof(SlotMarker), "TryAcceptOnce")]
    internal static class PanelTransferGuard
    {
        private static bool Prefix(SlotMarker __instance, int amount, ref int __result)
        {
            try
            {
                if (!NativePanel.OwnsInventory(__instance.inventory))
                {
                    return true;
                }
                GameItem item = __instance.item;
                if (item != null && NativeMover.IsTracked(item) && __instance.targetItem == null)
                {
                    int transfer = Math.Min(amount < 0 ? __instance.numTransfer : amount, __instance.numTransfer);
                    transfer = Math.Min(transfer, item.MaxNumRemove());
                    if (item.unitCount > 0 && transfer >= item.unitCount)
                    {
                        return true;
                    }
                }
                __result = 0;
                return false;
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Panel transfer guard failed: " + e.Message);
                return true;
            }
        }
    }

    // The game never saves the panel window and its scans of the store do not look into it, so each
    // of these restores synchronously, except dismissal which may run inside an inventory callback
    // and must defer. Close() is safe to repeat; every prefix lets the game's own method run.
    internal static class RestoreGuard
    {
        public static void Run(string source, bool beforeSave = false)
        {
            try
            {
                NativePanel.Close(beforeSave);
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Restore before " + source + " failed: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "SaveGame")]
    internal static class RestoreBeforeSave
    {
        private static bool Prefix()
        {
            RestoreGuard.Run("SaveGame", beforeSave: true);
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "EndNight")]
    internal static class RestoreBeforeEndNight
    {
        private static bool Prefix()
        {
            RestoreGuard.Run("EndNight", beforeSave: true);
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "DismissCurrentClient")]
    internal static class RestoreBeforeDismiss
    {
        private static bool Prefix()
        {
            try
            {
                // Inventory callbacks can start a dialogue that executes the old dialogue's end
                // action synchronously, including dismissal. Do not move items on that stack.
                LetterboxManager.RunNextUpdate(() => RestoreGuard.Run("DismissCurrentClient"));
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Scheduling restore before DismissCurrentClient failed: " + e.Message);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ContrabandHelper), "StartInspection")]
    internal static class RestoreBeforeInspection
    {
        private static bool Prefix()
        {
            RestoreGuard.Run("StartInspection");
            return true;
        }
    }

    [HarmonyPatch(typeof(ContrabandHelper), "SecurityCheckCounter")]
    internal static class RestoreBeforeCounterCheck
    {
        private static bool Prefix()
        {
            RestoreGuard.Run("SecurityCheckCounter");
            return true;
        }
    }

    [HarmonyPatch(typeof(ContrabandHelper), "SecurityCheckDisplayCase")]
    internal static class RestoreBeforeDisplayCaseCheck
    {
        private static bool Prefix()
        {
            RestoreGuard.Run("SecurityCheckDisplayCase");
            return true;
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "OnLeaveStore")]
    internal static class RestoreBeforeLeaveStore
    {
        private static bool Prefix()
        {
            RestoreGuard.Run("OnLeaveStore");
            return true;
        }
    }
}
