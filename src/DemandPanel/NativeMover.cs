using System;
using System.Collections.Generic;

namespace DemandPanel
{
    // Moves items into the panel grid and back. Nothing here may destroy an item or split or merge a
    // stack: every move is a whole stack to an explicit slot (SlotMarker.TryAcceptOnce splits a stack
    // when numTransfer is smaller than unitCount, and TryFindOneValidInventorySlot merges stackables).
    // An item that cannot be put back stays in the panel grid.
    internal static class NativeMover
    {
        private const int MaxDepth = 64;

        private sealed class Origin
        {
            public GameInventory Inventory;
            public int MinX;
            public int MinY;
            public bool Flipped;
            public int Orientation;
            public int UnitCount;
            public bool HadHiddenTag;
            public bool Constrained;
            public bool ProductInventory;
            public GameItem Owner;
            public GameGridInventory PanelGrid;

            // Number of items between the origin inventory and the store, so that a bag is put back
            // before the items that came out of it.
            public int Depth;
        }

        private sealed class ReferenceComparer : IEqualityComparer<GameItem>
        {
            public bool Equals(GameItem x, GameItem y) => ReferenceEquals(x, y);

            public int GetHashCode(GameItem item) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item);
        }

        private static readonly Dictionary<GameItem, Origin> records = new Dictionary<GameItem, Origin>(new ReferenceComparer());

        // True while this class moves an item, so the panel's own callbacks ignore the move.
        public static bool SelfMove { get; private set; }

        public static bool IsTracked(GameItem item) => item != null && records.ContainsKey(item);

        private enum MoveResult { Moved, NotMoved, Travelled }

        // Moves whole stacks into the grid in the given order. "notMoved" counts items that stayed
        // where they were; an item inside a bag that is itself in the panel counts as neither.
        public static int MoveIn(IList<GameItem> items, GameGridInventory grid, out int moved)
        {
            moved = 0;
            int notMoved = 0;
            foreach (GameItem item in items)
            {
                try
                {
                    switch (MoveOne(item, grid))
                    {
                        case MoveResult.Moved:
                            moved++;
                            break;
                        case MoveResult.NotMoved:
                            notMoved++;
                            break;
                    }
                }
                catch (Exception e)
                {
                    notMoved++;
                    ModEntry.Log.Log("Move in failed for " + item?.identifier + ": " + e.GetType().Name + ": " + e.Message);
                }
            }
            return notMoved;
        }

        public static int MoveInPlanned(IList<GameItem> items, IList<int> positionsX, IList<int> positionsY, GameGridInventory grid,
            IList<GameGridInventory> pages, Func<GameItem, GameGridInventory> addPage, out int moved)
        {
            moved = 0;
            int failed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                GameItem item = items[i];
                try
                {
                    switch (MoveOne(item, grid, positionsX[i], positionsY[i], pages, addPage))
                    {
                        case MoveResult.Moved: moved++; break;
                        case MoveResult.NotMoved: failed++; break;
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    ModEntry.Log.Log("Move in failed for " + item?.identifier + ": " + e.GetType().Name + ": " + e.Message);
                }
            }
            return failed;
        }

        private static MoveResult MoveOne(GameItem item, GameGridInventory grid, int x = -1, int y = -1,
            IList<GameGridInventory> pages = null, Func<GameItem, GameGridInventory> addPage = null)
        {
            if (item == null || item.parentInventory == null)
            {
                return MoveResult.NotMoved;
            }
            // An item inside a bag that already sits in the panel travels with that bag.
            if (HasTrackedAncestor(item))
            {
                return MoveResult.Travelled;
            }
            if (item.unitCount < 1 || !item.MayRemove() || item.MaxNumRemove() < item.unitCount)
            {
                return MoveResult.NotMoved;
            }
            if (!StockSources.IsStock(item, EmporiumEntry.Instance, out _))
            {
                return MoveResult.NotMoved;
            }

            Origin origin = RecordOrigin(item);
            origin.PanelGrid = grid;
            GridShape previousShape = item.modifiedShape;
            // The grid only accepts tracked items, so the record has to exist before the slot scan.
            records[item] = origin;
            SlotMarker marker = x < 0 ? FindFreeSlot(grid, item) : grid.TryInventorySlot(item, item.unitCount,
                new GridShapeBuilder(item.modifiedShape).SetTransform(x, y, item.modifiedShape.flipped, item.modifiedShape.orientation).Build());
            if (pages != null && !UsableMoveInMarker(marker, item, grid))
            {
                marker = null;
                foreach (GameGridInventory candidate in pages)
                {
                    SlotMarker available = FindFreeSlot(candidate, item);
                    if (UsableMoveInMarker(available, item, candidate))
                    { grid = candidate; marker = available; break; }
                }
                if (marker == null && addPage != null)
                {
                    GameGridInventory candidate = addPage(item);
                    if (candidate != null) { grid = candidate; marker = FindFreeSlot(grid, item); }
                }
                origin.PanelGrid = grid;
            }
            if (marker == null || !ReferenceEquals(marker.item, item)
                || !ReferenceEquals(marker.inventory, grid) || marker.targetItem != null
                || marker.itemGridShape == null || marker.numTransfer < item.unitCount
                || item.unitCount != origin.UnitCount || !item.MayRemove()
                || item.MaxNumRemove() < item.unitCount)
            {
                records.Remove(item);
                return MoveResult.NotMoved;
            }

            SelfMove = true;
            try
            {
                if (pages == null)
                {
                    marker.TryAcceptOnce(item.unitCount);
                }
                else
                {
                    // Source callbacks can occupy a reserved cell during Expel. Recheck after them,
                    // then commit with the native whole-stack marker only while still parentless.
                    item.parentInventory?.Expel(item);
                    if (item.parentInventory == null && item.unitCount == origin.UnitCount)
                    {
                        marker = grid.TryInventorySlot(item, item.unitCount, marker.itemGridShape);
                        if (!UsableMoveInMarker(marker, item, grid) || !HasSpace(grid, item, marker.itemGridShape))
                        {
                            marker = null;
                            foreach (GameGridInventory candidate in pages)
                            {
                                SlotMarker available = FindFreeSlot(candidate, item);
                                if (UsableMoveInMarker(available, item, candidate) && HasSpace(candidate, item, available.itemGridShape))
                                { grid = candidate; marker = available; break; }
                            }
                            if (marker == null && addPage != null)
                            {
                                GameGridInventory candidate = addPage(item);
                                if (candidate != null) { grid = candidate; marker = FindFreeSlot(grid, item); }
                            }
                            origin.PanelGrid = grid;
                        }
                        if (UsableMoveInMarker(marker, item, grid) && HasSpace(grid, item, marker.itemGridShape)
                            && item.MaxNumRemove() >= item.unitCount && item.MaxNumAdd(marker) >= item.unitCount
                            && item.CheckIsStateValid(grid, marker, marker.itemGridShape, item.modifiedState)
                            && grid.CheckAcceptParentShapeStateModifier(item, marker, marker.itemGridShape, item.modifiedState))
                            marker.TryAcceptOnce(item.unitCount);
                    }
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Move in threw for " + item.identifier + ": " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                SelfMove = false;
            }

            if (ReferenceEquals(item.parentInventory, grid))
            {
                return MoveResult.Moved;
            }
            if (item.parentInventory == null)
            {
                // TryAcceptOnce expels the item before it accepts it, so a failed accept leaves it with
                // no parent, and with the target position already written into modifiedShape.
                item.modifiedShape = previousShape;
                if (!Rescue(item, origin, grid, marker))
                {
                    ModEntry.Log.Log("ITEM WITHOUT PARENT: " + item.identifier + " could not be re-added anywhere");
                }
                else if (ReferenceEquals(item.parentInventory, grid))
                {
                    return MoveResult.Moved;
                }
            }
            RestoreHiddenTag(item, origin);
            // A failed rescue remains tracked so a later retry or D19 can still recover it.
            if (item.parentInventory != null)
            {
                records.Remove(item);
            }
            return MoveResult.NotMoved;
        }

        private static bool UsableMoveInMarker(SlotMarker marker, GameItem item, GameGridInventory grid) =>
            marker != null && ReferenceEquals(marker.item, item) && ReferenceEquals(marker.inventory, grid)
            && marker.itemGridShape != null && marker.targetItem == null && marker.numTransfer >= item.unitCount;

        // An item with no parent is in no inventory and would not be saved. Never overwrite a slot
        // during recovery, and never silently relocate a constrained source to main stock.
        private static bool Rescue(GameItem item, Origin origin, GameGridInventory grid, SlotMarker panelMarker = null)
        {
            // A previous failed panel accept may have written panel coordinates into the item.
            GridShape sourceShape = new GridShapeBuilder(item.modifiedShape)
                .SetTransform(origin.MinX, origin.MinY, origin.Flipped, origin.Orientation).Build();
            GameInventory[] targets =
            {
                origin.Inventory,
                grid,
                origin.Constrained ? null : EmporiumEntry.Instance?.invElement
            };
            foreach (GameInventory target in targets)
            {
                if (target == null || item.parentInventory != null)
                {
                    continue;
                }
                bool isPanel = ReferenceEquals(target, grid);
                if (!isPanel && (!IsUsable(target, item) || !HasSpace(target, item, sourceShape)))
                {
                    continue;
                }
                SelfMove = true;
                try
                {
                    if (isPanel)
                    {
                        TryPanelRescue(item, grid, panelMarker);
                    }
                    else if (ReferenceEquals(target, origin.Inventory) && origin.ProductInventory)
                    {
                        item.modifiedShape = sourceShape;
                        var marker = new SlotMarker
                        {
                            item = item, inventory = target, itemGridShape = item.modifiedShape,
                            numTransfer = item.unitCount, targetItem = null
                        };
                        if (ReferenceEquals(target.GetParentItem(), origin.Owner)
                            && StockSources.IsAllowedInventory(target, item, out bool product) && product
                            && item.unitCount == origin.UnitCount && item.MaxNumRemove() >= item.unitCount
                            && item.CheckIsStateValid(target, marker, marker.itemGridShape, item.modifiedState)
                            && target.CheckAcceptParentShapeStateModifier(item, marker, marker.itemGridShape, item.modifiedState)
                            && item.MaxNumAdd(marker) >= item.unitCount
                            && HasSpace(target, item, marker.itemGridShape))
                        {
                            marker.AcceptUnchecked();
                        }
                    }
                    else
                    {
                        item.modifiedShape = sourceShape;
                        target.UncheckedAccept(item);
                    }
                }
                catch (Exception e)
                {
                    ModEntry.Log.Log("Rescue of " + item.identifier + " failed: " + e.GetType().Name + ": " + e.Message);
                }
                finally
                {
                    SelfMove = false;
                }
            }
            RestoreHiddenTag(item, origin);
            return item.parentInventory != null;
        }

        private static void TryPanelRescue(GameItem item, GameGridInventory grid, SlotMarker previous)
        {
            if (grid == null || grid.IsInsertLocked() || item.parentInventory != null || item.unitCount < 1)
            {
                return;
            }
            // The source position and panel position have different coordinate systems. Refresh
            // the chosen panel cell, or scan another one if a callback occupied it in the meantime.
            SlotMarker marker = previous?.itemGridShape == null ? null
                : grid.TryInventorySlot(item, item.unitCount, previous.itemGridShape);
            if (!IsPanelRescueMarker(marker, item, grid))
            {
                marker = FindFreeSlot(grid, item);
            }
            if (IsPanelRescueMarker(marker, item, grid))
            {
                marker.AcceptUnchecked();
            }
        }

        private static bool IsPanelRescueMarker(SlotMarker marker, GameItem item, GameGridInventory grid)
        {
            return marker != null && ReferenceEquals(marker.item, item)
                && ReferenceEquals(marker.inventory, grid) && marker.targetItem == null
                && marker.itemGridShape != null && marker.numTransfer >= item.unitCount
                && item.MaxNumRemove() >= item.unitCount && item.MaxNumAdd(marker) >= item.unitCount
                && HasSpace(grid, item, marker.itemGridShape)
                && item.CheckIsStateValid(grid, marker, marker.itemGridShape, item.modifiedState)
                && grid.CheckAcceptParentShapeStateModifier(item, marker, marker.itemGridShape, item.modifiedState);
        }

        private static bool HasTrackedAncestor(GameItem item)
        {
            GameItem current = item;
            for (int depth = 0; current != null && depth < MaxDepth; depth++)
            {
                GameInventory inventory = current.parentInventory;
                if (inventory == null)
                {
                    return false;
                }
                current = inventory.GetParentItem();
                if (current != null && records.ContainsKey(current))
                {
                    return true;
                }
            }
            return false;
        }

        private static Origin RecordOrigin(GameItem item)
        {
            StockSources.IsStock(item, EmporiumEntry.Instance, out bool constrained);
            StockSources.IsAllowedInventory(item.parentInventory, item, out bool productInventory);
            GridShape shape = item.modifiedShape;
            int depth = 0;
            GameItem current = item.parentInventory.GetParentItem();
            while (current != null && depth < MaxDepth)
            {
                depth++;
                current = current.parentInventory?.GetParentItem();
            }
            return new Origin
            {
                Inventory = item.parentInventory,
                MinX = shape.minX,
                MinY = shape.minY,
                Flipped = shape.flipped,
                Orientation = shape.orientation,
                UnitCount = item.unitCount,
                Depth = depth,
                HadHiddenTag = item.IsTag(ContrabandHelper.ITEM_HIDDEN_TAG),
                Constrained = constrained,
                ProductInventory = productInventory,
                Owner = item.parentInventory.GetParentItem()
            };
        }

        // Own scan with the item's current orientation. TryInventorySlot on a grid with no target item
        // never stacks, and numTransfer >= unitCount means the whole stack fits.
        private static SlotMarker FindFreeSlot(GameGridInventory grid, GameItem item)
        {
            GridShape shape = item.modifiedShape;
            int columns = grid.inventoryShape.width;
            int rows = grid.inventoryShape.height;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    GridShape candidate = new GridShapeBuilder(shape).SetTransform(x, y, shape.flipped, shape.orientation).Build();
                    SlotMarker marker = grid.TryInventorySlot(item, item.unitCount, candidate);
                    if (marker != null && marker.targetItem == null && marker.numTransfer >= item.unitCount)
                    {
                        return marker;
                    }
                }
            }
            return null;
        }

        // Puts one item that is currently in the panel grid back. Returns false when it stays.
        public static bool RestoreOne(GameItem item, GameGridInventory grid)
        {
            if (item == null || !records.TryGetValue(item, out Origin origin))
            {
                return true;
            }
            if (IsGone(item) || !ReferenceEquals(item.parentInventory, grid))
            {
                return true;
            }
            return PlaceBack(item, origin);
        }

        // The single restore path. Safe to call at any time and any number of times.
        // Returns how many tracked items are still in the grid.
        public static int RestoreAll(GameGridInventory grid, bool beforeSave = false)
        {
            if (grid == null)
            {
                records.Clear();
                return 0;
            }
            return RestoreAllPages(new[] { grid }, beforeSave);
        }

        public static int RestoreAllPages(IReadOnlyCollection<GameGridInventory> pages, bool beforeSave = false)
        {
            var owned = new HashSet<GameGridInventory>(pages);
            var pending = GetPending(owned);
            if (pending.Count == 0)
            {
                // A reused empty window must not cancel an unrelated drag or selection.
                records.Clear();
                return 0;
            }
            try
            {
                EmporiumEntry.Instance?.DeselectDragItem();
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("DeselectDragItem failed: " + e.Message);
            }

            // Cancelling a drag can put another tracked item back into the grid.
            pending = GetPending(owned);
            pending.Sort((a, b) => a.Value.Depth.CompareTo(b.Value.Depth));

            var remaining = new List<GameItem>();
            foreach (KeyValuePair<GameItem, Origin> pair in pending)
            {
                bool placed = false;
                try
                {
                    if (pair.Key.parentInventory == null)
                    {
                        Rescue(pair.Key, pair.Value, pair.Value.PanelGrid);
                        placed = pair.Key.parentInventory != null
                            && !(pair.Key.parentInventory is GameGridInventory parent && owned.Contains(parent));
                    }
                    if (!placed)
                    {
                        placed = PlaceBack(pair.Key, pair.Value);
                    }
                }
                catch (Exception e)
                {
                    ModEntry.Log.Log("Restore failed for " + pair.Key.identifier + ": " + e.GetType().Name + ": " + e.Message);
                }
                if (!placed)
                {
                    remaining.Add(pair.Key);
                }
            }

            // Finish every normal restore before borrowing space in the showcase or counter.
            int stuck = 0;
            foreach (GameItem item in remaining)
            {
                if (!beforeSave || !PlaceBeforeSave(item))
                {
                    stuck++;
                }
            }

            // A failed close keeps the session alive, including items sent to the counter. They must
            // still be allowed back into the panel and retain their original location until it closes.
            var done = new List<GameItem>();
            foreach (GameItem item in records.Keys)
            {
                if (stuck == 0 || IsGone(item))
                {
                    done.Add(item);
                }
            }
            foreach (GameItem item in done)
            {
                records.Remove(item);
            }
            return stuck;
        }

        private static List<KeyValuePair<GameItem, Origin>> GetPending(HashSet<GameGridInventory> pages)
        {
            var pending = new List<KeyValuePair<GameItem, Origin>>();
            foreach (KeyValuePair<GameItem, Origin> pair in records)
            {
                if (!IsGone(pair.Key)
                    && (pair.Key.parentInventory == null
                        || (pair.Key.parentInventory is GameGridInventory parent && pages.Contains(parent))))
                {
                    pending.Add(pair);
                }
            }
            return pending;
        }

        // D19: only SaveGame/EndNight may use these extra destinations. All three are serialized by
        // PlayerStore.SaveGame (PlayerStore.cs:852-855). Ordinary closes leave stuck items visible.
        private static bool PlaceBeforeSave(GameItem item)
        {
            EmporiumEntry store = EmporiumEntry.Instance;
            if (store == null)
            {
                return false;
            }
            try
            {
                if (GameAdapter.CanUseShowcase(item)
                    && TrySaveSlot(item, store.showcaseElement, "showcase"))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Save recovery showcase check failed for " + item.identifier + ": " + e.Message);
            }
            if (TrySaveSlot(item, store.backInvinvElement, "counter"))
            {
                return true;
            }

            GameGridInventory main = store.invElement;
            if (main == null)
            {
                return false;
            }
            GameInventory before = item.parentInventory;
            GridShape shape = item.modifiedShape;
            SelfMove = true;
            try
            {
                // No slot scan, stack split or merge. Keep the current shape even if it overlaps.
                before?.Expel(item);
                if (item.parentInventory == null)
                {
                    main.UncheckedAccept(item);
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Forced save placement failed for " + item.identifier + ": " + e.Message);
            }
            finally
            {
                if (item.parentInventory == null && before != null)
                {
                    try
                    {
                        item.modifiedShape = shape;
                        before.UncheckedAccept(item);
                    }
                    catch (Exception e)
                    {
                        ModEntry.Log.Log("Re-adding after forced save placement failed for " + item.identifier + ": " + e.Message);
                    }
                }
                SelfMove = false;
            }
            if (ReferenceEquals(item.parentInventory, main))
            {
                LogSavePlacement(item, "main inventory (unchecked, possible overlap)");
                return true;
            }
            ModEntry.Log.Log("SAVE RECOVERY FAILED: " + item.identifier + " could not be placed in a saved inventory");
            return false;
        }

        private static bool TrySaveSlot(GameItem item, GameGridInventory target, string name)
        {
            try
            {
                if (target != null && TryMove(item, FindFreeSlot(target, item), item.unitCount))
                {
                    LogSavePlacement(item, ReferenceEquals(item.parentInventory, target)
                        ? name : "main inventory (recovery after failed transfer to " + name + ")");
                    return true;
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Save recovery to " + name + " failed for " + item.identifier + ": " + e.Message);
            }
            return false;
        }

        private static void LogSavePlacement(GameItem item, string target)
        {
            ModEntry.Log.Log("Save recovery placed " + item.identifier + " x" + item.unitCount + " in " + target);
        }

        // Exact origin first, then a legal free cell in that same grid. Hidden and machine sources
        // remain in the visible panel on failure; only D19 may relocate them before saving.
        private static bool PlaceBack(GameItem item, Origin origin)
        {
            int count = item.unitCount;
            if (count < 1)
            {
                return false;
            }

            if (IsUsable(origin.Inventory, item)
                && (!ReferenceEquals(origin.Inventory, EmporiumEntry.Instance?.showcaseElement)
                    || GameAdapter.CanUseShowcase(item)))
            {
                GridShape exact = new GridShapeBuilder(item.modifiedShape)
                    .SetTransform(origin.MinX, origin.MinY, origin.Flipped, origin.Orientation).Build();
                SlotMarker normal = origin.Inventory.TryInventorySlot(item, count, exact);
                if ((normal != null && ReferenceEquals(normal.inventory, origin.Inventory)
                        && TryMove(item, normal, count))
                    || (origin.ProductInventory && TryOriginalUnchecked(item, origin, exact)))
                {
                    RestoreHiddenTag(item, origin);
                    return true;
                }
                if (origin.Inventory is GameGridInventory originGrid
                    && TryMove(item, FindFreeSlot(originGrid, item), count))
                {
                    RestoreHiddenTag(item, origin);
                    return true;
                }
            }

            GameGridInventory main = EmporiumEntry.Instance?.invElement;
            if (!origin.Constrained && main != null && !ReferenceEquals(main, origin.Inventory))
            {
                if (TryMove(item, FindFreeSlot(main, item), count))
                {
                    return true;
                }
            }
            ModEntry.Log.Log("Could not put " + item.identifier + " back, it stays in the panel");
            return false;
        }

        private static bool TryMove(GameItem item, SlotMarker marker, int count)
        {
            if (marker == null || !ReferenceEquals(marker.item, item) || marker.inventory == null
                || marker.itemGridShape == null || marker.targetItem != null || marker.numTransfer < count
                || count != item.unitCount || count < 1 || !item.MayRemove() || item.MaxNumRemove() < count)
            {
                return false;
            }
            GameInventory before = item.parentInventory;
            GridShape previousShape = item.modifiedShape;
            SelfMove = true;
            try
            {
                if (marker.inventory is GameSlotInventory slot)
                {
                    // A slot's UncheckedAccept overwrites its occupant. Do not trust a marker
                    // captured before Expel callbacks, even on the normal filtered path.
                    before?.Expel(item);
                    if (item.parentInventory == null && slot.childItem == null
                        && item.unitCount == count && IsUsable(slot, item)
                        && item.CheckIsStateValid(slot, marker, marker.itemGridShape, item.modifiedState)
                        && slot.CheckAcceptParentShapeStateModifier(item, marker, marker.itemGridShape, item.modifiedState)
                        && item.MaxNumAdd(marker) >= count)
                    {
                        marker.AcceptUnchecked();
                    }
                }
                else
                {
                    marker.TryAcceptOnce(count);
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Move threw for " + item.identifier + ": " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                SelfMove = false;
            }
            if (item.parentInventory == null && before != null)
            {
                // Expelled but not accepted. The item must not stay parentless, and TryAcceptOnce has
                // already written the target position into modifiedShape.
                item.modifiedShape = previousShape;
                SelfMove = true;
                try
                {
                    if (HasSpace(before, item, previousShape))
                    {
                        before.UncheckedAccept(item);
                    }
                }
                catch (Exception e)
                {
                    ModEntry.Log.Log("Re-adding " + item.identifier + " failed: " + e.Message);
                }
                finally
                {
                    SelfMove = false;
                }
                if (item.parentInventory == null)
                {
                    item.modifiedShape = previousShape;
                    GameGridInventory main = EmporiumEntry.Instance?.invElement;
                    if (main != null && (!records.TryGetValue(item, out Origin origin) || !origin.Constrained))
                    {
                        try
                        {
                            main.UncheckedAccept(item);
                        }
                        catch (Exception e)
                        {
                            ModEntry.Log.Log("Re-adding " + item.identifier + " to the main inventory failed: " + e.Message);
                        }
                    }
                    if (item.parentInventory == null)
                    {
                        ModEntry.Log.Log("ITEM WITHOUT PARENT: " + item.identifier + " could not be re-added anywhere");
                    }
                }
            }
            return ReferenceEquals(item.parentInventory, marker.inventory) && item.unitCount == count;
        }

        private static bool HasSpace(GameInventory inventory, GameItem item, GridShape shape)
        {
            if (inventory is GameSlotInventory slot)
            {
                return slot.childItem == null || ReferenceEquals(slot.childItem, item);
            }
            if (!(inventory is GameGridInventory grid)
                || !shape.FullyIntersects(grid.inventoryShape, (b, bo) => b != 1 || bo == 0))
            {
                return false;
            }
            foreach (GameItem other in grid.childItems)
            {
                if (!ReferenceEquals(other, item) && shape.Intersects(other.modifiedShape))
                {
                    return false;
                }
            }
            return true;
        }

        // Bypass only the recorded known product inventory's player-add filter. All lifetime,
        // ownership, lock, shape, occupancy, stack and parent-modifier guards still apply.
        private static bool TryOriginalUnchecked(GameItem item, Origin origin, GridShape exact)
        {
            GameInventory target = origin.Inventory;
            int count = item.unitCount;
            if (!IsUsable(target, item) || !ReferenceEquals(target.GetParentItem(), origin.Owner)
                || !StockSources.IsAllowedInventory(target, item, out bool product)
                || !product || !StockSources.IsSafeOwner(item) || count < 1
                || !item.MayRemove() || item.MaxNumRemove() < count
                || !HasSpace(target, item, exact))
            {
                return false;
            }
            var marker = new SlotMarker
            {
                item = item, inventory = target, itemGridShape = exact,
                numTransfer = count, targetItem = null
            };
            if (!item.CheckIsStateValid(target, marker, exact, item.modifiedState)
                || !target.CheckAcceptParentShapeStateModifier(item, marker, exact, item.modifiedState)
                || item.MaxNumAdd(marker) < count)
            {
                return false;
            }
            GameInventory before = item.parentInventory;
            GridShape previous = item.modifiedShape;
            SelfMove = true;
            try
            {
                // Expel callbacks can change the destination; recheck immediately before commit.
                before?.Expel(item);
                if (item.parentInventory == null && item.unitCount == count
                    && IsUsable(target, item) && ReferenceEquals(target.GetParentItem(), origin.Owner)
                    && HasSpace(target, item, exact)
                    && item.CheckIsStateValid(target, marker, exact, item.modifiedState)
                    && target.CheckAcceptParentShapeStateModifier(item, marker, exact, item.modifiedState)
                    && item.MaxNumAdd(marker) >= count)
                {
                    marker.AcceptUnchecked();
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Original product restore failed for " + item.identifier + ": " + e.Message);
            }
            finally
            {
                if (item.parentInventory == null && before != null)
                {
                    item.modifiedShape = previous;
                    try
                    {
                        if (HasSpace(before, item, previous))
                        {
                            before.UncheckedAccept(item);
                        }
                    }
                    catch (Exception e)
                    {
                        ModEntry.Log.Log("Product restore recovery failed for " + item.identifier + ": " + e.Message);
                    }
                }
                SelfMove = false;
            }
            return ReferenceEquals(item.parentInventory, target) && item.unitCount == count;
        }

        private static void RestoreHiddenTag(GameItem item, Origin origin)
        {
            if (origin.HadHiddenTag && ReferenceEquals(item.parentInventory, origin.Inventory)
                && IsUsable(origin.Inventory, item) && !item.IsTag(ContrabandHelper.ITEM_HIDDEN_TAG))
            {
                item.EnableTag(ContrabandHelper.ITEM_HIDDEN_TAG);
            }
        }

        private static bool IsGone(GameItem item)
        {
            return item == null || (item is GameItemElement element && element.IsDestroyed());
        }

        // The origin must still be reachable from the store: every owning item alive and in an
        // inventory, ending at the main inventory or the showcase.
        private static bool IsUsable(GameInventory inventory, GameItem item = null)
        {
            return StockSources.IsReachable(inventory, item);
        }
    }
}
