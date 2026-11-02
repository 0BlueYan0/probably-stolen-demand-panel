using System;
using UnityEngine;

namespace DemandPanel
{
    // Native scrollbar art/events, kept outside the grid's masked item hierarchy.
    internal sealed class NativeVerticalScrollbar
    {
        private readonly TreeNodeRender track, thumb;
        private readonly Action<float> onChanged;
        private int height, thumbHeight;
        private float maximum, value, grabOffset;

        internal NativeVerticalScrollbar(TreeNodeRender parent, Action<float> onChanged)
        {
            this.onChanged = onChanged;
            track = TreeNodeRender.Instantiate().SetParent(parent);
            thumb = TreeNodeRender.Instantiate().SetParent(track);
            MachineryStyle.SetSliced(track, MachineryStyle.PlateNotched, MachineryStyle.SLIDER_TRACK);
            MachineryStyle.SetFlat(thumb, MachineryStyle.SLIDER_FILL);
            track.image.SetNativeSize();
            thumb.image.SetNativeSize();
            track.image.enabled = true;
            thumb.image.enabled = true;
            track.InitEventHandler();
            thumb.InitEventHandler();
            track.eventHandler.mouseDownFunc.Add(e =>
            {
                track.FindParentWindow()?.ToFront();
                Move(e.position, thumbHeight / 2f);
                return true;
            });
            track.eventHandler.mouseDragFunc.Add(e => { Move(e.position, thumbHeight / 2f); return true; });
            thumb.eventHandler.mouseDownFunc.Add(e =>
            {
                track.FindParentWindow()?.ToFront();
                grabOffset = -track.rectTransform.InverseTransformPoint(e.position).y - Top;
                return true;
            });
            thumb.eventHandler.mouseDragFunc.Add(e => { Move(e.position, grabOffset); return true; });
        }

        private float Top => 1f + (height - 2 - thumbHeight) * (maximum > 0f ? value / maximum : 0f);

        internal void Place(int x, int y, int visibleHeight, int contentHeight)
        {
            height = visibleHeight;
            maximum = Math.Max(0, contentHeight - visibleHeight);
            thumbHeight = Math.Min(height - 2, Math.Max(6, Mathf.RoundToInt((height - 2) * (float)visibleHeight / Math.Max(1, contentHeight))));
            track.gameObject.SetActive(maximum > 0);
            MachineryStyle.Place(track, x, y, 8, height);
            SetValue(value);
        }

        internal void SetValue(float newValue)
        {
            value = Mathf.Clamp(newValue, 0f, maximum);
            MachineryStyle.Place(thumb, 1, Mathf.RoundToInt(Top), 6, thumbHeight);
        }

        private void Move(Vector2 screenPosition, float offset)
        {
            // Preserve wheel rotation/stack controls while an item is held.
            if (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.GetCurrentItem() != null) return;
            float travel = height - 2 - thumbHeight;
            if (travel <= 0f || maximum <= 0f) return;
            float top = -track.rectTransform.InverseTransformPoint(screenPosition).y - offset - 1f;
            SetValue(top / travel * maximum);
            onChanged(value);
        }
    }
}
