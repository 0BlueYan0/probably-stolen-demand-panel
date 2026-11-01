using System.Collections.Generic;
using System.Linq;

namespace DemandPanel.Core
{
    public readonly struct ItemFacts
    {
        public ItemFacts(int key, long value, bool wanted, bool removable, bool onCounter, bool locked, bool overBudget)
        { Key = key; Value = value; Wanted = wanted; Removable = removable; OnCounter = onCounter; Locked = locked; OverBudget = overBudget; }
        public int Key { get; }
        public long Value { get; }
        public bool Wanted { get; }
        public bool Removable { get; }
        public bool OnCounter { get; }
        public bool Locked { get; }
        public bool OverBudget { get; }
    }

    public readonly struct PanelEntry
    {
        public PanelEntry(int key, long value, bool overBudget) { Key = key; Value = value; OverBudget = overBudget; }
        public int Key { get; }
        public long Value { get; }
        public bool OverBudget { get; }
    }

    public static class DemandFilter
    {
        public static List<PanelEntry> Build(IEnumerable<ItemFacts> items)
        {
            return items.Where(item => item.Wanted && item.Removable && !item.OnCounter && !item.Locked)
                .OrderBy(item => item.OverBudget).ThenByDescending(item => item.Value).ThenBy(item => item.Key)
                .Select(item => new PanelEntry(item.Key, item.Value, item.OverBudget)).ToList();
        }
    }
}
