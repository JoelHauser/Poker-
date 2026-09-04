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

        /// <summary>The largest amount every denomination divides into: 5,000.</summary>
        private const int Unit = 5_000;

        /// <summary>
        /// Beyond this many units the exact search is skipped and the biggest chips
        /// are peeled off first. 100,000 units is 500,000,000 -- far past any pot a
        /// table with these stakes can hold, and the array behind it is small.
        /// </summary>
        private const int MaxUnits = 100_000;

        /// <summary>
        /// Breaks an amount into the fewest chips that make it exactly.
        ///
        /// **Greedy is wrong for this set.** It is tempting, and it is what this
        /// first did, but 10,000 does not divide 25,000 -- so a pot of 30,000, which
        /// is three 10k chips, comes out of a greedy pass as one 25k chip and 5,000
        /// stranded. The pre-flop pot at these blinds is exactly 30,000, so the very
        /// first thing anyone sees would have been wrong.
        ///
        /// The denominations share a highest common factor of 5,000, so the search
        /// runs in units of that: a few hundred entries for any pot this table can
        /// hold. Anything not representable -- an amount that is not a whole number
        /// of 5,000, or a leftover under the smallest chip -- comes back as the
        /// remainder rather than being rounded away. A table that quietly loses the
        /// odd thousand is the sort of drift nobody notices until the numbers are far
        /// apart.
        /// </summary>
        internal static List<KeyValuePair<Chip, int>> Breakdown(int amount, out int remainder)
        {
            var stack = new List<KeyValuePair<Chip, int>>();
            var left = amount < 0 ? 0 : amount;

            // Peel the top chip off anything enormous so the search below stays small.
            // Safe to do greedily: every denomination divides 1,000,000.
            var biggest = Denominations[0];
            while (left / Unit > MaxUnits)
            {
                var count = (left - (MaxUnits * Unit)) / biggest.Value;
                if (count <= 0)
                {
                    break;
                }

                stack.Add(new KeyValuePair<Chip, int>(biggest, count));
                left -= count * biggest.Value;
            }

            var units = left / Unit;
            remainder = left - (units * Unit);

            // Fewest chips for each reachable total, and which chip was taken to get
            // there, so the counts can be walked back out afterwards.
            var best = new int[units + 1];
            var took = new int[units + 1];

            for (var u = 1; u <= units; u++)
            {
                best[u] = int.MaxValue;
                took[u] = -1;

                for (var d = 0; d < Denominations.Length; d++)
                {
                    var cost = Denominations[d].Value / Unit;
                    if (cost > u || best[u - cost] == int.MaxValue)
                    {
                        continue;
                    }

                    if (best[u - cost] + 1 < best[u])
                    {
                        best[u] = best[u - cost] + 1;
                        took[u] = d;
                    }
                }
            }

            // Nothing makes this total exactly -- 5,000 on its own, say. Take what can
            // be made and report the rest.
            var reachable = units;
            while (reachable > 0 && took[reachable] < 0)
            {
                reachable--;
            }

            remainder += (units - reachable) * Unit;

            var counts = new int[Denominations.Length];
            while (reachable > 0)
            {
                var d = took[reachable];
                counts[d]++;
                reachable -= Denominations[d].Value / Unit;
            }

            for (var d = 0; d < Denominations.Length; d++)
            {
                if (counts[d] > 0)
                {
                    stack.Add(new KeyValuePair<Chip, int>(Denominations[d], counts[d]));
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
            // A column: the chips, and the number underneath them. Side by side, a
            // stack of overlapping discs and a five-figure number fight for the same
            // horizontal space and the eye has to work out which belongs to which.
            // Stacked, the number reads as a caption on the pile it describes.
            var go = new GameObject("Chips", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var column = go.AddComponent<VerticalLayoutGroup>();
            column.spacing = size * 0.12f;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = false;
            column.childControlHeight = false;

            var stack = new GameObject("Stack", typeof(RectTransform));
            stack.transform.SetParent(go.transform, false);

            var row = stack.AddComponent<HorizontalLayoutGroup>();
            row.spacing = -size * 0.42f;
            row.childAlignment = TextAnchor.MiddleCenter;
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

                    chip.transform.SetParent(stack.transform, false);
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

            var label = new GameObject("Amount", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);

            var text = label.AddComponent<TextMeshProUGUI>();
            text.text = amount.ToString("N0");
            text.fontSize = size * 0.52f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.92f, 0.89f, 0.80f, 1f);
            text.raycastTarget = false;

            if (font != null)
            {
                text.font = font;
            }

            // Sizes are computed rather than left to a ContentSizeFitter. The parent
            // is itself a layout group with childControl off, so it places these by
            // their own rects -- a fitter that resolves a frame later would leave the
            // pot jumping on the first draw.
            var labelHeight = size * 0.66f;
            var stackWidth = StackWidth(drawn, size);

            ((RectTransform)stack.transform).sizeDelta = new Vector2(stackWidth, size);
            ((RectTransform)label.transform).sizeDelta = new Vector2(Mathf.Max(stackWidth, size * 4.4f), labelHeight);

            ((RectTransform)go.transform).sizeDelta = new Vector2(
                Mathf.Max(stackWidth, size * 4.4f),
                size + column.spacing + labelHeight);

            return go;
        }

        /// <summary>
        /// How wide a row of overlapping chips ends up.
        ///
        /// The negative spacing means each disc after the first only advances by the
        /// part of it that shows, so the row is much narrower than the count suggests.
        /// </summary>
        /// <summary>
        /// A rack of chips in front of a seat, tall when the seat is rich and short
        /// when it is nearly out.
        ///
        /// **Deliberately not a `Breakdown`, and this is the one place the two part
        /// company.** A breakdown answers "which chips make this amount", and it answers
        /// it in very few chips: 765,000 is a 500k, two 100ks, a 50k and a 10k -- five
        /// discs. So is 1,200,000, and so is 250,000. Exact, and useless as a picture,
        /// because the pile a player is meant to read at a glance would barely move
        /// between a short stack and a big one.
        ///
        /// A rack answers a different question -- "how much has that seat got" -- so the
        /// disc **count** is what carries the amount, one per <paramref name="slice"/>.
        /// The face is the largest denomination that slice will buy, so the pile is still
        /// made of real chips rather than tokens, and the exact figure is on the plaque a
        /// few pixels away. A live seat holding less than one slice still gets a single
        /// disc: a player who is still in the hand should not have an empty spot on the
        /// felt, and a busted one should.
        /// </summary>
        internal static GameObject Rack(
            Transform parent,
            int amount,
            int slice,
            float size = 17f,
            int perColumn = 6,
            int maxColumns = 4)
        {
            var go = new GameObject("Rack", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            // Sized to the largest rack it could hold, rather than left at zero. A
            // layout group on a rect with no size has nothing to align its children
            // within, so the columns would sit wherever the group happened to put them
            // and the whole pile would shift as it grew.
            var spacing = size * 0.12f;
            ((RectTransform)go.transform).sizeDelta = new Vector2(
                (maxColumns * size) + ((maxColumns - 1) * spacing),
                ColumnHeight(perColumn, size));

            var row = go.AddComponent<HorizontalLayoutGroup>();

            // Columns nearly touching, discs heavily overlapped: a rack is read by its
            // height, and a stack of discs drawn edge to edge reads as a ladder.
            row.spacing = spacing;
            row.childAlignment = TextAnchor.LowerCenter;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;

            if (amount <= 0 || slice <= 0)
            {
                return go;
            }

            var face = Face(slice);
            if (face == null)
            {
                return go;
            }

            var discs = Mathf.Clamp(
                Mathf.Max(1, Mathf.RoundToInt(amount / (float)slice)),
                1,
                perColumn * maxColumns);

            for (var placed = 0; placed < discs;)
            {
                var height = Mathf.Min(perColumn, discs - placed);
                Column(go.transform, face, height, size);
                placed += height;
            }

            return go;
        }

        /// <summary>One column of overlapping discs, built upwards.</summary>
        private static void Column(Transform parent, Sprite face, int height, float size)
        {
            var column = new GameObject("Column", typeof(RectTransform));
            column.transform.SetParent(parent, false);
            ((RectTransform)column.transform).sizeDelta = new Vector2(size, ColumnHeight(height, size));

            var stack = column.AddComponent<VerticalLayoutGroup>();

            // Negative, so each disc sits mostly behind the one below it and only its
            // edge shows -- which is what makes a pile of discs read as a stack seen
            // from a low angle rather than as a column of coins.
            stack.spacing = -size * 0.74f;
            stack.childAlignment = TextAnchor.LowerCenter;
            stack.childForceExpandWidth = false;
            stack.childForceExpandHeight = false;
            stack.childControlWidth = false;
            stack.childControlHeight = false;

            for (var i = 0; i < height; i++)
            {
                var disc = new GameObject("Disc", typeof(RectTransform), typeof(Image));
                disc.transform.SetParent(column.transform, false);
                ((RectTransform)disc.transform).sizeDelta = new Vector2(size, size);

                var image = disc.GetComponent<Image>();
                image.sprite = face;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
        }

        /// <summary>
        /// How tall a column of <paramref name="discs"/> stands once they are overlapped.
        /// One whole disc, then the sliver of each one behind it.
        /// </summary>
        private static float ColumnHeight(int discs, float size) =>
            size + (Mathf.Max(0, discs - 1) * size * 0.26f);

        /// <summary>
        /// The chip a slice is drawn as: the largest denomination it will buy, so the
        /// artwork never claims more than the slice is worth.
        /// </summary>
        private static Sprite Face(int slice)
        {
            foreach (var chip in Denominations)
            {
                if (chip.Value <= slice)
                {
                    return Sprite(chip);
                }
            }

            return Sprite(Denominations[Denominations.Length - 1]);
        }

        private static float StackWidth(int chips, float size) =>
            chips <= 0 ? 0f : size + ((chips - 1) * size * 0.58f);

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
