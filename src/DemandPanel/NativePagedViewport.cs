using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DemandPanel
{
    // All grids remain logical window storage; only the selected renderer is visible.
    internal sealed class NativePagedViewport : PixelElement, MachineryContainer
    {
        private readonly List<GameGridInventory> pages;
        private readonly List<Vector2> offsets = new List<Vector2>();
        private readonly int maxWidth, maxHeight;
        private readonly MachineryButton previous, next;
        private readonly MachineryText status, xLabel, yLabel;
        private readonly MachinerySlider xSlider, ySlider;
        private bool validating;
        private int selected;
        public bool canResize => false;
        public int widthPixels { get; private set; }
        public int heightPixels { get; private set; }
        public TreeNodeRender handler { get; }
        public RectTransform rectTransform => handler.rectTransform;
        public TreeNodeRender inner { get; }
        public MachineryStorageOwner owner { get; set; }
        internal int Selected => selected;

        internal NativePagedViewport(List<GameGridInventory> pages, int maxWidth, int maxHeight)
        {
            this.pages = pages;
            this.maxWidth = Math.Max(32, maxWidth);
            this.maxHeight = Math.Max(32, maxHeight);
            handler = TreeNodeRender.Instantiate(this, "Demand stock pages");
            handler.image.enabled = false;
            inner = TreeNodeRender.Instantiate().SetParent(handler);
            inner.image.enabled = false;
            inner.mask.enabled = true;
            previous = new MachineryButton("<", () => Select(selected - 1), 20, 16);
            next = new MachineryButton(">", () => Select(selected + 1), 20, 16);
            status = new MachineryText("", 7, HorizontalAlignmentOptions.Center, false);
            xLabel = new MachineryText("X", 7, HorizontalAlignmentOptions.Center, false);
            yLabel = new MachineryText("Y", 7, HorizontalAlignmentOptions.Center, false);
            xSlider = new MachinerySlider(0f, this.maxWidth - 16).SetFillWidth(false);
            ySlider = new MachinerySlider(0f, this.maxWidth - 16).SetFillWidth(false);
            foreach (PixelElement control in new PixelElement[] { previous, next, status, xLabel, yLabel, xSlider, ySlider })
                control.handler.SetParent(handler);
            xSlider.onChanged = value => SetPan(value, offsets[selected].y);
            ySlider.onChanged = value => SetPan(offsets[selected].x, value);
            foreach (GameGridInventory page in pages)
            {
                offsets.Add(Vector2.zero);
                ((PixelElement)page).SetRenderingParent(this);
            }
            Select(0);
        }

        internal void Select(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            selected = index;
            for (int i = 0; i < pages.Count; i++) pages[i].handler.gameObject.SetActive(i == index);
            Validate();
            if (owner is PixelWindow window) window.Validate();
        }

        internal void RegisterAppendedPage(GameGridInventory page)
        {
            offsets.Add(Vector2.zero);
            ((PixelElement)page).SetRenderingParent(this);
            page.handler.gameObject.SetActive(false);
            owner?.RelinkStorage();
            Validate();
        }

        private void SetPan(float x, float y)
        {
            var grid = pages[selected];
            x = Mathf.Clamp(x, 0f, Math.Max(0, grid.widthPixels - Math.Min(maxWidth, grid.widthPixels)));
            y = Mathf.Clamp(y, 0f, Math.Max(0, grid.heightPixels - Math.Min(maxHeight, grid.heightPixels)));
            offsets[selected] = new Vector2(x, y);
            grid.rectTransform.anchoredPosition = new Vector2(-x, y);
        }

        public void Validate()
        {
            if (validating || pages.Count == 0) return;
            validating = true;
            try
            {
                GameGridInventory grid = pages[selected];
                grid.Validate();
                int w = Math.Min(maxWidth, grid.widthPixels), h = Math.Min(maxHeight, grid.heightPixels);
                int maxX = Math.Max(0, grid.widthPixels - w), maxY = Math.Max(0, grid.heightPixels - h);
                bool showX = maxX > 0, showY = maxY > 0;
                widthPixels = Math.Max(160, w);
                heightPixels = 20 + h + (showX ? 12 : 0) + (showY ? 12 : 0);
                MachineryStyle.Place(inner, 0, 20, w, h);
                MachineryStyle.Place(previous.handler, 0, 0, 20, 16);
                MachineryStyle.Place(next.handler, widthPixels - 20, 0, 20, 16);
                status.SetText((selected + 1) + " / " + pages.Count);
                status.ResizePixels(widthPixels - 40, 16);
                MachineryStyle.Place(status.handler, 20, 0, widthPixels - 40, 16);
                if (selected == 0) previous.Disable(); else previous.Enable();
                if (selected == pages.Count - 1) next.Disable(); else next.Enable();
                xSlider.SetWidth(Math.Max(16, widthPixels - 16)).SetRange(0, maxX, 1);
                ySlider.SetWidth(Math.Max(16, widthPixels - 16)).SetRange(0, maxY, 1);
                xSlider.handler.gameObject.SetActive(showX); xLabel.handler.gameObject.SetActive(showX);
                ySlider.handler.gameObject.SetActive(showY); yLabel.handler.gameObject.SetActive(showY);
                int y = 20 + h;
                if (showX) { MachineryStyle.Place(xLabel.handler, 0, y, 16, 8); MachineryStyle.Place(xSlider.handler, 16, y, widthPixels - 16, 8); y += 12; }
                if (showY) { MachineryStyle.Place(yLabel.handler, 0, y, 16, 8); MachineryStyle.Place(ySlider.handler, 16, y, widthPixels - 16, 8); }
                SetPan(offsets[selected].x, offsets[selected].y);
                xSlider.SetValue(offsets[selected].x); ySlider.SetValue(offsets[selected].y);
            }
            finally { validating = false; }
        }

        public void CollectStorage(List<GraphNodeStorage> list) { foreach (var page in pages) list.Add(page); }
        public void ResizePixels(int width, int height) => Validate();
        public int XClamp(int x) => 0;
        public int YClamp(int y) => 0;
        public int WidthPixelsClamp(int width) => widthPixels;
        public int HeightPixelsClamp(int height) => heightPixels;
        public bool Attach(PixelElement element) => false;
        public bool Detach(PixelElement element) => false;
    }
}
