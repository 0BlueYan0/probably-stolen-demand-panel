using System.Linq;
using DemandPanel.Core;
using Xunit;

public class DemandFilterTests
{
    [Fact] public void Build_KeepsOnlyWantedRemovableNotOnCounterNotLocked()
    {
        var items = new[] { new ItemFacts(1, 1, true, true, false, false, false), new ItemFacts(2, 1, false, true, false, false, false), new ItemFacts(3, 1, true, false, false, false, false), new ItemFacts(4, 1, true, true, true, false, false), new ItemFacts(5, 1, true, true, false, true, false) };
        Assert.Equal(new[] { 1 }, DemandFilter.Build(items).Select(x => x.Key));
    }
    [Fact] public void Build_SortsByValueDescThenKeyAsc()
    {
        var items = new[] { new ItemFacts(3, 100, true, true, false, false, false), new ItemFacts(1, 100, true, true, false, false, false), new ItemFacts(2, 300, true, true, false, false, false) };
        Assert.Equal(new[] { 2, 1, 3 }, DemandFilter.Build(items).Select(x => x.Key));
    }
    [Fact] public void Build_OverBudgetSortedLast()
    {
        var items = new[] { new ItemFacts(1, 100, true, true, false, false, true), new ItemFacts(2, 50, true, true, false, false, false), new ItemFacts(3, 300, true, true, false, false, true), new ItemFacts(4, 200, true, true, false, false, false) };
        var result = DemandFilter.Build(items);
        Assert.Equal(new[] { 4, 2, 3, 1 }, result.Select(x => x.Key));
        Assert.Equal(new[] { false, false, true, true }, result.Select(x => x.OverBudget));
    }
    [Fact] public void Build_EmptyInput_ReturnsEmpty() => Assert.Empty(DemandFilter.Build(System.Array.Empty<ItemFacts>()));
}
