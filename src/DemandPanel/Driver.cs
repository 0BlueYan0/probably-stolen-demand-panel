using DemandPanel.Core;
using UnityEngine;

namespace DemandPanel
{
    internal sealed class Driver : MonoBehaviour
    {
        // PlayerStore.OnWeightedAreaChanged was not checked for a plain C# event, and a timer needs
        // no Harmony patch, so the open panel re-reads the game on this interval.
        private const float RefreshSeconds = 0.5f;

        private readonly PanelController controller = new PanelController();
        private float nextRefresh;

        private void OnEnable()
        {
            PanelView.ClosedByUser += OnPanelClosedByUser;
        }

        private void OnDisable()
        {
            PanelView.ClosedByUser -= OnPanelClosedByUser;
            PanelView.Close();
        }

        private void Update()
        {
            CustomerSnapshot customer = GameAdapter.GetCustomer();

            // A scene change destroys the window without telling anyone. Follow it, so the next
            // key press opens the panel again instead of closing a window that is not there.
            if (controller.IsOpen && !PanelView.IsOpen)
            {
                // Close() is safe on a dead window and removes a drag ghost the scene change left behind.
                PanelView.Close();
                controller.OnTogglePressed(customer);
            }

            if (GameAdapter.InStore && !GameAdapter.IsHotkeyBlocked && Input.GetKeyDown(Settings.ToggleKey))
            {
                switch (controller.OnTogglePressed(customer))
                {
                    case PanelCommand.Open:
                        PanelView.Open(GameAdapter.Snapshot(Settings.DimOverBudget));
                        nextRefresh = Time.unscaledTime + RefreshSeconds;
                        break;
                    case PanelCommand.Close:
                        PanelView.Close();
                        break;
                    case PanelCommand.ShowNoCustomer:
                        PanelView.ShowMessage(Loc.T("msg_no_customer"));
                        break;
                }
            }

            if (controller.OnFrame(customer) == PanelCommand.Close)
            {
                PanelView.Close();
            }

            if (controller.IsOpen && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                PanelView.Refresh(GameAdapter.Snapshot(Settings.DimOverBudget));
            }
        }

        // The window's own close button. The window is already gone, so only the controller is left to sync.
        private void OnPanelClosedByUser()
        {
            if (controller.IsOpen)
            {
                controller.OnTogglePressed(GameAdapter.GetCustomer());
            }
        }
    }
}
