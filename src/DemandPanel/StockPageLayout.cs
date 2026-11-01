using System;
using System.Collections.Generic;

namespace DemandPanel
{
    internal static class StockPageLayout
    {
        internal sealed class Page
        {
            internal readonly int Columns;
            internal readonly int Rows;
            internal readonly List<GameItem> Items = new List<GameItem>();
            internal readonly List<int> X = new List<int>();
            internal readonly List<int> Y = new List<int>();
            private readonly bool[,] occupied;

            internal Page(int columns, int rows)
            {
                Columns = columns;
                Rows = rows;
                occupied = new bool[rows, columns];
            }

            internal bool Add(GameItem item, int width, int height)
            {
                for (int y = 0; y + height <= Rows; y++)
                for (int x = 0; x + width <= Columns; x++)
                {
                    bool free = true;
                    for (int dy = 0; dy < height && free; dy++)
                    for (int dx = 0; dx < width; dx++)
                        if (occupied[y + dy, x + dx]) { free = false; break; }
                    if (!free) continue;
                    for (int dy = 0; dy < height; dy++)
                    for (int dx = 0; dx < width; dx++) occupied[y + dy, x + dx] = true;
                    Items.Add(item);
                    X.Add(x);
                    Y.Add(y);
                    return true;
                }
                return false;
            }

        }

        internal static List<Page> Plan(IList<GameItem> items, int columns, int rows, out int invalid)
        {
            var pages = new List<Page>();
            Page current = null;
            invalid = 0;
            foreach (GameItem item in items)
            {
                if (item == null || item.modifiedShape == null) { invalid++; continue; }
                int width = Math.Max(1, item.modifiedShape.globalWidth);
                int height = Math.Max(1, item.modifiedShape.globalHeight);
                if (width > columns || height > rows)
                {
                    var oversized = new Page(Math.Max(columns, width), Math.Max(rows, height));
                    oversized.Add(item, width, height);
                    pages.Add(oversized);
                    current = null;
                    continue;
                }
                if (current == null || !current.Add(item, width, height))
                {
                    current = new Page(columns, rows);
                    pages.Add(current);
                    current.Add(item, width, height);
                }
            }
            return pages;
        }
    }
}
