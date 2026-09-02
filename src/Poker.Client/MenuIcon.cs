using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Poker.Client
{
    /// <summary>
    /// Puts a card suit on a button cloned from one of EFT's own.
    ///
    /// Shared by the main-menu button and the task-bar tab, because both are copies of
    /// a neighbour and both therefore arrive wearing somebody else's icon.
    ///
    /// Ported from Blackjack, with one deliberate difference: **a spade, not a
    /// diamond.** The two mods sit on the same bar and the labels are the same size in
    /// the same colour, so the pip is the only thing telling them apart at a glance.
    /// Blackjack's note preferred the diamond because it is the one suit with no up or
    /// down, and a spade that inherits a mirrored or rotated transform comes out
    /// looking like a trophy. That risk is handled below by normalising the transform
    /// rather than by avoiding the shape.
    /// </summary>
    internal static class MenuIcon
    {
        /// <summary>
        /// The mod's suit, chosen in one place.
        ///
        /// It was chosen in two, and they disagreed: the tab drew a spade and the menu
        /// button drew a diamond from a copy of this file that had been pasted into
        /// MenuButtonPatch and then never kept up. The menu entry was therefore
        /// indistinguishable from Blackjack's at a glance, which is the exact thing the
        /// spade exists to prevent.
        /// </summary>
        private const char Pip = 'S';

        /// <summary>
        /// Swaps the borrowed icon for the mod's suit.
        ///
        /// A clone wears whatever icon it copied, so without this the POKER entry
        /// carries the hideout's or the handbook's. Blanking it is not the answer
        /// either: with a menu mod installed the icon is the button's main visual and
        /// the others would all have one, leaving ours conspicuously bare. A suit is
        /// drawn by the same code that draws the cards, so it needs no art shipped and
        /// looks deliberate either way.
        ///
        /// The container is left alone whatever happens, because its size is part of
        /// the row's spacing.
        /// </summary>
        internal static void Draw(Component owner)
        {
            if (owner == null)
            {
                return;
            }

            var images = owner.GetComponentsInChildren<Image>(true)
                .Where(i => i != null)
                .ToList();

            var icons = images
                .Where(i => i.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            // Nothing called an icon does not mean there is no icon. The task-bar tabs
            // name theirs after the screen they open, so fall back to shape: the small
            // square graphic that is not the button's own background.
            if (icons.Count == 0)
            {
                icons = images.Where(i => LooksLikeAPip(i, owner)).ToList();
            }

            if (icons.Count == 0)
            {
                return;
            }

            var pip = Textures.Suit(Pip, Color.white);

            foreach (var icon in icons)
            {
                var rect = icon.rectTransform;

                // Whatever the borrowed icon was, it may have been rotated or mirrored
                // to suit its own artwork, and a spade inherits that and comes out
                // upside down. Reported as well as reset, because a rotation here is
                // worth knowing about rather than silently undoing.
                if (rect.localRotation != Quaternion.identity ||
                    rect.localScale.x < 0f || rect.localScale.y < 0f)
                {
                    PokerClientPlugin.Log.LogInfo(
                        $"[Poker] icon '{icon.name}' had rotation {rect.localEulerAngles} " +
                        $"scale {rect.localScale}; normalising.");
                }

                rect.localRotation = Quaternion.identity;
                rect.localScale = new Vector3(
                    Mathf.Abs(rect.localScale.x),
                    Mathf.Abs(rect.localScale.y),
                    Mathf.Abs(rect.localScale.z));

                icon.color = Color.white;
                icon.sprite = pip;
                icon.preserveAspect = true;
            }
        }

        /// <summary>
        /// A graphic small enough and square enough to be an icon rather than the
        /// button's background or its label's backing plate.
        ///
        /// Both tests matter. Area alone catches a thin divider; aspect alone catches a
        /// square button. Requiring both leaves the pip.
        /// </summary>
        private static bool LooksLikeAPip(Image image, Component owner)
        {
            var rect = image.rectTransform;
            var root = owner is RectTransform asRect ? asRect : owner.GetComponent<RectTransform>();
            if (root == null || rect == root)
            {
                return false;
            }

            var size = rect.rect.size;
            var whole = root.rect.size;
            if (size.x <= 1f || size.y <= 1f || whole.x <= 1f || whole.y <= 1f)
            {
                return false;
            }

            var aspect = size.x / size.y;
            var share = (size.x * size.y) / (whole.x * whole.y);

            return aspect > 0.6f && aspect < 1.7f && share < 0.45f;
        }
    }
}
