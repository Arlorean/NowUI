using System;
using System.Collections.Generic;
using UnityEngine;

namespace NowUI.Samples.NativePreview
{
    /// <summary>
    /// Caller-owned application state and real NowUI controls. This file can also
    /// be compiled in a Unity project and drawn inside an existing NowUI host.
    /// </summary>
    public sealed class InteractiveContent : IDisposable
    {
        public int ClickCount;
        public string BoardTitle = "Fresh perspectives";
        public float TileScale = 1f;
        public bool Animate = true;
        public int PaletteIndex;
        public int SelectedBoard;

        public NowRect TitleFieldRect { get; private set; }
        public NowRect AddButtonRect { get; private set; }
        public NowRect ScaleSliderRect { get; private set; }
        public NowRect AnimationToggleRect { get; private set; }
        public NowRect PaletteDropdownRect { get; private set; }
        public NowRect BoardsScrollRect { get; private set; }
        public float AnimationSeconds => animationSeconds;
        public int BoardCount => boards.Count;

        static readonly string[] Palettes = { "Meadow", "Lavender", "Apricot" };
        static readonly Color[] Accents = { Rgb(201, 238, 140), Rgb(194, 183, 240), Rgb(245, 186, 144) };
        static readonly Color Background = Rgb(18, 21, 26);
        static readonly Color Panel = Rgb(25, 29, 35);
        static readonly Color Border = Rgb(47, 52, 60);
        static readonly Color White = Rgb(242, 242, 237);
        static readonly Color Muted = Rgb(150, 158, 169);

        readonly List<Board> boards = new List<Board>();
        readonly Action drawBoardList;
        NowThemeAsset theme;
        float animationSeconds;
        int animationFrame = -1;

        sealed class Board
        {
            public readonly int Id;
            public string Title;
            public Board(int id, string title) { Id = id; Title = title; }
        }

        public InteractiveContent()
        {
            string[] titles = { "Fresh perspectives", "Quiet moments", "Things in motion", "A slower kind of Sunday",
                "Shapes from the garden", "Everyday color", "Letters to keep", "Somewhere new", "The little details",
                "Room for a daydream", "Collected along the way", "Something worth making" };
            for (int i = 0; i < titles.Length; i++) boards.Add(new Board(i, titles[i]));
            drawBoardList = DrawBoardList;
        }

        public void Draw(NowRect view)
        {
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<NowThemeAsset>();
                theme.name = "Fieldnotes interactive theme";
                theme.ResetToDefaults(dark: true);
            }

            using (NowTheme.Scope(theme))
            {
                Box(view, Background);
                NowRect inner = view.Inset(24);
                Box(new NowRect(inner.x, inner.y + 7, 29, 29), Accents[0], 8);
                Box(new NowRect(inner.x + 7, inner.y + 14, 5, 15), Background, 1);
                Box(new NowRect(inner.x + 16, inner.y + 14, 5, 9), Background, 1);
                Label("fieldnotes", new NowRect(inner.x + 42, inner.y, 180, 42), 22, White, true);
                Label("A little room to play.", new NowRect(inner.x + 242, inner.y + 4, inner.width - 242, 36), 14, Muted);
                Box(new NowRect(inner.x, inner.y + 63, inner.width, 1), Border);

                NowRect body = new NowRect(inner.x, inner.y + 92, inner.width, Mathf.Max(0, inner.height - 92));
                float settingsWidth = Mathf.Min(280, body.width * .4f);
                NowRect settings = new NowRect(body.x, body.y, settingsWidth, body.height);
                NowRect workspace = new NowRect(settings.xMax + 20, body.y,
                    Mathf.Max(0, body.width - settingsWidth - 20), body.height);
                DrawSettings(settings);

                if (!NowLayout.isMeasurePass && animationFrame != Time.frameCount)
                {
                    animationFrame = Time.frameCount;
                    if (Animate) animationSeconds += Mathf.Max(0, Time.deltaTime);
                }

                float previewHeight = Mathf.Clamp(workspace.height * .52f, 218, 338);
                var preview = new NowRect(workspace.x, workspace.y, workspace.width, previewHeight);
                DrawPreview(preview);
                var library = new NowRect(workspace.x, preview.yMax + 20, workspace.width,
                    Mathf.Max(0, workspace.height - previewHeight - 20));
                DrawLibrary(library);
            }
        }

        void DrawSettings(NowRect rect)
        {
            Box(rect, Panel, 14);
            float x = rect.x + 20;
            float width = rect.width - 40;
            float y = rect.y;
            Label("Make it your own", new NowRect(x, y + 15, width, 30), 18, White, true);
            Label("BOARD TITLE", new NowRect(x, y + 61, width, 22), 10, Muted);
            TitleFieldRect = new NowRect(x, y + 88, width, 38);
            if (Now.TextField(TitleFieldRect, "board-title").SetPlaceholder("Give your idea a name")
                .SetBackgroundColor(Background).SetBorderColor(Border).SetFocusColor(Accents[0])
                .SetTextColor(White).SetRadius(8).Draw(ref BoardTitle))
                boards[SelectedBoard].Title = BoardTitle;

            Label("PALETTE", new NowRect(x, y + 147, width, 22), 10, Muted);
            PaletteDropdownRect = new NowRect(x, y + 174, width, 38);
            Now.Dropdown(PaletteDropdownRect, "palette", Palettes).Draw(ref PaletteIndex);

            Label("SHAPE SIZE", new NowRect(x, y + 233, width - 62, 22), 10, Muted);
            Label(Mathf.RoundToInt(TileScale * 100) + "%", new NowRect(x + width - 60, y + 233, 60, 22), 12, White);
            ScaleSliderRect = new NowRect(x, y + 259, width, 32);
            Now.Slider(ScaleSliderRect, .65f, 1.35f).SetId("shape-size").SetStep(.01f).Draw(ref TileScale);

            AnimationToggleRect = new NowRect(x, y + 309, width, 36);
            Now.Switch(AnimationToggleRect, "Keep things moving").SetId("animate").SetTextStyle(NowTextStyle.Label).Draw(ref Animate);

            AddButtonRect = new NowRect(x, y + 374, width, 40);
            if (Now.Button(AddButtonRect, "+  Add an idea").SetId("add-idea").Draw())
            {
                ClickCount++;
                SelectedBoard = boards.Count;
                BoardTitle = "Fresh idea " + ClickCount;
                boards.Add(new Board(SelectedBoard, BoardTitle));
            }
            string counter = ClickCount == 1 ? "1 idea added this session" : ClickCount + " ideas added this session";
            Label(counter, new NowRect(x, y + 421, width, 22), 11, Muted);

            if (rect.height >= 540)
                Label("Type, drag, choose, explore.", new NowRect(x, rect.yMax - 48, width, 24), 11, Muted);
        }

        void DrawPreview(NowRect rect)
        {
            Color accent = Accents[Mathf.Clamp(PaletteIndex, 0, Accents.Length - 1)];
            Box(rect, accent, 14);
            var title = new NowRect(rect.x + 24, rect.y + 19, rect.width - 48, 38);
            Label(string.IsNullOrWhiteSpace(BoardTitle) ? "An untitled possibility" : BoardTitle,
                title, rect.width < 500 ? 22 : 27, Background, true);
            Label("A space for your next good idea.", new NowRect(rect.x + 24, rect.y + 62, rect.width - 48, 24), 12, Background);

            var artwork = new NowRect(rect.x + 20, rect.y + 95, rect.width - 40, Mathf.Max(40, rect.height - 144));
            using (Now.Mask(artwork))
            {
                Color ink = new Color(Background.r, Background.g, Background.b, .18f);
                Color strongerInk = new Color(Background.r, Background.g, Background.b, .32f);
                float unit = Mathf.Min(Mathf.Min(88, artwork.width * .21f),
                    Mathf.Max(24, (artwork.height - 24) / 1.35f)) * TileScale;
                for (int i = 0; i < 3; i++)
                {
                    float bob = Mathf.Sin(animationSeconds * 1.25f + i * 1.1f) * Mathf.Min(12, artwork.height * .12f);
                    float cx = artwork.x + artwork.width * (.2f + i * .3f);
                    float cy = artwork.center.y + bob;
                    if (i == 0)
                    {
                        Box(new NowRect(cx - unit * .5f, cy - unit * .5f, unit, unit), ink, unit * .5f);
                        Box(new NowRect(cx - unit * .16f, cy - unit * .16f, unit * .32f, unit * .32f), strongerInk, unit * .16f);
                    }
                    else if (i == 1)
                    {
                        Box(new NowRect(cx - unit * .5f, cy - unit * .5f, unit, unit), ink, 14);
                        Box(new NowRect(cx - unit * .3f, cy - unit * .3f, unit * .6f, unit * .6f), strongerInk, 8);
                    }
                    else
                    {
                        for (int bar = 0; bar < 4; bar++)
                        {
                            float barHeight = unit * (.55f + .15f * Mathf.Sin(animationSeconds + bar));
                            Box(new NowRect(cx - unit * .5f + bar * unit * .27f, cy - barHeight * .5f,
                                unit * .19f, barHeight), ink, unit * .095f);
                        }
                    }
                }
            }

            Label(Palettes[Mathf.Clamp(PaletteIndex, 0, Palettes.Length - 1)] + " collection",
                new NowRect(rect.x + 24, rect.yMax - 38, rect.width - 130, 24), 11, Background);
            Box(new NowRect(rect.xMax - 88, rect.yMax - 34, 7, 7), Background, 4);
            Label(Animate ? "In motion" : "Paused", new NowRect(rect.xMax - 74, rect.yMax - 42, 62, 24), 10, Background);
        }

        void DrawLibrary(NowRect rect)
        {
            Box(rect, Panel, 14);
            Label("Your idea shelf", new NowRect(rect.x + 20, rect.y + 13, rect.width - 120, 32), 18, White, true);
            Label(boards.Count + " boards", new NowRect(rect.xMax - 97, rect.y + 17, 77, 24), 11, Muted);
            Label("Select a board or scroll to find something new.", new NowRect(rect.x + 20, rect.y + 47, rect.width - 40, 24), 11, Muted);
            BoardsScrollRect = new NowRect(rect.x + 14, rect.y + 84, rect.width - 28, Mathf.Max(0, rect.height - 98));
            // This portable component owns only this measured subregion. The host
            // still owns the enclosing Now frame and graphics/input lifecycle.
            NowLayout.RunMeasured(BoardsScrollRect, drawBoardList);
        }

        void DrawBoardList()
        {
            using (Now.ScrollView(BoardsScrollRect, "idea-shelf").Begin())
            {
                for (int i = 0; i < boards.Count; i++)
                {
                    Board board = boards[i];
                    using (NowControls.KeyedItem(board.Id))
                    {
                        string label = (board.Id + 1).ToString("00") + "    " +
                            (string.IsNullOrWhiteSpace(board.Title) ? "Untitled idea" : board.Title);
                        if (NowLayout.SelectableRow(label).SetSelected(i == SelectedBoard)
                            .SetColor(White).SetTextStyle(NowTextStyle.Label).SetHeight(40).SetStretchWidth().Draw())
                        {
                            SelectedBoard = i;
                            BoardTitle = board.Title;
                        }
                        NowLayout.Space(4);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (theme != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(theme);
                else UnityEngine.Object.DestroyImmediate(theme);
            }
            theme = null;
        }

        static void Box(NowRect rect, Color color, float radius = 0) =>
            Now.Rectangle(rect).SetColor(color).SetRadius(radius).Draw();

        static void Label(string value, NowRect rect, float size, Color color, bool bold = false)
        {
            var bounds = Now.font.MeasureTextBounds(value, size);
            Now.Text(rect.Offset(-bounds.x, (rect.height - bounds.w) * .5f - bounds.y))
                .SetFontSize(size).SetColor(color).SetBold(bold).SetMask(rect.Outset(1)).Draw(value);
        }

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1);
    }
}
