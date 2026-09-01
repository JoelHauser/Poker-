using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Poker.Client
{
    /// <summary>
    /// Draws an amount as a stack of chips.
    ///
    /// The six denominations are the ones printed on the artwork, so the picture and
    /// the arithmetic cannot drift apart: a chip's value is read from the same table
    /// that names its file. Adding a denomination means adding an image and one row
    /// here.
    ///
    /// Sprites come from disk beside the DLL through <see cref="Textures.FromFile"/>,
    /// which caches, so a redraw costs nothing after the first.
    /// </summary>
    internal static class ChipView
    {
        /// <summary>One denomination: what it is worth and what it is called on the art.</summary>
        internal struct Chip
        {
            public readonly int Value;
            public readonly string File;

            public Chip(int value, string file)
            {
                Value = value;
                File = file;
            }
        }

        /// <summary>
        /// Highest first, which is the order a greedy breakdown needs and the order a
        /// real rack is stacked in.
        /// </summary>
        internal static readonly Chip[] Denominations =
        {
            new Chip(1_000_000, "1M"),
            new Chip(500_000, "500k"),
            new Chip(100_000, "100k"),
            new Chip(50_000, "50k"),
            new Chip(25_000, "25k"),
            new Chip(10_000, "10k"),
        };

        internal static int Smallest => Denominations[Denominations.Length - 1].Value;

        private static string _directory;

        /// <summary>
        /// Breaks an amount into chips, largest first.
        ///
        /// Greedy is exact here because every denomination divides the one above it,
        /// so there is no amount a greedy pass renders in more chips than necessary.
        /// Add a denomination that breaks that -- a 20k, say, beneath a 50k -- and
        /// this needs revisiting.
        ///
        /// Whatever is left under the smallest chip is returned as the remainder
        /// rather than being rounded away. A table that quietly loses the odd
        /// thousand is the sort of drift nobody notices until the numbers are far
        /// apart.
        /// </summary>
        internal static List<KeyValuePair<Chip, int>> Breakdown(int amount, out int remainder)
        {
            var stack = new List<KeyValuePair<Chip, int>>();
            remainder = amount < 0 ? 0 : amount;

            foreach (var chip in Denominations)
            {
                var count = remainder / chip.Value;
                if (count > 0)
                {
                    stack.Add(new KeyValuePair<Chip, int>(chip, count));
                    remainder -= count * chip.Value;
                }
            }

            return stack;
        }

        /// <summary>
        /// Draws the amount as chips with the number beside them.
        ///
        /// The number is always shown. Chips read at a glance and the exact figure
        /// does not, and an amount smaller than the smallest chip has no chips to
        /// draw at all -- so the text is the truth and the chips are the emphasis.
        /// </summary>
        /// <param name="maxChips">
        /// How many chip faces to draw before giving up and letting the number carry
        /// it. A pot worth forty chips is a wall of artwork, not information.
        /// </param>
        internal static GameObject Build(
            Transform parent,
            int amount,
            TMP_FontAsset font,
            float size = 44f,
            int maxChips = 6)
        {
            var go = new GameObject("Chips", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var row = go.AddComponent<HorizontalLayoutGroup>();
            row.spacing = -size * 0.42f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;

            var drawn = 0;
            int remainder;

            foreach (var entry in Breakdown(amount, out remainder))
            {
                var sprite = Sprite(entry.Key);
                if (sprite == null)
                {
                    continue;
                }

                for (var i = 0; i < entry.Value && drawn < maxChips; i++, drawn++)
                {
                    var chip = new GameObject(
                        "Chip_" + entry.Key.File,
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

                    chip.transform.SetParent(go.transform, false);
                    ((RectTransform)chip.transform).sizeDelta = new Vector2(size, size);

                    var image = chip.GetComponent<Image>();
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                }

                if (drawn >= maxChips)
                {
                    break;
                }
            }

            // Overlapping chips need a gap before the number, which a negative
            // spacing would otherwise eat into.
            var spacer = new GameObject("Gap", typeof(RectTransform));
            spacer.transform.SetParent(go.transform, false);
            ((RectTransform)spacer.transform).sizeDelta = new Vector2(size * 0.55f, size);

            var label = new GameObject("Amount", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);

            var text = label.AddComponent<TextMeshProUGUI>();
            text.text = amount.ToString("N0");
            text.fontSize = size * 0.52f;
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.92f, 0.89f, 0.80f, 1f);
            text.raycastTarget = false;

            if (font != null)
            {
                text.font = font;
            }

            ((RectTransform)label.transform).sizeDelta = new Vector2(size * 4.4f, size);

            return go;
        }

        private static Sprite Sprite(Chip chip)
        {
            if (_directory == null)
            {
                var beside = Path.GetDirectoryName(PokerClientPlugin.Instance?.Info?.Location ?? ".") ?? ".";
                _directory = Path.Combine(beside, "chips");
            }

            return Textures.FromFile(Path.Combine(_directory, chip.File + ".png"));
        }
    }
}
