namespace DemandPanel.Core
{
    public enum TransferDecision { Send, Gone, Locked, CounterFull }
    public static class TransferGuard
    {
        public static TransferDecision Decide(bool stillInContainer, bool removable, bool counterCanAccept)
        {
            if (!stillInContainer) return TransferDecision.Gone;
            if (!removable) return TransferDecision.Locked;
            if (!counterCanAccept) return TransferDecision.CounterFull;
            return TransferDecision.Send;
        }
    }
}
