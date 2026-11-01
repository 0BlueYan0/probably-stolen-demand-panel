using DemandPanel.Core;
using UnityEngine;

namespace DemandPanel
{
    internal sealed class Driver : MonoBehaviour
    {
        // PlayerStore.OnWeightedAreaChanged was not checked for a plain C# event, and a timer needs
        // no Harmony patch, so the open panel re-reads the game on this interval.
        private const float RefreshSeconds = 0.5f;

        // Only used when the mod is enabled in the middle of a scene, after the tablet was built.
        private const float TabletCheckSeconds = 1f;

        private static Driver instance;

        private readonly PanelController controller = new PanelController();
        private float nextRefresh;
        private float nextTabletCheck;

        private void OnEnable()
        {
            instance = this;
            PanelView.ClosedByUser += OnPanelClosedByUser;
        }

        private void OnDisable()
        {
            PanelView.ClosedByUser -= OnPanelClosedByUser;
            PanelView.Close();
            TabletButton.Remove();
            if (instance == this)
            {
                instance = null;
            }
        }

        // The one code path for F4 and for the tablet button. The typing and hotkey gate is applied
        // by Update for F4 only, because a click on the button is not a key press.
        public static void TogglePanel()
        {
            if (instance != null)
            {
                instance.Toggle(GameAdapter.GetCustomer());
            }
        }

        private void Toggle(CustomerSnapshot customer)
        {
            switch (controller.OnTogglePressed(customer))
            {
                case PanelCommand.Open:
                    var entries = GameAdapter.Snapshot(Settings.DimOverBudget);
                    if (entries.Count == 0)
                    {
                        controller.OnTogglePressed(customer);
                        PanelView.ShowMessage(Loc.T("msg_no_wanted_stock"));
                        break;
                    }
                    PanelView.Open(entries);
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
                Toggle(customer);
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

            if (!TabletButton.IsAlive && Time.unscaledTime >= nextTabletCheck)
            {
                nextTabletCheck = Time.unscaledTime + TabletCheckSeconds;
                TabletButton.TryBuildFromScene();
            }
            TabletButton.SetVisible(GameAdapter.InStore && customer.Eligible);
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
