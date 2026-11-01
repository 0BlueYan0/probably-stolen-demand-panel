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
        public PanelEntry(int key, long value, bool dimmed) { Key = key; Value = value; Dimmed = dimmed; }
        public int Key { get; }
        public long Value { get; }
        public bool Dimmed { get; }
    }

    public static class DemandFilter
    {
        public static List<PanelEntry> Build(IEnumerable<ItemFacts> items, bool dimOverBudget)
        {
            return items.Where(item => item.Wanted && item.Removable && !item.OnCounter && !item.Locked)
                .OrderByDescending(item => item.Value).ThenBy(item => item.Key)
                .Select(item => new PanelEntry(item.Key, item.Value, dimOverBudget && item.OverBudget)).ToList();
        }
    }
}
