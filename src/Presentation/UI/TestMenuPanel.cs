using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Full-screen panel listing available test scenarios grouped by category.
/// Presented in debug builds via the "Testing Mode" main menu button.
///
/// Constructor takes the discovered scenario list so this panel has no
/// file-system knowledge — discovery happens in Main.cs.
/// </summary>
public sealed partial class TestMenuPanel : Control
{
    [Signal] public delegate void ScenarioSelectedEventHandler(string path);
    [Signal] public delegate void BackRequestedEventHandler();

    private readonly List<(string Name, string Path, string Category)> _scenarios;

    public TestMenuPanel(List<(string name, string path, string category)> scenarios)
    {
        _scenarios = scenarios;
    }

    public override void _Ready()
    {
        BuildLayout();
    }

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var bg = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.85f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var outer = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("separation", 12);
        outer.OffsetLeft   = 40f;
        outer.OffsetTop    = 60f;
        outer.OffsetRight  = -40f;
        outer.OffsetBottom = -40f;
        AddChild(outer);

        var title = new Label
        {
            Text                = "Testing Mode",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 36);
        outer.AddChild(title);

        var scroll = new ScrollContainer { MouseFilter = MouseFilterEnum.Stop };
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        outer.AddChild(scroll);

        var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        list.AddThemeConstantOverride("separation", 6);
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(list);

        if (_scenarios.Count == 0)
        {
            var empty = new Label
            {
                Text                = "No test scenarios found.",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode        = TextServer.AutowrapMode.Word,
                MouseFilter         = MouseFilterEnum.Ignore,
            };
            empty.AddThemeFontSizeOverride("font_size", 18);
            empty.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f, 1f));
            list.AddChild(empty);
        }
        else
        {
            BuildCategorisedList(list);
        }

        var backBtn = new TouchButton
        {
            Text              = "← Back",
            FontSize          = 22,
            BackgroundColor   = new Color(0.35f, 0.2f, 0.2f, 0.95f),
            CustomMinimumSize = new Vector2(0, 56),
        };
        backBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        backBtn.Pressed += () => EmitSignal(SignalName.BackRequested);
        outer.AddChild(backBtn);
    }

    private void BuildCategorisedList(VBoxContainer list)
    {
        // Group by category (already sorted category-first by DiscoverTestScenarios).
        string? currentCategory = null;
        foreach (var (name, path, category) in _scenarios)
        {
            if (category != currentCategory)
            {
                currentCategory = category;

                // Category header — spacer + label
                if (list.GetChildCount() > 0)
                {
                    var spacer = new Control
                    {
                        CustomMinimumSize = new Vector2(0, 8),
                        MouseFilter       = MouseFilterEnum.Ignore,
                    };
                    list.AddChild(spacer);
                }

                var header = new Label
                {
                    Text                = category.ToUpperInvariant(),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MouseFilter         = MouseFilterEnum.Ignore,
                };
                header.AddThemeFontSizeOverride("font_size", 14);
                header.AddThemeColorOverride("font_color", new Color(0.55f, 0.75f, 0.95f, 1f));
                list.AddChild(header);

                // Thin rule under header
                var rule = new ColorRect
                {
                    Color             = new Color(0.4f, 0.5f, 0.65f, 0.6f),
                    CustomMinimumSize = new Vector2(0, 1),
                    MouseFilter       = MouseFilterEnum.Ignore,
                };
                list.AddChild(rule);
            }

            var capturedPath = path;
            var btn = new TouchButton
            {
                Text              = name,
                FontSize          = 20,
                BackgroundColor   = new Color(0.15f, 0.28f, 0.45f, 0.92f),
                CustomMinimumSize = new Vector2(0, 54),
            };
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.Pressed += () => EmitSignal(SignalName.ScenarioSelected, capturedPath);
            list.AddChild(btn);
        }
    }
}
