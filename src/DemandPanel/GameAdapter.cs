using System.Collections.Generic;
using DemandPanel.Core;

namespace DemandPanel
{
    internal static class GameAdapter
    {
        // Guards against a malformed parent chain; real nesting is a handful of levels.
        private const int MaxDepth = 64;

        private static readonly List<GameItem> currentItems = new List<GameItem>();

        // The panel only works inside the store. At an after-hours location the game moves the
        // counter's items into a hidden holding inventory and swaps in a different customer.
        public static bool InStore =>
            EmporiumEntry.Instance != null
            && EmporiumEntry.Instance.backInvinvElement != null
            && PlayerStore.HasInstance
            && !(MapUIManager.Instance != null && MapUIManager.Instance.isOutside);

        public static List<GameItem> CurrentItems => currentItems;

        public static bool IsHotkeyBlocked => StoreUIManager.IsAnyTyping() || KeyBindHelper.SuppressHotkeys;

        public static CustomerSnapshot GetCustomer()
        {
            if (!InStore)
            {
                return new CustomerSnapshot(null, false, false);
            }
            var store = PlayerStore.Instance;
            StoreClientInstance instance = store.currentClientInstance;
            if (instance == null)
            {
                return new CustomerSnapshot(null, false, false);
            }
            StoreClient client = instance.GetClientBlueprint();
            bool buying = client != null && client.IsClientBuying();
            return new CustomerSnapshot(instance, store.isClientArrived, buying);
        }

        public static List<PanelEntry> Snapshot(bool dimOverBudget)
        {
            currentItems.Clear();
            if (!InStore)
            {
                return new List<PanelEntry>();
            }
            StoreClientInstance instance = PlayerStore.Instance.currentClientInstance;
            StoreClient client = instance?.GetClientBlueprint();
            if (client == null)
            {
                return new List<PanelEntry>();
            }

            GameGridInventory counter = EmporiumEntry.Instance.backInvinvElement;
            GameGridInventory counterHolding = EmporiumEntry.Instance.backInvinvElementCounter;
            // Same arguments as the game's own item tooltip, so the panel shows the number the tooltip shows.
            GameCharacterItem barterCharacter = OverlayHandler.current?.currentBarterCharacter;
            var facts = new List<ItemFacts>();
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems())
            {
                int key = currentItems.Count;
                currentItems.Add(item);
                ScanAncestors(item, counter, counterHolding, out bool onCounter, out bool locked);
                bool overBudget = client.useClientBudget && !client.HasBudgetLeftToBuy(item);
                facts.Add(new ItemFacts(
                    key,
                    item.GetValue(new List<GameCharacterItem>(), barterCharacter),
                    client.IsClientBuyingThisItem(item),
                    item.MayRemove(),
                    onCounter,
                    locked,
                    overBudget));
            }
            return DemandFilter.Build(facts, dimOverBudget);
        }

        public static TransferDecision TrySendToCounter(GameItem item)
        {
            if (item == null || !InStore)
            {
                return TransferDecision.Gone;
            }
            GameGridInventory counter = EmporiumEntry.Instance.backInvinvElement;
            bool stillInContainer = item.parentInventory != null;
            bool removable = stillInContainer && item.MayRemove();
            bool counterCanAccept = stillInContainer && GraphUtils.CanAccept(counter, item);
            TransferDecision decision = TransferGuard.Decide(stillInContainer, removable, counterCanAccept);
            if (decision == TransferDecision.Send)
            {
                // TryAcceptOnce returns how many units did not move; the whole stack was requested.
                int requested = item.unitCount;
                int notMoved = GraphUtils.TryAcceptOnce(counter, item);
                if (requested > 0 && notMoved >= requested)
                {
                    return TransferDecision.CounterFull;
                }
            }
            return decision;
        }

        public static string LocationName(GameItem item)
        {
            if (item == null || item.parentInventory == null)
            {
                return "";
            }
            GameItem parentItem = item.parentInventory.GetParentItem();
            if (parentItem != null)
            {
                return parentItem.GetDisplayName() ?? "";
            }
            PixelWindow window = item.parentInventory.parent as PixelWindow;
            return window?.titleString ?? "";
        }

        // One walk up the container chain. An item counts as on the counter when any ancestor
        // inventory is the counter or the counter's holding inventory, and as locked when it or
        // any ancestor item carries the lock tag.
        private static void ScanAncestors(GameItem item, GameInventory counter, GameInventory counterHolding, out bool onCounter, out bool locked)
        {
            onCounter = false;
            locked = false;
            GameItem current = item;
            for (int depth = 0; current != null && depth < MaxDepth; depth++)
            {
                if (current.IsTag(LockHelper.ITEM_BEHIND_LOCK_TAG))
                {
                    locked = true;
                }
                GameInventory inventory = current.parentInventory;
                if (inventory == null)
                {
                    return;
                }
                if (ReferenceEquals(inventory, counter) || ReferenceEquals(inventory, counterHolding))
                {
                    onCounter = true;
                }
                current = inventory.GetParentItem();
            }
        }
    }
}
