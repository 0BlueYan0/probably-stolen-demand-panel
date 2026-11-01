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

        private static MoveResult MoveOne(GameItem item, GameGridInventory grid)
        {
            if (item == null || item.parentInventory == null || item.unitCount < 1)
            {
                return MoveResult.NotMoved;
            }
            // An item inside a bag that already sits in the panel travels with that bag.
            if (HasTrackedAncestor(item))
            {
                return MoveResult.Travelled;
            }

            Origin origin = RecordOrigin(item);
            GridShape previousShape = item.modifiedShape;
            // The grid only accepts tracked items, so the record has to exist before the slot scan.
            records[item] = origin;
            SlotMarker marker = FindFreeSlot(grid, item);
            if (marker == null)
            {
                records.Remove(item);
                return MoveResult.NotMoved;
            }

            SelfMove = true;
            try
            {
                marker.TryAcceptOnce(item.unitCount);
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
            records.Remove(item);
            if (item.parentInventory == null)
            {
                // TryAcceptOnce expels the item before it accepts it, so a failed accept leaves it with
                // no parent, and with the target position already written into modifiedShape.
                item.modifiedShape = previousShape;
                if (!Rescue(item, origin, grid))
                {
                    ModEntry.Log.Log("ITEM WITHOUT PARENT: " + item.identifier + " could not be re-added anywhere");
                }
                else if (ReferenceEquals(item.parentInventory, grid))
                {
                    records[item] = origin;
                    return MoveResult.Moved;
                }
            }
            return MoveResult.NotMoved;
        }

        // An item with no parent is in no inventory and would not be saved. Tries its origin, then the
        // panel grid, then the main inventory, always at the shape it had before the failed move.
        private static bool Rescue(GameItem item, Origin origin, GameGridInventory grid)
        {
            GameInventory[] targets =
            {
                origin.Inventory,
                grid,
                EmporiumEntry.Instance?.invElement
            };
            foreach (GameInventory target in targets)
            {
                if (target == null || item.parentInventory != null)
                {
                    continue;
                }
                SelfMove = true;
                try
                {
                    target.UncheckedAccept(item);
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
            return item.parentInventory != null;
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
                Depth = depth
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
            var pending = GetPending(grid);
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
            pending = GetPending(grid);
            pending.Sort((a, b) => a.Value.Depth.CompareTo(b.Value.Depth));

            var remaining = new List<GameItem>();
            foreach (KeyValuePair<GameItem, Origin> pair in pending)
            {
                bool placed = false;
                try
                {
                    placed = PlaceBack(pair.Key, pair.Value);
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

        private static List<KeyValuePair<GameItem, Origin>> GetPending(GameGridInventory grid)
        {
            var pending = new List<KeyValuePair<GameItem, Origin>>();
            foreach (KeyValuePair<GameItem, Origin> pair in records)
            {
                if (!IsGone(pair.Key) && ReferenceEquals(pair.Key.parentInventory, grid))
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

        // Exact slot, then any free cell of the origin, then the main inventory. A failure leaves the
        // item exactly where it is.
        private static bool PlaceBack(GameItem item, Origin origin)
        {
            int count = item.unitCount;
            if (count < 1)
            {
                return false;
            }

            if (IsUsable(origin.Inventory)
                && (!ReferenceEquals(origin.Inventory, EmporiumEntry.Instance?.showcaseElement)
                    || GameAdapter.CanUseShowcase(item)))
            {
                GridShape exact = new GridShapeBuilder(item.modifiedShape)
                    .SetTransform(origin.MinX, origin.MinY, origin.Flipped, origin.Orientation).Build();
                if (TryMove(item, origin.Inventory.TryInventorySlot(item, count, exact), count))
                {
                    return true;
                }
                if (origin.Inventory is GameGridInventory originGrid
                    && TryMove(item, FindFreeSlot(originGrid, item), count))
                {
                    return true;
                }
            }

            GameGridInventory main = EmporiumEntry.Instance?.invElement;
            if (main != null && !ReferenceEquals(main, origin.Inventory))
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
            if (marker == null || marker.targetItem != null || marker.numTransfer < count)
            {
                return false;
            }
            GameInventory before = item.parentInventory;
            GridShape previousShape = item.modifiedShape;
            SelfMove = true;
            try
            {
                marker.TryAcceptOnce(count);
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
                    before.UncheckedAccept(item);
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
                    if (main != null)
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
            return item.parentInventory != null && !ReferenceEquals(item.parentInventory, before);
        }

        private static bool IsGone(GameItem item)
        {
            return item == null || (item is GameItemElement element && element.IsDestroyed());
        }

        // The origin must still be reachable from the store: every owning item alive and in an
        // inventory, ending at the main inventory or the showcase.
        private static bool IsUsable(GameInventory inventory)
        {
            EmporiumEntry store = EmporiumEntry.Instance;
            if (inventory == null || store == null || inventory.IsInsertLocked())
            {
                return false;
            }
            GameInventory current = inventory;
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                GameItem owner = current.GetParentItem();
                if (owner == null)
                {
                    return ReferenceEquals(current, store.invElement) || ReferenceEquals(current, store.showcaseElement);
                }
                if (IsGone(owner) || owner.parentInventory == null)
                {
                    return false;
                }
                current = owner.parentInventory;
            }
            return false;
        }
    }
}
