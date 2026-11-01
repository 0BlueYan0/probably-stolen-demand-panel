using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemandPanel
{
    internal static class NativePanel
    {
        private const float RetrySeconds = 0.5f;
        private static PixelWindow window;
        private static NativePagedViewport viewport;
        private static readonly List<GameGridInventory> pages = new List<GameGridInventory>();
        private static bool open, restorePending, stuckNoticeShown;
        private static float nextRetry;
        public static bool HasStuckItems => restorePending;
        public static bool IsOpen => open && IsWindowAlive();
        public static event Action ClosedByPlayer;
        internal static bool OwnsInventory(GameInventory inventory)
        {
            foreach (var page in pages) if (ReferenceEquals(page, inventory)) return true;
            return false;
        }

        public static bool Open(IList<GameItem> items, out int notMoved)
        {
            notMoved = 0;
            try
            {
                Close();
                if (restorePending) return false;
                DisposeEmptyWindow();
                int columns = Settings.PageColumns, rows = Settings.PageRows;
                var plan = StockPageLayout.Plan(items, columns, rows, out notMoved);
                if (plan.Count == 0) return false;
                if (!IsWindowObjectAlive())
                {
                    if (window != null) GraphHandler.windowGraphNodeRoots?.Remove(window);
                    window = new PixelWindow(true, Loc.T("panel_title", 0), WindowStyle.Machinery);
                }
                window.closeCallback = OnWindowClosed;
                foreach (var layout in plan)
                {
                    CreatePage(layout.Columns, layout.Rows);
                }
                // Show first so the window can dock before measuring local canvas dimensions.
                window.Show();
                RectTransform root = window.rectTransform.parent as RectTransform;
                if (root == null) root = LetterboxManager.DockRoot;
                float availableWidth = root != null ? root.rect.width : 640f;
                float availableHeight = root != null ? root.rect.height : 480f;
                if (availableWidth <= 0 || availableHeight <= 0) { availableWidth = 640; availableHeight = 480; }
                viewport = new NativePagedViewport(pages, Math.Max(32, (int)(availableWidth * .8f) - 32), Math.Max(32, (int)(availableHeight * .8f) - 80));
                window.Attach(viewport);
                window.Validate();
                window.Center();
                open = true;
                stuckNoticeShown = false;
                int moved = 0;
                for (int i = 0; i < plan.Count; i++)
                {
                    notMoved += NativeMover.MoveInPlanned(plan[i].Items, plan[i].X, plan[i].Y, pages[i], pages,
                        item => CreatePage(Math.Max(columns, item.modifiedShape.globalWidth), Math.Max(rows, item.modifiedShape.globalHeight)), out int pageMoved);
                    moved += pageMoved;
                }
                if (moved == 0) { Close(); return false; }
                window.SetTitle(Loc.T("panel_title", moved));
                window.Validate();
                return true;
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Panel open failed: " + e.GetType().Name + ": " + e.Message);
                try { Close(); } catch (Exception inner) { ModEntry.Log.Log("Close after failed open failed: " + inner.Message); }
                return false;
            }
        }

        public static void Close(bool beforeSave = false)
        {
            int stuck;
            try { stuck = NativeMover.RestoreAllPages(pages, beforeSave); }
            catch (Exception e) { ModEntry.Log.Log("RestoreAllPages failed: " + e.GetType().Name + ": " + e.Message); stuck = 1; }
            if (stuck > 0)
            {
                open = true; restorePending = true;
                if (IsWindowAlive())
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        bool found = false;
                        foreach (var item in pages[i].childItems) if (NativeMover.IsTracked(item)) { found = true; break; }
                        if (found) { viewport.Select(i); break; }
                    }
                    if (!SafeVisible()) window.Show();
                }
                if (!stuckNoticeShown) { stuckNoticeShown = true; GameAdapter.ShowMessage(Loc.T("msg_restore_failed")); }
                return;
            }
            open = false; restorePending = false; stuckNoticeShown = false;
            if (IsWindowAlive() && SafeVisible()) window.Hide();
        }

        public static void Tick()
        {
            if (!open) return;
            if (!IsWindowAlive()) { Close(); return; }
            if (!SafeVisible() || (restorePending && Time.unscaledTime >= nextRetry))
            { nextRetry = Time.unscaledTime + RetrySeconds; Close(); }
        }

        private static bool IsWindowAlive()
        {
            try { return IsWindowObjectAlive() && viewport != null; }
            catch { return false; }
        }
        private static bool IsWindowObjectAlive()
        {
            try { return window != null && window.handler != null && window.handler.gameObject != null; }
            catch { return false; }
        }
        private static bool SafeVisible() { try { return window.IsVisible(); } catch { return false; } }
        private static GameGridInventory CreatePage(int columns, int rows)
        {
            var page = new GameGridInventory(columns, rows).SetMachineryStyle();
            pages.Add(page);
            page.EnableTag(HopperHelper.NO_HOPPER_INVENTORY_TAG);
            page.mayInventoryAddItemFunc = (item, inventory) => NativeMover.IsTracked(item);
            viewport?.RegisterAppendedPage(page);
            return page;
        }
        private static void DisposeEmptyWindow()
        {
            if (viewport != null)
            {
                if (IsWindowObjectAlive() && !window.Detach(viewport))
                {
                    throw new InvalidOperationException("Could not detach empty viewport");
                }
                if (viewport.handler != null && viewport.handler.gameObject != null)
                    UnityEngine.Object.Destroy(viewport.handler.gameObject);
            }
            viewport = null; pages.Clear();
        }
        private static void OnWindowClosed()
        {
            try { Close(); ClosedByPlayer?.Invoke(); }
            catch (Exception e) { ModEntry.Log.Log("Window close failed: " + e.Message); }
        }
    }
}
