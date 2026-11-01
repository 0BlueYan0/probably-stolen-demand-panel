using DemandPanel.Core;
using Xunit;
public class TransferGuardTests
{
    [Fact] public void Decide_AllOk_Send() => Assert.Equal(TransferDecision.Send, TransferGuard.Decide(true, true, true));
    [Fact] public void Decide_ItemGone_ReturnsGone() => Assert.Equal(TransferDecision.Gone, TransferGuard.Decide(false, false, false));
    [Fact] public void Decide_NotRemovable_ReturnsLocked() => Assert.Equal(TransferDecision.Locked, TransferGuard.Decide(true, false, false));
    [Fact] public void Decide_CounterFull_ReturnsCounterFull() => Assert.Equal(TransferDecision.CounterFull, TransferGuard.Decide(true, true, false));
}
