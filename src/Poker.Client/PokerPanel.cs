using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Poker.Client
{
    /// <summary>
    /// The table window.
    ///
    /// Scaffold only. It opens, talks to the server and closes again; it does not yet
    /// draw a table. The point of having it this early is that it proves the whole
    /// chain end to end from inside the game -- Harmony patch, menu button, canvas,
    /// and SPT's own transport reaching the mod's routes -- before any of the layout
    /// work is built on top of it. Every one of those has its own way of failing
    /// silently, and finding out which one broke is far cheaper with nothing else in
    /// the frame.
    ///
    /// Built the same way as Blackjack's panel: a static class owning one screen-space
    /// canvas, created on first open and kept afterwards.
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

        private static GameObject _root;
        private static TextMeshProUGUI _status;
        private static TMP_FontAsset _font;
        private static RectTransform _board;

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

                Refresh();
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
        /// prompt or a record sheet exists, they are handled here first, in the order
        /// they are stacked, or escape closes the table out from under them.
        /// </summary>
        internal static void OnEscape()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            Close();
        }

        /// <summary>
        /// Asks the server what it knows. Ping rather than state, because at this
        /// stage the useful question is whether the two halves can talk at all --
        /// and its answer carries the version and the balances, which is exactly
        /// what tells a player the mod is alive.
        /// </summary>
        private static void Refresh()
        {
            var reply = PokerApi.Ping();

            if (reply == null)
            {
                SetStatus("No answer from the server mod.\nIs it loaded? Look for a [Poker] block in the server console.");
                return;
            }

            var version = (string)reply["ModVersion"] ?? "unknown";
            var hasProfile = (bool?)reply["HasProfile"] ?? false;
            var balances = reply["Balances"];

            var money = balances == null
                ? "no balances returned"
                : string.Join(
                    "    ",
                    balances.Children<Newtonsoft.Json.Linq.JProperty>()
                        .Select(p => $"{p.Name} {(long?)p.Value ?? 0:N0}")
                        .ToArray());

            SetStatus(
                $"Server mod v{version} answered.\n"
                + $"Profile {(hasProfile ? "found" : "NOT found")}.\n\n{money}\n\n"
                + "The chips are notional in this build -- nothing above is at stake.\n"
                + "No table is drawn yet. Escape closes this.");
        }

        private static void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

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
            var backdrop = NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.86f));
            Stretch(backdrop);

            var window = NewBox("Window", canvasObject.transform, new Color(0f, 0f, 0f, 0f));
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(1400f, 1000f);

            var title = NewText("Title", window, "POKER", 34f, TextAlignmentOptions.Top);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-60f, 60f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -10f);

            BuildTable(window);

            var status = NewText("Status", window, string.Empty, 19f, TextAlignmentOptions.Top);
            status.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            status.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            status.rectTransform.pivot = new Vector2(0.5f, 0f);
            status.rectTransform.sizeDelta = new Vector2(1200f, 130f);
            status.rectTransform.anchoredPosition = new Vector2(0f, 84f);
            _status = status;

            BuildCloseButton(window);
        }

        /// <summary>
        /// The felt, with the five community card slots on it.
        ///
        /// The photograph is loaded from beside the DLL. If it is missing, FromFile
        /// returns null and the cloth falls back to a flat green -- a table without a
        /// photograph is still a table, and a hard failure here would take the whole
        /// panel with it.
        /// </summary>
        private static void BuildTable(RectTransform parent)
        {
            var felt = NewBox("Felt", parent, Color.white);
            felt.anchorMin = felt.anchorMax = new Vector2(0.5f, 0.5f);
            felt.pivot = new Vector2(0.5f, 0.5f);
            felt.sizeDelta = new Vector2(1230f, 1230f / TableAspect);
            felt.anchoredPosition = new Vector2(0f, 40f);

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

            // The board. Five slots, face down until the server says otherwise -- the
            // view fills in a card only once it has actually been dealt, so an empty
            // code is the honest state rather than a placeholder.
            _board = NewBox("Board", felt, Color.clear);
            _board.anchorMin = _board.anchorMax = new Vector2(0.5f, 0.5f);
            _board.pivot = new Vector2(0.5f, 0.5f);
            _board.sizeDelta = new Vector2(5f * CardView.Width + 4f * 14f, CardView.Height);

            var row = _board.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;

            SetBoard(null);
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

        private static void BuildCloseButton(RectTransform parent)
        {
            var box = NewBox("Close", parent, Color.clear);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0f);
            box.pivot = new Vector2(0.5f, 0f);
            box.sizeDelta = new Vector2(220f, 48f);
            box.anchoredPosition = new Vector2(0f, 24f);

            var image = box.GetComponent<Image>();
            image.sprite = Textures.ButtonFace(
                6,
                new Color(0.20f, 0.22f, 0.20f, 1f),
                new Color(0.12f, 0.14f, 0.12f, 1f),
                new Color(0.55f, 0.47f, 0.25f, 1f));
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var label = NewText("Label", box, "CLOSE", 20f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            box.gameObject.AddComponent<Button>().onClick.AddListener(Close);
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
            label.color = new Color(0.86f, 0.84f, 0.78f, 1f);
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
