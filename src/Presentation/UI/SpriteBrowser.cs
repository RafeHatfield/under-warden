using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Debug-only sprite browser for index-based tilesets (e.g. Oryx 16-bit Fantasy).
/// Toggle with F8 during gameplay.
///
/// Navigates raw sprite file numbers directly (no formula assumptions).
/// Shows the sprite, the file number, and the YAML key to use.
///
/// YAML key = the file number itself (frame_stride: 1, frame_offset: -1).
/// Frame A (standing) and frame B (action) of the same creature are 18 files apart.
/// Use the frame A file number as the YAML key — the game will load that exact file.
///
/// Navigation:
///   ← / →   step by 1 sprite file
///   ↑ / ↓   step by 10 sprite files
///   Type a file number + Enter to jump directly
///   Esc or F8 to close
/// </summary>
public sealed partial class SpriteBrowser : Control
{
    private readonly SpriteMapping _mapping;
    private int _fileIndex = 1;
    private const int MaxFileIndex = 396; // creatures_01.png … creatures_396.png

    private TextureRect? _spriteDisplay;
    private Label? _fileLabel;
    private Label? _yamlLabel;
    private LineEdit? _fileInput;

    public SpriteBrowser(SpriteMapping mapping)
    {
        _mapping = mapping;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        BuildUI();
        UpdateDisplay();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey key || !key.Pressed) return;

        if (key.Keycode == Key.F8)
        {
            Visible = !Visible;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!Visible) return;

        switch (key.Keycode)
        {
            case Key.Escape: Visible = false; break;
            case Key.Right:  Step(+1);  break;
            case Key.Left:   Step(-1);  break;
            case Key.Up:     Step(+10); break;
            case Key.Down:   Step(-10); break;
        }
        GetViewport().SetInputAsHandled();
    }

    private void Step(int delta)
    {
        _fileIndex = System.Math.Clamp(_fileIndex + delta, 1, MaxFileIndex);
        if (_fileInput != null) _fileInput.Text = _fileIndex.ToString();
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        // Load the sprite file directly by number — no creature key formula involved.
        var fileName = $"oryx_16bit_fantasy_creatures_{_fileIndex:D2}.png";
        var path = $"{_mapping.SpritesRoot}/{fileName}";
        var tex  = GD.Load<Texture2D>(path);

        if (_spriteDisplay != null) _spriteDisplay.Texture = tex;

        // YAML key: with frame_stride=1, frame_offset=-1, the key IS the file number.
        // Put this number directly in config/tilesets/16bit_fantasy.yaml entities section.
        // Frame A (standing) and frame B (action) of the same creature are 18 files apart.
        int yamlKey = _fileIndex;
        // Detect frame A vs B: within each batch of 36 files (18 A + 18 B),
        // files 1-18 are frame A, files 19-36 are frame B.
        int posInBatch = ((_fileIndex - 1) % 36) + 1;
        string frameLabel = posInBatch <= 18 ? "frame A (standing)" : "frame B (action)";

        if (_fileLabel != null)
            _fileLabel.Text = $"File: {fileName}   ({frameLabel})";
        if (_yamlLabel != null)
            _yamlLabel.Text = $"YAML key to use:  \"{yamlKey}\"";
    }

    private void BuildUI()
    {
        // Semi-transparent full-screen backdrop
        var backdrop = new ColorRect();
        backdrop.Color = new Color(0f, 0f, 0f, 0.75f);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(backdrop);

        // Centered panel
        var panel = new PanelContainer();
        panel.AnchorLeft   = panel.AnchorRight  = 0.5f;
        panel.AnchorTop    = panel.AnchorBottom  = 0.5f;
        panel.OffsetLeft   = -190f;
        panel.OffsetRight  =  190f;
        panel.OffsetTop    = -220f;
        panel.OffsetBottom =  220f;
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        // Title
        var title = new Label { Text = "Sprite Browser" };
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(title);
        vbox.AddChild(new HSeparator());

        // Sprite display — nearest-filter scale-up
        _spriteDisplay = new TextureRect();
        _spriteDisplay.CustomMinimumSize = new Vector2(360, 192);
        _spriteDisplay.ExpandMode   = TextureRect.ExpandModeEnum.IgnoreSize;
        _spriteDisplay.StretchMode  = TextureRect.StretchModeEnum.KeepAspectCentered;
        _spriteDisplay.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        vbox.AddChild(_spriteDisplay);

        vbox.AddChild(new HSeparator());

        // File name line
        _fileLabel = new Label();
        _fileLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _fileLabel.AddThemeFontSizeOverride("font_size", 13);
        _fileLabel.Modulate = new Color(0.8f, 0.8f, 0.8f);
        vbox.AddChild(_fileLabel);

        // YAML key line — highlighted
        _yamlLabel = new Label();
        _yamlLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _yamlLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(_yamlLabel);

        vbox.AddChild(new HSeparator());

        // Navigation row
        var nav = new HBoxContainer();
        nav.Alignment = BoxContainer.AlignmentMode.Center;
        nav.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(nav);

        var btnPrev = new Button { Text = "◀" };
        btnPrev.CustomMinimumSize = new Vector2(48, 36);
        btnPrev.Pressed += () => Step(-1);
        nav.AddChild(btnPrev);

        _fileInput = new LineEdit { Text = "1" };
        _fileInput.CustomMinimumSize = new Vector2(72, 36);
        _fileInput.Alignment = HorizontalAlignment.Center;
        _fileInput.TextSubmitted += OnFileSubmitted;
        nav.AddChild(_fileInput);

        var btnNext = new Button { Text = "▶" };
        btnNext.CustomMinimumSize = new Vector2(48, 36);
        btnNext.Pressed += () => Step(+1);
        nav.AddChild(btnNext);

        // Hint
        var hint = new Label { Text = "← → step 1   ↑ ↓ step 10   type file # + Enter   Esc / F8 close" };
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.Modulate = new Color(0.6f, 0.6f, 0.6f);
        vbox.AddChild(hint);
    }

    private void OnFileSubmitted(string text)
    {
        if (int.TryParse(text, out var v))
        {
            _fileIndex = System.Math.Clamp(v, 1, MaxFileIndex);
            UpdateDisplay();
        }
        if (_fileInput != null) _fileInput.Text = _fileIndex.ToString();
    }
}
