using System;
using UnityEngine;

namespace DemandPanel
{
    internal static class Settings
    {
        // Literal on purpose: Define takes a plain string for the section, and the language files
        // are not loaded yet when Init runs, so it cannot be localized here.
        private const string Section = "Demand Panel";

        private static ConfigEntry<string> toggleKey;
        private static ConfigEntry<bool> dimOverBudget;
        private static ConfigEntry<int> cellSize;

        public static void Init()
        {
            var cfg = ModHelper.GetConfig(ModEntry.ModId);
            toggleKey = cfg.Define("ToggleKey", KeyCode.F4.ToString(), Section, () => Loc.T("key_toggle_panel"));
            dimOverBudget = cfg.Define("DimOverBudget", true, Section, () => Loc.T("cfg_dim_over_budget"));
            cellSize = cfg.Define("CellSize", 48, Section, () => Loc.T("cfg_cell_size"), 32, 96);
        }

        public static KeyCode ToggleKey
        {
            get
            {
                if (toggleKey != null
                    && Enum.TryParse<KeyCode>(toggleKey.Value, true, out var key)
                    && Enum.IsDefined(typeof(KeyCode), key)
                    && key != KeyCode.None)
                {
                    return key;
                }
                return KeyCode.F4;
            }
        }

        public static void SetToggleKey(KeyCode key)
        {
            if (toggleKey != null)
            {
                toggleKey.Set(key.ToString());
            }
        }

        public static bool DimOverBudget => dimOverBudget == null || dimOverBudget.Value;

        public static int CellSize => cellSize == null ? 48 : Mathf.Clamp(cellSize.Value, 32, 96);
    }
}
