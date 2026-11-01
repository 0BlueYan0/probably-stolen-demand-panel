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

        public static void Init()
        {
            var cfg = ModHelper.GetConfig(ModEntry.ModId);
            toggleKey = cfg.Define("ToggleKey", KeyCode.F4.ToString(), Section, () => Loc.T("key_toggle_panel"));
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
    }
}
