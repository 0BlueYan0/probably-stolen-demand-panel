using System;

namespace DemandPanel
{
    // Select preparation stock only; the game decides what can be used.
    internal static class PreparationRules
    {
        internal enum Kind { None, Buying, GunOrder, GunService, Barter, Dialogue }

        internal static Kind Classify(StoreClient client)
        {
            if (client == null) return Kind.None;
            if (client.IsClientBuying()) return Kind.Buying;
            if (client.isBusinessComplete) return Kind.None;
            switch (client.clientIntent)
            {
                case StoreClient.ClientIntent.GUNSMITH:
                    return client.gunOrderData == null ? Kind.None :
                        client.gunOrderData.isServiceOrder ? Kind.GunService : Kind.GunOrder;
                case StoreClient.ClientIntent.BARTER:
                    var offer = client.barterOffer;
                    if (offer == null || offer.isFreeReward ||
                        (offer.ownerClient != null && !ReferenceEquals(offer.ownerClient, client))) return Kind.None;
                    return HasConditions(offer) || IsFallback(client.identifier) ? Kind.Barter : Kind.None;
                case StoreClient.ClientIntent.DIALOGUE:
                    return client.identifier == "cartelHandlerWeekly" ? Kind.Dialogue : Kind.None;
                default: return Kind.None;
            }
        }

        private static bool IsFallback(string id) => id == "addictBarterBlood" || id == "scavBarter" || id == "lowerLevelChemistBarter";
        private static bool Valid(BarterCondition condition) => condition != null &&
            (!string.IsNullOrEmpty(condition.requiredItemId) || !string.IsNullOrEmpty(condition.requiredItemTag));
        private static bool HasConditions(BarterOffer offer)
        {
            if (offer.conditions == null) return false;
            foreach (var condition in offer.conditions) if (Valid(condition)) return true;
            return false;
        }

        internal static bool Matches(StoreClient client, Kind kind, GameItem item)
        {
            if (kind == Kind.GunOrder || kind == Kind.GunService)
            {
                if (item.IsTag("SERVICE_GUN_TAG")) return false;
                return item.IsTag("WEAPON_MOD_TAG") || item.identifier == "gun_part" ||
                    (kind == Kind.GunOrder && (item.IsTag("ADV_GUN_TAG") || item.IsTag("GUN_FRAME_TAG")));
            }
            if (kind == Kind.Dialogue) return item.identifier == "dream_dust";
            if (kind != Kind.Barter) return false;
            BarterOffer offer = client.barterOffer;
            if (HasConditions(offer))
            {
                foreach (BarterCondition condition in offer.conditions)
                {
                    if (!Valid(condition)) continue;
                    try { if (condition.IsItemValid(item)) return true; }
                    catch (Exception) { /* Malformed conditions do not admit stock. */ }
                }
                return false;
            }
            switch (client.identifier)
            {
                case "addictBarterBlood": return item.identifier == "pink_injector";
                case "scavBarter": return item.identifier == "moonshine";
                case "lowerLevelChemistBarter": return item.IsTag("FOOD");
                default: return false;
            }
        }
    }
}
