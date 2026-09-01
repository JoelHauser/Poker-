using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Poker.Client
{
    /// <summary>
    /// The table window.
    ///
    /// The server owns the game completely. This renders the view it is handed and
    /// posts what the player pressed; it never decides whether a move is legal, never
    /// works out who won, and never knows a card the server did not send. When the
    /// engine refuses a move it answers with the real view attached, so the fix for a
    /// client that has drifted is simply to draw what came back.
    ///
    /// Laid out as rows rather than as seats around an oval. That is a deliberate
    /// first pass: the data path is worth proving before a thousand lines of layout
    /// are built on top of it, and every number here is one the finished table needs
    /// anyway.
    /// </summary>
    internal static class PokerPanel
    {
        private const string RootName = "PokerTableCanvas";

        /// <summary>
        /// The table photograph, taken from Blackjack. It is an oval on a rectangular
        /// image, and 1.655 is that image's aspect -- keeping it stops the cloth
        /// stretching into a shape no table has.
        /// </summary>
        private const float TableAspect = 1.655f;

        private static readonly Color Gold = new Color(0.55f, 0.47f, 0.25f, 1f);
        private static readonly Color Ink = new Color(0.88f, 0.86f, 0.80f, 1f);
        private static readonly Color Dim = new Color(0.55f, 0.54f, 0.50f, 1f);

        private static GameObject _root;
        private static TMP_FontAsset _font;

        private static RectTransform _board;
        private static RectTransform _seatColumn;
        private static RectTransform _actionRow;
        private static TextMeshProUGUI _status;
        private static TextMeshProUGUI _potLabel;

        // What the player is asking to raise to. Held between redraws because the
        // whole action strip is rebuilt whenever the view changes.
        private static int _raiseTo;

        // The last view the server sent. Kept so that changing the raise amount can
        // redraw the strip without a round trip -- picking a number is not a move,
        // and asking the server for a view it has already sent invites the screen to
        // change under the player between pressing + and pressing raise.
        private static JObject _lastReply;

        private static string TableImagePath => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(PokerClientPlugin.Instance?.Info?.Location ?? ".") ?? ".",
            "table.png");

        internal static bool IsOpen => _root != null && _root.activeSelf;

        internal static void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            Open();
        }

        internal static void Open()
        {
            try
            {
                if (_root == null)
                {
                    Build();
                }

                if (_root == null)
                {
                    return;
                }

                _root.SetActive(true);

                // A canvas built this frame has not had a layout pass yet, so its
                // controls have no real size or position until one happens.
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_root.transform);

                // Resume rather than assume: a hand can still be live from an earlier
                // visit, and /poker/state is what says so. Its failure is the ordinary
                // "not at a table" case, not an error worth showing as one.
                var state = PokerApi.State();
                if (Ok(state))
                {
                    Render(state);
                }
                else
                {
                    ShowLobby();
                }
            }
            catch (Exception ex)
            {
                PokerClientPlugin.Log.LogError("[Poker] could not open the table: " + ex);
            }
        }

        internal static void Close()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        /// <summary>
        /// Escape closes the table. Nothing is stacked over it yet; when a confirm
        /// prompt exists it is handled here first, in the order things are stacked,
        /// or escape closes the table out from under an unanswered question.
        /// </summary>
        internal static void OnEscape()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            Close();
        }

        // ---------------------------------------------------------------- actions

        private static void Sit()
        {
            var reply = PokerApi.Sit(seats: 5, buyIn: 5_000, bigBlind: 50);

            if (!Ok(reply))
            {
                SetStatus(ErrorOf(reply) ?? "Could not sit down.");
                return;
            }

            Render(reply);
        }

        private static void Leave()
        {
            PokerApi.Leave();
            ShowLobby();
        }

        private static void Deal()
        {
            var reply = PokerApi.Deal();

            if (!Ok(reply))
            {
                SetStatus(ErrorOf(reply) ?? "Could not deal.");

                // A refusal still carries the table, so the screen stays truthful.
                if (reply?["Table"] != null)
                {
                    Render(reply, keepStatus: true);
                }

                return;
            }

            Render(reply);
        }

        private static void Act(string move, int to = 0)
        {
            var reply = PokerApi.Act(move, to);

            if (reply == null)
            {
                SetStatus("No answer from the server.");
                return;
            }

            // The engine is the authority on legality, and when it refuses it hands
            // back the real view with the reason attached. Draw the view either way:
            // a client whose picture has drifted is exactly the case this covers.
            var error = ErrorOf(reply);
            Render(reply, keepStatus: error != null);

            if (error != null)
            {
                SetStatus(error);
            }
        }

        // ---------------------------------------------------------------- rendering

        private static void ShowLobby()
        {
            _lastReply = null;

            SetBoard(null);
            ClearSeats();
            SetPot(null);

            SetStatus(
                "Not at a table.\n\n"
                + "Five seats, 5,000 in chips, blinds 25 / 50.\n"
                + "The chips are notional in this build -- nothing is at stake.");

            BuildActions(new[]
            {
                Action("SIT DOWN", Sit),
                Action("CLOSE", Close),
            });
        }

        private static void Render(JObject reply, bool keepStatus = false)
        {
            var table = reply?["Table"] as JObject;

            if (table == null)
            {
                ShowLobby();
                return;
            }

            _lastReply = reply;

            var street = (string)table["Street"] ?? "Idle";
            var pot = (int?)table["Pot"] ?? 0;
            var awaiting = (bool?)table["AwaitingPlayer"] ?? false;
            var button = (int?)table["Button"] ?? -1;

            SetBoard(table["Community"]?.Select(c => (string)c).ToArray());
            SetPot(pot);
            RenderSeats(table["Seats"] as JArray, button);

            if (!keepStatus)
            {
                SetStatus(Headline(street, table));
            }

            BuildActions(ActionsFor(street, awaiting, table["Options"] as JObject));
        }

        /// <summary>
        /// One line saying where the hand is. At a showdown it says who won instead,
        /// because that is the only moment the player cannot read it off the table.
        /// </summary>
        private static string Headline(string street, JObject table)
        {
            if (!string.Equals(street, "Showdown", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(street, "Idle", StringComparison.OrdinalIgnoreCase)
                    ? "Waiting to deal."
                    : street;
            }

            var winners = (table["Seats"] as JArray)?
                .Where(s => ((int?)s["Won"] ?? 0) > 0)
                .Select(s =>
                {
                    var name = (string)s["Name"] ?? "seat";
                    var won = (int?)s["Won"] ?? 0;
                    var hand = (string)s["Hand"];
                    return hand == null
                        ? $"{name} wins {won:N0}"
                        : $"{name} wins {won:N0} with {hand}";
                })
                .ToArray();

            return winners != null && winners.Length > 0
                ? string.Join("\n", winners)
                : "Hand over.";
        }

        private static void RenderSeats(JArray seats, int button)
        {
            ClearSeats();

            if (seats == null || _seatColumn == null)
            {
                return;
            }

            foreach (var seat in seats.OfType<JObject>())
            {
                var index = (int?)seat["Index"] ?? 0;
                var name = (string)seat["Name"] ?? $"Seat {index}";
                var stack = (int?)seat["Stack"] ?? 0;
                var committed = (int?)seat["CommittedThisStreet"] ?? 0;
                var folded = (bool?)seat["Folded"] ?? false;
                var allIn = (bool?)seat["IsAllIn"] ?? false;
                var isTurn = (bool?)seat["IsTurn"] ?? false;
                var isPlayer = (bool?)seat["IsPlayer"] ?? false;
                var hand = (string)seat["Hand"];

                // Cards are absent rather than blanked when they may not be seen, so
                // an empty list is the honest instruction to draw backs. Never key
                // this off the street: a hand that ends with everybody folding never
                // reaches a showdown, and reading the street would show the winner's
                // cards on most pots.
                var cards = seat["Cards"]?.Select(c => (string)c).ToArray() ?? new string[0];

                var marks = new List<string>();
                if (index == button) marks.Add("D");
                if (folded) marks.Add("folded");
                if (allIn) marks.Add("all in");
                if (hand != null) marks.Add(hand);

                var label =
                    $"{(isTurn ? ">" : " ")} {(isPlayer ? "YOU" : name)}"
                    + $"    {stack:N0}"
                    + (committed > 0 ? $"    bet {committed:N0}" : string.Empty)
                    + (marks.Count > 0 ? $"    [{string.Join(", ", marks)}]" : string.Empty);

                BuildSeatRow(label, cards, folded, isTurn, isPlayer);
            }
        }

        /// <summary>
        /// What the player may press. Built from the server's own list of legal moves
        /// rather than from the client's idea of the rules -- there is one authority
        /// on legality and it is not this side.
        /// </summary>
        private static List<KeyValuePair<string, Action>> ActionsFor(
            string street, bool awaiting, JObject options)
        {
            var actions = new List<KeyValuePair<string, Action>>();

            var betweenHands =
                string.Equals(street, "Idle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(street, "Showdown", StringComparison.OrdinalIgnoreCase);

            if (betweenHands)
            {
                actions.Add(Action("DEAL", Deal));
                actions.Add(Action("LEAVE", Leave));
                actions.Add(Action("CLOSE", Close));
                return actions;
            }

            if (!awaiting || options == null)
            {
                actions.Add(Action("CLOSE", Close));
                return actions;
            }

            var moves = options["Moves"]?.Select(m => (string)m).ToArray() ?? new string[0];
            var toCall = (int?)options["ToCall"] ?? 0;
            var minRaise = (int?)options["MinRaiseTo"] ?? 0;
            var maxRaise = (int?)options["MaxRaiseTo"] ?? 0;

            foreach (var move in moves)
            {
                if (string.Equals(move, "Raise", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var label = string.Equals(move, "Call", StringComparison.OrdinalIgnoreCase) && toCall > 0
                    ? $"CALL {toCall:N0}"
                    : move.ToUpperInvariant();

                var captured = move;
                actions.Add(Action(label, () => Act(captured)));
            }

            if (moves.Any(m => string.Equals(m, "Raise", StringComparison.OrdinalIgnoreCase)) && maxRaise > 0)
            {
                _raiseTo = Mathf.Clamp(_raiseTo <= 0 ? minRaise : _raiseTo, minRaise, maxRaise);

                actions.Add(Action("-", () => Nudge(-minRaise, minRaise, maxRaise)));
                actions.Add(Action($"RAISE TO {_raiseTo:N0}", () => Act("Raise", _raiseTo)));
                actions.Add(Action("+", () => Nudge(minRaise, minRaise, maxRaise)));

                if (maxRaise > minRaise)
                {
                    actions.Add(Action($"ALL IN {maxRaise:N0}", () => Act("Raise", maxRaise)));
                }
            }

            return actions;
        }

        /// <summary>
        /// Steps the raise by the minimum increment. Redrawn from the view already in
        /// hand rather than by asking the server, because choosing an amount is not a
        /// move: a round trip here would let the table change between the player
        /// pressing + and pressing raise.
        /// </summary>
        private static void Nudge(int by, int min, int max)
        {
            _raiseTo = Mathf.Clamp(_raiseTo + by, min, max);

            if (_lastReply != null)
            {
                Render(_lastReply, keepStatus: true);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static KeyValuePair<string, Action> Action(string label, Action onClick) =>
            new KeyValuePair<string, Action>(label, onClick);

        private static bool Ok(JObject reply) => reply != null && ((bool?)reply["Ok"] ?? false);

        private static string ErrorOf(JObject reply)
        {
            var error = (string)reply?["Error"];
            return string.IsNullOrEmpty(error) ? null : error;
        }

        private static void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

        private static void SetPot(int? pot)
        {
            if (_potLabel != null)
            {
                _potLabel.text = pot.HasValue && pot.Value > 0 ? $"POT  {pot.Value:N0}" : string.Empty;
            }
        }

        private static void ClearSeats()
        {
            if (_seatColumn == null)
            {
                return;
            }

            for (var i = _seatColumn.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_seatColumn.GetChild(i).gameObject);
            }
        }

        // ---------------------------------------------------------------- building

        private static void Build()
        {
            _font = BorrowFont();

            var canvasObject = new GameObject(
                RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the menu, which is the only place this opens from.
            canvas.sortingOrder = 30000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Match height, not a blend. Blending grows the window with screen width,
            // so an ultrawide gets it stretched across the monitor.
            scaler.matchWidthOrHeight = 1f;

            _root = canvasObject;

            // The backdrop is what swallows clicks meant for the menu underneath. It
            // needs a Graphic to be raycast at all, hence a nearly-opaque image rather
            // than an empty transform.
            var backdrop = NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.88f));
            Stretch(backdrop);

            var window = NewBox("Window", canvasObject.transform, Color.clear);
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(1500f, 1030f);

            var title = NewText("Title", window, "POKER", 32f, TextAlignmentOptions.Top);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-60f, 46f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -6f);

            BuildTable(window);
            BuildSeatColumn(window);
            BuildStatus(window);
            BuildActionRow(window);
        }

        /// <summary>
        /// The felt, with the community cards and the pot on it.
        ///
        /// The photograph is loaded from beside the DLL. If it is missing, FromFile
        /// returns null and the cloth falls back to a flat green -- a table without a
        /// photograph is still a table, and a hard failure here would take the whole
        /// panel with it.
        /// </summary>
        private static void BuildTable(RectTransform parent)
        {
            var felt = NewBox("Felt", parent, Color.white);
            felt.anchorMin = felt.anchorMax = new Vector2(0.5f, 1f);
            felt.pivot = new Vector2(0.5f, 1f);
            felt.sizeDelta = new Vector2(1150f, 1150f / TableAspect);
            felt.anchoredPosition = new Vector2(0f, -56f);

            var image = felt.GetComponent<Image>();
            var photo = Textures.FromFile(TableImagePath);

            if (photo != null)
            {
                image.sprite = photo;
                image.preserveAspect = true;
            }
            else
            {
                image.color = new Color(0.09f, 0.28f, 0.18f, 1f);
                PokerClientPlugin.Log.LogWarning(
                    "[Poker] no table.png beside the plugin; falling back to flat cloth.");
            }

            _board = NewBox("Board", felt, Color.clear);
            _board.anchorMin = _board.anchorMax = new Vector2(0.5f, 0.5f);
            _board.pivot = new Vector2(0.5f, 0.5f);
            _board.sizeDelta = new Vector2(5f * CardView.Width + 4f * 14f, CardView.Height);
            _board.anchoredPosition = new Vector2(0f, 20f);

            var row = _board.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;

            _potLabel = NewText("Pot", felt, string.Empty, 26f, TextAlignmentOptions.Center);
            _potLabel.rectTransform.anchorMin = _potLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _potLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _potLabel.rectTransform.sizeDelta = new Vector2(400f, 40f);
            _potLabel.rectTransform.anchoredPosition = new Vector2(0f, -76f);
            _potLabel.color = Gold;

            SetBoard(null);
        }

        private static void BuildSeatColumn(RectTransform parent)
        {
            _seatColumn = NewBox("Seats", parent, Color.clear);
            _seatColumn.anchorMin = new Vector2(0.5f, 1f);
            _seatColumn.anchorMax = new Vector2(0.5f, 1f);
            _seatColumn.pivot = new Vector2(0.5f, 1f);
            _seatColumn.sizeDelta = new Vector2(1150f, 320f);
            _seatColumn.anchoredPosition = new Vector2(0f, -(56f + 1150f / TableAspect) - 12f);

            var column = _seatColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 6f;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = false;
            column.childControlHeight = false;
        }

        private static void BuildSeatRow(
            string label, string[] cards, bool folded, bool isTurn, bool isPlayer)
        {
            var row = NewBox("Seat", _seatColumn, isTurn ? new Color(0.16f, 0.15f, 0.09f, 0.85f) : Color.clear);
            row.sizeDelta = new Vector2(1100f, 54f);

            var strip = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 8f;
            strip.padding = new RectOffset(14, 14, 4, 4);
            strip.childAlignment = TextAnchor.MiddleLeft;
            strip.childForceExpandWidth = false;
            strip.childForceExpandHeight = false;
            strip.childControlWidth = false;
            strip.childControlHeight = false;

            var text = NewText("Label", row, label, isPlayer ? 21f : 19f, TextAlignmentOptions.Left);
            text.rectTransform.sizeDelta = new Vector2(760f, 44f);
            text.color = folded ? Dim : (isPlayer ? Gold : Ink);

            // Small cards on the row. Two per seat, scaled down so a five-handed table
            // fits under the felt without the rows becoming card-sized themselves.
            var holder = NewBox("Cards", row, Color.clear);
            holder.sizeDelta = new Vector2(2f * CardView.Width * 0.34f + 6f, CardView.Height * 0.34f);

            var pair = holder.gameObject.AddComponent<HorizontalLayoutGroup>();
            pair.spacing = 6f;
            pair.childAlignment = TextAnchor.MiddleLeft;
            pair.childForceExpandWidth = false;
            pair.childForceExpandHeight = false;
            pair.childControlWidth = false;
            pair.childControlHeight = false;

            for (var i = 0; i < 2; i++)
            {
                var code = cards != null && i < cards.Length ? cards[i] : null;
                var card = CardView.Build(holder, code, _font);
                card.transform.localScale = new Vector3(0.34f, 0.34f, 1f);
            }
        }

        private static void BuildStatus(RectTransform parent)
        {
            _status = NewText("Status", parent, string.Empty, 19f, TextAlignmentOptions.Top);
            _status.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            _status.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _status.rectTransform.pivot = new Vector2(0.5f, 0f);
            _status.rectTransform.sizeDelta = new Vector2(1200f, 110f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, 84f);
        }

        private static void BuildActionRow(RectTransform parent)
        {
            _actionRow = NewBox("Actions", parent, Color.clear);
            _actionRow.anchorMin = new Vector2(0.5f, 0f);
            _actionRow.anchorMax = new Vector2(0.5f, 0f);
            _actionRow.pivot = new Vector2(0.5f, 0f);
            _actionRow.sizeDelta = new Vector2(1400f, 56f);
            _actionRow.anchoredPosition = new Vector2(0f, 16f);

            var strip = _actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 10f;
            strip.childAlignment = TextAnchor.MiddleCenter;
            strip.childForceExpandWidth = false;
            strip.childForceExpandHeight = false;
            strip.childControlWidth = false;
            strip.childControlHeight = false;
        }

        /// <summary>
        /// Rebuilt from scratch on every view, rather than shown and hidden. What is
        /// legal changes every action, and a stale button that is still clickable is
        /// a move the player did not mean to make.
        /// </summary>
        private static void BuildActions(IEnumerable<KeyValuePair<string, Action>> actions)
        {
            if (_actionRow == null)
            {
                return;
            }

            for (var i = _actionRow.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_actionRow.GetChild(i).gameObject);
            }

            foreach (var action in actions)
            {
                BuildButton(_actionRow, action.Key, action.Value);
            }
        }

        private static void BuildButton(Transform parent, string label, Action onClick)
        {
            var box = NewBox("Button_" + label, parent, Color.white);
            box.sizeDelta = new Vector2(Mathf.Max(120f, 22f + label.Length * 13f), 46f);

            var image = box.GetComponent<Image>();
            image.sprite = Textures.ButtonFace(
                6,
                new Color(0.20f, 0.22f, 0.20f, 1f),
                new Color(0.12f, 0.14f, 0.12f, 1f),
                Gold);
            image.type = Image.Type.Sliced;

            var text = NewText("Label", box, label, 19f, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            var button = box.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => onClick());
        }

        /// <summary>
        /// Redraws the community cards. Rebuilt rather than mutated: five cards is
        /// nothing to build, and reusing them means tracking which slot holds what.
        /// </summary>
        private static void SetBoard(string[] codes)
        {
            if (_board == null)
            {
                return;
            }

            for (var i = _board.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_board.GetChild(i).gameObject);
            }

            for (var i = 0; i < 5; i++)
            {
                var code = codes != null && i < codes.Length ? codes[i] : null;
                CardView.Build(_board, code, _font);
            }
        }

        private static RectTransform NewBox(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = colour;
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI NewText(
            string name, Transform parent, string text, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = Ink;
            label.raycastTarget = false;

            if (_font != null)
            {
                label.font = _font;
            }

            return label;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Borrows a font the game has already loaded rather than shipping one.
        /// TextMeshPro renders nothing at all with a null font, so a label that never
        /// appears looks like a layout bug rather than a missing asset.
        /// </summary>
        private static TMP_FontAsset BorrowFont()
        {
            try
            {
                return Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            }
            catch (Exception ex)
            {
                PokerClientPlugin.Log.LogWarning("[Poker] could not borrow a font: " + ex.Message);
                return null;
            }
        }
    }
}
