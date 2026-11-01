using UnityEngine;

namespace DemandPanel
{
    public sealed class ModEntry : IMod
    {
        public const string ModId = "demand_panel";
        private const string ToggleOptionId = "toggle_panel";

        internal static ModLog Log;

        private static GameObject driverObject;

        public void Init(ModManifest manifest)
        {
            Log = new ModLog(manifest);
            Settings.Init();
        }

        // The loader calls OnEnable right after Init and OnDisable when it stops the mod mid-session,
        // so everything that must go away on disable is created here.
        public void OnEnable()
        {
            // Remove first so a second OnEnable does not register the option twice.
            ModHelper.RemoveGameOption(ModId, ToggleOptionId);
            ModHelper.AddGameOptionKeyBind(
                ModId,
                ModHelper.OPTION_SECTION_KEYBIND,
                ToggleOptionId,
                () => Loc.T("key_toggle_panel"),
                () => Settings.ToggleKey,
                k => Settings.SetToggleKey(k));

            // Unity's overloaded null check is true for a destroyed object too.
            if (driverObject == null)
            {
                driverObject = new GameObject("DemandPanel.Driver");
                Object.DontDestroyOnLoad(driverObject);
                driverObject.AddComponent<Driver>();
            }
        }

        public void OnDisable()
        {
            ModHelper.RemoveGameOptions(ModId);
            if (driverObject != null)
            {
                Object.Destroy(driverObject);
            }
            driverObject = null;
        }
    }
}
