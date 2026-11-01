using DemandPanel.Core;
using UnityEngine;

namespace DemandPanel
{
    internal sealed class Driver : MonoBehaviour
    {
        private readonly PanelController controller = new PanelController();

        private void Update()
        {
            CustomerSnapshot customer = GameAdapter.GetCustomer();

            if (GameAdapter.InStore && !GameAdapter.IsHotkeyBlocked && Input.GetKeyDown(Settings.ToggleKey))
            {
                switch (controller.OnTogglePressed(customer))
                {
                    case PanelCommand.Open:
                        LogSnapshot();
                        break;
                    case PanelCommand.Close:
                        ModEntry.Log.Log("Panel closed by key");
                        break;
                    case PanelCommand.ShowNoCustomer:
                        ModEntry.Log.Log(Loc.T("msg_no_customer"));
                        break;
                }
            }

            if (controller.OnFrame(customer) == PanelCommand.Close)
            {
                ModEntry.Log.Log("Panel closed: customer left or changed");
            }
        }

        private static void LogSnapshot()
        {
            var entries = GameAdapter.Snapshot(Settings.DimOverBudget);
            ModEntry.Log.Log(Loc.T("panel_title", entries.Count));
            foreach (PanelEntry entry in entries)
            {
                GameItem item = GameAdapter.CurrentItems[entry.Key];
                ModEntry.Log.Log(item.GetDisplayName()
                    + " | value " + entry.Value
                    + " | dimmed " + entry.Dimmed
                    + " | location " + GameAdapter.LocationName(item));
            }
        }
    }
}
