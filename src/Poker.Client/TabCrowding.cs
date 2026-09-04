using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Poker.Client
{
    /// <summary>
    /// Gives way when the task bar runs out of room.
    ///
    /// ## The bug this exists for
    ///
    /// The bar sizes its tabs from their contents and then squeezes them all when the
    /// row is over-subscribed. Past a certain number of tabs every label in the row
    /// starts wrapping mid-word -- HIDEOU/T, CHARACTE/R, BLACKJAC/K -- and the bar
    /// becomes unreadable. It takes about four mod-added tabs to get there: Raid
    /// Review's optional menu item, PIT Fireteam's slots, and one each from Blackjack
    /// and Poker.
    ///
    /// **We are guests on that row, so we are the ones who give way.** When the bar is
    /// tight this drops our label and leaves the pip, which takes our tab from about
    /// 112 units to about 40 and hands the difference back to the game's own tabs. When
    /// there is room again it takes the label back.
    ///
    /// It cannot fix the row on its own and does not pretend to: two tabs going compact
    /// frees perhaps 145 units of 1920, which is worth having and is not a whole
    /// answer. What it does guarantee is that these two mods are no longer part of the
    /// problem.
    ///
    /// ## Why it is measured rather than counted
    ///
    /// Counting tabs would need a number to compare against, and that number depends on
    /// the resolution, the UI scale, how long the other mods' labels are and what the
    /// game itself put on the row this patch. Measuring asks the question that actually
    /// matters -- is anything being squeezed below what it asked for -- and gets it
    /// right at any resolution without knowing what else is installed.
    /// </summary>
    internal static class TabCrowding
    {
        /// <summary>
        /// How far below its preferred width a label has to be squeezed before it
        /// counts. A couple of units is rounding; ten means it is wrapping.
        /// </summary>
        private const float SqueezeTolerance = 10f;

        /// <summary>
        /// How much more room than our label needs before we take it back. Expanding
        /// costs exactly what collapsing saved, so without a margin the row would sit
        /// on the boundary and flip once a second forever.
        /// </summary>
        private const float ExpandMargin = 1.35f;

        private static bool _compact;
        private static bool _announced;

        // What Relabel worked out for the full-width tab, kept so it can be restored
        // rather than recomputed -- recomputing it while the label is hidden measures
        // an empty string.
        private static float _fullPreferred;
        private static float _fullMin;
        private static float _labelWidth;
        private static bool _measured;

        internal static bool IsCompact => _compact;

        /// <summary>Forgets everything. Called when the tab is destroyed and rebuilt.</summary>
        internal static void Forget()
        {
            _compact = false;
            _measured = false;
            _announced = false;
        }

        /// <summary>
        /// Decides whether our tab should be wearing its label, and applies it.
        ///
        /// Runs on the tab's own once-a-second heartbeat, so it follows the row as
        /// other mods add and remove tabs and as the resolution changes, without
        /// watching for either.
        /// </summary>
        internal static void Apply(GameObject tab)
        {
            if (tab == null)
            {
                return;
            }

            var label = OurLabel(tab);
            var row = tab.transform.parent;

            if (label == null || row == null)
            {
                return;
            }

            Measure(tab, label);

            var squeezed = Squeezed(row, tab);

            if (!_compact && squeezed)
            {
                Set(tab, label, compact: true);
                return;
            }

            if (_compact && !squeezed && Fits(row))
            {
                Set(tab, label, compact: false);
            }
        }

        /// <summary>
        /// Is anything on the row narrower than it asked to be?
        ///
        /// Our own tab is skipped: once it is compact its label is off, so it is never
        /// squeezed, and letting it vote would mean the row looked healthier simply
        /// because we had already given way.
        /// </summary>
        private static bool Squeezed(Transform row, GameObject ours)
        {
            foreach (var label in Labels(row))
            {
                if (label.transform.IsChildOf(ours.transform))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(label.text) || !label.isActiveAndEnabled)
                {
                    continue;
                }

                var wanted = label.GetPreferredValues(label.text).x;

                if (wanted - label.rectTransform.rect.width > SqueezeTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Is there room to put our label back?
        ///
        /// Nothing being squeezed is not the same as there being spare room -- the row
        /// can be exactly full. This asks for our label's width plus a margin of
        /// genuine slack before expanding, which is what stops the two states
        /// alternating on the heartbeat.
        /// </summary>
        private static bool Fits(Transform row)
        {
            var container = row as RectTransform;

            if (container == null || _labelWidth <= 0f)
            {
                return true;
            }

            var used = 0f;

            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i) as RectTransform;

                if (child != null && child.gameObject.activeSelf)
                {
                    used += LayoutUtility.GetPreferredWidth(child);
                }
            }

            var group = container.GetComponent<HorizontalLayoutGroup>();

            if (group != null)
            {
                used += group.spacing * Mathf.Max(0, container.childCount - 1);
                used += group.padding.left + group.padding.right;
            }

            return container.rect.width - used >= _labelWidth * ExpandMargin;
        }

        /// <summary>
        /// Records the full-width numbers once, while the tab is still wearing its
        /// label. Everything after this is a restore rather than a recalculation.
        /// </summary>
        private static void Measure(GameObject tab, TextMeshProUGUI label)
        {
            if (_measured || _compact)
            {
                return;
            }

            _labelWidth = label.GetPreferredValues(label.text).x;

            var hint = label.GetComponentInParent<LayoutElement>();

            if (hint != null)
            {
                _fullPreferred = hint.preferredWidth;
                _fullMin = hint.minWidth;
            }

            _measured = true;
        }

        private static void Set(GameObject tab, TextMeshProUGUI label, bool compact)
        {
            label.enabled = !compact;

            var hint = label.GetComponentInParent<LayoutElement>();

            if (hint != null)
            {
                // The tab still has to be wide enough for the pip and its padding. That
                // is the tab's width less the label's, which is the same "chrome"
                // Relabel measures off the template.
                var chrome = Mathf.Max(0f, _fullPreferred - _labelWidth);

                if (_fullPreferred > 0f)
                {
                    hint.preferredWidth = compact ? Mathf.Max(chrome, 36f) : _fullPreferred;
                }

                if (_fullMin > 0f)
                {
                    hint.minWidth = compact ? Mathf.Max(chrome, 36f) : _fullMin;
                }
            }

            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)tab.transform);

            _compact = compact;

            // Said once rather than every second, and worth saying at all: a tab that
            // has quietly dropped its own name is otherwise a mystery to whoever is
            // looking at the bar wondering where POKER went.
            if (compact && !_announced)
            {
                PokerClientPlugin.Log.LogInfo(
                    "[Poker] the task bar is crowded, so the tab is showing its pip without a label. "
                    + "It takes the label back when there is room.");

                _announced = true;
            }
        }

        private static TextMeshProUGUI OurLabel(GameObject tab) =>
            tab.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();

        private static IEnumerable<TextMeshProUGUI> Labels(Transform row)
        {
            for (var i = 0; i < row.childCount; i++)
            {
                var child = row.GetChild(i);

                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                foreach (var label in child.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    yield return label;
                }
            }
        }
    }
}
