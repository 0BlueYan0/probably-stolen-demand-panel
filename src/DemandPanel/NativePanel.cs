using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemandPanel
{
    // The panel is a real game window with a real grid inventory, so drag, quick transfer, tooltip
    // and slot highlights are the game's own. The game does not save this window, so every item in
    // it must be back in the store before a save: Close() is the one way out and is safe to repeat.
    internal static class NativePanel
    {
        private const int Columns = 12;
        private const int MaxRows = 10;
        private const int MinRows = 2;
        private const float RetrySeconds = 0.5f;

        private static PixelWindow window;
        private static GameGridInventory grid;
        private static bool open;
        private static float nextRetry;
        private static bool stuckNoticeShown;

        // Set only when Close() found items that could not go back. Tick() retries on a timer while it is set.
        private static bool restorePending;

        // True while items that could not be returned are still in the window.
        public static bool HasStuckItems => restorePending;

        // Raised when the player closes the window with its own X button.
        public static event Action ClosedByPlayer;

        public static bool IsOpen => open && IsWindowAlive();

        internal static bool OwnsInventory(GameInventory inventory) =>
            grid != null && ReferenceEquals(inventory, grid);

        // Opens the window and moves the items in. Returns false when nothing could be moved.
        public static bool Open(IList<GameItem> items, out int notMoved)
        {
            notMoved = 0;
            try
            {
                Close();
                if (restorePending)
                {
                    // Never reshape or fill a grid that still holds items.
                    return false;
                }
                var fitting = new List<GameItem>();
                int rows = Layout(items, fitting);
                notMoved = items.Count - fitting.Count;
                if (fitting.Count == 0)
                {
                    return false;
                }

                EnsureWindow(rows);
                window.Show();
                window.Center();
                open = true;
                stuckNoticeShown = false;

                int failed = NativeMover.MoveIn(fitting, grid, out int moved);
                notMoved += failed;
                if (moved == 0)
                {
                    Close();
                    return false;
                }
                window.SetTitle(Loc.T("panel_title", moved));
                window.Validate();
                return true;
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Panel open failed: " + e.GetType().Name + ": " + e.Message);
                try
                {
                    Close();
                }
                catch (Exception inner)
                {
                    ModEntry.Log.Log("Close after failed open failed: " + inner.Message);
                }
                return false;
            }
        }

        // Puts everything back and hides the window. Items that cannot go back keep the window open.
        public static void Close(bool beforeSave = false)
        {
            int stuck = 0;
            try
            {
                stuck = NativeMover.RestoreAll(grid, beforeSave);
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("RestoreAll failed: " + e.GetType().Name + ": " + e.Message);
                stuck = 1;
            }

            if (stuck > 0)
            {
                // Hiding a window that still holds items would hide them as well.
                open = true;
                restorePending = true;
                if (IsWindowAlive() && !SafeVisible())
                {
                    window.Show();
                }
                if (!stuckNoticeShown)
                {
                    stuckNoticeShown = true;
                    GameAdapter.ShowMessage(Loc.T("msg_restore_failed"));
                }
                return;
            }

            open = false;
            restorePending = false;
            stuckNoticeShown = false;
            if (IsWindowAlive() && SafeVisible())
            {
                window.Hide();
            }
        }

        // Called every frame. A window the game hid without calling closeCallback (for example
        // WindowsHandler.HideAllMovable) must not keep items.
        public static void Tick()
        {
            if (!open)
            {
                return;
            }
            if (!IsWindowAlive())
            {
                // A scene change destroyed the window. The inventories went with it, so only references are left.
                open = false;
                restorePending = false;
                grid = null;
                window = null;
                NativeMover.RestoreAll(null);
                return;
            }
            bool hidden = !SafeVisible();
            if (hidden || (restorePending && Time.unscaledTime >= nextRetry))
            {
                nextRetry = Time.unscaledTime + RetrySeconds;
                Close();
            }
        }

        private static bool IsWindowAlive()
        {
            try
            {
                return window != null && grid != null && window.handler != null && window.handler.gameObject != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeVisible()
        {
            try
            {
                return window.IsVisible();
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureWindow(int rows)
        {
            if (!IsWindowAlive())
            {
                window = new PixelWindow(true, Loc.T("panel_title", 0), WindowStyle.Machinery);
                grid = new GameGridInventory(Columns, rows).SetMachineryStyle();
                // Hoppers otherwise bypass mayInventoryAddItemFunc before the tracked-item check.
                grid.EnableTag(HopperHelper.NO_HOPPER_INVENTORY_TAG);
                window.Attach(grid);
                grid.mayInventoryAddItemFunc = (item, inventory) => NativeMover.IsTracked(item);
                grid.onSlotAddItemEndFunc += OnArrived;
                window.closeCallback = OnWindowClosed;
                window.Validate();
                return;
            }
            // Open() returns before this when items are stuck, so the grid is empty here.
            grid.SetShape(Columns, rows);
            window.Validate();
        }

        // Layout is simulated with bounding boxes so the window gets only the rows it needs.
        // Items that do not fit in MaxRows are left where they are.
        private static int Layout(IList<GameItem> items, List<GameItem> fitting)
        {
            var used = new bool[MaxRows, Columns];
            int usedRows = 0;
            foreach (GameItem item in items)
            {
                if (item == null || item.modifiedShape == null)
                {
                    continue;
                }
                int w = Math.Max(1, item.modifiedShape.width);
                int h = Math.Max(1, item.modifiedShape.height);
                if (TryPlaceBox(used, w, h, out int bottom))
                {
                    fitting.Add(item);
                    usedRows = Math.Max(usedRows, bottom);
                }
            }
            return Math.Max(MinRows, usedRows);
        }

        private static bool TryPlaceBox(bool[,] used, int w, int h, out int bottom)
        {
            bottom = 0;
            if (w > Columns || h > MaxRows)
            {
                return false;
            }
            for (int y = 0; y + h <= MaxRows; y++)
            {
                for (int x = 0; x + w <= Columns; x++)
                {
                    if (BoxFree(used, x, y, w, h))
                    {
                        for (int dy = 0; dy < h; dy++)
                        {
                            for (int dx = 0; dx < w; dx++)
                            {
                                used[y + dy, x + dx] = true;
                            }
                        }
                        bottom = y + h;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool BoxFree(bool[,] used, int x, int y, int w, int h)
        {
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    if (used[y + dy, x + dx])
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // Runs inside UncheckedAccept (and inside the drag handler's TryAcceptOnce), so the item is
        // not moved here. A tracked item dropped back into the window goes home on the next update.
        private static void OnArrived(GameItem item, GameInventory inventory, SlotMarker slot)
        {
            try
            {
                if (NativeMover.SelfMove || !NativeMover.IsTracked(item))
                {
                    return;
                }
                LetterboxManager.RunNextUpdate(() => SendHome(item));
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Arrival handler failed: " + e.Message);
            }
        }

        private static void SendHome(GameItem item)
        {
            try
            {
                if (IsWindowAlive() && !NativeMover.RestoreOne(item, grid))
                {
                    // Stays in the window and is tried again when the panel closes.
                    ModEntry.Log.Log("Dropped item " + item.identifier + " could not go home yet");
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Send home failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // The X button runs this before the game hides the window.
        private static void OnWindowClosed()
        {
            try
            {
                Close();
                ClosedByPlayer?.Invoke();
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Window close failed: " + e.Message);
            }
        }
    }
}
