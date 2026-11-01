using System.Linq;
using DemandPanel.Core;
using Xunit;

public class DemandFilterTests
{
    [Fact] public void Build_KeepsOnlyWantedRemovableNotOnCounterNotLocked()
    {
        var items = new[] { new ItemFacts(1, 1, true, true, false, false, false), new ItemFacts(2, 1, false, true, false, false, false), new ItemFacts(3, 1, true, false, false, false, false), new ItemFacts(4, 1, true, true, true, false, false), new ItemFacts(5, 1, true, true, false, true, false) };
        Assert.Equal(new[] { 1 }, DemandFilter.Build(items, true).Select(x => x.Key));
    }
    [Fact] public void Build_SortsByValueDescThenKeyAsc()
    {
        var items = new[] { new ItemFacts(3, 100, true, true, false, false, false), new ItemFacts(1, 100, true, true, false, false, false), new ItemFacts(2, 300, true, true, false, false, false) };
        Assert.Equal(new[] { 2, 1, 3 }, DemandFilter.Build(items, false).Select(x => x.Key));
    }
    [Fact] public void Build_DimsOverBudgetOnlyWhenSettingOn()
    {
        var items = new[] { new ItemFacts(1, 1, true, true, false, false, true), new ItemFacts(2, 1, true, true, false, false, false) };
        Assert.True(DemandFilter.Build(items, true)[0].Dimmed);
        Assert.False(DemandFilter.Build(items, true)[1].Dimmed);
        Assert.False(DemandFilter.Build(items, false)[0].Dimmed);
    }
    [Fact] public void Build_EmptyInput_ReturnsEmpty() => Assert.Empty(DemandFilter.Build(System.Array.Empty<ItemFacts>(), true));
}
