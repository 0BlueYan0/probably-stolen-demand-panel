using System;
using System.Collections.Generic;

namespace DemandPanel
{
    // Source admission is distinct from reachability: a butcher's products can be stock even
    // when the butcher itself is installed in a cage's equipment slot.
    internal static class StockSources
    {
        private const int MaxDepth = 64;

        internal static bool IsSafeOwner(GameItem item) =>
            item != null && !(item is GameCharacterItem)
            && !(item is GameItemElement element && element.IsDestroyed())
            && GeneralHelper.IsItemOwned(item)
            && !GeneralHelper.IsItemLocked(item)
            && !item.IsTag(LockHelper.ITEM_BEHIND_LOCK_TAG)
            && !LockHelper.IsCurrentlyLocked(item);

        internal static bool IsStock(GameItem item, EmporiumEntry store, out bool constrained)
        {
            constrained = false;
            if (item == null || store == null || item.unitCount < 1
                || !item.MayRemove() || item.MaxNumRemove() < item.unitCount)
            {
                return false;
            }
            bool mounting = false;
            bool unsafeShowcase = false;
            GameItem current = item;
            for (int depth = 0; current != null && depth < MaxDepth; depth++)
            {
                if (!IsSafeOwner(current))
                {
                    return false;
                }
                unsafeShowcase |= ContrabandHelper.IsContraband(current) || StolenHelper.IsStolenItem(current);
                GameInventory inventory = current.parentInventory;
                if (inventory == null)
                {
                    return false;
                }
                GameItem owner = inventory.GetParentItem();
                if (owner == null)
                {
                    if (ReferenceEquals(inventory, store.hiddenElement))
                    {
                        constrained = true;
                        return true;
                    }
                    return ReferenceEquals(inventory, store.invElement)
                        || (ReferenceEquals(inventory, store.showcaseElement)
                            && !unsafeShowcase && GameAdapter.CanUseShowcase(item));
                }
                // Search protection belongs to the containing bay, even when that bay is in
                // main stock rather than hiddenElement. Nested bags inherit this constraint.
                constrained |= owner.IsTag(ContrabandHelper.ITEM_HIDDEN_TAG)
                    || ContrabandHelper.IsSmugglerBay(owner);
                if (!mounting)
                {
                    if (!IsAllowedInventory(inventory, current, out bool product))
                    {
                        return false;
                    }
                    if (product)
                    {
                        constrained = true;
                        mounting = true;
                    }
                }
                current = owner;
            }
            return false;
        }

        // Only a verified owner and role pair can admit a machine inventory. Collect handles
        // MachineryStack/Intake/Tabs without relying on fragile positional inventory indices.
        internal static bool IsAllowedInventory(GameInventory inventory, GameItem product, out bool machineProduct)
        {
            machineProduct = false;
            GameItem owner = inventory?.GetParentItem();
            if (owner == null || !IsSafeOwner(owner))
            {
                return false;
            }
            if (owner.contentWindow?.childElement is MachineryPanelRow row)
            {
                foreach (MachineryPanel panel in row.Panels)
                {
                    foreach (GraphNodeStorage storage in MachineryContainerUtils.Collect(panel.content))
                    {
                        if (!ReferenceEquals(storage, inventory))
                        {
                            continue;
                        }
                        if (owner.IsGameItemType("STORAGE") && panel.role == "storage"
                            && inventory is GameGridInventory)
                        {
                            return true;
                        }
                        machineProduct = IsProduct(owner, panel.role, inventory, product);
                        return machineProduct;
                    }
                }
                return false;
            }
            // Legacy plain bag/bay windows have no machinery row. Equipment, unknown machines
            // and single-slot components never become ordinary storage through this branch.
            return inventory is GameGridInventory && !owner.IsGameItemType("MACHINE")
                && !owner.IsTag(MachineFridge.FRIDGE_TAG);
        }

        private static bool IsProduct(GameItem owner, string role, GameInventory inventory, GameItem item)
        {
            string id = owner.identifier;
            if (inventory is GameGridInventory)
            {
                return (id == "wine_rack" && role == "wine")
                    || (id == "fridge" && (role == "chill" || role == "freeze"))
                    || (id == "plant_dna_manipulator" && role == "eject")
                    || (role == "output" && (id == "furnace" || id == "hydroponic"
                        || id == "printer" || id == "bottle_printer" || id == "box_dispenser"
                        || id == "chem_finisher" || id == "auto_butcher"));
            }
            if (!(inventory is GameSlotInventory) || item == null)
            {
                return false;
            }
            if ((role == "container" && (id == "water_purifier" || id == "moisture_farm" || id == "blender"))
                || (id == "evaporator" && role == "distillate"))
            {
                return item.IsTag("LIQUID_CONTAINER_TAG") && !WaterHelper.IsEmpty(item);
            }
            if (role == "output")
            {
                return (id == "desequencer" && item.identifier != "blank_keycard" && item.IsGameItemType("ACCESS_CARD"))
                    || owner.IsTag(CraftingHelper.CraftingTags.Table);
            }
            return id == "plant_dna_manipulator" && role == "output_disk"
                && PlantGeneticsHelper.IsWrittenDisk(item)
                && MachineProgressHelper.GetMachineState(owner) == MachineProgressHelper.STATE_READY
                && item.MayRemove();
        }

        // The destination's owners must still exist in an owned, unlocked store chain. Mounted
        // machinery is reachable here; this check never grants source admission on its own.
        internal static bool IsReachable(GameInventory inventory, GameItem item = null)
        {
            EmporiumEntry store = EmporiumEntry.Instance;
            if (inventory == null || store == null || inventory.IsInsertLocked())
            {
                return false;
            }
            GameInventory current = inventory;
            bool unsafeShowcase = item != null && !GameAdapter.CanUseShowcase(item);
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                GameItem owner = current.GetParentItem();
                if (owner == null)
                {
                    return ReferenceEquals(current, store.invElement)
                        || (ReferenceEquals(current, store.showcaseElement) && !unsafeShowcase)
                        || ReferenceEquals(current, store.hiddenElement);
                }
                if (!IsSafeOwner(owner) || owner.parentInventory == null || current.IsInsertLocked())
                {
                    return false;
                }
                unsafeShowcase |= ContrabandHelper.IsContraband(owner) || StolenHelper.IsStolenItem(owner);
                current = owner.parentInventory;
            }
            return false;
        }
    }
}
