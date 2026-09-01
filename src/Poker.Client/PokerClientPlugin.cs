using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Poker.Client
{
    /// <summary>
    /// The in-game half of Poker.
    ///
    /// The server owns the game entirely -- it shuffles, deals, runs the bots and
    /// (once the money path exists) moves the currency. This side renders what it is
    /// handed and sends what the player asked for. It never sees another seat's hole
    /// cards during a hand, because the server does not send them: the view fills in
    /// a hand only for seats that reached a showdown.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class PokerClientPlugin : BaseUnityPlugin
    {
        // Deliberately identical to the server mod's ModGuid, and with no ".client"
        // on the end. The Forge checks that both halves declare the GUID the mod is
        // registered under, and rejects an upload where they differ. BepInEx keeps
        // its own plugin registry and SPT's mod GUID lives in the server metadata,
        // so the two identifiers never meet and there is nothing to collide with.
        public const string PluginGuid = "com.mybutthasarash.poker";
        public const string PluginName = "Poker";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>
        /// The plugin itself, so code that is not a MonoBehaviour can still start a
        /// coroutine. The menu button needs one to wait a frame for other menu mods to
        /// finish before it copies what they left behind.
        /// </summary>
        internal static PokerClientPlugin Instance;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            new Harmony(PluginGuid).PatchAll(typeof(MenuButtonPatch));

            Log.LogInfo("[Poker] client loaded");
        }

        /// <summary>
        /// Escape closes the table.
        ///
        /// Watched here rather than patched into EFT's own input handling: the table
        /// is our window, not one of the game's screens, so nothing in the game knows
        /// to close it. The key is only acted on while the table is open, so this
        /// cannot interfere with escape anywhere else.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                PokerPanel.OnEscape();
            }
        }
    }
}
