using BepInEx;
using BepInEx.Configuration;
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

        /// <summary>
        /// Whether POKER appears on the bar along the bottom of the menu.
        ///
        /// On by default, because the main-menu button only exists on the main menu and
        /// the bar is on every out-of-raid screen -- which is the difference between
        /// reaching the table from the hideout and backing out of it first.
        /// </summary>
        internal static ConfigEntry<bool> ShowTaskBarTab;

        /// <summary>
        /// Which end of the bar the tab sits on: with MAIN MENU and HIDEOUT on the left,
        /// or with CHARACTER and the rest on the right.
        ///
        /// Left by default, matching Blackjack. Those two are places you go, which is
        /// what the table is; the right-hand group is things you look at while you are
        /// somewhere. With both mods installed the two tabs simply sit beside each other
        /// -- the row measures itself and neither has to know about the other.
        /// </summary>
        internal static ConfigEntry<bool> TabOnRight;

        /// <summary>
        /// Whether POKER also appears in the main menu's own list of buttons.
        ///
        /// **Off by default**, and the tab is the whole reason. The button only exists on
        /// the main menu, reaches the same table, and adding a sixth and seventh entry to
        /// a list of five puts two card games among ESCAPE FROM TARKOV and EXIT -- with
        /// Blackjack installed as well the list grows by 40%. The bar along the bottom is
        /// where the game already keeps "places you can go", it is on every out-of-raid
        /// screen, and it costs the menu nothing.
        ///
        /// Kept rather than deleted because it is a working second way in and the code
        /// has already been paid for. It is a Harmony patch, so unlike the tab it is
        /// applied once at load: changing this takes a restart rather than a second.
        /// </summary>
        internal static ConfigEntry<bool> ShowMenuButton;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ShowTaskBarTab = Config.Bind(
                "Menu",
                "Show the task-bar tab",
                true,
                "Adds POKER to the bar along the bottom of the menu, so the table opens "
                + "from the hideout, the flea market or a trader screen and not just the main menu.");

            TabOnRight = Config.Bind(
                "Menu",
                "Put the tab on the right",
                false,
                "Sits the tab with CHARACTER and the rest instead of beside MAIN MENU and HIDEOUT. "
                + "The tab moves a second or two after this is changed.");

            ShowMenuButton = Config.Bind(
                "Menu",
                "Show the main-menu button",
                false,
                "Adds POKER to the main menu's own list, under EXIT, as well as to the task bar. "
                + "Off because the tab reaches the same table from everywhere and keeps the menu "
                + "list to the game's own five entries. Takes effect on the next restart.");

            if (ShowMenuButton.Value)
            {
                try
                {
                    new Harmony(PluginGuid).PatchAll(typeof(MenuButtonPatch));
                }
                catch (System.Exception ex)
                {
                    // The menu button is the second way in, not the only one. A patch that
                    // will not apply on this build must not take the task-bar tab down with
                    // it, and the tab is not a patch at all.
                    Log.LogError("[Poker] the main-menu button could not be installed: " + ex.Message);
                }
            }

            // The tab is not a patch. It watches for the bar instead, because the bar has
            // to be found again after every raid and after any mod that rebuilds the row,
            // and a poll notices both without naming a method that could be renamed.
            StartCoroutine(TaskBarTab.Heartbeat());

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
