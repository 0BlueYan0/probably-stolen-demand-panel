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
        private static ConfigEntry<int> pageColumns;
        private static ConfigEntry<int> pageRows;

        public static void Init()
        {
            var cfg = ModHelper.GetConfig(ModEntry.ModId);
            toggleKey = cfg.Define("ToggleKey", KeyCode.F4.ToString(), Section, () => Loc.T("key_toggle_panel"));
            pageColumns = cfg.Define("PageColumns", 16, Section, () => Loc.T("setting_page_columns"));
            pageRows = cfg.Define("PageRows", 30, Section, () => Loc.T("setting_page_rows"));
        }

        public static int PageColumns => ValidOrDefault(pageColumns?.Value ?? 16, 4, 64, 16);
        public static int PageRows => ValidOrDefault(pageRows?.Value ?? 30, 4, 100, 30);

        internal static int ValidOrDefault(int value, int min, int max, int fallback) =>
            value >= min && value <= max ? value : fallback;

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
    }
}
