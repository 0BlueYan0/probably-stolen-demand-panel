using DemandPanel.Core;
using UnityEngine;

namespace DemandPanel
{
    internal sealed class Driver : MonoBehaviour
    {
        // Only used when the mod is enabled in the middle of a scene, after the tablet was built.
        private const float TabletCheckSeconds = 1f;

        private static Driver instance;

        private readonly PanelController controller = new PanelController();
        private float nextTabletCheck;

        private void OnEnable()
        {
            instance = this;
            NativePanel.ClosedByPlayer += OnPanelClosedByPlayer;
        }

        private void OnDisable()
        {
            NativePanel.ClosedByPlayer -= OnPanelClosedByPlayer;
            // Restore first: the items are only safe once they are back in the store. When the loader
            // stops the mod it calls OnDisable and then removes all Harmony patches (ModLoader.cs:628,
            // :639), and this Driver is destroyed, so nothing retries after this. A few attempts here
            // are the last chance; items that still do not fit stay in the window with a notice.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                NativePanel.Close();
                if (!NativePanel.HasStuckItems)
                {
                    break;
                }
            }
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
                    OpenPanel(customer);
                    break;
                case PanelCommand.Close:
                    NativePanel.Close();
                    break;
                case PanelCommand.ShowNoCustomer:
                    GameAdapter.ShowMessage(Loc.T("msg_no_customer"));
                    break;
            }
        }

        private void OpenPanel(CustomerSnapshot customer)
        {
            if (GameAdapter.IsInspectionCustomer())
            {
                controller.OnTogglePressed(customer);
                GameAdapter.ShowMessage(Loc.T("msg_cannot_open_now"));
                return;
            }
            var items = GameAdapter.Snapshot();
            if (items.Count == 0)
            {
                controller.OnTogglePressed(customer);
                GameAdapter.ShowMessage(Loc.T("msg_no_wanted_stock"));
                return;
            }
            if (!NativePanel.Open(items, out int notMoved))
            {
                controller.OnTogglePressed(customer);
                // Items stuck from an earlier close already got the restore-failed notice.
                if (!NativePanel.HasStuckItems)
                {
                    GameAdapter.ShowMessage(Loc.T("msg_not_fit", notMoved));
                }
                return;
            }
            if (notMoved > 0)
            {
                GameAdapter.ShowMessage(Loc.T("msg_not_fit", notMoved));
            }
        }

        private void Update()
        {
            CustomerSnapshot customer = GameAdapter.GetCustomer();

            // Hidden windows and scene changes are handled here, before anything else this frame.
            NativePanel.Tick();

            // The window went away without the controller knowing (a scene change, or the game hid it
            // and the items are back). Follow it so the next key press opens instead of closing.
            if (controller.IsOpen && !NativePanel.IsOpen)
            {
                controller.OnTogglePressed(customer);
            }

            if (GameAdapter.InStore && !GameAdapter.IsHotkeyBlocked && Input.GetKeyDown(Settings.ToggleKey))
            {
                Toggle(customer);
            }

            if (controller.OnFrame(customer) == PanelCommand.Close)
            {
                NativePanel.Close();
            }

            if (!TabletButton.IsAlive && Time.unscaledTime >= nextTabletCheck)
            {
                nextTabletCheck = Time.unscaledTime + TabletCheckSeconds;
                TabletButton.TryBuildFromScene();
            }
            TabletButton.SetVisible(GameAdapter.InStore && customer.Eligible);
        }

        // The window's own close button. The items are already back, so only the controller is left to sync.
        private void OnPanelClosedByPlayer()
        {
            if (controller.IsOpen)
            {
                controller.OnTogglePressed(GameAdapter.GetCustomer());
            }
        }
    }
}
