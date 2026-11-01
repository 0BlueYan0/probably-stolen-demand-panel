using System;
using System.Collections.Generic;
using DemandPanel.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DemandPanel
{
    internal static class PanelView
    {
        private const string WindowId = "demand_panel.window";
        private const int Columns = 6;
        private const int VisibleRows = 5;
        private const float Spacing = 4f;
        private const float DimmedAlpha = 0.4f;
        private const float GhostAlpha = 0.6f;

        private readonly struct CellState
        {
            public CellState(GameItem item, bool dimmed, long count, long value, string location)
            {
                Item = item;
                Dimmed = dimmed;
                Count = count;
                Value = value;
                Location = location;
            }

            public GameItem Item { get; }
            public bool Dimmed { get; }
            public long Count { get; }
            public long Value { get; }
            public string Location { get; }

            public bool SameAs(CellState other)
            {
                return ReferenceEquals(Item, other.Item)
                    && Dimmed == other.Dimmed
                    && Count == other.Count
                    && Value == other.Value
                    && Location == other.Location;
            }
        }

        private static CustomUIWindow window;
        private static RectTransform gridRoot;
        private static CustomUIElement emptyLabel;
        private static int cellSize = 48;
        private static bool suppressClosedEvent;
        private static GameObject ghost;

        private static readonly List<CellInput> cells = new List<CellInput>();
        private static List<CellState> shownState = new List<CellState>();

        // Raised only when something other than Close() closed the window (its close button,
        // or the game), so the controller can follow.
        public static event Action ClosedByUser;

        public static bool IsOpen => window != null && window.IsAlive;

        public static bool IsPointerOverPanel
        {
            get
            {
                if (!IsOpen || !window.IsVisible)
                {
                    return false;
                }
                RectTransform rect = window.Rect;
                if (rect == null)
                {
                    return false;
                }
                return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, CanvasCamera(RootCanvas(rect)));
            }
        }

        public static void Open(List<PanelEntry> entries)
        {
            try
            {
                Close();
                if (CustomUIManager.Instance == null)
                {
                    ModEntry.Log.Log("CustomUIManager is not available, panel not opened");
                    return;
                }

                cellSize = Settings.CellSize;
                float scrollHeight = VisibleRows * (cellSize + Spacing) + Spacing;
                float width = Columns * (cellSize + Spacing) + 56f;

                CustomUIBuilder builder;
                suppressClosedEvent = true;
                try
                {
                    builder = CustomUIManager.Window(WindowId, Loc.T("panel_title", entries.Count), CustomUIManager.LAYER_PAPER);
                }
                finally
                {
                    suppressClosedEvent = false;
                }
                if (builder == null)
                {
                    return;
                }

                builder.SetDraggable(true).OnClose(OnWindowClosed).SetSize(width, scrollHeight + 96f);
                builder.AddLabel(Loc.T("panel_empty"));
                emptyLabel = builder.Last;
                builder.BeginScroll(scrollHeight);
                builder.BeginGrid(Columns, cellSize, cellSize, Spacing);
                gridRoot = builder.CurrentContainer;
                builder.End();
                builder.End();
                window = builder.Center().Show();

                Populate(entries);
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Panel open failed: " + e.Message);
                Close();
            }
        }

        public static void Refresh(List<PanelEntry> entries)
        {
            try
            {
                if (!IsOpen || gridRoot == null)
                {
                    return;
                }
                window.SetTitle(Loc.T("panel_title", entries.Count));

                List<CellState> next = BuildState(entries);
                if (SameState(next))
                {
                    // Same items in the same order. Only the keys can differ, because every Snapshot
                    // renumbers CurrentItems, and a cell must point at the latest number.
                    for (int i = 0; i < cells.Count; i++)
                    {
                        cells[i].Key = entries[i].Key;
                    }
                    return;
                }
                if (ghost != null)
                {
                    // Rebuilding destroys the cell being dragged, so its release would never arrive
                    // and the ghost would stay on screen. The next refresh after the drop rebuilds.
                    return;
                }
                Populate(entries);
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Panel refresh failed: " + e.Message);
            }
        }

        public static void Close()
        {
            EndGhost();
            CustomUIWindow closing = window;
            ForgetWindow();
            if (closing == null || !closing.IsAlive)
            {
                return;
            }
            suppressClosedEvent = true;
            try
            {
                closing.Close();
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Panel close failed: " + e.Message);
            }
            finally
            {
                suppressClosedEvent = false;
            }
        }

        public static void ShowMessage(string text)
        {
            try
            {
                if (StoreUIManager.Instance != null)
                {
                    StoreUIManager.Instance.Notify(text);
                }
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Notify failed: " + e.Message);
            }
        }

        internal static void BeginGhost(Sprite sprite)
        {
            EndGhost();
            if (!IsOpen)
            {
                return;
            }
            Canvas canvas = RootCanvas(window.Rect);
            if (canvas == null)
            {
                return;
            }
            var go = new GameObject("DemandPanel.Ghost", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(cellSize, cellSize);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, GhostAlpha);
            var sorting = go.AddComponent<Canvas>();
            sorting.overrideSorting = true;
            sorting.sortingOrder = short.MaxValue;
            ghost = go;
        }

        internal static void MoveGhost(PointerEventData eventData)
        {
            if (ghost == null)
            {
                return;
            }
            var rect = (RectTransform)ghost.transform;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    (RectTransform)rect.parent, eventData.position, eventData.pressEventCamera, out Vector3 world))
            {
                rect.position = world;
            }
        }

        // Returns whether a ghost existed. A drag that Close() cancelled has none, and its release
        // must do nothing.
        internal static bool EndGhost()
        {
            if (ghost == null)
            {
                return false;
            }
            UnityEngine.Object.Destroy(ghost);
            ghost = null;
            return true;
        }

        private static void OnWindowClosed()
        {
            if (suppressClosedEvent)
            {
                return;
            }
            EndGhost();
            ForgetWindow();
            ClosedByUser?.Invoke();
        }

        private static void ForgetWindow()
        {
            window = null;
            gridRoot = null;
            emptyLabel = null;
            cells.Clear();
            shownState = new List<CellState>();
        }

        private static Canvas RootCanvas(RectTransform rect)
        {
            if (rect == null)
            {
                return null;
            }
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            return canvas == null ? null : canvas.rootCanvas;
        }

        private static Camera CanvasCamera(Canvas canvas)
        {
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }
            return canvas.worldCamera;
        }

        private static List<CellState> BuildState(List<PanelEntry> entries)
        {
            var state = new List<CellState>(entries.Count);
            foreach (PanelEntry entry in entries)
            {
                GameItem item = ItemFor(entry.Key);
                state.Add(new CellState(
                    item,
                    entry.Dimmed,
                    item == null ? 0 : item.unitCount,
                    entry.Value,
                    GameAdapter.LocationName(item)));
            }
            return state;
        }

        private static bool SameState(List<CellState> next)
        {
            if (next.Count != shownState.Count || next.Count != cells.Count)
            {
                return false;
            }
            for (int i = 0; i < next.Count; i++)
            {
                if (!next[i].SameAs(shownState[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static GameItem ItemFor(int key)
        {
            List<GameItem> items = GameAdapter.CurrentItems;
            return key >= 0 && key < items.Count ? items[key] : null;
        }

        private static void Populate(List<PanelEntry> entries)
        {
            CustomUIBuilder.ClearContainer(gridRoot);
            cells.Clear();
            shownState = BuildState(entries);

            var builder = new CustomUIBuilder(CustomUIManager.Instance, window, gridRoot);
            for (int i = 0; i < entries.Count; i++)
            {
                CellState state = shownState[i];
                if (state.Item == null)
                {
                    // The list was built from a Snapshot that has since been rebuilt. Nothing to show,
                    // and the next Refresh replaces it.
                    continue;
                }
                AddCell(builder, entries[i], state);
            }
            emptyLabel?.SetVisible(cells.Count == 0);
            CustomUIBuilder.RebuildLayout(window.Rect);
        }

        private static void AddCell(CustomUIBuilder builder, PanelEntry entry, CellState state)
        {
            GameItem item = state.Item;
            Sprite sprite = (item as GameItemElement)?.ResolveSpriteByName(item.spritePath);

            builder.AddImage(sprite, cellSize, cellSize);
            builder.WithTooltip(TooltipText(item, state));
            CustomUIElement cell = builder.Last;
            if (cell == null || cell.root == null)
            {
                return;
            }
            if (cell.image != null)
            {
                cell.image.preserveAspect = true;
                cell.image.raycastTarget = true;
                float alpha = sprite == null ? 0.15f : (state.Dimmed ? DimmedAlpha : 1f);
                cell.image.color = new Color(1f, 1f, 1f, alpha);
            }

            if (state.Count > 1)
            {
                AddCountLabel(builder, cell, state);
            }

            var input = cell.root.AddComponent<CellInput>();
            input.Key = entry.Key;
            input.Item = item;
            // CustomUIElement finds its Image with GetComponentInChildren, so read the sprite from there
            // instead of from the cell root.
            input.Sprite = cell.image == null ? sprite : cell.image.sprite;
            cells.Add(input);
        }

        private static string TooltipText(GameItem item, CellState state)
        {
            string text = item.GetDisplayName();
            if (!string.IsNullOrEmpty(state.Location))
            {
                text += "\n" + Loc.T("tip_location", state.Location);
            }
            return text + "\n" + Loc.T("tip_value", state.Value);
        }

        private static void AddCountLabel(CustomUIBuilder builder, CustomUIElement cell, CellState state)
        {
            builder.AddLabel(state.Count.ToString());
            CustomUIElement count = builder.Last;
            if (count == null || count.root == null)
            {
                return;
            }
            // The label is spawned as a grid child, so move it into the cell before the layout runs.
            count.root.transform.SetParent(cell.root.transform, false);
            if (count.root.transform is RectTransform rect)
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-2f, 1f);
                rect.sizeDelta = new Vector2(0f, Mathf.Max(12f, cellSize * 0.35f));
            }
            foreach (Graphic graphic in count.root.GetComponentsInChildren<Graphic>(true))
            {
                // Hover, click and drag must reach the cell underneath.
                graphic.raycastTarget = false;
            }
            if (count.text != null)
            {
                count.text.horizontalAlignment = HorizontalAlignmentOptions.Right;
                count.text.verticalAlignment = VerticalAlignmentOptions.Bottom;
                count.text.overflowMode = TextOverflowModes.Overflow;
                count.text.fontSize = Mathf.Max(10f, cellSize * 0.3f);
                if (state.Dimmed)
                {
                    count.text.alpha = DimmedAlpha;
                }
            }
        }
    }
}
