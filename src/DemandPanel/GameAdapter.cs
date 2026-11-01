using System;
using System.Collections.Generic;
using DemandPanel.Core;

namespace DemandPanel
{
    internal static class GameAdapter
    {
        // Guards against a malformed parent chain; real nesting is a handful of levels.
        private const int MaxDepth = 64;

        private static readonly HashSet<string> loggedItemErrors = new HashSet<string>();

        // The panel only works inside the store. At an after-hours location the game moves the
        // counter's items into a hidden holding inventory and swaps in a different customer.
        public static bool InStore =>
            EmporiumEntry.Instance != null
            && EmporiumEntry.Instance.backInvinvElement != null
            && PlayerStore.HasInstance
            && !(MapUIManager.Instance != null && MapUIManager.Instance.isOutside);

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

        // ContrabandHelper.StartInspection and the security checks only scan the game's own
        // inventories, so items parked in the panel window would be missed. Security customers
        // (isSecurity, set by CompleteSecClient), lazy inspectors and the fake inspector "wanted9"
        // (the id StartInspection itself tests) therefore never get the panel.
        public static bool IsInspectionCustomer()
        {
            try
            {
                StoreClient client = PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
                return client != null
                    && (client.isSecurity || client.isLazyInspector || client.identifier == "wanted9");
            }
            catch (Exception e)
            {
                ModEntry.Log.Log("Inspection customer check failed: " + e.Message);
                return true;
            }
        }

        // Items the customer would buy, in panel order (affordable first, then by value).
        public static List<GameItem> Snapshot()
        {
            var result = new List<GameItem>();
            if (!InStore)
            {
                return result;
            }
            StoreClient client = PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
            if (client == null)
            {
                return result;
            }

            EmporiumEntry store = EmporiumEntry.Instance;
            // Same arguments as the game's own item tooltip, so sorting uses the number the tooltip shows.
            GameCharacterItem barterCharacter = OverlayHandler.current?.currentBarterCharacter;
            var pool = new List<GameItem>();
            var facts = new List<ItemFacts>();
            AddCandidates(store.invElement, store, client, barterCharacter, pool, facts);
            AddCandidates(store.showcaseElement, store, client, barterCharacter, pool, facts);
            AddCandidates(store.hiddenElement, store, client, barterCharacter, pool, facts);

            var ordered = new List<GameItem>();
            foreach (PanelEntry entry in DemandFilter.Build(facts))
            {
                ordered.Add(pool[entry.Key]);
            }

            // An item inside a bag that is also on the list travels with the bag, so it gets no cell
            // of its own and is not counted as an item that did not fit.
            var listed = new HashSet<GameItem>(ordered);
            foreach (GameItem item in ordered)
            {
                if (!HasListedAncestor(item, listed))
                {
                    result.Add(item);
                }
            }
            return result;
        }

        private static bool HasListedAncestor(GameItem item, HashSet<GameItem> listed)
        {
            GameItem current = item.parentInventory?.GetParentItem();
            for (int depth = 0; current != null && depth < MaxDepth; depth++)
            {
                if (listed.Contains(current))
                {
                    return true;
                }
                current = current.parentInventory?.GetParentItem();
            }
            return false;
        }

        private static void AddCandidates(
            GameGridInventory root,
            EmporiumEntry store,
            StoreClient client,
            GameCharacterItem barterCharacter,
            List<GameItem> pool,
            List<ItemFacts> facts)
        {
            if (root == null)
            {
                return;
            }
            foreach (GameItem item in GraphUtils.FindAllChildrenType<GameItem>(root))
            {
                try
                {
                    if (item == null || !GeneralHelper.IsItemOwned(item) || !IsPullable(item, store))
                    {
                        continue;
                    }
                    int key = pool.Count;
                    bool overBudget = client.useClientBudget && !client.HasBudgetLeftToBuy(item);
                    facts.Add(new ItemFacts(
                        key,
                        item.GetValue(new List<GameCharacterItem>(), barterCharacter),
                        client.IsClientBuyingThisItem(item),
                        item.MayRemove(),
                        false,
                        false,
                        overBudget));
                    pool.Add(item);
                }
                catch (Exception e)
                {
                    string identifier = item?.identifier ?? "";
                    if (loggedItemErrors.Add(identifier))
                    {
                        ModEntry.Log.Log("Snapshot item " + identifier + " failed: " + e.GetType().Name + ": " + e.Message);
                    }
                }
            }
        }

        // D18 admits known product sources and hidden storage, while keeping parts/inputs out.
        private static bool IsPullable(GameItem item, EmporiumEntry store)
        {
            return StockSources.IsStock(item, store, out _);
        }

        // A container carries its contents with it, so a clean outer item is not enough.
        internal static bool CanUseShowcase(GameItem item)
        {
            if (ContrabandHelper.IsContraband(item) || StolenHelper.IsStolenItem(item))
            {
                return false;
            }
            foreach (GameItem child in GraphUtils.FindAllChildrenType<GameItem>(item))
            {
                if (ContrabandHelper.IsContraband(child) || StolenHelper.IsStolenItem(child))
                {
                    return false;
                }
            }
            return true;
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
    }
}
