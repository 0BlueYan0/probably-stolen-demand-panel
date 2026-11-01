using System;
using System.Collections.Generic;
using DemandPanel.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DemandPanel
{
    internal sealed class CellInput : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int Key;

        // The item this cell was built for. Key is renumbered by every Snapshot, so the pair is
        // checked before anything is sent.
        internal GameItem Item;

        internal Sprite Sprite;

        private bool dragging;

        // The tooltip comes from the CustomUITooltipTrigger on the same object, so hover needs no code here.
        public void OnPointerEnter(PointerEventData eventData)
        {
        }

        public void OnPointerExit(PointerEventData eventData)
        {
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging)
            {
                return;
            }
            if (GameAdapter.IsHotkeyBlocked || !KeyBindHelper.GetKey("quick_move_storage"))
            {
                return;
            }
            SendAndRefresh();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            dragging = true;
            PanelView.BeginGhost(Sprite);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragging)
            {
                PanelView.MoveGhost(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                return;
            }
            dragging = false;
            // No ghost means Close() cancelled this drag, so the release must not send anything.
            if (!PanelView.EndGhost())
            {
                return;
            }
            if (IsOverCounter())
            {
                SendAndRefresh();
            }
        }

        // Releasing on the panel itself is not a drop on the counter, even when the counter is
        // underneath: the game's raycast skips uGUI objects that are not game nodes. For the same
        // reason, any other uGUI window lying over the counter counts as the counter.
        private static bool IsOverCounter()
        {
            if (!GameAdapter.InStore || PanelView.IsPointerOverPanel)
            {
                return false;
            }
            PixelWindow under = RenderHandler.RaycastFindParentElement<PixelWindow>(Input.mousePosition);
            return under != null && ReferenceEquals(under, EmporiumEntry.Instance.backInvWindow);
        }

        private void SendAndRefresh()
        {
            try
            {
                // Every Snapshot renumbers CurrentItems and a refresh is skipped while dragging,
                // so Key can be stale. Look the item up by reference; not found means it is gone.
                List<GameItem> items = GameAdapter.CurrentItems;
                GameItem current = Item != null && items.Contains(Item) ? Item : null;
                TransferDecision decision = current != null
                    ? GameAdapter.TrySendToCounter(current)
                    : TransferDecision.Gone;
                switch (decision)
                {
                    case TransferDecision.CounterFull:
                        PanelView.ShowMessage(Loc.T("msg_counter_full"));
                        break;
                    case TransferDecision.Locked:
                        PanelView.ShowMessage(Loc.T("msg_item_locked"));
                        break;
                }
                PanelView.Refresh(GameAdapter.Snapshot(Settings.DimOverBudget));
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Send failed: " + e.Message);
            }
        }
    }
}
