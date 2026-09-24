using System.Linq;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using UnderWarden.Presentation.Animation;
using UnderWarden.Presentation.Entities;
using UnderWarden.Presentation.Map;
using UnderWarden.Presentation.Persistence;
using UnderWarden.Presentation.UI;
using Godot;

namespace UnderWarden.Presentation;

/// <summary>
/// Root scene node. Loads content, creates GameState, initialises all
/// presentation systems, and routes input to the GameController.
///
/// Two entry paths:
///   LoadAndStart() — loads a scenario YAML (existing harness/dev path, still the default).
///   StartDungeon(depth) — procedural dungeon via DungeonFloorBuilder (new campaign path).
///
/// Factories are created once in _Ready via InitFactories() and reused across floor
/// transitions. LoadAndStart was previously re-creating them every call — that worked
/// for scenarios (single floor) but is wrong for dungeon mode where Build() is called
/// repeatedly on the same DungeonFloorBuilder.
/// </summary>
public partial class Main : Node
{
    private GameController? _gameController;
    private GameState? _state;
    private Node2D? _gameView;
    private HUD? _hud;
    private ToastLog? _toastLog;
    private GameOverScreen? _gameOverScreen;
    private QuickSlotBar? _inventoryPanel;
    private EquipmentPanel? _equipmentPanel;
    private MenuButtonBar? _menuButtonBar;
    // Stored as field so SetupPresentation can call SetGameState after each floor build.
    private DebugOverlay? _debugOverlay;
    private RectDebugDraw? _rectDebugDraw;

    // Fog-of-war: tile layer tracks sprites for per-turn visibility updates
    private TileLayer? _tileLayer;
    // Tile map parent node — needed to add door overlay sprites on SecretDoorFoundEvent.
    private Node2D? _tileMapLayer;
    // Entity sprite manager is stored here so OnTurnCompleted can call UpdateVisibility
    private EntitySpriteManager? _entitySprites;
    // Item sprite manager tracks floor item overlay sprites
    private ItemSpriteManager? _itemSprites;
    // Corpse sprite manager renders remains of dead monsters (reconciled against state.Corpses)
    private CorpseSpriteManager? _corpseSprites;
    // Ground hazard overlay — persistent tile tints for burning/poison ground.
    private GroundHazardOverlay? _groundHazardOverlay;
    // Floating HP bars — small red bars above damaged enemy sprites (Phase 4).
    private FloatingHpBarManager? _floatingHpBars;

    // Tileset-backed sprite mapping — created once at boot, shared across all floors.
    private SpriteMapping? _spriteMapping;

    // Tile theme config — loaded once at boot, passed to DungeonRenderer.Render on every floor.
    // Covers dungeon tile assets (floors, walls, stairs, decorations). Does not change per floor.
    private TileThemeConfig? _tileThemeConfig;

    // Map renderer — created once at boot, injected into all presentation consumers.
    private IMapRenderer _renderer = new TopDownRenderer(); // safe default until _Ready

    // --- Factories (created once, reused across floor transitions) ---
    private ContentLoader? _contentLoader;
    private MonsterFactory? _monsterFactory;
    private ItemFactory? _itemFactory;
    private ConsumableFactory? _consumableFactory;
    private SpellItemFactory? _spellItemFactory;
    private EntityFactory? _entityFactory;
    private DungeonFloorBuilder? _floorBuilder;
    private LevelTemplateRegistry? _levelTemplates;
    // Set while running a test scenario so floor transitions reuse the same guaranteed spawns.
    private DungeonFloorBuilder? _testScenarioBuilder;

    // Map drag-to-pan state
    private bool _isDragging;
    private bool _dragStartRecorded; // true only when _UnhandledInput saw the matching DOWN event
    private Vector2 _dragStartScreenPos;
    private Vector2 _cameraPositionAtDragStart;

    // Bot mode (debug builds only) — never instantiated in release builds.
    private UnderWarden.Presentation.Bot.BotPlayerDriver? _botDriver;
    private UnderWarden.Presentation.Bot.BotModeHud? _botHud;
    private static readonly string[] BotPersonaCycle = ["balanced", "cautious", "aggressive", "greedy", "speedrunner"];
    private static readonly float[] BotSpeedCycle = [1.0f, 0.5f, 0.25f, 0.1f, 0.0f];
    private int _botPersonaIdx;
    private int _botSpeedIdx;
    private const float DragThreshold = 10f; // pixels before drag mode activates

    // VFX overlay — spell and status visual effects. Created once per floor setup.
    private VfxOverlay? _vfxOverlay;

    // Active portal sprites keyed by entity ID. Spawned on PortalPlacedEvent,
    // despawned on PortalRemovedEvent / PortalEntranceCancelledEvent.
    private readonly Dictionary<int, Sprite2D> _portalSprites = new();

    // Tracked tap indicators — alpha lerped in _Process, no Tween involved.
    // SpawnTime is when the indicator appeared; fade starts after FadeDelay seconds.
    // CanvasItem covers both ColorRect (top-down) and any future Sprite2D variant.
    private readonly List<(CanvasItem Node, double SpawnTime)> _tapIndicators = new();
    private const double TapFadeDelay    = 0.15; // seconds at full alpha before fade starts
    private const double TapFadeDuration = 0.35; // seconds to fade to zero

    // Minimap and zoom — zoom limits come from the renderer after boot
    private MiniMap? _miniMap;
    // Msg button — created once, anchored to bottom-left of ViewportOverlay (Phase 5).
    private MsgButton? _msgButton;
    // Message log panel — full-screen overlay opened by Msg button (Phase 6.2).
    private MessageLogPanel? _messageLogPanel;
    // Status effect badge row — created once, anchored to top-left of ViewportOverlay (Phase 5).
    // Hides itself when the player has no active effects, so no viewport clutter during normal play.
    private StatusEffectBar? _statusEffectBar;
    private float _currentZoom;   // initialised in _Ready after renderer is created
    private const float ZoomStep = 0.5f;

    // The Tier 0 review light rig, RETAINED. It used to be a local in LaunchCorridorScene: the
    // light was positioned once on the player's spawn tile and the object was then dropped, so
    // walking moved the figure out of a stationary pool. Headless captures never noticed because
    // they are all taken on the spawn frame. The device walk is where it mattered, and §6.2.1's
    // tuning pass — legibility ACROSS the lit radius, at gameplay distance — cannot run through
    // a lamp that does not move. Held here so _Process can keep it on the player and so
    // ReviewRigPanel can turn its knobs. Null outside a review build.
    private ReviewLighting? _reviewLighting;
    // The occlusion decision, made once by the overlay planner and consumed by the floor family
    // that bakes it. Null means no overlay manifest — the floor then paints no boundary, which
    // is the same thing that happened before when there was no sprite to draw.
    private OcclusionBake? _occlusionBake;

    // Review-build only, and reached solely from LaunchCorridorScene. The wall family is held so
    // the rig panel's VOID row can re-lay it live: §13.1 gives the void to Rafe at the gate, and
    // three candidates rebuilt one at a time are three walks rather than one comparison.
    private ReviewRigPanel? _rigPanel;
    private string? _wallManifest;
    private int _voidChoice;

    // Dungeon run state
    private int _baseSeed = 1337;
    private int _currentDepth = 1;

    // --art-scene-capture state (tools/art_lint/capture_scene.py driver).
    // Set true in _Ready when the flag is present; consumed in _Process once the
    // post-SetupPresentation camera snap (_pendingCameraSnapFrames) has settled to 0.
    private bool _pendingCapture;
    private string? _captureOutputPath;
    // Floor session two, precondition 2: the points a floor scene declares it must be able to
    // see, and the points it declares must stay dark. See ProbeFloorLegibility.
    private IReadOnlyList<UnderWarden.Logic.Core.CorridorReviewSceneBuilder.LegibilityPoint>?
        _legibility;

    // Tier 0 junction-lit probe state. See ProbeJunctionLuminance.
    private (int X, int Y)? _junctionTile;
    private (int X, int Y)? _litReferenceTile;
    private int _captureSettleBuffer = 2; // extra idle frames after camera-snap settles

    // Cross-run persistence — loaded once at app start, flushed at narrative-event boundaries.
    private GodotPersistencePathProvider? _persistenceProvider;
    private PersistentRunState? _persistentState;

    // Mid-run save/resume (M1.4 4b): off-critical-path autosave every turn + on descent/pause,
    // deleted on run-end (record-then-delete), silently resumed at startup. Dungeon mode only.
    private Logic.Persistence.MidRun.MidRunAutosaveWriter? _midRunWriter;
    private Dictionary<int, Logic.Balance.BoonDefinition>? _boonTable;   // for LoadMidRun (RECONSTRUCT-class)
    // Daily-seed sibling file — loaded once at app start, separate from character state.
    private DailySeedsFile? _dailySeeds;

    // Voice line registry — loaded once, shared across all turns.
    // Null until InitFactories succeeds; VoiceLineEvent handling is a no-op when null.
    private VoiceLineRegistry? _voiceLineRegistry;
    private Logic.Content.WeighingAuditRegistry? _weighingAuditRegistry;
    // Tracks which trigger IDs have already fired their canonical (first) line this run.
    private readonly HashSet<string> _voiceLineFiredSet = new();

    // Hollowmark ribbon (M1.5b). The scheduler is a Main-level field that persists across floor
    // GameState rebuilds; it is attached to _state before each save and reconstructed on resume.
    // Dungeon mode only — scenario/harness paths never construct it.
    private Logic.Voice.VoiceScheduler? _voiceScheduler;
    private Logic.Voice.VoiceTierMetadata? _voiceTierMeta;   // RECONSTRUCT (fixture until Voice authors real tiers)
    private VoiceLineRegistry? _voiceRibbonRegistry;         // scheduler's pools — real registry, or the dev fixture
    private readonly Logic.Voice.VoiceTriggerReader _voiceTriggerReader = new();
    private Logic.Voice.VoiceSettings _voiceSettings = new();   // DEVICE setting, loaded from user:// — never in the run save
    private UI.VoiceRibbon? _voiceRibbon;
    // Phase 1 device diagnostic (E): each turn's ribbon decision + reason code is written to
    // diag_structured.jsonl. DEFAULT-ON IN DEBUG BUILDS so the evidence needs no toggle; the Options
    // toggle can turn it off. OS.IsDebugBuild() is false in release, so this never logs there.
    private bool _voiceDiag = Godot.OS.IsDebugBuild();

    // Under-Warden memo delivery — registry loaded once at boot alongside voice lines.
    // Evaluator is stateless; _memoRegistry is null until InitFactories succeeds.
    private MemoRegistry? _memoRegistry;
    private readonly MemoDeliveryEvaluator _memoEvaluator = new();

    // Inbox panel — created once alongside _gameOverScreen; shown after run ends when
    // PendingMemos is non-empty.
    private MemoInboxPanel? _memoInboxPanel;

    // Stats accumulation for game-over screen
    private int _turnCount;
    private int _monstersKilled;
    private int _damageDealt;
    private int _damageTaken;

    public override void _Ready()
    {
        // --art-scene-capture at a specific logical resolution: project.godot's stretch mode
        // (canvas_items / keep_width / integer) keeps the render canvas pinned to the
        // project's base viewport size (720x1280) regardless of --resolution, which only
        // resizes the OS window around that fixed canvas. ContentScaleSize overrides the
        // base canvas size itself for this run only — no change to project.godot, no effect
        // outside a capture run. Must happen before any viewport-size-dependent setup below
        // (renderer creation, camera math) so everything sees the final size from frame 0.
        if (ReadArtSceneCaptureResolution(out var captureWidth, out var captureHeight))
            GetTree().Root.ContentScaleSize = new Vector2I(captureWidth, captureHeight);

        GD.Print("The Under-Warden — loading...");
        Diag.Init();

        // Load cross-run persistence. Missing file → fresh defaults (no write until first dirty flush).
        _persistenceProvider = new GodotPersistencePathProvider();
        _midRunWriter = new Logic.Persistence.MidRun.MidRunAutosaveWriter(
            _persistenceProvider.GetMidRunSaveFilePath(), GD.PrintErr);
        _persistentState = PersistentRunState.LoadFromDisk(_persistenceProvider, GD.PrintErr);
        _dailySeeds = PersistentRunState.LoadDailySeedsFromDisk(_persistenceProvider, GD.PrintErr);
        GD.Print($"[Main] Persistence loaded — {_persistentState.RunCounter.TotalRuns} runs ever.");

        InitSpriteMapping();
        // --tile-theme-config <res:// or user:// path>: Tier 0 review harness only. Points the
        // theme loader at an alternate tile_root/tile_pattern/tile-ID set so candidate floor and
        // wall tiles (and the ART-BIBLE-v0 §6.4 probe arms) render through the production
        // renderer without any file in the worktree being overwritten. Must precede the load.
        if (ReadTileThemeConfigFlag(out var themeConfigPath))
            TileThemeLoader.OverrideConfigPath(themeConfigPath!);
        else if (ReviewBuildMarker.TryLoad(out var bootMarker))
            TileThemeLoader.OverrideConfigPath(bootMarker!.ThemeConfigPath);
        _tileThemeConfig = TileThemeLoader.LoadWithFallback();
        if (_tileThemeConfig.Themes.Count == 0)
            GD.PrintErr("[Main] TileThemeConfig loaded with no themes — dungeon tiles will not render. Check config/tile_themes.yaml.");
        _renderer = CreateRenderer(ReadMapMode());
        _currentZoom = _renderer.DefaultZoom;
        GD.Print($"[Main] Map renderer: {_renderer.GetType().Name} (tile={_renderer.TileWidth}x{_renderer.TileHeight}, zoom default={_renderer.DefaultZoom}, min={_renderer.MinZoom}, max={_renderer.MaxZoom})");
        InitFactories();

        // --art-scene: dev/debug launch flag (same convention as --tileset/--map-mode) that
        // boots directly into the fixed art-acceptance test scene instead of the main menu.
        // See ArtAcceptanceSceneBuilder (Logic layer) for the authored floor data.
        //
        // --art-scene-capture --capture-out <path>: same boot path (reuses
        // LaunchArtAcceptanceScene — no parallel boot logic), but additionally captures the
        // settled viewport to a PNG and quits. Driven by tools/art_lint/capture_scene.py.
        if (ReadArtSceneCaptureFlag(out var captureOutputPath))
        {
            _pendingCapture = true;
            _captureOutputPath = captureOutputPath;
            // --review-scene <json>: boot the ReviewSceneBuilder round (in-scene candidate review)
            // instead of the fixed acceptance scene, on the same capture path. See ReviewSceneBuilder.
            // --corridor-scene <json>: boot the Tier 0 review corridor (lit corridor with a
            // junction) on the same capture path. See CorridorReviewSceneBuilder.
            if (ReadCorridorSceneFlag(out var corridorJson))
                LaunchCorridorScene(corridorJson!);
            else if (ReadReviewSceneFlag(out var reviewJson))
                LaunchReviewScene(reviewJson!);
            else
                LaunchArtAcceptanceScene();
        }
        else if (ReadArtSceneFlag())
        {
            LaunchArtAcceptanceScene();
        }
        else if (ReviewBuildMarker.TryLoad(out var marker))
        {
            // TIER 0 REVIEW BUILD (ART-BIBLE-v0 §13.1). An iOS app receives no command line, so
            // a review build is identified by a marker file baked into the export instead. Absent
            // — which is every normal build — this branch never runs and boot is unchanged.
            GD.Print("[Tier0] REVIEW BUILD marker present — booting the review corridor.");
            LaunchCorridorScene(marker!.ScenePath, marker);
        }
        else
        {
            // Silent resume of a valid mid-run save, else the normal menu/new-game flow.
            ResumeOrShowMenu();
        }

        // Debug overlay: only created in editor/debug builds — zero cost in release.
        // Stored as a field so SetupPresentation can wire it to the current floor's objects.
        if (OS.IsDebugBuild())
        {
            _debugOverlay = new DebugOverlay();
            _debugOverlay.Visible = ReadDebugOverlayVisible();
            GetNode<CanvasLayer>("UILayer").AddChild(_debugOverlay);

            _rectDebugDraw = new RectDebugDraw();
            _rectDebugDraw.Visible = false;
            _rectDebugDraw.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _rectDebugDraw.MouseFilter = Control.MouseFilterEnum.Ignore;
            GetNode<CanvasLayer>("UILayer").AddChild(_rectDebugDraw);

            // Sprite browser — only for index-based tilesets (16bf etc.). F8 to toggle.
            if (_spriteMapping?.IsIndexBased == true)
            {
                var browser = new UI.SpriteBrowser(_spriteMapping);
                GetNode<CanvasLayer>("UILayer").AddChild(browser);
            }
        }
    }

    public override void _Process(double delta)
    {
        // Re-snap camera for the first N frames after SetupPresentation. Running each frame
        // handles layout settling after the MenuLayer hides — GetVisibleRect() can report a
        // plausible but stale size on the first frame before Godot has processed the change.
        if (_pendingCameraSnapFrames > 0)
        {
            _pendingCameraSnapFrames--;
            _DoInitialCameraSnap();
        }

        // THE PLAYER IS THE LAMP (§6.2, and §6.5's whole derivation rests on it: "the face is
        // always one tile nearer the light than its own top"). Driven from _Process rather than
        // from a turn-completion hook because the carried light belongs to the figure, not to a
        // turn — and because a review build must not depend on which of several turn paths ran.
        // Cheap: one Vector2 assignment, no texture work.
        if (_reviewLighting != null && _state != null)
        {
            _reviewLighting.Follow(_state.Player.X, _state.Player.Y);
            _reviewLighting.Tick(delta);
            LogFrameTime(delta);
        }

        // --art-scene-capture: wait for the camera-snap sequence above to fully settle
        // (_pendingCameraSnapFrames reaches 0), then a couple more idle frames as a buffer
        // against any one-frame-late layout settling, then capture and quit.
        if (_pendingCapture && _pendingCameraSnapFrames == 0)
        {
            if (_captureSettleBuffer > 0)
                _captureSettleBuffer--;
            else
                CaptureAndQuit();
        }

        // Animate and clean up tap indicators entirely in _Process — no Tween involved,
        // so no tween holds a reference to the sprite after it's freed.
        double now = Time.GetTicksMsec() / 1000.0;
        for (int i = _tapIndicators.Count - 1; i >= 0; i--)
        {
            var (node, spawnTime) = _tapIndicators[i];

            // Guard: node may have been freed if the floor transitioned while it was alive.
            if (!GodotObject.IsInstanceValid(node))
            {
                _tapIndicators.RemoveAt(i);
                continue;
            }

            double age = now - spawnTime;
            double fadeStart = TapFadeDelay;
            double fadeEnd   = TapFadeDelay + TapFadeDuration;

            if (age >= fadeEnd)
            {
                node.SafeFree();
                _tapIndicators.RemoveAt(i);
            }
            else if (age >= fadeStart)
            {
                float t = (float)((age - fadeStart) / TapFadeDuration);
                var m = node.Modulate;
                node.Modulate = new Color(m.R, m.G, m.B, 1f - t);
            }
        }
    }

    /// <summary>
    /// Load the active tileset config and create the SpriteMapping instance.
    /// Priority: --tileset CLI arg → game_settings.yaml → default "ultimate_fantasy".
    /// Called once from _Ready before InitFactories.
    /// </summary>
    private void InitSpriteMapping()
    {
        var tilesetId = ReadTilesetId();
        GD.Print($"[Main] Loading tileset: {tilesetId}");
        var config = TilesetLoader.LoadWithFallback(tilesetId);
        _spriteMapping = new SpriteMapping(config);
        GD.Print($"[Main] Tileset loaded: {config.Name} ({config.SpriteSize}px, {config.FrameCount} frames)");
    }

    /// <summary>
    /// Determine which tileset ID to load.
    /// Checks CLI args first (--tileset &lt;id&gt;), then game_settings.yaml, then defaults to ultimate_fantasy.
    /// </summary>
    private static string ReadTilesetId()
    {
        // 1. Check --tileset CLI arg (dev override — no file edit needed to switch)
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--tileset")
                return args[i + 1];
        }

        // 2. Check config/game_settings.yaml
        const string settingsPath = "res://config/game_settings.yaml";
        try
        {
            using var file = Godot.FileAccess.Open(settingsPath, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var text = file.GetAsText();
                // Simple line-by-line parse — only need the single `tileset:` field.
                // Full YamlDotNet deserialization is overkill for one string value.
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("tileset:", System.StringComparison.Ordinal)) continue;

                    var value = trimmed["tileset:".Length..].Trim();
                    // Strip surrounding quotes if present
                    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                        value = value[1..^1];
                    if (value.Length > 0) return value;
                }
            }
        }
        catch (System.Exception ex)
        {
            // game_settings.yaml is optional — log and continue to default
            GD.PrintErr($"[Main] Failed to read game_settings.yaml: {ex.Message}");
        }

        // 3. Default
        return "ultimate_fantasy";
    }

    /// <summary>
    /// Determine which map renderer mode to use.
    /// Checks CLI args first (--map-mode &lt;mode&gt;), then game_settings.yaml, then defaults to "iso".
    /// </summary>
    private static string ReadMapMode()
    {
        // 1. Check --map-mode CLI arg (dev override)
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--map-mode")
                return args[i + 1];
        }

        // 2. Check config/game_settings.yaml
        const string settingsPath = "res://config/game_settings.yaml";
        try
        {
            using var file = Godot.FileAccess.Open(settingsPath, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var text = file.GetAsText();
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("map_mode:", System.StringComparison.Ordinal)) continue;

                    var value = trimmed["map_mode:".Length..].Trim();
                    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                        value = value[1..^1];
                    if (value.Length > 0) return value;
                }
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Main] Failed to read map_mode from game_settings.yaml: {ex.Message}");
        }

        // 3. Default
        return "iso";
    }

    /// <summary>
    /// --art-scene: dev launch flag that boots directly into the fixed art-acceptance test
    /// scene (docs/art_test_scene_spec_v2.md), bypassing the main menu. Same CLI-arg convention
    /// as --tileset/--map-mode. No game_settings.yaml fallback — this is a one-shot debug
    /// launch mode, not a persistent setting.
    /// </summary>
    private static bool ReadArtSceneFlag()
    {
        var args = OS.GetCmdlineArgs();
        foreach (var arg in args)
            if (arg == "--art-scene") return true;
        return false;
    }

    /// <summary>
    /// --art-scene-capture --capture-out &lt;path&gt;: both required together.
    /// </summary>
    private static bool ReadArtSceneCaptureFlag(out string? outputPath)
    {
        outputPath = null;
        var args = OS.GetCmdlineArgs();
        bool present = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--art-scene-capture") present = true;
            if (args[i] == "--capture-out" && i + 1 < args.Length) outputPath = args[i + 1];
        }
        return present;
    }

    /// <summary>
    /// --capture-width/--capture-height: the resolution.width/height values from
    /// scene_capture_config.yaml, passed through by tools/art_lint/capture_scene.py (the
    /// YAML remains the single source of truth; these flags carry its values into the
    /// engine process, they are not an independent second place to set resolution).
    /// </summary>
    private static bool ReadArtSceneCaptureResolution(out int width, out int height)
    {
        width = height = 0;
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--capture-width") width = int.Parse(args[i + 1]);
            if (args[i] == "--capture-height") height = int.Parse(args[i + 1]);
        }
        return width > 0 && height > 0;
    }

    private static bool ReadDebugOverlayVisible()
    {
        const string settingsPath = "res://config/game_settings.yaml";
        try
        {
            using var file = Godot.FileAccess.Open(settingsPath, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                foreach (var line in file.GetAsText().Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("show_debug_overlay:", System.StringComparison.Ordinal)) continue;
                    var value = trimmed["show_debug_overlay:".Length..].Trim().Trim('"');
                    return value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch { /* silent — default to hidden */ }
        return false;
    }

    /// <summary>
    /// Read the show_prop_inspect setting from game_settings.yaml.
    /// Defaults to true — feature inspection is on unless explicitly disabled.
    /// </summary>
    private static bool ReadShowPropInspect()
    {
        const string settingsPath = "res://config/game_settings.yaml";
        try
        {
            using var file = Godot.FileAccess.Open(settingsPath, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                foreach (var line in file.GetAsText().Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("show_prop_inspect:", System.StringComparison.Ordinal)) continue;
                    var value = trimmed["show_prop_inspect:".Length..].Trim().Trim('"');
                    return value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch { /* silent — default to true */ }
        return true;
    }

    /// <summary>
    /// Create the IMapRenderer for the given mode string.
    /// "topdown" is the default active renderer using 16bf world tiles (24x24).
    /// "iso" preserves the legacy isometric path — can be reactivated via game_settings.yaml.
    /// Unknown values fall back to TopDownRenderer with a warning.
    /// </summary>
    private static IMapRenderer CreateRenderer(string mode)
    {
        var (tileSize, scale) = ReadTileParams();
        return mode.ToLowerInvariant() switch
        {
            "iso" => new IsometricRenderer(),
            "topdown" => new TopDownRenderer(tileSize, scale),
            _ => FallbackRenderer(mode),
        };

        IMapRenderer FallbackRenderer(string value)
        {
            GD.PrintErr($"[Main] map_mode '{value}' is unknown — falling back to topdown.");
            return new TopDownRenderer(tileSize, scale);
        }
    }

    /// <summary>
    /// Tile size and integer scale for the top-down renderer, from --tile-size / --tile-scale.
    ///
    /// Unlike the light rig, these DO fall back to the renderer's own defaults when absent, and
    /// the difference is deliberate. The light values have no defensible default at all
    /// (ART-BIBLE-v0 §6.2 marks them PLACEHOLDER), so <see cref="ReadLightParams"/> refuses to
    /// invent one and throws. Tile size has an incumbent: the value the shipped game has always
    /// drawn at. Refusing to default it would make every ordinary launch require a flag, which
    /// is a far larger change than this one and not what the harness needs.
    ///
    /// The value IS reported at startup either way, so no capture can circulate without naming
    /// the grid it was drawn on — which is the actual failure this closes.
    /// </summary>
    private static (int tileSize, float scale) ReadTileParams()
    {
        int tileSize = TopDownRenderer.DefaultTileSize;
        float scale = TopDownRenderer.DefaultScale;

        // The review-build marker supplies the grid where there is no command line to read —
        // an iOS app receives none. Without this a device build would draw at the default 24
        // while the desktop captures it exists to be compared against were taken at 32, and
        // nothing would have reported the mismatch. CLI flags still win, exactly as they do
        // for the light rig, so a desktop run can override a marker.
        if (ReviewBuildMarker.TryLoad(out var marker) && marker != null)
        {
            if (marker.TileSize is int ms && ms > 0) tileSize = ms;
            if (marker.TileScale is float msc && msc > 0f) scale = msc;
        }

        var rawSize = ReadStringArg("--tile-size");
        if (!string.IsNullOrEmpty(rawSize))
        {
            if (int.TryParse(rawSize, System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out int parsedSize)
                && parsedSize > 0)
                tileSize = parsedSize;
            else
                GD.PrintErr($"[Main] --tile-size '{rawSize}' is not a positive integer — "
                            + $"using {tileSize}.");
        }

        var rawScale = ReadStringArg("--tile-scale");
        if (!string.IsNullOrEmpty(rawScale))
        {
            if (float.TryParse(rawScale, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture,
                               out float parsedScale)
                && parsedScale > 0f)
                scale = parsedScale;
            else
                GD.PrintErr($"[Main] --tile-scale '{rawScale}' is not a positive number — "
                            + $"using {scale}.");
        }

        return (tileSize, scale);
    }

    /// <summary>
    /// Create all content factories and build the level template registry.
    /// Called once from _Ready. Subsequent floor transitions reuse these objects.
    /// </summary>
    private void InitFactories()
    {
        // Read YAML via Godot's FileAccess — on iOS, res:// files are packed inside the
        // .pck bundle and cannot be read via System.IO.File. FileAccess handles this
        // transparently across all platforms.
        var entitiesYaml = ReadGodotResource("res://config/entities.yaml");
        var levelTemplatesYaml = ReadGodotResource("res://config/level_templates.yaml");

        _contentLoader = new ContentLoader();
        ContentBundle content;
        try
        {
            content = _contentLoader.LoadAll(entitiesYaml);
            GD.Print($"Content loaded: {content.Monsters.Count} monsters, {content.Items.Count} items, {content.Consumables.Count} consumables");
            foreach (var (key, mon) in content.Monsters)
                GD.Print($"  Monster: {key} → {mon.Name ?? "(null)"}, hp={mon.Stats?.Hp ?? -1}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"YAML entities deserialization failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                GD.PrintErr($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            GD.PrintErr($"  Stack: {ex.StackTrace}");
            throw;
        }

        _entityFactory = new EntityFactory();
        _itemFactory = new ItemFactory(content.Items, _entityFactory);
        _monsterFactory = new MonsterFactory(content.Monsters, _entityFactory, _itemFactory);
        _consumableFactory = new ConsumableFactory(content.Consumables, _entityFactory);
        _spellItemFactory = new SpellItemFactory(content.SpellItems, _entityFactory);

        try
        {
            _levelTemplates = LevelTemplateRegistry.FromYaml(levelTemplatesYaml);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"YAML level_templates deserialization failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                GD.PrintErr($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            GD.PrintErr($"  Stack: {ex.StackTrace}");
            throw;
        }
        // Load depth boons (optional — missing file means no boons, not a crash)
        Dictionary<int, UnderWarden.Logic.Balance.BoonDefinition>? boonTable = null;
        try
        {
            var boonYaml = ReadGodotResource("res://config/depth_boons.yaml");
            boonTable = _contentLoader.LoadBoons(boonYaml);
            _boonTable = boonTable;   // kept for LoadMidRun on resume (RECONSTRUCT-class, from config)
            GD.Print($"Depth boons loaded: {boonTable.Count} entries");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Depth boons load failed (non-fatal): {ex.Message}");
        }

        PropRegistry? propRegistry = null;
        string? propsYamlForDescriptions = null;
        try
        {
            var propsYaml = ReadGodotResource("res://config/props.yaml");
            propsYamlForDescriptions = propsYaml;
            propRegistry = _contentLoader.LoadProps(propsYaml);
            GD.Print($"Props loaded: {propRegistry.All.Count} prop definitions");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Props load failed (non-fatal — no props will appear): {ex.Message}");
        }

        // Load prop description registry for long-press inspect panel.
        // Both YAMLs are read here; the registry's static ctor already seeded tile-feature entries.
        // Non-fatal: if either file is missing, tile-based feature descriptions still work.
        try
        {
            var interactivePropsYaml = ReadGodotResource("res://config/interactive_props.yaml");
            UnderWarden.Logic.Content.PropDescriptionRegistry.Load(
                propsYamlForDescriptions ?? "",
                interactivePropsYaml);
            GD.Print("[Main] PropDescriptionRegistry loaded");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"PropDescriptionRegistry load failed (non-fatal): {ex.Message}");
        }

        UnderWarden.Logic.Content.SignpostMessageRegistry? signpostRegistry = null;
        try
        {
            var signYaml = ReadGodotResource("res://config/signpost_messages.yaml");
            signpostRegistry = UnderWarden.Logic.Content.SignpostMessageRegistry.FromYaml(signYaml);
            GD.Print($"Signpost registry loaded");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Signpost registry load failed (non-fatal — no signs will appear): {ex.Message}");
        }

        UnderWarden.Logic.Content.MuralRegistry? muralRegistry = null;
        try
        {
            var muralYaml = ReadGodotResource("res://config/murals_inscriptions.yaml");
            muralRegistry = UnderWarden.Logic.Content.MuralRegistry.FromYaml(muralYaml);
            GD.Print($"Mural registry loaded: {muralRegistry.Count} entries");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Mural registry load failed (non-fatal — no murals will appear): {ex.Message}");
        }

        UnderWarden.Logic.Content.LootTagRegistry? lootTagRegistry = null;
        try
        {
            var lootTagsYaml = ReadGodotResource("res://config/loot_tags.yaml");
            lootTagRegistry = UnderWarden.Logic.Content.LootTagRegistry.FromYaml(lootTagsYaml);
            GD.Print($"Loot tag registry loaded: {lootTagRegistry.Count} entries");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Loot tag registry load failed (non-fatal — falling back to flat pool): {ex.Message}");
        }

        UnderWarden.Logic.Content.LootPolicyConfig? lootPolicy = null;
        try
        {
            var lootPolicyYaml = ReadGodotResource("res://config/loot_policy.yaml");
            lootPolicy = UnderWarden.Logic.Content.LootPolicyConfig.FromYaml(lootPolicyYaml);
            GD.Print($"Loot policy loaded");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Loot policy load failed (non-fatal — falling back to flat pool): {ex.Message}");
        }

        _floorBuilder = new DungeonFloorBuilder(
            _levelTemplates, _monsterFactory, _itemFactory, _consumableFactory,
            content.FloorItemPool, spellItemFactory: _spellItemFactory,
            boonTable: boonTable, propRegistry: propRegistry,
            signpostRegistry: signpostRegistry, muralRegistry: muralRegistry,
            lootTagRegistry: lootTagRegistry, lootPolicy: lootPolicy);

        // Voice line registries — merged into a single registry at boot.
        try
        {
            var hollowmarkYaml = ReadGodotResource("res://config/voice_lines/hollowmark.yaml");
            var quippingShadeYaml = ReadGodotResource("res://config/voice_lines/quipping_shade.yaml");
            var catalogYaml = ReadGodotResource("res://config/voice_lines/catalog_past_selves.yaml");
            var possessionYaml = ReadGodotResource("res://config/voice_lines/possession.yaml");
            _voiceLineRegistry = VoiceLineRegistry.LoadFromYaml(hollowmarkYaml);
            _voiceLineRegistry.Merge(VoiceLineRegistry.LoadFromYaml(quippingShadeYaml));
            _voiceLineRegistry.Merge(VoiceLineRegistry.LoadFromYaml(catalogYaml));
            _voiceLineRegistry.Merge(VoiceLineRegistry.LoadFromYaml(possessionYaml));
            GD.Print("[Main] Voice line registry loaded.");

            // Voice tier metadata (M1.5, real pools). The ribbon delivers Hollowmark's OWN voice, so its
            // registry is hollowmark.yaml only — a fresh parse, not the merged _voiceLineRegistry, which
            // also holds the possession/catalog/shade pools other systems own. The tiers resolve each
            // hollowmark pool key by family prefix (cross-validated headlessly).
            _voiceRibbonRegistry = VoiceLineRegistry.LoadFromYaml(hollowmarkYaml);
            _voiceTierMeta = Logic.Voice.VoiceTierMetadata.LoadFromYaml(
                ReadGodotResource("res://config/voice_lines/voice_tiers.yaml"));
            GD.Print($"[Main] Voice tiers loaded — {_voiceTierMeta.Families.Count} families; ribbon uses hollowmark.yaml pools.");

            // Device voice settings (mode + duration) — survive across runs, never in the run save.
            _voiceSettings = Presentation.Persistence.VoiceSettingsStore.Load();

            var weighingAuditYaml = ReadGodotResource("res://config/voice_lines/weighing_audit.yaml");
            _weighingAuditRegistry = Logic.Content.WeighingAuditRegistry.LoadFromYaml(weighingAuditYaml);
            GD.Print("[Main] Weighing audit registry loaded.");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Main] Voice line registry load failed (non-fatal): {ex.Message}");
        }

        // Under-Warden memo registry — loaded once at boot alongside voice lines.
        // Non-fatal: inbox UI silently stays disabled if files are missing.
        try
        {
            var memosYaml = ReadGodotResource("res://config/under_warden/memos.yaml");
            var causeNamesYaml = ReadGodotResource("res://config/under_warden/cause_display_names.yaml");
            _memoRegistry = MemoRegistry.LoadFromYaml(memosYaml, causeNamesYaml);
            GD.Print("[Main] Memo registry loaded.");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Main] Memo registry load failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Load a scenario YAML and start the game. Existing path — used for dev/harness work.
    /// Keeps working as before; factories are now pre-created by InitFactories().
    /// </summary>
    private void LoadAndStart()
    {
        var scenarioYaml = ReadGodotResource("res://config/levels/scenario_depth1_tuned.yaml");
        var scenario = _contentLoader!.LoadScenario(scenarioYaml);
        _state = GameStateFactory.FromScenario(
            scenario, _baseSeed, _monsterFactory!, _itemFactory!, _consumableFactory!);

        SetupPresentation(_state);
        GD.Print($"Ready (scenario) — {_state.Monsters.Count} monsters. Tap to play.");
    }

    /// <summary>
    /// Procedural dungeon entry point. Builds a fresh floor at the given depth.
    /// Pass existingPlayer=null for a new run; pass _state.Player to carry the player forward.
    /// explorationMode=true spawns no monsters — useful for visually inspecting generated floors.
    /// </summary>
    public void StartDungeon(int depth = 1, Entity? existingPlayer = null, bool explorationMode = false)
    {
        _testScenarioBuilder = null; // clear any test scenario override
        _currentDepth = depth;
        var rng = new SeededRandom(_baseSeed + depth * 1_000_003);
        _state = _floorBuilder!.Build(depth, rng, existingPlayer, explorationMode: explorationMode,
            persistentState: _persistentState);

        // Increment run counter at the start of a real run (depth 1, not explore mode).
        // Explore mode is a visual tool, not a campaign run — don't count it.
        if (depth == 1 && !explorationMode && _persistentState != null && _persistenceProvider != null)
        {
            _persistentState.RunCounter.IncrementRunCount();
            _persistentState.MarkDirty();
            _persistentState.Flush(_persistenceProvider, GD.PrintErr);
        }

        // Reset per-run voice line first-fire set on every new run (depth 1).
        if (depth == 1)
            _voiceLineFiredSet.Clear();

        // Voice scheduler (M1.5b): fresh on a new run (depth 1), carried across floors otherwise.
        // Dungeon-mode only — this path is never taken by scenario/harness. New run also re-arms the
        // edge-triggered trigger reader.
        if (depth == 1 && _voiceRibbonRegistry != null && _voiceTierMeta != null)
        {
            _voiceScheduler = new Logic.Voice.VoiceScheduler(_voiceRibbonRegistry, _voiceTierMeta, _baseSeed);
            _voiceTriggerReader.Reset();
        }
        _voiceScheduler?.OnFloorEntered();      // clear the floor-silence flag on entering any floor
        _state.VoiceScheduler = _voiceScheduler; // attach so mid-run saves serialize it

        SetupPresentation(_state);
        GD.Print($"Ready (dungeon depth {depth}) — {_state.Monsters.Count} monsters, {_state.FloorItems.Count} floor items. Tap to play.");
    }

    /// <summary>
    /// Shared presentation setup used by both LoadAndStart and StartDungeon.
    /// Tears down any existing presentation nodes, renders the new state, and wires up GameController.
    /// </summary>
    private void SetupPresentation(GameState state)
    {
        // Reset per-run stats
        _turnCount = 0;
        _monstersKilled = 0;
        _damageDealt = 0;
        _damageTaken = 0;

        // Get scene nodes
        _gameView = GetNode<Node2D>("GameView");
        var tileMapLayer = GetNode<Node2D>("GameView/TileMapLayer");
        _tileMapLayer = tileMapLayer;
        var entityLayer = GetNode<Node2D>("GameView/EntityLayer");
        var vfxLayerNode = GetNode<Node2D>("GameView/VfxLayer");
        var uiLayer = GetNode<CanvasLayer>("UILayer");
        var hudNode = GetNode<Control>("UILayer/StatusBar");
        var toastLogNode = GetNode<Control>("UILayer/ToastLog");
        var inventoryPanelNode = GetNode<Control>("UILayer/QuickSlotBar");
        var equipmentPanelNode = GetNode<Control>("UILayer/EquipmentPanel");

        // Phase 1 zone containers — content added in later phases.
        var quickSlotZone   = inventoryPanelNode;  // UILayer/QuickSlotBar
        var menuButtonsZone = GetNode<Control>("UILayer/MenuButtons");
        var bottomSafeArea  = GetNode<Control>("UILayer/BottomSafeArea");

        // These nodes overlay the dungeon — must not block taps on the game view.
        toastLogNode.MouseFilter      = Control.MouseFilterEnum.Ignore;
        equipmentPanelNode.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Clear any previous render — RemoveChild before QueueFree so ghost nodes
        // don't linger in layout containers until end-of-frame.
        foreach (var node in new Node[] { tileMapLayer, entityLayer, hudNode, toastLogNode, inventoryPanelNode, equipmentPanelNode, quickSlotZone, menuButtonsZone, bottomSafeArea })
            foreach (var child in node.GetChildren())
                child.SafeFree();

        // Tap indicators are children of EntityLayer — they were just freed above.
        // Clear the list so _Process doesn't try to fade/free already-disposed nodes.
        _tapIndicators.Clear();

        // Phase 1 placeholder backgrounds — make zones visible during development.
        // QuickSlotBar placeholder replaced by real QuickSlotBar in Phase 3.
        AddZonePlaceholder(quickSlotZone,  new Color(0.08f, 0.08f, 0.12f, 0.92f)); // dark blue-grey
        AddZonePlaceholder(bottomSafeArea, new Color(0.00f, 0.00f, 0.00f, 1.00f)); // solid black
        // MenuButtons zone: populated below by MenuButtonBar (Phase 2 — no placeholder needed).

        // Render dungeon (stair overlays handled inside DungeonRenderer — second pass)
        // Returns TileLayer so we can apply fog-of-war each turn without re-creating nodes.
        // TileThemeConfig is loaded once at boot and reused across all floor transitions.
        // Pass props from GameState so Pass 4 renders placed furniture/overlays.
        // Props is empty in scenario mode (IsDungeonMode=false) — no regression there.
        _tileLayer = DungeonRenderer.Render(state.Map, tileMapLayer, _renderer, _tileThemeConfig,
            props: state.Props, features: state.Features, lockedDoors: state.LockedDoors);

        // Entity sprites — store reference so OnTurnCompleted can call UpdateVisibility
        _entitySprites = new EntitySpriteManager(entityLayer, _spriteMapping!, _renderer);
        _entitySprites.Initialize(state);

        // Item sprites — floor items rendered as tinted overlay sprites on entityLayer.
        // TileThemeConfig is passed so key items can resolve the world_24x24 key sprite
        // (tile 5039) directly, bypassing the items_16x16 tileset lookup.
        _itemSprites = new ItemSpriteManager(entityLayer, _spriteMapping!, _renderer, _tileThemeConfig);
        _itemSprites.Initialize(state);

        // Corpse sprites — remains of dead monsters, rendered under live entities on the same
        // entityLayer. Reconciled against state.Corpses each turn (Sync); FOV-only like items.
        // On resume, Initialize rebuilds every corpse the run is carrying on this floor.
        _corpseSprites = new CorpseSpriteManager(entityLayer, _spriteMapping!, _renderer);
        _corpseSprites.Initialize(state);

        // HUD
        _hud = new HUD();
        hudNode.AddChild(_hud);
        _hud.SetState(state);

        // Menu button bar (Phase 2) — Gear and Explore buttons in the MenuButtons zone.
        // Replaces the temporary Phase 1 placeholder for that zone.
        _menuButtonBar = new MenuButtonBar();
        menuButtonsZone.AddChild(_menuButtonBar);
        _menuButtonBar.GearRequested    += OnGearRequested;
        _menuButtonBar.ExploreRequested += () => _gameController?.StartAutoExplore();
        _menuButtonBar.PossessRequested += () => _gameController?.StartPossessionTargeting();
        _menuButtonBar.ExitPossessionRequested += () => _gameController?.ExitPossessionAction();
        _menuButtonBar.CancelPossessionTargetingRequested += () => _gameController?.CancelPossessionTargeting();
        _menuButtonBar.MenuRequested    += () => ShowMainMenu();

        // Quick-slot bar (Phase 3) — scrollable consumable/wand strip + weapon indicator.
        // Replaces InventoryPanel. Drop goes through long-press → action sheet.
        _inventoryPanel = new QuickSlotBar();
        _inventoryPanel.SpriteMappingInstance = _spriteMapping;
        inventoryPanelNode.AddChild(_inventoryPanel);
        _inventoryPanel.Initialize(state);
        _inventoryPanel.ItemTapped     += OnInventoryItemTapped;
        _inventoryPanel.WeaponTapped        += () => _toastLog?.AddMessage("Ranged toggle coming soon");
        _inventoryPanel.WeaponLongPressed   += () =>
        {
            var weapon = _state?.Player.Get<Equipment>()?.MainHand;
            if (weapon != null)
                _gameController?.HandleInventoryLongPress(weapon.Id);
        };
        _rectDebugDraw?.SetQuickSlotBar(_inventoryPanel);

        // Equipment panel — full-screen overlay, starts hidden.
        _equipmentPanel = new EquipmentPanel();
        _equipmentPanel.SpriteMappingInstance = _spriteMapping;
        equipmentPanelNode.AddChild(_equipmentPanel);
        _equipmentPanel.EquipRequested     += itemId => _gameController?.HandleEquipRequest(itemId);
        _equipmentPanel.UnequipRequested   += slot   => _gameController?.HandleUnequipRequest(slot);
        _equipmentPanel.ItemDropRequested  += itemId => _gameController?.HandleDropRequest(itemId);

        // Combat log
        _toastLog = new ToastLog();
        toastLogNode.AddChild(_toastLog);
        _toastLog.SetPlayerId(state.Player.Id);

        // Game over screen (reused across floor transitions — create once, hide/show)
        if (_gameOverScreen == null)
        {
            _gameOverScreen = new GameOverScreen();
            uiLayer.AddChild(_gameOverScreen);
            _gameOverScreen.ReplayRequested += OnReplayRequested;
        }
        else
        {
            _gameOverScreen.Visible = false;
        }

        // Memo inbox panel (created once, shown after run ends when memos are pending)
        if (_memoInboxPanel == null)
        {
            _memoInboxPanel = new MemoInboxPanel();
            uiLayer.AddChild(_memoInboxPanel);
            _memoInboxPanel.InboxClosed += OnInboxClosed;
        }
        // Hide on EVERY setup, not just first creation (mirrors _gameOverScreen above). A modal left
        // visible from a prior run would keep IsRibbonSurfaceAvailable false for the whole next run,
        // silencing the ribbon — the device regression where voice never fired for a session.
        _memoInboxPanel.Visible = false;

        PlayerCamera.Update(_gameView!, state.ControlledEntity, _currentZoom, _renderer);

        // Minimap: create once, reuse across floors (just call Refresh on each floor).
        if (_miniMap == null)
        {
            _miniMap = new MiniMap();
            _miniMap.MouseFilter = Control.MouseFilterEnum.Ignore;
            // Anchor to top-right of ViewportOverlay — consistent with MsgButton/StatusEffectBar
            // pattern. ViewportOverlay's top edge IS the StatusBar's bottom edge, so an 8px
            // OffsetTop here naturally tracks any future StatusBar height change.
            _miniMap.AnchorLeft   = 1f;
            _miniMap.AnchorTop    = 0f;
            _miniMap.AnchorRight  = 1f;
            _miniMap.AnchorBottom = 0f;
            GetNode<Control>("UILayer/ViewportOverlay").AddChild(_miniMap);

            // Zoom buttons: small +/− panel anchored to the left of the minimap area.
            // Also parented to ViewportOverlay for consistency.
            var zoomPanel = BuildZoomPanel();
            zoomPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
            GetNode<Control>("UILayer/ViewportOverlay").AddChild(zoomPanel);
        }
        _miniMap.OffsetLeft   = -state.Map.Width  * 2 - 8;
        _miniMap.OffsetTop    = 8f;  // 8px gap below ViewportOverlay top (= StatusBar bottom)
        _miniMap.OffsetRight  = -8f;
        _miniMap.OffsetBottom = 8f + state.Map.Height * 2;
        _miniMap.Refresh(state);

        // Apply initial fog-of-war so the floor renders correctly from turn 0.
        // In dungeon mode: DungeonFloorBuilder.Build called RecomputeFov — player sees start area.
        // In scenario mode: map.RevealAll was called — all tiles visible.
        if (_tileLayer != null)
            DungeonRenderer.UpdateVisibility(_tileLayer, state.Map);
        _entitySprites?.UpdateVisibility(state);
        _entitySprites?.UpdateStatusTints(state);
        _itemSprites?.UpdateVisibility(state);
        _corpseSprites?.Sync(state);
        _corpseSprites?.UpdateVisibility(state);
        // Initial HP bar pass — shows bars for any pre-damaged monsters at floor start.
        if (_entitySprites != null)
            _floatingHpBars?.Refresh(state, _entitySprites);

        // Game controller — free old one if it exists
        // Clear portal sprites on floor setup (new floor = no active portals)
        foreach (var sprite in _portalSprites.Values) sprite.QueueFree();
        _portalSprites.Clear();

        // Spawn sprites for any pre-placed static portals (from DungeonFloorBuilder.PlacePortalPairs).
        // Wand-placed portals arrive via PortalPlacedEvent during the turn loop; static portals are
        // placed at floor build time before the first turn, so we seed sprites here instead.
        foreach (var portal in state.Portals)
        {
            var comp = portal.Get<UnderWarden.Logic.Combat.PortalComponent>();
            if (comp != null)
                SpawnPortalSprite(portal.Id, portal.X, portal.Y, comp.Type);
        }

        // VFX overlay — create once per floor. ClearAll hides any lingering pooled nodes
        // from the previous floor before we create a fresh overlay for the new one.
        _vfxOverlay?.ClearAll();
        _vfxOverlay = new VfxOverlay(vfxLayerNode, _renderer);

        // Ground hazard overlay — persistent tile tints for burning/poison ground.
        // Clear previous floor's nodes, then create fresh for this floor.
        _groundHazardOverlay?.Clear();
        _groundHazardOverlay = new GroundHazardOverlay(vfxLayerNode, _renderer);

        // Floating HP bars — small red bars above damaged enemy sprites.
        // Clear old bars before entityLayer children are freed (child-free loop already ran above),
        // then create a fresh manager pointing at the new entityLayer.
        _floatingHpBars?.Clear();
        _floatingHpBars = new FloatingHpBarManager(entityLayer, _renderer);

        if (_gameController != null)
        {
            Diag.Log($"SetupPresentation: disposing old GameController, phase={_gameController.Phase}");
            _gameController.TurnCompleted -= OnTurnCompleted;
            _gameController.GameEnded -= OnGameEnded;
            _gameController.FloorTransitionRequested -= OnFloorTransitionRequested;
            _gameController.PortalEntranceCancelled -= OnPortalEntranceCancelled;
            _gameController.SafeFree();
        }
        _gameController = new GameController();
        AddChild(_gameController);
        _gameController.Initialize(state, _entitySprites!, this, _itemSprites, _inventoryPanel,
            _equipmentPanel, _toastLog, _monsterFactory, _renderer, _gameView, _entityFactory,
            _vfxOverlay, showPropInspect: ReadShowPropInspect(), corpseSprites: _corpseSprites);
        _gameController.TurnCompleted += OnTurnCompleted;
        _gameController.GameEnded += OnGameEnded;
        _gameController.FloorTransitionRequested += OnFloorTransitionRequested;
        _gameController.PortalEntranceCancelled += OnPortalEntranceCancelled;

        // Bot driver — debug builds only. Re-initialize across floor transitions.
        // Release builds: _botDriver is never assigned (BotPlayerDriver is never instantiated).
        if (OS.IsDebugBuild())
        {
            if (_botDriver == null)
            {
                _botDriver = new UnderWarden.Presentation.Bot.BotPlayerDriver();
                AddChild(_botDriver);

                _botHud = new UnderWarden.Presentation.Bot.BotModeHud();
                GetNode<CanvasLayer>("UILayer").AddChild(_botHud);
                _botHud.Initialize(_botDriver);
            }
            _botDriver.Initialize(_gameController, state);
        }

        // Message log panel — Phase 6.2. Created once, lives at UILayer root (same level as
        // EquipmentPanel) so it sits above everything else when visible. Starts hidden.
        if (_messageLogPanel == null)
        {
            _messageLogPanel = new MessageLogPanel();
            GetNode<CanvasLayer>("UILayer").AddChild(_messageLogPanel);
        }
        _messageLogPanel.Visible = false;   // clean slate each run — see the memo-inbox note above

        // Hollowmark ribbon (M1.5b) — its own Control, created once, top band clear of touch controls.
        // History anchor renders the scheduler's serialized last-20; quiet button mutes the current floor.
        if (_voiceRibbon == null)
        {
            _voiceRibbon = new UI.VoiceRibbon();
            GetNode<CanvasLayer>("UILayer").AddChild(_voiceRibbon);
            _voiceRibbon.HistoryRequested += () =>
                _voiceRibbon.ToggleHistory(_voiceScheduler?.HistorySnapshot()
                    ?? System.Array.Empty<Logic.Voice.VoiceHistoryEntry>());
            _voiceRibbon.QuietRequested += () => _voiceScheduler?.SilenceCurrentFloor();
        }

        // Msg button — Phase 5. Created once and parented to the ViewportOverlay zone.
        // The Pressed lambda captures `this` (Main), resolving _toastLog/_messageLogPanel at
        // call time so they always refer to the current floor's objects after floor transitions.
        if (_msgButton == null)
        {
            _msgButton = new MsgButton();
            GetNode<Control>("UILayer/ViewportOverlay").AddChild(_msgButton);
            _msgButton.Pressed += () => _messageLogPanel?.Open(_toastLog?.History ?? System.Array.Empty<string>());
        }

        // Status effect bar — Phase 5. Created once, lives in ViewportOverlay just below
        // the StatusBar zone. The ViewportOverlay's top edge IS the StatusBar's bottom edge,
        // so a small offset (8px left, 4px top) positions the badge row flush with the bar.
        // The control hides itself when no effects are active — no viewport clutter.
        if (_statusEffectBar == null)
        {
            _statusEffectBar = new StatusEffectBar();
            _statusEffectBar.CustomMinimumSize = new Vector2(0, 24);
            _statusEffectBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _statusEffectBar.MouseFilter = Control.MouseFilterEnum.Ignore;
            // Anchor to top-left of ViewportOverlay with a small margin from the zone edges.
            _statusEffectBar.AnchorLeft   = 0f;
            _statusEffectBar.AnchorTop    = 0f;
            _statusEffectBar.AnchorRight  = 1f;
            _statusEffectBar.AnchorBottom = 0f;
            _statusEffectBar.OffsetLeft   = 8f;
            _statusEffectBar.OffsetTop    = 4f;
            _statusEffectBar.OffsetRight  = 0f;
            _statusEffectBar.OffsetBottom = 28f; // top offset + height (4 + 24)
            GetNode<Control>("UILayer/ViewportOverlay").AddChild(_statusEffectBar);
        }
        // Refresh immediately so any pre-existing effects show on floor entry.
        _statusEffectBar.Refresh(state.Player);

        // Wire HUD buttons to GameController / EquipmentPanel.
        // Phase 2: Gear/Explore wired via MenuButtonBar (created above).
        // Phase 5: Msg button wired above (MsgButton in ViewportOverlay).

        // Debug overlay — update references each floor so it reflects the new state.
        // No-op in release builds (_debugOverlay is null).
        _debugOverlay?.SetGameState(_gameController, state, _entitySprites, _itemSprites, _toastLog);

        // Queue 4 frames of camera snaps. The first frame handles the immediate stale-viewport
        // case; frames 2–4 handle slower layout settling after MenuLayer hides. Each snap is
        // cheap (one Position/Scale assignment), so running 4 times is safe.
        _pendingCameraSnapFrames = 4;
    }

    // Set > 0 at end of SetupPresentation; decremented each frame until 0.
    // Retrying multiple frames handles the case where GetVisibleRect() reports a plausible
    // but stale size on the first frame after the MenuLayer hides (layout hasn't settled yet).
    private int _pendingCameraSnapFrames;
    private bool _pendingCameraSnap => _pendingCameraSnapFrames > 0;

    private void _DoInitialCameraSnap()
    {
        if (_state == null || _gameView == null) return;
        var viewSize = _gameView.GetViewport().GetVisibleRect().Size;
        if (viewSize.X < 100 || viewSize.Y < 100)
        {
            // Viewport not ready yet — keep retrying.
            _pendingCameraSnapFrames = System.Math.Max(_pendingCameraSnapFrames, 1);
            return;
        }
        PlayerCamera.Update(_gameView, _state.ControlledEntity, _currentZoom, _renderer);
        if (_tileLayer != null)
            DungeonRenderer.UpdateVisibility(_tileLayer, _state.Map);
        _entitySprites?.UpdateVisibility(_state);
        _entitySprites?.UpdateStatusTints(_state);
        _itemSprites?.UpdateVisibility(_state);
        _corpseSprites?.Sync(_state);
        _corpseSprites?.UpdateVisibility(_state);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_gameController == null || _gameView == null) return;

        // --- Touch (mobile) ---
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                _dragStartScreenPos = touch.Position;
                _cameraPositionAtDragStart = _gameView.Position;
                _isDragging = false;
                _dragStartRecorded = true;
            }
            else
            {
                // Only fire if _UnhandledInput saw the matching DOWN. If a UI button
                // consumed the DOWN via AcceptEvent(), _dragStartRecorded stays false
                // and the orphaned UP is silently ignored.
                bool fingerMoved = (touch.Position - _dragStartScreenPos).Length() > DragThreshold;
                if (_dragStartRecorded && !_isDragging && !fingerMoved)
                {
                    Diag.Log($"_UnhandledInput tap at {_dragStartScreenPos}, phase={_gameController.Phase}");
                    var localPos = _gameView.ToLocal(_dragStartScreenPos);
                    SpawnTapIndicator(localPos);
                    _gameController.HandleTap(localPos);
                }
                _isDragging = false;
                _dragStartRecorded = false;
            }
            return;
        }

        if (@event is InputEventScreenDrag screenDrag)
        {
            var delta = screenDrag.Position - _dragStartScreenPos;
            if (!_isDragging && delta.Length() > DragThreshold)
            {
                _isDragging = true;
                PlayerCamera.CancelTween();
            }
            if (_isDragging)
                _gameView.Position = _cameraPositionAtDragStart + delta;
            return;
        }

        // --- Mouse (desktop) ---
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _dragStartScreenPos = mb.Position;
                _cameraPositionAtDragStart = _gameView.Position;
                _isDragging = false;
                _dragStartRecorded = true;
            }
            else
            {
                bool fingerMoved = (mb.Position - _dragStartScreenPos).Length() > DragThreshold;
                if (_dragStartRecorded && !_isDragging && !fingerMoved)
                {
                    Diag.Log($"_UnhandledInput tap at {_dragStartScreenPos}, phase={_gameController.Phase}");
                    var localPos = _gameView.ToLocal(_dragStartScreenPos);
                    SpawnTapIndicator(localPos);
                    _gameController.HandleTap(localPos);
                }
                _isDragging = false;
                _dragStartRecorded = false;
            }
            return;
        }

        if (@event is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            var delta = motion.Position - _dragStartScreenPos;
            if (!_isDragging && delta.Length() > DragThreshold)
            {
                _isDragging = true;
                PlayerCamera.CancelTween();
            }
            if (_isDragging)
                _gameView.Position = _cameraPositionAtDragStart + delta;
            return;
        }

        if (OS.IsDebugBuild() && @event is InputEventKey key && key.Pressed && key.Keycode == Key.F3)
        {
            if (_rectDebugDraw != null) _rectDebugDraw.Visible = !_rectDebugDraw.Visible;
        }

        // ── Bot mode hotkeys (debug builds only) ──────────────────────────────
        if (OS.IsDebugBuild() && _botDriver != null && @event is InputEventKey botKey && botKey.Pressed)
        {
            switch (botKey.Keycode)
            {
                case Key.F4:
                    // Toggle bot mode
                    if (_botDriver.Enabled) _botDriver.Disable();
                    else                    _botDriver.Enable();
                    _botHud?.RefreshDisplay();
                    break;

                case Key.F5 when _botDriver.Enabled:
                    // Cycle persona: balanced → cautious → aggressive → greedy → speedrunner → balanced
                    _botPersonaIdx = (_botPersonaIdx + 1) % BotPersonaCycle.Length;
                    _botDriver.SetPersona(BotPersonaCycle[_botPersonaIdx]);
                    _botHud?.RefreshDisplay();
                    Diag.Log($"[Bot] Persona → {BotPersonaCycle[_botPersonaIdx]}");
                    break;

                case Key.F6 when _botDriver.Enabled:
                    // Cycle speed: 1.0 → 0.5 → 0.25 → 0.1 → 0.0 (max) → 1.0
                    _botSpeedIdx = (_botSpeedIdx + 1) % BotSpeedCycle.Length;
                    _botDriver.TurnDelaySeconds = BotSpeedCycle[_botSpeedIdx];
                    _botHud?.RefreshDisplay();
                    Diag.Log($"[Bot] Speed → {BotSpeedCycle[_botSpeedIdx]}s/turn");
                    break;
            }
        }
    }

    private void SpawnTapIndicator(Vector2 localPos)
    {
        if (_gameView == null) return;
        var (gridX, gridY) = _renderer.ScreenToGrid(localPos);

        // Top-left of the tile — ColorRect is not centered, so position at grid origin.
        var tileOrigin = _renderer.GridToScreen(gridX, gridY);

        // Programmatic colored square — no sprite asset needed. Warm yellow-white at low
        // opacity so the highlight is visible without obscuring the tile underneath.
        var rect = new ColorRect
        {
            Size     = new Vector2(_renderer.TileWidth, _renderer.TileHeight),
            Color    = new Color(1f, 1f, 0.7f, 0.3f),
            Position = tileOrigin,
            ZIndex   = _renderer.GetTileSortOrder(gridX, gridY) + 1,
        };
        _gameView.GetNode<Node2D>("EntityLayer").AddChild(rect);

        // No Tween — alpha is lerped in _Process. Avoids a Tween holding a reference
        // to the ColorRect after _Process SafeFree's it (use-after-free on the tween's
        // PropertyTweener when Godot later processes or frees the stopped tween).
        double now = Time.GetTicksMsec() / 1000.0;
        _tapIndicators.Add((rect, now));
    }

    private Texture2D? _portalTexture;

    private void SpawnPortalSprite(int entityId, int gridX, int gridY, PortalType type)
    {
        if (_gameView == null) return;

        // Cyan for entrance, orange for exit — visually distinct at a glance
        var color = type == PortalType.Entrance
            ? new Color(0f, 0.85f, 1f, 0.9f)
            : new Color(1f, 0.5f, 0f, 0.9f);

        _portalTexture ??= GD.Load<Texture2D>(
            "res://src/Presentation/assets/sprites_16bf/fx_32x32/oryx_16bit_fantasy_fx_05.png");

        var sprite = new Sprite2D();
        sprite.Texture = _portalTexture;
        sprite.Position = _renderer.GridToScreenCenter(gridX, gridY);
        sprite.Modulate = color;
        sprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        // Scale: fx sprites are 32px native. 1.0× matches the 32px iso tile width — large
        // enough to be clearly visible without spilling over adjacent tiles at zoom=4.
        sprite.Scale = new Vector2(1.0f, 1.0f);
        // ZIndex: use the renderer's tile sort order so portals appear above the floor tile at
        // this grid position regardless of renderer mode (iso uses gridX+gridY, top-down uses gridY).
        // +1 places portals above tiles but below entities (entities use GetEntitySortOrder = tile+1).
        sprite.ZIndex = _renderer.GetTileSortOrder(gridX, gridY) + 1;
        _gameView.GetNode<Node2D>("EntityLayer").AddChild(sprite);
        _portalSprites[entityId] = sprite;
    }

    private void DespawnPortalSprite(int entityId)
    {
        if (_portalSprites.TryGetValue(entityId, out var sprite))
        {
            sprite.QueueFree();
            _portalSprites.Remove(entityId);
        }
    }

    private void OnPortalEntranceCancelled(int entranceEntityId)
    {
        DespawnPortalSprite(entranceEntityId);
    }

    private void SwapDoorSprite(int x, int y)
    {
        if (_tileLayer == null || _tileThemeConfig == null || _state == null) return;
        if (!_tileLayer.DoorOverlaySprites.TryGetValue((x, y), out var sprite)) return;

        var theme = _state.Map.GetTileTheme(x, y);
        string themeName = DungeonRenderer.ThemeToConfigName(theme);
        var openPath = _tileThemeConfig.GetDoorOpen(themeName);
        if (openPath == null) return;

        var tex = ResourceLoader.Load<Texture2D>(openPath);
        if (tex != null && sprite is Sprite2D s2d)
            s2d.Texture = tex;
    }

    /// <summary>
    /// Swap the chest sprite at the given cell to the open or empty state texture.
    /// Mirrors the SwapDoorSprite pattern — update existing sprite rather than recreating it.
    /// </summary>
    private void SwapChestSprite(int x, int y, bool looted)
    {
        if (_tileLayer == null || _tileThemeConfig == null || _state == null) return;
        if (!_tileLayer.FeatureOverlaySprites.TryGetValue((x, y), out var sprite)) return;

        var themeName = DungeonRenderer.ThemeToConfigName(_state.Map.GetTileTheme(x, y));
        int tileId = looted ? _tileThemeConfig.GetChestEmpty(themeName) : _tileThemeConfig.GetChestOpen(themeName);
        if (tileId == 0) return;
        var path = _tileThemeConfig.GetTexturePath(tileId);
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex != null)
        {
            sprite.Texture = tex;
            sprite.Modulate = Colors.White; // clear any lock tint
        }
    }

    /// <summary>
    /// Handle a locked door being unlocked. The tile has already changed to DoorOpen in the
    /// logic layer (TurnController.TryHandleLockedDoorBump). Here we:
    ///   - Swap the door sprite from locked (tile 203) to open (tile 202).
    ///   - Remove the key icon overlay that was showing the lock color.
    ///
    /// The DoorOverlaySprites entry is reused (same position, new texture).
    /// </summary>
    private void HandleDoorUnlocked(int x, int y)
    {
        if (_tileLayer == null || _tileThemeConfig == null || _state == null) return;

        // Remove key icon overlay for the door.
        if (_tileLayer.LockKeyOverlaySprites.Remove((x, y), out var keyIcon))
            keyIcon.SafeFree();

        // Swap door sprite from locked → open.
        // The tile kind in the map is already DoorOpen — use the same theme-driven path as SwapDoorSprite.
        if (_tileLayer.DoorOverlaySprites.TryGetValue((x, y), out var doorSprite))
        {
            var theme = _state.Map.GetTileTheme(x, y);
            string themeName = DungeonRenderer.ThemeToConfigName(theme);
            var openPath = _tileThemeConfig.GetDoorOpen(themeName);
            if (openPath != null)
            {
                var tex = ResourceLoader.Load<Texture2D>(openPath);
                if (tex != null && doorSprite is Sprite2D s2d)
                {
                    s2d.Texture = tex;
                    s2d.Modulate = Colors.White; // clear lock color tint
                    s2d.Centered = false;
                    s2d.RotationDegrees = 0f; // reset rotation — open door doesn't need it
                }
            }
        }
    }

    /// <summary>
    /// Handle a secret door being revealed. The tile kind has already changed to TileKind.Door
    /// in the logic layer. Here we:
    ///   1. Swap the wall base sprite at (x, y) to the floor tile (the revealed area is now
    ///      passable; the full re-render on the next UpdateVisibility will handle floor color).
    ///      We keep the existing base sprite and let UpdateVisibility re-modulate it naturally —
    ///      the wall sprite sits at (x, y) in TileSprites and will be visible/explored already.
    ///   2. Add a new door overlay sprite at (x, y) tracked in DoorOverlaySprites, exactly
    ///      mirroring how the initial render pass creates door overlays.
    ///
    /// The wall base sprite visually becomes "wrong" (wall texture, but now a door). We fix
    /// this by swapping it to the floor tile texture so the door overlay renders on floor.
    /// </summary>
    private void HandleSecretDoorFound(int x, int y)
    {
        if (_tileLayer == null || _tileThemeConfig == null || _state == null || _tileMapLayer == null)
            return;

        var map = _state.Map;
        var theme = map.GetTileTheme(x, y);
        string themeName = DungeonRenderer.ThemeToConfigName(theme);

        // Step 1: replace the wall base sprite with a floor sprite so the door overlay
        // renders on a floor background rather than a mismatched wall background.
        if (_tileLayer.TileSprites.TryGetValue((x, y), out var baseSprite))
        {
            var floorPath = _tileThemeConfig.GetFloorTile(themeName, x, y);
            if (floorPath != null)
            {
                var floorTex = ResourceLoader.Load<Texture2D>(floorPath);
                if (floorTex != null && baseSprite is Sprite2D s2d)
                    s2d.Texture = floorTex;
            }
        }

        // Step 2: add a door overlay at (x, y) — exactly mirrors DungeonRenderer Pass 2.
        // The door sprite is a closed door (the player still has to open it).
        var doorPath = _tileThemeConfig.GetDoor(themeName);
        if (doorPath == null) return;

        var doorTex = ResourceLoader.Load<Texture2D>(doorPath);
        if (doorTex == null) return;

        // Determine orientation: horizontal if walls above AND below (passage runs E-W).
        // After reveal the tile is now TileKind.Door, so neighbors are still wall/secret-door.
        bool wallN = !map.InBounds(x, y - 1) || map.IsWallTile(x, y - 1);
        bool wallS = !map.InBounds(x, y + 1) || map.IsWallTile(x, y + 1);
        bool isDoorHorizontal = wallN && wallS;

        var screenPos = _renderer.GridToScreen(x, y);
        var doorOverlay = new Sprite2D
        {
            Texture = doorTex,
            Position = isDoorHorizontal ? _renderer.GridToScreenCenter(x, y) : screenPos,
            Centered = isDoorHorizontal,
            RotationDegrees = isDoorHorizontal ? 90f : 0f,
            ZIndex = _renderer.GetTileSortOrder(x, y) + 1,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };

        _tileMapLayer.AddChild(doorOverlay);
        _tileLayer.DoorOverlaySprites[(x, y)] = doorOverlay;
    }

    /// <summary>
    /// Remove the key icon overlay from a locked chest when it is unlocked.
    /// Resets the chest sprite tint to White so it looks like a normal closed chest
    /// (it will be swapped to open in the immediately-following ChestOpenedEvent handler).
    /// </summary>
    private void HandleChestUnlocked(int x, int y)
    {
        if (_tileLayer == null) return;

        // Remove the key icon overlay sprite.
        if (_tileLayer.LockKeyOverlaySprites.Remove((x, y), out var keyIcon))
            keyIcon.SafeFree();

        // Reset chest sprite tint to White — the chest is now unlocked and about to open.
        if (_tileLayer.FeatureOverlaySprites.TryGetValue((x, y), out var chestSprite))
            chestSprite.Modulate = Colors.White;
    }

    /// <summary>
    /// Human-readable color name for a lock color ID, used in toast messages.
    /// Must stay in sync with DungeonRenderer.GetLockColor palette.
    /// </summary>
    private static string LockColorName(int colorId) => colorId switch
    {
        0 => "red",
        1 => "blue",
        2 => "green",
        3 => "gold",
        4 => "purple",
        _ => "unknown",
    };

    /// <summary>
    /// After UpdateVisibility sets all visible sprites to White, re-apply lock color tints to:
    ///   - Locked chests still in the player's FOV (FeatureOverlaySprites).
    ///   - Locked doors still in the player's FOV (DoorOverlaySprites via LockedDoors registry).
    ///
    /// Called from OnTurnCompleted after DungeonRenderer.UpdateVisibility. This two-pass approach
    /// (UpdateVisibility sets White baseline → RefreshLockedChestTints overrides where needed)
    /// avoids storing per-sprite original tints in TileLayer.
    /// </summary>
    private void RefreshLockedChestTints()
    {
        if (_tileLayer == null || _state == null) return;

        // Re-tint locked chests (feature overlay sprites).
        foreach (var feature in _state.Features)
        {
            var lockable = feature.Get<LockableComponent>();
            if (lockable == null || !lockable.IsLocked) continue;

            // Only re-tint if this cell is currently visible (explored state keeps the dim tint).
            if (!_state.Map.IsVisible(feature.X, feature.Y)) continue;

            if (_tileLayer.FeatureOverlaySprites.TryGetValue((feature.X, feature.Y), out var sprite))
                sprite.Modulate = DungeonRenderer.GetLockColor(lockable.LockColorId);
        }

        // Re-tint locked doors (door overlay sprites).
        foreach (var ((x, y), colorId) in _state.LockedDoors)
        {
            if (!_state.Map.IsVisible(x, y)) continue;

            if (_tileLayer.DoorOverlaySprites.TryGetValue((x, y), out var doorSprite))
                doorSprite.Modulate = DungeonRenderer.GetLockColor(colorId);
        }
    }

    /// <summary>
    /// DEV instrumentation (symptom A): capture two mid-run saves for save-side vs load-side diffing —
    /// (1) the on-disk file BYTE-VERBATIM (the exact bytes LoadMidRun reads — copied, never re-serialized)
    /// and (2) a fresh SaveMidRun of the current in-memory state. Written as &lt;timestamp&gt;-disk.json and
    /// &lt;timestamp&gt;-memory.json under user://. Debug-build only (gated at the call site via OS.IsDebugBuild).
    ///
    /// NOTE: Godot core has no iOS share-sheet API and the project has no share GDExtension, so this does
    /// not pop the share sheet — the files land in the app's user data dir (path logged). Getting them off
    /// device (share sheet or Finder file-sharing) is a flagged infra follow-up.
    /// </summary>
    private void ExportSaveForDebug()
    {
        try
        {
            string ts = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string userDir = ProjectSettings.GlobalizePath("user://");
            string diskDest = System.IO.Path.Combine(userDir, $"{ts}-disk.json");
            string memDest  = System.IO.Path.Combine(userDir, $"{ts}-memory.json");

            string savePath = _persistenceProvider?.GetMidRunSaveFilePath() ?? "";
            if (!string.IsNullOrEmpty(savePath) && System.IO.File.Exists(savePath))
                System.IO.File.Copy(savePath, diskDest, overwrite: true);   // byte-verbatim — no re-serialize
            else
                GD.PrintErr("[ExportSave] no on-disk mid-run save to export.");

            if (_state != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    Logic.Persistence.MidRun.MidRunSerializer.SaveMidRun(_state),
                    Logic.Persistence.MidRun.MidRunSaveJsonContext.Default.MidRunSaveDto);
                System.IO.File.WriteAllText(memDest, json);
            }
            else
                GD.PrintErr("[ExportSave] no in-memory state to serialize.");

            GD.Print($"[ExportSave] wrote:\n  {diskDest}\n  {memDest}");
            _toastLog?.AddMessage($"[color=#88cc99]Save exported: {ts}-disk/-memory.json[/color]");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[ExportSave] failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The real <c>surfaceAvailable</c> probe for the ribbon (M1.5b): true only when the dungeon HUD is
    /// active and no modal is covering it. Never hardcoded true — a false result must keep the scheduler
    /// from consuming.
    /// </summary>
    private bool IsRibbonSurfaceAvailable() => RibbonSurfaceBlocker() == null;

    /// <summary>
    /// The specific condition making the ribbon surface unavailable, or null if it IS available. Split
    /// out so the device diagnostic can name WHY a line was suppressed — a modal left visible from a
    /// prior run keeps this false for the whole next run, silencing voice until the modal is dismissed.
    /// </summary>
    private string? RibbonSurfaceBlocker()
    {
        if (_state == null) return "state-null";
        if (!_state.IsDungeonMode) return "not-dungeon";
        if (_state.IsGameOver) return "game-over";
        if (GetNodeOrNull<CanvasLayer>("MenuLayer") is { Visible: true }) return "menu-layer";
        if (_equipmentPanel?.Visible == true) return "equipment-panel";
        if (_memoInboxPanel?.Visible == true) return "memo-inbox";
        if (_messageLogPanel?.Visible == true) return "message-log";
        if (_gameOverScreen?.Visible == true) return "game-over-screen";
        return null;
    }

    private void OnTurnCompleted(TurnResult result)
    {
        if (_state == null) return;

        // Hollowmark ribbon (M1.5b): derive trigger families from this committed turn and offer them to
        // the scheduler with REAL probes — the ribbon's current tier (strict supersede) and whether the
        // surface is available (dungeon HUD active, no modal). The scheduler consumes nothing on any
        // non-render path, so hardcoding these true would silently burn bag draws / one-shots. Runs
        // BEFORE the autosave below so a delivered line is captured in this turn's save (resume shows it).
        if (_state.IsDungeonMode && _voiceScheduler != null && _voiceRibbon != null)
        {
            var families = _voiceTriggerReader.Read(result, _state);
            bool surf = IsRibbonSurfaceAvailable();
            Logic.Voice.VoiceDeliverReason reason = Logic.Voice.VoiceDeliverReason.NoEligibleFamily;
            // currentRibbonTier: null — the ribbon now STACKS (up to 3 cards), so a new line no longer
            // needs to supersede whatever is showing. The scheduler still filters by cooldown / mode /
            // once-per-run and delivers at most one line per turn.
            Logic.Voice.VoiceDelivery? delivery = families.Count > 0
                ? _voiceScheduler.TryDeliver(families, _voiceSettings.Mode, _state.TurnCount,
                      currentRibbonTier: null, surf, out reason)
                : null;
            if (delivery != null)
                _voiceRibbon.ShowLine(delivery.Line, _voiceSettings.DurationSeconds,
                    manualDismiss: _voiceSettings.Dismiss == Logic.Voice.VoiceDismiss.Manual);

            if (_voiceDiag)   // debug-only device diagnostic (E)
                Diag.Event("voice_turn", new
                {
                    turn = _state.TurnCount,
                    fams = string.Join(",", families),
                    mode = _voiceSettings.Mode.ToString(),
                    surface = surf,
                    surfaceBlocker = RibbonSurfaceBlocker() ?? "(available)",   // WHY surface is unavailable
                    reason = families.Count == 0 ? "no-triggers-derived" : reason.ToString(),
                    delivered = delivery?.Line ?? "(none)",
                    // If a line WAS delivered, dump geometry so a silent widget is caught (suspect a).
                    ribbon = delivery != null ? _voiceRibbon.DiagState() : "",
                });
        }
        else if (_voiceDiag)
        {
            Diag.Event("voice_skip", new
            {
                turn = _state.TurnCount,
                dungeon = _state.IsDungeonMode,
                schedulerNull = _voiceScheduler == null,
                ribbonNull = _voiceRibbon == null,
            });
        }

        // Mid-run autosave (M1.4 4b). This is the turn-commit seam: GameController fires TurnCompleted
        // immediately after TurnController.ProcessTurn, so _state is fully advanced here. Snapshot on
        // the game thread, write off the critical path. Dungeon mode only (scenario/harness never write
        // device saves). Skip terminal turns — record-then-delete owns those — and descend turns, whose
        // NEW floor is saved synchronously in OnFloorTransitionRequested.
        if (_state.IsDungeonMode && !result.GameOver && _midRunWriter != null
            && !result.Events.Any(e => e is DescendEvent))
            _midRunWriter.RequestWrite(Logic.Persistence.MidRun.MidRunSerializer.SaveMidRun(_state));

        // Accumulate stats
        _turnCount = _state.TurnCount;
        foreach (var evt in result.Events)
        {
            if (evt is AttackEvent atk)
            {
                if (atk.ActorId == _state.Player.Id && atk.Hit) _damageDealt += atk.Damage;
                else if (atk.TargetId == _state.Player.Id && atk.Hit) _damageTaken += atk.Damage;
                if (atk.TargetKilled && atk.ActorId == _state.Player.Id) _monstersKilled++;
            }
            else if (evt is PortalPlacedEvent pp)
                SpawnPortalSprite(pp.PortalEntityId, pp.X, pp.Y, pp.Type);
            else if (evt is PortalRemovedEvent pr)
            {
                DespawnPortalSprite(pr.EntranceEntityId);
                DespawnPortalSprite(pr.ExitEntityId);
            }
            else if (evt is DoorOpenedEvent doorEvt)
                SwapDoorSprite(doorEvt.X, doorEvt.Y);
            else if (evt is ChestUnlockedEvent unlockEvt)
            {
                HandleChestUnlocked(unlockEvt.X, unlockEvt.Y);
                string colorName = LockColorName(unlockEvt.LockColorId);
                _toastLog?.AddMessage($"The {colorName} key unlocks the chest!");
            }
            else if (evt is ChestLockedEvent lockedEvt)
            {
                string colorName = LockColorName(lockedEvt.LockColorId);
                _toastLog?.AddMessage($"This chest is locked. You need a {colorName} key.");
            }
            else if (evt is DoorUnlockedEvent doorUnlockEvt)
            {
                HandleDoorUnlocked(doorUnlockEvt.X, doorUnlockEvt.Y);
                string doorColorName = LockColorName(doorUnlockEvt.LockColorId);
                _toastLog?.AddMessage($"The {doorColorName} key unlocks the door!");
            }
            else if (evt is LockedDoorBumpedEvent lockedDoorEvt)
            {
                string doorColorName = LockColorName(lockedDoorEvt.LockColorId);
                _toastLog?.AddMessage($"This door is locked. You need a {doorColorName} key.");
            }
            else if (evt is ChestOpenedEvent chestEvt)
            {
                SwapChestSprite(chestEvt.X, chestEvt.Y, looted: false);
                _toastLog?.AddMessage("You open the chest.");
            }
            else if (evt is ChestLootedEvent lootEvt)
            {
                SwapChestSprite(lootEvt.X, lootEvt.Y, looted: true);
                _toastLog?.AddMessage("You loot the chest!");
            }
            else if (evt is SignpostReadEvent signEvt)
            {
                // Signs are free actions — show the message text as a toast.
                // Sign type could drive color coding in a future pass; neutral for now.
                _toastLog?.AddMessage($"Sign: {signEvt.Message}");
            }
            else if (evt is MuralExaminedEvent muralEvt)
            {
                _gameController?.ShowMuralInspect(muralEvt.Text);
            }
            else if (evt is Logic.Endgame.DebtChoiceGateEvent gateEvt)
            {
                // TODO (presentation tail): show Force / (Self if SwapAvailable) / Refuse buttons.
                // Three gate shapes: Force+Self+Refuse (swap available),
                //                    Force+Refuse (heavy, no swap),
                //                    [never emitted] (clean, no swap → auto-resolved).
                // Button labels and confirmation copy come from the audit registry.
                string forceLabel   = _weighingAuditRegistry?.GetUiText("ui.force_button")  ?? "Take her by force.";
                string selfLabel    = _weighingAuditRegistry?.GetUiText("ui.self_button")   ?? "Give yourself in her place.";
                string refuseLabel  = _weighingAuditRegistry?.GetUiText("ui.refuse_button") ?? "Turn back. Carry the debt.";
                string opts = gateEvt.SwapAvailable
                    ? $"[{forceLabel}] [{selfLabel}] [{refuseLabel}]"
                    : $"[{forceLabel}] [{refuseLabel}]";
                _toastLog?.AddMessage($"[color=#c0c0c0]The Weighing: {opts}[/color]");
            }
            else if (evt is WeighingDialogueEvent dialogueEvt)
            {
                // TODO (presentation tail): replace with WeighingDialoguePanel (blocking, paged,
                // tap-to-advance). Until that panel exists the dialogue is printed to the toast log
                // in sequence so the content is visible during development.
                foreach (var page in dialogueEvt.Pages)
                {
                    string color = page.Speaker switch
                    {
                        "under_warden" => "#c8b880",
                        "guardian"     => "#d0806060",
                        _              => "#aaaaaa",
                    };
                    _toastLog?.AddMessage($"[color={color}]{page.Text}[/color]");
                }
            }
            else if (evt is SecretDoorFoundEvent secretEvt)
            {
                HandleSecretDoorFound(secretEvt.X, secretEvt.Y);
                _toastLog?.AddMessage(secretEvt.Hint);
            }
            else if (evt is PossessionEnteredEvent possEnterEvt)
            {
                _toastLog?.AddMessage($"You possess the {result.Events.OfType<PossessionEnteredEvent>().FirstOrDefault()?.HostSpecies ?? "creature"}.");
                _ = possEnterEvt; // used above via linq
            }
            else if (evt is PossessionExitedEvent possExitEvt)
            {
                string reason = possExitEvt.Reason switch
                {
                    "voluntary"         => "You withdraw your spirit.",
                    "host_died"         => "The host body collapses — your spirit returns.",
                    "visibility_broken" => "The host strays too far. Your spirit snaps back.",
                    "dispelled"         => "The spell is broken. You return to yourself.",
                    "home_body_died"    => "Your home body has fallen.",
                    _                   => "Possession ends.",
                };
                _toastLog?.AddMessage(reason);

                // Track hall warden possessions for Under-Warden memo triggers.
                // Threshold memos (1st, 3rd, 6th+) are queued here so the evaluator
                // can fire mid-run rather than waiting for run end.
                if (possExitEvt.HostSpecies == "hall_warden"
                    && _memoRegistry != null && _persistentState != null)
                {
                    _memoEvaluator.EvaluateHallWardenPossession(
                        _persistentState, _memoRegistry,
                        _persistentState.RunCounter.TotalRuns);
                    // Dirty flag set inside EvaluateHallWardenPossession — flush deferred
                    // to the next natural persistence boundary (floor descent or run end).
                }

                // Variant 3 spell-break: past-self freed. Record in persistence and fire
                // the catalog_referenced memo on first occurrence.
                // NOTE: FreedPastSashaId uses the most recent PastSashaRecord as an
                // approximation until the spawn system stores record IDs on entities.
                if (possExitEvt.Reason == "warden_dispelled"
                    && _persistentState != null && _memoRegistry != null)
                {
                    var mostRecent = _persistentState.PastSashas.Records.Count > 0
                        ? _persistentState.PastSashas.Records[^1]
                        : null;
                    if (mostRecent != null)
                    {
                        _persistentState.FreedPastSelves.AddRecord(
                            freedPastSashaId: mostRecent.Id,
                            freedRun: _persistentState.RunCounter.TotalRuns,
                            freedFloor: _currentDepth);
                        _persistentState.MarkDirty();
                    }

                    string? catalogEntry = null;
                    if (mostRecent != null && _state != null && _voiceLineRegistry != null)
                        catalogEntry = CatalogEntryRenderer.RenderEntry(
                            mostRecent, _persistentState.PastSashas, _voiceLineRegistry, _state.Rng);

                    _memoEvaluator.EvaluateCatalogReferenced(
                        _persistentState, _memoRegistry,
                        _persistentState.RunCounter.TotalRuns,
                        catalogEntry);
                }
            }
            else if (evt is PossessionNearDeathWarningEvent)
            {
                _toastLog?.AddMessage("[color=#ff4444]Your body is failing — drain is critical![/color]");
            }
            else if (evt is VoiceLineEvent voiceEvt && _state != null)
            {
                var line = _voiceLineRegistry?.GetLine(voiceEvt.TriggerId, _state.Rng, _voiceLineFiredSet);
                if (line != null)
                    _toastLog?.AddMessage($"[color=#b090d0]{line}[/color]");
            }
        }

        // Determine possession state for UI updates.
        bool isPossessing = _state != null && !ReferenceEquals(_state.ControlledEntity, _state.Player);
        bool isPossessionTargeting = _gameController?.IsPossessionTargetingActive ?? false;

        if (_hud != null && _state != null)
            _hud.OnTurnCompleted(result, _state);
        _statusEffectBar?.Refresh(_state.Player);
        _menuButtonBar?.SetAutoExploreActive(_gameController?.IsAutoExploreActive ?? false);
        if (isPossessionTargeting)
            _menuButtonBar?.SetPossessionMode(MenuButtonBar.PossessionMode.Targeting);
        else if (isPossessing)
        {
            // Pass species abilities so the button bar can render them when Hall Wardens ship.
            var abilities = _state?.ControlledEntity.Get<HostAbilityComponent>()?.Abilities;
            _menuButtonBar?.SetPossessionMode(MenuButtonBar.PossessionMode.Active, abilities);
        }
        else
            _menuButtonBar?.SetPossessionMode(MenuButtonBar.PossessionMode.Idle);

        if (_equipmentPanel?.Visible == true && _state != null)
            _equipmentPanel.Refresh(_state);

        // Camera follows the controlled entity. Forced possession exits snap; voluntary tweens.
        var forcedExit = result.Events.OfType<PossessionExitedEvent>()
            .FirstOrDefault(e => e.Reason is not "voluntary" and not null);
        if (forcedExit != null && _gameView != null && _state != null)
            PlayerCamera.Update(_gameView, _state.ControlledEntity, _currentZoom, _renderer); // snap
        else if (_gameView != null && _state != null)
            PlayerCamera.AnimateTo(_gameView, _state.ControlledEntity, this, zoom: _currentZoom, renderer: _renderer);
        _miniMap?.Refresh(_state);
        _toastLog?.RecordTurn(result, _state);
        _groundHazardOverlay?.Refresh(_state);

        // Update fog-of-war — TurnController called RecomputeFov twice this turn
        // (after player action, after monster turns). Apply the result to the renderer.
        if (_tileLayer != null)
        {
            DungeonRenderer.UpdateVisibility(_tileLayer, _state.Map);
            // Re-apply lock color tints to visible locked chests.
            // UpdateVisibility sets all visible feature sprites to White as a baseline;
            // this pass overrides with the lock tint where the chest is still locked.
            RefreshLockedChestTints();
        }
        _entitySprites?.UpdateVisibility(_state);
        _entitySprites?.UpdateStatusTints(_state);
        _itemSprites?.UpdateVisibility(_state);
        _corpseSprites?.Sync(_state);
        _corpseSprites?.UpdateVisibility(_state);
        // Update floating HP bars after visibility is resolved for this turn.
        if (_entitySprites != null)
            _floatingHpBars?.Refresh(_state, _entitySprites);
    }

    private void OnGameEnded(bool playerWon)
    {
        var stats = $"Turns: {_turnCount}\nMonsters killed: {_monstersKilled}\n" +
                    $"Damage dealt: {_damageDealt}\nDamage taken: {_damageTaken}";
        _gameOverScreen?.Show(playerWon, stats);

        if (_state != null && _persistentState != null)
        {
            // A refusal at the Weighing is NOT a death — Sasha assessed the cost and walked out
            // alive, debt open. No corpse to loot/quip/possess; recording a past-Sasha would be a
            // category error (a living man's body in the catalog of the dead). File the refusal
            // instead — the Under-Warden remembers you turned back. [decision 2026-06-01]
            bool refused = _state.PlayerDeathCause == Logic.Endgame.WeighingConstants.LossRefusedCause;
            if (refused)
            {
                _persistentState.RunCounter.UpdateBestFloor(_currentDepth);
                _persistentState.UnderWarden.WeighingRefusals++;
                _persistentState.MarkDirty();
            }
            // Record a past-Sasha on player death (spec §6.2).
            else if (!playerWon)
            {
                var gear = SnapshotEquippedGear(_state.Player);
                var bestFloor = Math.Max(_persistentState.RunCounter.BestFloorReached, _currentDepth);
                _persistentState.RunCounter.UpdateBestFloor(_currentDepth);

                var killerSpecies = _state.PlayerDeathKillerSpecies;
                bool killerWasFirst = killerSpecies != null
                    && _state.Knowledge.GetEntry(killerSpecies).EngagedCount <= 1;

                // "Clean run" heuristic: died to non-self-inflicted cause at floor 10+.
                bool prevClean = _state.PlayerDeathCause is not ("oil_slick_fire" or "own_poison"
                    or "own_trap" or "possessed_wrong_host") && _currentDepth >= 10;

                _persistentState.PastSashas.AddRecord(
                    diedRun: _persistentState.RunCounter.TotalRuns,
                    diedFloor: _currentDepth,
                    causeOfDeath: _state.PlayerDeathCause,
                    killerSpecies: killerSpecies,
                    gearCarried: gear,
                    bestFloorReachedAtDeath: bestFloor,
                    previousRunWasClean: prevClean,
                    killerWasFirstEncounter: killerWasFirst);
                _persistentState.MarkDirty();
            }

            // Weighing outcome (TASK-008): record the ending. Clean Audit sets the sticky
            // audit_completed flag (drives different memo content forever after); reaching floor 25
            // with a Weighing that resolved counts as an audit attempt.
            if (_state.WeighingArena != null && _state.Ending != Logic.Endgame.EndingType.None)
            {
                _persistentState.UnderWarden.AuditAttemptedRuns++;
                if (_state.Ending == Logic.Endgame.EndingType.CleanAudit)
                    _persistentState.UnderWarden.AuditCompleted = true;
                _persistentState.MarkDirty();
            }

            // Faction reputation: apply transitions at run end (spec §6.3).
            // (Reads UnprovokedOrcKillsThisRun, now fed by the run aggression tally — TASK-003.)
            ApplyFactionRunEnd(_state, _persistentState);

            // Excess metric (TASK-003): flush this run's unprovoked cross-faction kills into the
            // cross-run cumulative total that feeds the Weighing's Auditor's Own / Oathkeeper audit.
            var tally = _state.Player.Get<RunAggressionTally>();
            if (tally != null && tally.Total() > 0)
            {
                foreach (var (faction, count) in tally.UnprovokedKillsByFaction)
                    _persistentState.UnderWarden.AddUnprovokedKill(faction, count);
                _persistentState.MarkDirty();
            }

            // Under-Warden memo delivery: evaluate post-run incidents and queue any
            // memos that should surface in the inbox. Must run BEFORE the persistence
            // flush so queued memos are written in the same atomic write.
            if (_memoRegistry != null)
            {
                var ctx = new PostRunContext(
                    Died: !playerWon,
                    CauseOfDeath: _state.PlayerDeathCause,
                    KillerSpecies: _state.PlayerDeathKillerSpecies,
                    FloorReached: _currentDepth,
                    RunNumber: _persistentState.RunCounter.TotalRuns,
                    Ending: _state.Ending
                );
                _memoEvaluator.EvaluateRunEnd(ctx, _persistentState, _memoRegistry);
                // MarkDirty is called inside EvaluateRunEnd if any memos were queued.
            }
        }

        // Run end is a forced flush — write regardless of dirty state (spec §5).
        if (_persistentState != null && _persistenceProvider != null)
            _persistentState.Flush(_persistenceProvider, GD.PrintErr);

        // RECORD-then-DELETE (M1.4 4b): the run's outcome is now committed to the cross-run save above,
        // so it survives any crash from here. ONLY now delete the mid-run save. Never the reverse — a
        // crash between must lose the mid-run save, never the record. The load-time terminal check is
        // the net for any mid-run save that slipped through (its state IsGameOver → routed back here).
        if (_persistenceProvider != null)
            Logic.Persistence.MidRun.MidRunFile.Delete(_persistenceProvider.GetMidRunSaveFilePath());
    }

    /// <summary>
    /// Snapshot equipped weapon, armor, and rings at death time (OQ-2 resolution A: equipped only).
    /// </summary>
    private static List<GearItemRecord> SnapshotEquippedGear(Entity player)
    {
        var result = new List<GearItemRecord>();
        var equipment = player.Get<Equipment>();
        if (equipment == null) return result;

        foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.Chest, EquipmentSlot.LeftRing, EquipmentSlot.RightRing })
        {
            var item = equipment.GetSlot(slot);
            if (item == null) continue;
            var tag = item.Get<ItemTag>();
            if (tag == null) continue;
            var eq = item.Get<Equippable>();
            var enchantment = eq?.ToHitBonus ?? 0;
            result.Add(new GearItemRecord
            {
                TypeId = tag.TypeId,
                Enchantment = enchantment,
                Condition = (eq is { BaseDamageMax: > 0 } && eq.DamageMax < eq.BaseDamageMax)
                    ? "corroded" : "normal",
                // Notable: enchanted (+1 or better) or a specific NPC-gifted item.
                IsNotable = enchantment > 0 || IsNamedItem(tag.TypeId),
            });
        }
        return result;
    }

    // Named / NPC-gifted items that are always "notable" regardless of enchantment.
    // Expand when items like the Borrek knife are added.
    private static readonly HashSet<string> _namedItemIds = new(StringComparer.Ordinal)
    {
        // placeholder — no named items yet; populated as content is added
    };

    private static bool IsNamedItem(string typeId) => _namedItemIds.Contains(typeId);

    /// <summary>
    /// Apply faction reputation transitions at run end (spec §6.3).
    /// Orc: threshold unprovoked kills → Hostile; otherwise increment/check decay.
    /// </summary>
    private static void ApplyFactionRunEnd(GameState state, PersistentRunState persistent)
    {
        bool hadNegativeAction = state.UnprovokedOrcKillsThisRun >= FactionsData.HostileThreshold;
        if (hadNegativeAction)
            persistent.Factions.ApplyNegativeAction(FactionsData.OrcFactionId);
        else
            persistent.Factions.OnRunEndNoNegativeAction(FactionsData.OrcFactionId);
        persistent.MarkDirty();
    }

    public override void _Notification(int what)
    {
        // App backgrounding: forced flush so in-progress state is not lost on iOS/Android.
        if (what == NotificationApplicationPaused)
        {
            if (_persistentState != null && _persistenceProvider != null)
                _persistentState.Flush(_persistenceProvider, GD.PrintErr);
            // Belt-and-suspenders mid-run flush: write the current floor synchronously and wait for any
            // in-flight background write, so a force-kill after backgrounding still resumes losslessly.
            if (_state != null && _state.IsDungeonMode && !_state.IsGameOver && _midRunWriter != null)
                _midRunWriter.FlushSync(Logic.Persistence.MidRun.MidRunSerializer.SaveMidRun(_state));
            _midRunWriter?.WaitForIdle();
        }
    }

    /// <summary>
    /// Fires after animations complete when the player descends a staircase.
    /// Builds the next floor, carrying the player's current state forward.
    /// </summary>
    private void OnFloorTransitionRequested(int newDepth)
    {
        GD.Print($"Floor transition: depth {_currentDepth} → {newDepth}");
        // Track best floor reached — recorded at stairs-down, so the player gets credit for clearing it.
        if (_persistentState != null)
        {
            _persistentState.RunCounter.UpdateBestFloor(_currentDepth);
            _persistentState.MarkDirty();
        }
        var builder = _testScenarioBuilder ?? _floorBuilder;
        if (builder == null) return;
        var rng = new SeededRandom(_baseSeed + newDepth * 1_000_003);
        _currentDepth = newDepth;
        _state = builder.Build(newDepth, rng, _state?.Player,
            identificationRegistry: _state?.IdentificationRegistry,
            appearancePool: _state?.AppearancePool,
            boonTracker: _state?.BoonTracker,
            muralTracker: _state?.MuralTracker,
            pityTracker: _state?.PityTracker,
            persistentState: _persistentState);

        // Inject the audit dialogue registry on the Weighing floor so the orchestrator
        // can emit voiced beats when Guardians rise.
        if (_state?.WeighingArena != null && _weighingAuditRegistry != null)
            _state.WeighingAudit = _weighingAuditRegistry;

        // Voice scheduler carries across floors: clear the floor-silence flag and re-attach so the
        // new floor's synchronous save serializes it.
        if (_state != null)
        {
            _voiceScheduler?.OnFloorEntered();
            _state.VoiceScheduler = _voiceScheduler;
        }

        SetupPresentation(_state);

        // Mid-run save: write the NEW floor synchronously so a kill in the Build-to-first-turn window
        // resumes into this floor, never the previous one (M1.4 4b ruling).
        if (_state != null && _state.IsDungeonMode && _midRunWriter != null)
            _midRunWriter.FlushSync(Logic.Persistence.MidRun.MidRunSerializer.SaveMidRun(_state));

        // Floor descent is a narrative-event-boundary flush (spec §5).
        if (_persistentState != null && _persistenceProvider != null && _persistentState.IsDirty)
            _persistentState.Flush(_persistenceProvider, GD.PrintErr);
    }

    private void OnInventoryItemTapped(int itemId)
    {
        _gameController?.HandleInventoryTap(itemId);
    }

    private void OnGearRequested()
    {
        if (_equipmentPanel == null || _state == null) return;
        if (_equipmentPanel.Visible)
            _equipmentPanel.Hide();
        else
            _equipmentPanel.Show(_state);
    }

    private void OnReplayRequested()
    {
        // Hide the game-over screen first in all paths.
        _gameOverScreen?.Hide();

        // If there are pending Under-Warden memos, show the inbox before returning
        // to the main menu. OnInboxClosed handles the menu navigation.
        if (_persistentState != null && _persistentState.UnderWarden.PendingMemos.Count > 0
            && _memoInboxPanel != null && _memoRegistry != null && _persistenceProvider != null)
        {
            _memoInboxPanel.Show(_persistentState, _persistenceProvider, _memoRegistry);
        }
        else
        {
            _currentDepth = 1;
            ShowMainMenu();
        }
    }

    /// <summary>
    /// Called when the player has dismissed all pending memos and the inbox closes.
    /// Navigates back to the main menu to begin a new run.
    /// </summary>
    private void OnInboxClosed()
    {
        if (_memoInboxPanel != null)
            _memoInboxPanel.Visible = false;
        _currentDepth = 1;
        ShowMainMenu();
    }

    // -------------------------------------------------------------------------
    // Zone layout helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Add a full-rect ColorRect placeholder to a zone container so the zone is
    /// visible during development. Replaced with real content in later phases.
    /// The placeholder MouseFilter=Ignore so it doesn't consume taps.
    /// </summary>
    private static void AddZonePlaceholder(Control zone, Color color)
    {
        var bg = new ColorRect { Color = color };
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        zone.AddChild(bg);
    }

    // -------------------------------------------------------------------------
    // Menu system
    // -------------------------------------------------------------------------

    /// <summary>
    /// Show the main menu. Clears any existing panels in MenuLayer and creates fresh ones.
    /// MenuLayer (layer=20) sits above UILayer (layer=10) so it covers everything.
    /// </summary>
    private void ShowMainMenu(string? notice = null)
    {
        var menuLayer = GetNode<CanvasLayer>("MenuLayer");
        ClearMenuLayer(menuLayer);
        menuLayer.Visible = true;

        var panel = new MainMenuPanel();
        menuLayer.AddChild(panel);

        panel.NewGameRequested      += OnNewGameRequested;
        panel.ExploreModeRequested  += OnExploreModeRequested;
        panel.TestingModeRequested  += ShowTestMenu;
        panel.OptionsRequested      += ShowOptions;

        // Quiet one-line notice (e.g. a corrupt save was set aside). Bottom-anchored, unobtrusive.
        if (!string.IsNullOrEmpty(notice))
        {
            GD.Print($"[Main] menu notice: {notice}");
            var label = new Label
            {
                Text = notice,
                HorizontalAlignment = HorizontalAlignment.Center,
                AnchorTop = 0.92f, AnchorBottom = 1f, AnchorLeft = 0f, AnchorRight = 1f,
            };
            label.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
            menuLayer.AddChild(label);
        }
    }

    /// <summary>
    /// Startup entry: silently RESUME a valid non-terminal mid-run save (no menu, no prompt), reusing
    /// the existing floor-entry presentation path; otherwise fall through to the menu. Corrupt/mismatched
    /// saves are ARCHIVED (never deleted) and the menu shows a quiet notice. A terminal save routes to the
    /// death/victory flow (record + delete), never into play. (M1.4 4b — Rafe's rulings.)
    /// </summary>
    private void ResumeOrShowMenu()
    {
        if (_persistenceProvider == null) { ShowMainMenu(); return; }
        var path = _persistenceProvider.GetMidRunSaveFilePath();
        var result = Logic.Persistence.MidRun.MidRunFile.LoadMidRunFromFile(path);

        switch (result.Status)
        {
            case Logic.Persistence.MidRun.MidRunLoadStatus.Ok:
                GameState loaded;
                // Pass the ribbon registry + tier metadata (RECONSTRUCT) so a voice-bearing save resumes
                // its scheduler; LoadMidRun throws if the save carries voice state and these are absent.
                try { loaded = Logic.Persistence.MidRun.MidRunSerializer.LoadMidRun(
                        result.Save!, _boonTable, _voiceRibbonRegistry, _voiceTierMeta); }
                catch (System.Exception ex)
                {
                    GD.PrintErr($"[Main] mid-run load failed post-parse: {ex.Message}");
                    Logic.Persistence.MidRun.MidRunFile.ArchiveCorrupt(path);
                    ShowMainMenu("A saved run couldn't be loaded and was set aside.");
                    return;
                }
                _state = loaded;
                _currentDepth = loaded.CurrentDepth;

                // Adopt the reconstructed scheduler; a pre-voice save has none, so build a fresh one so
                // voice works post-resume. Edge state re-arms for the resumed session.
                _voiceScheduler = loaded.VoiceScheduler
                    ?? (_voiceRibbonRegistry != null && _voiceTierMeta != null
                        ? new Logic.Voice.VoiceScheduler(_voiceRibbonRegistry, _voiceTierMeta, loaded.Rng.Seed)
                        : null);
                loaded.VoiceScheduler = _voiceScheduler;
                _voiceTriggerReader.Reset();
                if (loaded.IsGameOver)
                {
                    // Load-time terminal net: never resume into play. Route to the death/victory flow,
                    // which records the outcome + deletes the save idempotently.
                    bool won = loaded.IsDungeonVictory
                        || (loaded.Ending == Logic.Endgame.EndingType.None && loaded.PlayerWon);
                    OnGameEnded(won);
                    if (_gameOverScreen == null) ShowMainMenu();   // no game-over UI exists yet at startup
                }
                else
                {
                    GD.Print($"[Main] Resuming mid-run save — depth {loaded.CurrentDepth}, turn {loaded.TurnCount}.");
                    _currentDepth = loaded.CurrentDepth;
                    SetupPresentation(loaded);   // reuse the floor-entry path (sprites/camera/FOV) — silent
                }
                return;

            case Logic.Persistence.MidRun.MidRunLoadStatus.Corrupt:
            case Logic.Persistence.MidRun.MidRunLoadStatus.SchemaMismatch:
                var archived = Logic.Persistence.MidRun.MidRunFile.ArchiveCorrupt(path);
                GD.PrintErr($"[Main] mid-run save unreadable ({result.Status}: {result.Error}); archived to {archived}.");
                ShowMainMenu("A saved run was unreadable and was set aside.");
                return;

            default:  // FileNotFound — the normal fresh-launch path
                ShowMainMenu();
                return;
        }
    }

    private void OnExploreModeRequested()
    {
        GetNode<CanvasLayer>("MenuLayer").Visible = false;
        _currentDepth = 1;
        _baseSeed = Random.Shared.Next();
        GD.Print($"[Main] Explore mode seed: {_baseSeed}");
        StartDungeon(explorationMode: true);
    }

    private void OnNewGameRequested()
    {
        GetNode<CanvasLayer>("MenuLayer").Visible = false;
        _currentDepth = 1;
        // Each new game gets a unique seed so no two runs are identical.
        // The seed is stored in _baseSeed and recoverable from GameState.Rng.Seed
        // for future "show seed" or seed-entry UI.
        _baseSeed = Random.Shared.Next();
        GD.Print($"[Main] New game seed: {_baseSeed}");
        StartDungeon();
    }

    /// <summary>
    /// Discover available test scenarios, then show the test scenario picker panel.
    /// </summary>
    private void ShowTestMenu()
    {
        var menuLayer = GetNode<CanvasLayer>("MenuLayer");
        ClearMenuLayer(menuLayer);

        var scenarios = DiscoverTestScenarios();
        var panel = new TestMenuPanel(scenarios);
        menuLayer.AddChild(panel);

        panel.ScenarioSelected += LaunchTestScenario;
        panel.BackRequested    += () => ShowMainMenu();
    }

    private void ShowOptions()
    {
        var menuLayer = GetNode<CanvasLayer>("MenuLayer");
        ClearMenuLayer(menuLayer);

        // Pass the loaded tileset ID so the panel can detect changes made this session.
        var loadedId = _spriteMapping?.TilesetId ?? "ultimate_fantasy";
        var panel = new OptionsPanel(loadedId, _debugOverlay, _voiceSettings, newSettings =>
        {
            _voiceSettings = newSettings;                                   // live for the next TryDeliver
            Presentation.Persistence.VoiceSettingsStore.Save(newSettings);  // survives across runs (device store)
        },
        onExportSave: OS.IsDebugBuild() ? ExportSaveForDebug : null,
        voiceDiagOn: _voiceDiag,
        onVoiceDiagToggle: OS.IsDebugBuild() ? (on =>
        {
            _voiceDiag = on;
            Diag.Log($"[voice-diag] {(on ? "ENABLED" : "disabled")} — per-turn reason codes to diag_structured.jsonl");
        }) : null);
        menuLayer.AddChild(panel);

        panel.BackRequested += () => ShowMainMenu();
    }

    /// <summary>
    /// Boot directly into the fixed art-acceptance test scene (--art-scene launch flag).
    ///
    /// The authored floor data comes entirely from ArtAcceptanceSceneBuilder (Logic layer,
    /// no Godot dependency, unit-tested in tests/Core/ArtAcceptanceSceneBuilderTests.cs).
    /// This method's only job is the seam: hand that GameState to SetupPresentation, the
    /// exact same shared entry point StartDungeon/LaunchTestScenario use, which in turn
    /// calls DungeonRenderer.Render(state.Map, ..., props: state.Props, features: state.Features)
    /// — the same render call a procedurally generated floor goes through. No parallel
    /// rendering path exists for this scene.
    /// </summary>
    private void LaunchArtAcceptanceScene()
    {
        GetNode<CanvasLayer>("MenuLayer").Visible = false;

        _currentDepth = 1;
        _state = ArtAcceptanceSceneBuilder.Build(_monsterFactory!, _itemFactory!, _consumableFactory!);

        SetupPresentation(_state);
        GD.Print("Ready (art acceptance scene) — " +
                 $"{_state.Monsters.Count} monsters, {_state.Props.Count} props, {_state.Features.Count} features, " +
                 $"{_state.FloorItems.Count} floor items.");
    }

    /// <summary>--review-scene &lt;json&gt;: in-scene candidate-review floor (ReviewSceneBuilder).</summary>
    private static bool ReadReviewSceneFlag(out string? jsonPath)
    {
        jsonPath = null;
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--review-scene" && i + 1 < args.Length) jsonPath = args[i + 1];
        return jsonPath != null;
    }

    private void LaunchReviewScene(string jsonPath)
    {
        GetNode<CanvasLayer>("MenuLayer").Visible = false;
        _currentDepth = 1;
        _state = ReviewSceneBuilder.Build(jsonPath);
        SetupPresentation(_state);
        GD.Print($"Ready (review scene: {jsonPath}) — {_state.Props.Count} props.");
    }

    // ---------------------------------------------------------------------------------------
    // Tier 0 review harness — lit corridor with a junction (ART-BIBLE-v0 §13.1, §6).
    //
    // Every value the light rig needs is supplied on the command line by the harness driver and
    // echoed back into the capture log. Nothing is defaulted here: §6.2 marks the Boundary's
    // light values PLACEHOLDER, and a default in engine code would quietly become the derived
    // value that the §6.4 probe is supposed to establish.
    // ---------------------------------------------------------------------------------------

    /// <summary>--corridor-scene &lt;json&gt;: Tier 0 lit review corridor (CorridorReviewSceneBuilder).</summary>
    private static bool ReadCorridorSceneFlag(out string? jsonPath)
    {
        jsonPath = null;
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--corridor-scene" && i + 1 < args.Length) jsonPath = args[i + 1];
        return jsonPath != null;
    }

    /// <summary>--tile-theme-config &lt;path&gt;: alternate tile theme config (candidate injection).</summary>
    private static bool ReadTileThemeConfigFlag(out string? path)
    {
        path = null;
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--tile-theme-config" && i + 1 < args.Length) path = args[i + 1];
        return path != null;
    }

    // ── THE SE PERFORMANCE GATE (§6.4: "does the SE hold frame rate") ─────────────────────
    //
    // Frame time on the handset, in windows, so a shadows on/off toggle on the rig panel gives
    // a before/after on ONE build. Pulled off the device with the boot log. A window past
    // 16.7 ms/frame is a STOP, not a tune (cast-shadows round, ruling triggers).
    private readonly System.Collections.Generic.List<double> _frameMs = new();
    private const int PerfWindow = 240;

    private void LogFrameTime(double delta)
    {
        _frameMs.Add(delta * 1000.0);
        if (_frameMs.Count < PerfWindow) return;
        var sorted = new System.Collections.Generic.List<double>(_frameMs);
        sorted.Sort();
        double mean = 0; foreach (var v in sorted) mean += v; mean /= sorted.Count;
        double p95 = sorted[(int)(sorted.Count * 0.95)];
        double max = sorted[sorted.Count - 1];
        string line = $"[Perf] window={PerfWindow} mean_ms={mean:0.00} p95_ms={p95:0.00} " +
                      $"max_ms={max:0.00} fps={1000.0 / mean:0.0} " +
                      $"({_reviewLighting?.Settings()})";
        GD.Print(line);
        Diag.Log(line);
        _frameMs.Clear();
    }

    private static string? ReadStringArg(string flag)
    {
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length; i++)
            if (args[i] == flag && i + 1 < args.Length) return args[i + 1];
        return null;
    }

    /// <summary>
    /// Assemble the light rig from --light-* flags. Every flag is REQUIRED when
    /// --corridor-scene is used: a missing value aborts the run rather than substituting one,
    /// so no capture can be produced by an undeclared rig.
    /// </summary>
    private static ReviewLighting.Params ReadLightParams()
    {
        string Require(string flag)
        {
            var v = ReadStringArg(flag);
            if (string.IsNullOrEmpty(v))
                throw new System.InvalidOperationException(
                    $"--corridor-scene requires {flag}. The Boundary's rig is RULED (§6.2.1, "
                    + "Ruling 56) and every other region's is still PLACEHOLDER; either way this "
                    + "harness refuses to supply one — state it explicitly, so no capture can be "
                    + "produced by an undeclared rig.");
            return v!;
        }

        return new ReviewLighting.Params(
            Ambient:     new Color(Require("--light-ambient")),
            LightColor:  new Color(Require("--light-color")),
            Energy:      float.Parse(Require("--light-energy"),
                             System.Globalization.CultureInfo.InvariantCulture),
            RadiusTiles: float.Parse(Require("--light-radius-tiles"),
                             System.Globalization.CultureInfo.InvariantCulture),
            // RULED at the §6.2.1 gate (Ruling 56). REQUIRED rather than defaulted: these two
            // were code defaults while the values were PLACEHOLDER, and now that they are law a
            // default would let a ratified rig drift without anything saying so.
            Falloff:     float.Parse(Require("--light-falloff"),
                             System.Globalization.CultureInfo.InvariantCulture),
            AmbientLevel: float.Parse(Require("--light-ambient-level"),
                             System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Read a spec file through Godot's FileAccess rather than System.IO. On iOS and Android the
    /// res:// tree is packed inside the .pck and System.IO cannot see it at all — the desktop
    /// path would work and the device path, which is the one §13.1 actually cares about, would
    /// fail at runtime.
    /// </summary>
    private static string ReadTextOrThrow(string path)
    {
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null)
            throw new System.IO.FileNotFoundException($"Tier 0: cannot read '{path}'.");
        return f.GetAsText();
    }

    private void LaunchCorridorScene(string jsonPath, ReviewBuildMarker? marker = null)
    {
        GetNode<CanvasLayer>("MenuLayer").Visible = false;
        _currentDepth = 1;

        var spec = CorridorReviewSceneBuilder.ParseSpecJson(ReadTextOrThrow(jsonPath));
        _state = CorridorReviewSceneBuilder.Build(spec);
        SetupPresentation(_state);

        // The junction is load-bearing (the critic is asked which way they would walk), so its
        // presence is asserted and reported rather than assumed from the spec.
        bool hasJunction = CorridorReviewSceneBuilder.HasJunction(_state.Map, out var junctionAt);
        if (!hasJunction)
            GD.PrintErr("[Tier0] ABORT: carved geometry contains no junction — a straight corridor "
                        + "cannot answer 'which way would you walk'. Fix the carve list.");

        // CLI flags win over the marker, so a review build can still be driven explicitly on
        // desktop; the marker only supplies the rig where there is no command line to read.
        // Remembered for the capture-time luminance probe (see ProbeJunctionLuminance). The
        // reference cell is one tile BELOW the player: it is lit floor with no sprite standing on
        // it, so it measures the light rather than the player.
        _junctionTile = hasJunction ? junctionAt : null;
        _litReferenceTile = (_state.Player.X, _state.Player.Y + 1);
        _legibility = spec.Legibility;

        var lighting = new ReviewLighting(marker?.Light ?? ReadLightParams());
        lighting.Attach(GetNode<Node2D>("GameView"), _renderer!.TileWidth, _renderer.TileHeight,
                        _state.Player.X, _state.Player.Y);
        _reviewLighting = lighting;   // retained so _Process can keep the lamp on the player

        // The floor overlays need the rig to anchor §12.1's contact occlusion to AMBIENT rather
        // than let it scale away with the lamp. Handed over BEFORE the overlays attach, because
        // the occlusion's layer count is decided as each cell is drawn.
        Tier1FloorOverlays.UseLighting(lighting);

        // §12.1a IS NOT WIRED, AND THE ATTEMPT IS RECORDED RATHER THAN SHIPPED.
        //
        // `ReviewLighting.AddOccluders` implements the clause and MEASURES WORSE. A
        // LightOccluder2D covering a wall cell blocks the light before it reaches that cell's own
        // sprite, so the wall shadows its own face: exposed wall fell 37.90 -> 5.51 against a
        // floor unchanged at 41.35, and "the lamp stops at the wall face" became "the lamp stops
        // at the wall". Turning culling on (only far edges occlude) did not move it — 5.51 either
        // way, because the player is south of the face and the occluder's south edge is reached
        // first whichever winding casts.
        //
        // The clause is right and this implementation of it is not. What it needs is an occluder
        // that begins BEHIND the reveal — §3 already defines that split at `FACE_TOP_ROW` — or a
        // shadow pass that lights the first surface it meets and stops. Outstanding, deliberately
        // un-wired, and not left as dead code by accident: the method stays so the next attempt
        // starts from a measured failure rather than a blank page.

        // THE RIG LADDER — §6.2.1's tuning pass, on device, in Rafe's hands. Review builds only:
        // this is reached from LaunchCorridorScene, which a player build never enters.
        var rigPanel = new ReviewRigPanel(lighting);
        _rigPanel = rigPanel;
        GetNode<Control>("UILayer/ViewportOverlay").AddChild(rigPanel);

        // ── A BYPASSED REVIEW GATE IS VISIBLE FROM THE PHONE ──────────────────────────────────
        //
        // Art rounds are judged by eyes on delivered frames, and no build reaches this handset
        // without a frame-critic PASS for that exact build — unless somebody set YARL_SKIP_CRITIC
        // and waved it through, which is legitimate for producing something to MEASURE and is
        // never a build to take a verdict on.
        //
        // The two are indistinguishable in the hand, and that is the whole hazard: a measurement
        // build gets walked as a gate build and a verdict is taken on a frame no critic ever saw.
        // So the stamp is drawn ON SCREEN rather than only logged. A log nobody pulls is not a
        // warning, and every process rule in this project that depended on being remembered was
        // eventually not remembered.
        if (!string.IsNullOrEmpty(marker?.ReviewStatus))
        {
            var warn = new Label
            {
                Text = marker!.ReviewStatus,
                Modulate = new Color(1f, 0.35f, 0.35f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Position = new Vector2(8, 8),
                ZIndex = 4096,
            };
            warn.AddThemeFontSizeOverride("font_size", 18);
            GetNode<Control>("UILayer/ViewportOverlay").AddChild(warn);
        }

        // Through Diag as well as GD.Print: on iOS there is no console to read, and GD.Print goes
        // nowhere retrievable. Diag writes to the app container, which can be pulled back off the
        // device — which is the only way to show that a review build actually booted the corridor
        // rather than the menu. Verified, not assumed.
        void Report(string line) { GD.Print(line); Diag.Log(line); }

        // FIRST LINE, DELIBERATELY. A device walk is only evidence if the walk can say what it
        // was built from (LOOP-PROCESS §2.3), and this is the line a log-pull greps for.
        // The BUNDLE ID is deliberately NOT self-reported: an app claiming its own identity is
        // the weakest possible evidence of it. It is read back off the handset with devicectl,
        // which is the authority. What only the app can supply is what SOURCE it was built from,
        // and that is what this line carries.
        Report($"[Tier1] BUILD IDENTITY: commit={marker?.Commit ?? "UNSTAMPED"} "
               + $"built={marker?.BuiltAt ?? "UNSTAMPED"} "
               + $"review={marker?.ReviewStatus ?? "GATED"} "
               + $"app={ProjectSettings.GetSetting("application/config/name")}");
        Report($"[Tier0] corridor scene: {spec.Name} ({jsonPath})");
        Report($"[Tier0] map={spec.Width}x{spec.Height} player=({spec.PlayerX},{spec.PlayerY}) "
               + $"carve_rects={spec.Carve.Count}");
        Report($"[Tier0] junction={(hasJunction ? $"YES at ({junctionAt.X},{junctionAt.Y})" : "NO")}");
        Report($"[Tier0] tile_theme_config={TileThemeLoader.ActiveConfigPath}");
        Report($"[Tier0] light rig: {lighting.Describe(_renderer.TileWidth, _renderer.TileHeight)}");

        // THE GRID, IN CAPTURED PIXELS. Emitted so an off-line measurement does not have to
        // reverse-engineer the camera to know which pixels are which tile.
        //
        // Every measurement this project takes off a capture — the value stack, the perceptual
        // floor, a field census — needs the tile lattice in image coordinates, and until now each
        // one either guessed it or measured something adjacent to it. A guessed grid does not
        // fail loudly: it reports a confident number about the wrong pixels, which is the failure
        // mode §13.5 exists for. The engine already knows the answer exactly; it just never said
        // it out loud.
        {
            var gv = GetNodeOrNull<Node2D>("GameView");
            if (gv != null)
            {
                var gx = gv.GetGlobalTransform();
                var o = gx * _renderer.GridToScreenCenter(0, 0);
                var o1 = gx * _renderer.GridToScreenCenter(1, 1);
                var vp = GetNodeOrNull<Control>("UILayer/ViewportOverlay");
                var band = vp != null ? vp.GetGlobalRect() : new Rect2(0, 0, 0, 0);
                Report($"[Tier0] grid map: centre00=({o.X:0.###},{o.Y:0.###}) "
                     + $"pitch=({o1.X - o.X:0.###},{o1.Y - o.Y:0.###}) "
                     + $"view=({band.Position.X:0.###},{band.Position.Y:0.###})..."
                     + $"({band.End.X:0.###},{band.End.Y:0.###})");
            }
        }


        // PRECONDITION 1 (floor session two): what did the variant picker ACTUALLY lay?
        // Emitted on every review capture, overlays or not, so the linear-hash defect cannot
        // return unnoticed — LOOP-PROCESS §4.2's "what goes red if it silently stops holding?"
        if (_tileLayer != null)
            Report(FloorVariantCensus.Describe(_tileLayer, _state.Map));

        // THE INCIDENT SYSTEM — §8.3's overlays and §8.2.1's trodden channel, placed per cell.
        // Reported unconditionally, including when there is no manifest: a scene that quietly
        // drew no overlays looks exactly like a scene whose floor has no incident in it, and
        // LOOP-PROCESS §4.2 asks of every step what goes red if it silently does nothing.
        // CLI wins over the marker, same precedence the light rig already uses: a review build
        // has no command line, and a headless capture has no marker. Both paths must be able to
        // reach the overlay system or the seats would be judging a floor with no incident on it
        // while the device showed one — two different floors under one name.
        string? overlayManifest = ReadStringArg("--floor-overlays") ?? marker?.FloorOverlays;
        Dictionary<(int X, int Y), FloorIncident>? incidentPlan = null;
        if (_tileLayer != null && overlayManifest is { Length: > 0 })
        {
            // The channel wash is suppressed when the edge-matched family is in play: that family
            // carries the channel in its own material, and drawing the wash on top of it puts a
            // flat per-cell value block over the very thing it is meant to express.
            bool wangActive = (ReadStringArg("--wang-floor") ?? marker?.WangFloor)
                              is { Length: > 0 }
                              || (ReadStringArg("--ashlar-floor") ?? marker?.AshlarFloor)
                              is { Length: > 0 };
            // The ashlar family draws its own incident at field scale and carries the channel in
            // its own stones, so both of the overlay system's per-tile treatments are switched
            // off under it. What remains is the occlusion, which is §12.1's form and not a mark.
            // THE ASHLAR FAMILY PAINTS §12.1's BOUNDARY ITSELF, so the sprite must not also be
            // laid over the top. The overlay's version is an alpha blend toward rgb(22,22,22)
            // and compositing it took the family's nine authored albedo values to a hundred and
            // thirty-two — a continuous-tone layer over a quantised floor. The decision is still
            // made by the overlay planner and handed to the floor; only the DRAWING moves.
            bool ashlarActive = (ReadStringArg("--ashlar-floor") ?? marker?.AshlarFloor)
                                is { Length: > 0 };
            Report("[Tier1] " + Tier1FloorOverlays.Attach(_tileLayer, _state.Map,
                                                          overlayManifest, _baseSeed,
                                                          out incidentPlan,
                                                          out var occBake,
                                                          drawChannel: !wangActive,
                                                          drawMarks: !wangActive,
                                                          drawOcclusion: !ashlarActive));
            _occlusionBake = occBake;
        }
        else
            Report("[Tier1] floor overlays: none declared (no --floor-overlays, no marker "
                   + "floorOverlays) — base tiles only");

        // THE EDGE-MATCHED FLOOR. Runs AFTER the overlays so it can use their channel decision,
        // and swapping a sprite's texture does not disturb the overlay children already parented
        // to it. --wang-floor selects it; absent, the scene keeps whatever the theme picked.
        string? wangManifest = ReadStringArg("--wang-floor") ?? marker?.WangFloor;
        if (_tileLayer != null && wangManifest is { Length: > 0 })
        {
            var plan = incidentPlan;
            Report(Tier1WangFloor.Apply(_tileLayer, _state.Map, wangManifest,
                (x, y) => plan != null && plan.TryGetValue((x, y), out var inc)
                          && inc.Channel != ChannelKind.None));
        }
        else
        {
            Report("[Tier1] wang floor: none declared (no --wang-floor, no marker wangFloor)");
        }

        // THE COURSE-ALIGNED ASHLAR FLOOR, which supersedes the edge-matched one under rulings
        // (1) and (2). It does not merely choose a texture: the shipped asset is the BOND, and
        // every stone's value and grain are painted here from that stone's world address, because
        // anything the TILE knows repeats wherever the family pattern repeats and a value on the
        // tile lattice is section 8.3.1 arriving through value instead of shape.
        string? ashlarManifest = ReadStringArg("--ashlar-floor") ?? marker?.AshlarFloor;
        if (_tileLayer != null && ashlarManifest is { Length: > 0 })
        {
            var aplan = incidentPlan;
            Report(Tier1AshlarFloor.Apply(_tileLayer, _state.Map, ashlarManifest,
                (x, y) => aplan != null && aplan.TryGetValue((x, y), out var inc)
                          && inc.Channel != ChannelKind.None,
                _occlusionBake));
        }
        else
        {
            Report("[Tier1] ashlar floor: none declared (no --ashlar-floor, no marker "
                   + "ashlarFloor)");
        }
        // THE WALLS. Last, because it replaces sprites the floor systems never touch and because
        // a wall family that failed to attach must be visible as the magenta mock rather than as
        // a plausible grey — the same reasoning that made the floor's fallback tile magenta.
        string? wallManifest = ReadStringArg("--boundary-wall") ?? marker?.BoundaryWall;
        if (_tileLayer != null && wallManifest is { Length: > 0 })
        {
            string? vArg = ReadStringArg("--void-choice");
            int voidChoice = vArg != null && int.TryParse(vArg, out int vv) ? vv
                           : marker?.VoidChoice ?? 0;
            // A CAPTURE-TIME DEPARTURE FROM A RULED MANIFEST VALUE, declared on the command line
            // so it lands in the log of every capture it produced. See Tier1BoundaryWall.Apply.
            string? rArg = ReadStringArg("--void-ring");
            // The marker is the device's command line: an iOS app has none, and without this the
            // handset ran the manifest's ruled 0 while every capture it is walked against was
            // taken at 1. Same precedence as every other flag here — CLI first, marker second.
            int? voidRing = rArg != null && int.TryParse(rArg, out int rr) ? rr
                          : marker?.VoidRing;
            _wallManifest = wallManifest;
            _voidChoice = voidChoice;
            string? bindings = ReadStringArg("--wall-bindings") ?? marker?.WallBindings;
            string? capManifest = ReadStringArg("--wall-cap") ?? marker?.WallCap;
            Report(Tier1BoundaryWall.Apply(_tileLayer, _state.Map, wallManifest, voidChoice,
                                           bindings, capManifest, voidRing));
            var layer = _tileLayer;
            var map = _state.Map;
            _rigPanel?.AddVoidRow(Tier1BoundaryWall.LastVoidCount, () => _voidChoice, v =>
            {
                _voidChoice = v;
                string line = Tier1BoundaryWall.Apply(layer, map, wallManifest, v, bindings,
                                                      capManifest, voidRing);
                GD.Print(line);
                Diag.Log(line);
            });
        }

        // ── CAST SHADOWS — the lamp meets the walls (§12.1a) and the objects (§3.2) ──────────
        //
        // One mechanism: LightOccluder2D per solid cell and per prop footprint, culled so the
        // occluder begins BEHIND the visible reveal; shadow tint = the ruled ambient hue; the
        // orc fire attached as the second light (#205). `--occluders none` is the control and
        // reproduces every capture taken before this round. CLI first, marker second, as every
        // other flag here — an iOS app has no command line.
        if (_reviewLighting != null && _tileLayer != null)
        {
            string occl = ReadStringArg("--occluders") ?? marker?.Occluders ?? "none";
            var gv = GetNode<Node2D>("GameView");
            if (occl != "none")
            {
                _reviewLighting.AddOccluders(_state.Map, gv, occl);
                _reviewLighting.AddPropOccluders(_state.Props, _tileLayer, gv, occl);
            }
            int fires = _reviewLighting.AddPropLights(_state.Props, gv);
            string? softArg = ReadStringArg("--shadow-softness");
            _reviewLighting.ShadowSoftness =
                softArg != null && float.TryParse(softArg, System.Globalization.NumberStyles.Float,
                                                  System.Globalization.CultureInfo.InvariantCulture,
                                                  out float sf) ? sf
                : marker?.ShadowSoftness ?? 12.0f;  // RE-RATIFIED (Rafe, 2026-09-13): "no beams at 12"
            string? dkArg = ReadStringArg("--shadow-darkness");
            if (dkArg != null && float.TryParse(dkArg, System.Globalization.NumberStyles.Float,
                                                System.Globalization.CultureInfo.InvariantCulture,
                                                out float dk))
                _reviewLighting.ShadowDarkness = dk;
            else
                _reviewLighting.ShadowDarkness = marker?.ShadowDarkness ?? 0.8f;   // Rafe's mark
            string? flArg = ReadStringArg("--fire-flicker");
            // RULED ON (Rafe, shadow walk, 2026-09-13): the tended exception over §9.2 — the orc fire
            // is the one thing in the world that moves. Default on; the panel row can still show
            // the still state for comparison.
            _reviewLighting.FireFlicker = flArg != null ? flArg == "1" : (marker?.FireFlicker ?? true);
            Report($"[Tier1] shadows: mode={occl} wall_occluders={_reviewLighting.OccluderCount} " +
                   $"prop_occluders={_reviewLighting.PropOccluderCount} fire_lights={fires} " +
                   $"softness={_reviewLighting.ShadowSoftness:0.#} " +
                   $"flicker={(_reviewLighting.FireFlicker ? "on" : "off")} " +
                   "(§12.1a occlusion; §3.2 footprints; #205 the fire emits; flicker RULED ON — Rafe, 2026-09-13)");
            _rigPanel?.AddShadowRows();
        }
        else
        {
            Report("[Tier1] boundary wall: none declared (no --boundary-wall, no marker "
                   + "boundaryWall) — the walls in this capture are the tier-0 magenta mocks");
        }
        Report($"[Tier0] review_build_marker={(marker != null ? ReviewBuildMarker.Path : "none (CLI flags)")}");
        // Reported so the device log proves the no-losable-state fix is actually on the device.
        // The defect was turn_limit=1, which ended the run on the first step and surfaced as the
        // end-of-run overlay — read on device as the player dying.
        Report($"[Tier0] losable-state check: turn_limit={_state.TurnLimit} "
               + $"monsters={_state.Monsters.Count} ending={_state.Ending} "
               + $"alive={_state.PlayerFighter.IsAlive} game_over={_state.IsGameOver}");
    }

    /// <summary>
    /// --art-scene-capture: log the worn-tile (3001) in-frame report, save the settled
    /// viewport to --capture-out, and quit. Called once _pendingCameraSnapFrames has
    /// reached 0 and the extra settle buffer has elapsed (see _Process).
    /// </summary>
    /// <summary>
    /// Instrument threshold, NOT an art value. The junction must reach at least this fraction of
    /// the luminance of lit floor beside the player.
    ///
    /// Set by measurement, not taste. Sweeping light.radius_tiles against this scene:
    ///
    ///     radius  5.5 -> 0.5301   green   (the configured working value)
    ///     radius  5.0 -> 0.4586   green
    ///     radius  4.5 -> 0.3724   green
    ///     radius  4.0 -> 0.2766   green   (marginal)
    ///     radius  3.5 -> 0.1767   RED
    ///     radius  3.0 -> 0.1200   RED
    ///
    /// 0.25 puts the boundary between radius 4.0 and 3.5, which is where the junction stops being
    /// legible — matching the coupling reported from the Tier 0 session ("below ~4 the junction
    /// goes dark"). Note the honest caveat: 4.0 clears the bar by only ~10%, so the threshold is
    /// NOT comfortably clear of the marginal case. It is comfortably clear of the WORKING case,
    /// which is the one that matters: 5.5 sits at 2.1x the threshold.
    ///
    /// Recorded here rather than in harness_config.yaml because it describes how this check
    /// works, not how the art should look.
    /// </summary>
    private const float JunctionLitMinRatio = 0.25f;

    /// <summary>
    /// Floor-scene legibility bounds, as a fraction of lit floor one tile from the player.
    /// INSTRUMENT THRESHOLDS, not art values — set by measurement, like JunctionLitMinRatio above.
    ///
    /// Swept on the ratified rig (Ruling 56: radius 5.0, falloff 1.00, ambient 0.70) with
    /// tools/tier1_floors/sweep_legibility.py. Ratios against lit floor at (8,7):
    ///
    ///     RADIUS DOWN, ambient held at 0.70          AMBIENT UP, radius held at 5.0
    ///       9.0   min lit 0.427                        0.3   max dark 0.025
    ///       6.0   min lit 0.126                        0.7   max dark 0.059   &lt;- the rig
    ///       5.0   min lit 0.060  &lt;- the rig            1.5   max dark 0.115
    ///       4.0   min lit 0.064                        3.0   max dark 0.203
    ///
    /// THE AMBIENT FLOOR IS ~0.058, and it is what makes these numbers derivable rather than
    /// chosen. Both declared dark points sit there at the ratified rig, and so does a point at
    /// the nominal 5.0-tile radius — 0.060. A "lit" point is one meaningfully ABOVE that floor,
    /// not one above some absolute brightness, which is why the outermost declared lit point is
    /// at four tiles (0.159, 2.7x the floor) and not at five.
    ///
    ///   0.12  LIT MINIMUM — twice the ambient floor. The weakest declared lit point clears it by
    ///         33%; a point at the nominal radius does not clear it at all, which is the honest
    ///         result rather than a threshold tuned until it did.
    ///   0.10  DARK MAXIMUM — the dark points clear it by 41% at the ratified ambient and breach
    ///         it at roughly ambient 1.3, so the guard reds when the arc has been flooded to
    ///         about twice its ratified level. §6.2.1: the pass is "not a licence to flood the
    ///         Boundary with light", and a brightness-only check cannot see that failure at all.
    ///
    /// ⚠ These bound the INSTRUMENT, and the eye still rules (§13.2). A ratio of 0.06 may well be
    /// perfectly visible on a phone in a dark room; what the sweep establishes is that nominal
    /// radius and delivered reach are different quantities, not that any particular tile is
    /// invisible to a person. Do NOT lower them to make a capture pass.
    /// </summary>
    // ⚠ THE RATIO BOUNDS ARE RETIRED — RULED (Rafe, 2026-09-07). They are kept, unused, as the
    // provenance of the absolute bounds that replaced them: each declared point's `bound_lum` was
    // derived as `reference_luminance_on_the_nulled_build * (this ratio)`, at the ratified rig,
    // which preserves the pass state those bounds had on the day they were replaced.
    //
    // THE LAW THIS COST: **an instrument whose reference can saturate measures the ceiling, not
    // the scene.** The reference cell — lit floor beside the player — clipped at 255, so every
    // dark declaration was a ratio against a pinned value. A highlight shoulder that left every
    // dark pixel BYTE-IDENTICAL made two of them "fail", because only the denominator moved.
    private const float RetiredLegibleMinRatio = 0.12f;
    private const float RetiredDarkMaxRatio = 0.10f;

    /// <summary>Mean perceived luminance of a small patch, clipped to the image.</summary>
    private static float PatchLuminance(Image img, Vector2 centre, int half)
    {
        int cx = Mathf.RoundToInt(centre.X), cy = Mathf.RoundToInt(centre.Y);
        float sum = 0f; int n = 0;
        for (int y = cy - half; y <= cy + half; y++)
        {
            for (int x = cx - half; x <= cx + half; x++)
            {
                if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
                var c = img.GetPixel(x, y);
                sum += 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;   // Rec.709
                n++;
            }
        }
        return n == 0 ? 0f : sum / n;
    }

    /// <summary>
    /// True when the junction is lit well enough to be seen. Logs the measurement either way, so
    /// a capture carries the evidence that its junction was legible.
    /// </summary>
    /// <summary>
    /// The floor scene's declared points must read, and the dark ones must stay dark.
    ///
    /// Same shape and same reasoning as <see cref="ProbeJunctionLuminance"/> — measured on the
    /// captured pixels rather than asserted from radius arithmetic, and RELATIVE to lit floor
    /// beside the player so it self-calibrates against ambient and energy. What it adds is the
    /// second direction: a point declared dark must not brighten. §6.2.1 rules the readability
    /// pass "not a licence to flood the Boundary with light", so a guard that only asked whether
    /// the scene was bright enough would green a drowned arc — the opposite failure, equally
    /// fatal, and the one a brightness check is blind to by construction.
    /// </summary>
    private bool ProbeFloorLegibility(Image image)
    {
        if (_legibility == null || _legibility.Count == 0 || _litReferenceTile == null
            || _renderer == null)
            return true;   // no declared points — nothing to guard

        var gameView = GetNode<Node2D>("GameView");
        var xform = gameView.GetGlobalTransform();
        Vector2 ScreenOf(int x, int y) => xform * _renderer.GridToScreenCenter(x, y);
        int half = Mathf.Max(1, Mathf.RoundToInt(_renderer.TileWidth * gameView.Scale.X * 0.25f));

        float rLum = PatchLuminance(image,
            ScreenOf(_litReferenceTile.Value.X, _litReferenceTile.Value.Y), half);

        bool ok = true;
        foreach (var p in _legibility)
        {
            var at = ScreenOf(p.X, p.Y);

            // A DECLARED POINT THAT IS NOT ON SCREEN CANNOT BE CHECKED, and silently measuring it
            // anyway is the exact failure this guard exists to prevent, occurring inside the
            // guard. PatchLuminance clips to the image, so an off-screen point returns the
            // luminance of whatever sits at the clipped edge — the HUD, the letterbox — and the
            // probe reports a confident number about the interface.
            //
            // Measured: the first two dark points declared for this scene landed at x=-9 and
            // y=1371 on a 750x1334 capture, and read 0.059 and 0.160. Neither was floor. One
            // "dark" point came back brighter than a "lit" one, which is what sent this back for
            // a look. A point off the edge is a SPEC error and fails loudly rather than passing.
            //
            // AND IMAGE BOUNDS ALONE WERE NOT ENOUGH. The replacement point landed at y=1307 —
            // inside a 1334-tall image and inside the HUD's button row. It read 0.796 and got
            // DARKER as ambient rose, which is backwards for floor and is the signature of
            // measuring interface. The region that matters is where the DUNGEON is drawn, and the
            // game already names it: UILayer/ViewportOverlay is the control whose rect is the
            // viewing area. A point must be inside THAT, not merely inside the PNG.
            int m = half + 1;
            var vp = GetNodeOrNull<Control>("UILayer/ViewportOverlay");
            var band = vp != null ? vp.GetGlobalRect()
                                  : new Rect2(0, 0, image.GetWidth(), image.GetHeight());
            if (at.X < band.Position.X + m || at.Y < band.Position.Y + m
                || at.X >= band.End.X - m || at.Y >= band.End.Y - m)
            {
                string off =
                    $"[Tier1] legibility({p.X},{p.Y}) OUTSIDE THE DUNGEON VIEW at "
                  + $"px({at.X:0},{at.Y:0}) — view is "
                  + $"({band.Position.X:0},{band.Position.Y:0})..({band.End.X:0},{band.End.Y:0}). "
                  + $"A declared point that cannot be seen cannot be checked. — {p.Why}";
                GD.PrintErr(off);
                Diag.Log(off);
                ok = false;
                continue;
            }

            float lum = PatchLuminance(image, at, half);
            // ABSOLUTE, not relative. The question is "can a viewer see this point", which is a
            // property of the delivered frame and of nothing else in it.
            bool pass = p.MustBeLit ? lum >= p.BoundLum : lum <= p.BoundLum;
            // The reference is still MEASURED and still PRINTED — it is a useful datum and the
            // record of how these bounds were derived — but it no longer decides anything.
            float ratio = rLum <= 0.0001f ? 0f : lum / rLum;
            string line =
                $"[Tier1] legibility({p.X},{p.Y}) expect={(p.MustBeLit ? "lit " : "dark")} "
              + $"lum={lum:0.0000} at px({at.X:0},{at.Y:0}) "
              + $"bound={p.BoundLum:0.0000} "
              + $"{(pass ? "OK" : "FAIL")}  (ratio={ratio:0.0000}, informational)  - {p.Why}";
            GD.Print(line);
            Diag.Log(line);
            if (!pass) ok = false;
        }

        string summary = $"[Tier1] floor-legibility probe: {_legibility.Count} declared points, "
                       + $"reference lum={rLum:0.0000} (informational — bounds are ABSOLUTE "
                       + $"since 2026-09-07), verdict={(ok ? "PASS" : "FAIL")}";
        GD.Print(summary);
        Diag.Log(summary);

        if (!ok)
        {
            GD.PrintErr("[Tier1] FLOOR-LEGIBILITY CHECK FAILED. Either the subject has fallen "
                        + "outside the carried light, or the arc has been flooded and ground the "
                        + "scene declares should be dark is not. Fix the rig or the scene - do "
                        + "NOT lower these thresholds.");
        }
        return ok;
    }

    private bool ProbeJunctionLuminance(Image image)
    {
        if (_junctionTile == null || _litReferenceTile == null || _renderer == null)
            return true;   // not a corridor review capture — nothing to guard

        var gameView = GetNode<Node2D>("GameView");
        var xform = gameView.GetGlobalTransform();

        Vector2 ScreenOf((int X, int Y) t)
            => xform * _renderer.GridToScreenCenter(t.X, t.Y);

        // Half a tile at the current zoom, so the patch stays inside the tile it is measuring.
        int half = Mathf.Max(1, Mathf.RoundToInt(_renderer.TileWidth * gameView.Scale.X * 0.25f));

        var jScreen = ScreenOf(_junctionTile.Value);
        var rScreen = ScreenOf(_litReferenceTile.Value);

        float jLum = PatchLuminance(image, jScreen, half);
        float rLum = PatchLuminance(image, rScreen, half);
        float ratio = rLum <= 0.0001f ? 0f : jLum / rLum;

        string line =
            $"[Tier0] junction-lit probe: junction({_junctionTile.Value.X},{_junctionTile.Value.Y}) "
          + $"lum={jLum:0.0000} at px({jScreen.X:0},{jScreen.Y:0}) | "
          + $"reference({_litReferenceTile.Value.X},{_litReferenceTile.Value.Y}) lum={rLum:0.0000} "
          + $"at px({rScreen.X:0},{rScreen.Y:0}) | ratio={ratio:0.0000} "
          + $"min={JunctionLitMinRatio:0.0000} patch={2 * half + 1}px";
        GD.Print(line);
        Diag.Log(line);

        if (ratio >= JunctionLitMinRatio)
            return true;

        GD.PrintErr($"[Tier0] JUNCTION-LIT CHECK FAILED — ratio {ratio:0.0000} < {JunctionLitMinRatio:0.0000}.");
        GD.PrintErr("[Tier0] The junction is outside the carried light's reach. The scene still "
                    + "renders and would still pass determinism, but the critic cannot see the "
                    + "choice it is being asked about — the capture would measure nothing (MISFED).");
        GD.PrintErr("[Tier0] Fix: raise light.radius_tiles, or move the junction closer to the "
                    + "player in the corridor spec. Do NOT lower this threshold.");
        return false;
    }

    private void CaptureAndQuit()
    {
        _pendingCapture = false;

        if (string.IsNullOrEmpty(_captureOutputPath))
        {
            GD.PrintErr("[Main] --art-scene-capture requires --capture-out <path>. Not capturing.");
            GetTree().Quit(1);
            return;
        }

        LogWornTilePositions();

        var image = GetViewport().GetTexture().GetImage();

        // ── The junction must be VERIFIABLY LIT, not merely present ─────────────────────────
        //
        // This is the MISFED guard. Junction placement is coupled to the carried light's reach:
        // at radius 5.5 the junction sits 3 tiles ahead and reads clearly, but shrink the radius
        // and the junction falls outside the lit area. The capture still renders, still looks
        // clean, and still passes the determinism control — and the blind critic is then asked
        // "which way would you walk" while standing in front of an invisible junction. Its answer
        // is garbage that reads as data.
        //
        // Measured on the ACTUAL CAPTURED PIXELS rather than asserted from the radius arithmetic.
        // An assertion on the geometry would only re-encode this file's assumptions about the
        // light model — falloff shape, energy scale, blend mode — and MISFED is exactly the case
        // where the numbers look fine. Sampling the image measures what the critic will see.
        //
        // The test is RELATIVE, not an absolute luminance floor: the junction is compared against
        // lit floor one tile from the player. That self-calibrates against ambient and energy, so
        // re-tuning the rig (which the §6.4 probe will do) cannot silently invalidate the check.
        // The floor scene's own declared points. The junction guard above no-ops where the
        // geometry has no junction, which is every floor review scene — junction=NO, guard
        // skipped, capture written. A floor capture therefore had no legibility check at all,
        // and MISFED could walk straight through the one artefact §13.1 gives the verdict to.
        if (!ProbeFloorLegibility(image))
        {
            GD.PrintErr("[Tier1] CAPTURE REFUSED — a declared legibility point failed.");
            if (!string.IsNullOrEmpty(_captureOutputPath))
                GD.PrintErr($"[Tier1] No PNG written to {_captureOutputPath}.");
            GetTree().Quit(2);
            return;
        }

        if (!ProbeJunctionLuminance(image))
        {
            // Loud, and it BLOCKS THE CAPTURE. A dark junction must not produce a usable artifact,
            // because a usable artifact is one that gets reviewed.
            GD.PrintErr("[Tier0] CAPTURE REFUSED — no PNG written.");
            GetTree().Quit(2);
            return;
        }
        var dir = System.IO.Path.GetDirectoryName(_captureOutputPath);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);
        var err = image.SavePng(_captureOutputPath);
        if (err != Error.Ok)
            GD.PrintErr($"[Main] Capture save failed ({err}): {_captureOutputPath}");
        else
            GD.Print($"[Main] Capture written: {_captureOutputPath}");

        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    /// <summary>
    /// The tile-coordinate rect actually visible in the playable strip between the HUD
    /// margins — same math PlayerCamera.Update used to position/scale _gameView, inverted.
    /// Read-only consumption of existing camera state; does not modify rendering.
    /// </summary>
    private (int x0, int y0, int x1, int y1) ComputeVisibleTileRect()
    {
        var viewport = _gameView!.GetViewport().GetVisibleRect().Size;
        var topLeftScreen = new Vector2(0, PlayerCamera.UiTopMargin);
        var bottomRightScreen = new Vector2(viewport.X, viewport.Y - PlayerCamera.UiBottomMargin);

        Vector2 ToLocal(Vector2 screen) => (screen - _gameView.Position) / _gameView.Scale;

        var (x0, y0) = _renderer!.ScreenToGrid(ToLocal(topLeftScreen));
        var (x1, y1) = _renderer.ScreenToGrid(ToLocal(bottomRightScreen));
        return (x0, y0, x1, y1);
    }

    /// <summary>
    /// Honest floor_worn (3001) in-frame check (spec §5 merge evidence). Calls
    /// FloorComposer.Compose a second time with the exact inputs DungeonRenderer.Render
    /// already used internally (state.Map, seed: 0 — Render's own default, not reproduced
    /// from a guess) to recover which cells it marked Worn; that dictionary is otherwise
    /// discarded by Render and never returned to the caller. Read-only — does not touch
    /// render code. Reproduces Render's own prop-footprint suppression (worn/accent tiles
    /// under blocking props render as Standard) so a suppressed cell isn't misreported as
    /// "worn and in frame" when it visually isn't.
    /// </summary>
    private void LogWornTilePositions()
    {
        if (_state == null) return;

        var floorMap = FloorComposer.Compose(_state.Map, seed: 0);

        var propFootprint = new HashSet<(int, int)>();
        foreach (var p in _state.Props)
            if (p.BlocksMovement)
                for (int fx = p.X; fx < p.X + p.FootprintW; fx++)
                    for (int fy = p.Y; fy < p.Y + p.FootprintH; fy++)
                        propFootprint.Add((fx, fy));

        var wornPositions = floorMap
            .Where(kv => kv.Value == FloorTileType.Worn && !propFootprint.Contains(kv.Key))
            .Select(kv => kv.Key)
            .OrderBy(p => p.Y).ThenBy(p => p.X)
            .ToList();

        var (x0, y0, x1, y1) = ComputeVisibleTileRect();
        var inFrame = wornPositions.Where(p => p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1).ToList();

        GD.Print($"[Main] Visible tile rect: ({x0},{y0})-({x1},{y1})");
        GD.Print($"[Main] floor_worn (3001) tiles: {wornPositions.Count} rendered in the authored room, " +
                  $"{inFrame.Count} in frame: " +
                  string.Join(", ", inFrame.Select(p => $"({p.X},{p.Y})")));
    }

    /// <summary>
    /// Load a test scenario YAML by res:// path and start the game.
    /// Hides the menu layer so the dungeon is visible.
    ///
    /// Routing: when scenario.DungeonMode=true, builds a procedural floor via DungeonFloorBuilder
    /// with the scenario's GuaranteedSpawns injected. Player uses CreateDefaultPlayer() stats.
    /// When false (default), routes through GameStateFactory.FromScenario (flat arena, unchanged).
    ///
    /// Test scenarios always use the current _baseSeed (default 1337) for deterministic replay.
    /// </summary>
    private void LaunchTestScenario(string resPath)
    {
        GetNode<CanvasLayer>("MenuLayer").Visible = false;

        var yaml     = ReadGodotResource(resPath);
        var scenario = _contentLoader!.LoadScenario(yaml);

        if (scenario.DungeonMode)
        {
            // Build a LevelOverride from the scenario's guaranteed spawns.
            // Use small map dimensions for fast load times in test scenarios.
            var levelOverride = new LevelOverride
            {
                GuaranteedSpawns = scenario.GuaranteedSpawns,
                Parameters = new GenerationParameters
                {
                    MapWidth = 60,
                    MapHeight = 40,
                    MaxRooms = 20,
                },
            };

            var registry = LevelTemplateRegistry.FromSingleDepth(scenario.Depth, levelOverride);
            _testScenarioBuilder = new DungeonFloorBuilder(
                registry, _monsterFactory!, _itemFactory!, _consumableFactory!,
                spellItemFactory: _spellItemFactory);

            // Deterministic seed: test scenarios use _baseSeed (default 1337), not randomized.
            var rng = new SeededRandom(_baseSeed + scenario.Depth * 1_000_003);
            _currentDepth = scenario.Depth;
            _state = _testScenarioBuilder.Build(scenario.Depth, rng);

            if (scenario.AllItemsIdentified && _state.IdentificationRegistry != null)
                _state.IdentificationRegistry.AlwaysIdentified = true;

            SetupPresentation(_state);
            GD.Print($"Ready (dungeon-mode test scenario: {resPath}) — depth {scenario.Depth}, " +
                     $"{_state.Monsters.Count} monsters, {_state.FloorItems.Count} floor items. Tap to play.");
        }
        else
        {
            _state = GameStateFactory.FromScenario(
                scenario, _baseSeed, _monsterFactory!, _itemFactory!, _consumableFactory!);

            if (scenario.AllItemsIdentified && _state.IdentificationRegistry != null)
                _state.IdentificationRegistry.AlwaysIdentified = true;

            SetupPresentation(_state);
            GD.Print($"Ready (test scenario: {resPath}) — {_state.Monsters.Count} monsters. Tap to play.");
        }

        // Auto-start bot mode if the scenario specifies a default persona.
        if (!string.IsNullOrEmpty(scenario.DefaultBotPersona) && _botDriver != null)
        {
            _botDriver.SetPersona(scenario.DefaultBotPersona);
            _botDriver.Enable();
            GD.Print($"[TestMode] Bot auto-started with persona: {scenario.DefaultBotPersona}");
        }
    }

    /// <summary>
    /// Scan res://config/testing/ and user://testing/ for .yaml files.
    /// Returns a sorted list of (name, path, category) tuples.
    /// Never throws — returns empty list on missing directory.
    /// </summary>
    private static List<(string name, string path, string category)> DiscoverTestScenarios()
    {
        var results = new List<(string, string, string)>();
        ScanTestDirectory("res://config/testing/", results);
        ScanTestDirectory("user://testing/", results);
        results.Sort((a, b) =>
        {
            var cat = string.Compare(a.Item3, b.Item3, System.StringComparison.Ordinal);
            return cat != 0 ? cat : string.Compare(a.Item1, b.Item1, System.StringComparison.Ordinal);
        });
        return results;
    }

    private static void ScanTestDirectory(string dir, List<(string, string, string)> results)
    {
        using var access = DirAccess.Open(dir);
        if (access == null) return;

        access.ListDirBegin();
        string fileName;
        while ((fileName = access.GetNext()) != "")
        {
            if (!fileName.EndsWith(".yaml", System.StringComparison.OrdinalIgnoreCase)) continue;
            var resPath = dir + fileName;
            var (name, category) = ExtractScenarioMeta(resPath, fileName);
            results.Add((name, resPath, category));
        }
        access.ListDirEnd();
    }

    /// <summary>
    /// Extract `name:` and `category:` fields from a scenario YAML file without full deserialisation.
    /// Falls back to filename stem / "Uncategorised" if fields are absent.
    /// </summary>
    private static (string name, string category) ExtractScenarioMeta(string resPath, string fileName)
    {
        string name     = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string category = "Uncategorised";
        try
        {
            using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null) return (name, category);

            var text = file.GetAsText();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("name:", System.StringComparison.Ordinal))
                {
                    var v = StripYamlInline(trimmed["name:".Length..]);
                    if (v.Length > 0) name = v;
                }
                else if (trimmed.StartsWith("category:", System.StringComparison.Ordinal))
                {
                    var v = StripYamlInline(trimmed["category:".Length..]);
                    if (v.Length > 0) category = v;
                }
            }
        }
        catch (System.Exception) { /* Best-effort */ }
        return (name, category);
    }

    private static string StripYamlInline(string raw)
    {
        var v = raw.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1];
        return v;
    }

    /// <summary>
    /// Remove all children from the menu layer so panels are created fresh each time.
    /// Keeps the implementation simple — no panel caching.
    /// </summary>
    private static void ClearMenuLayer(CanvasLayer layer)
    {
        foreach (var child in layer.GetChildren())
            child.SafeFree();
    }

    /// <summary>
    /// Build the zoom +/− button panel. Positioned to the left of the minimap.
    /// Map is always 120×80, so minimap is 240×160 px at 2px/tile.
    /// Minimap OffsetRight = -8, OffsetLeft = -248. Zoom panel sits at OffsetRight = -252, OffsetLeft = -292.
    /// </summary>
    private Control BuildZoomPanel()
    {
        var panel = new VBoxContainer();
        panel.AnchorLeft   = 1f;
        panel.AnchorTop    = 0f;
        panel.AnchorRight  = 1f;
        panel.AnchorBottom = 0f;
        panel.OffsetTop    = 120f; // 210 - 90 (ViewportOverlay starts at StatusBar bottom)
        panel.OffsetLeft   = -292f;
        panel.OffsetRight  = -252f;

        var btnZoomIn = new Button { Text = "+" };
        btnZoomIn.AddThemeFontSizeOverride("font_size", 18);
        btnZoomIn.CustomMinimumSize = new Vector2(36, 36);
        btnZoomIn.Pressed += () =>
        {
            _currentZoom = System.Math.Min(_renderer.MaxZoom, _currentZoom + ZoomStep);
            if (_gameView != null && _state != null)
                PlayerCamera.Update(_gameView, _state.ControlledEntity, _currentZoom, _renderer);
        };

        var btnZoomOut = new Button { Text = "−" };
        btnZoomOut.AddThemeFontSizeOverride("font_size", 18);
        btnZoomOut.CustomMinimumSize = new Vector2(36, 36);
        btnZoomOut.Pressed += () =>
        {
            _currentZoom = System.Math.Max(_renderer.MinZoom, _currentZoom - ZoomStep);
            if (_gameView != null && _state != null)
                PlayerCamera.Update(_gameView, _state.ControlledEntity, _currentZoom, _renderer);
        };

        panel.AddChild(btnZoomIn);
        panel.AddChild(btnZoomOut);

        return panel;
    }

    /// <summary>
    /// Read a file from Godot's resource system. On desktop, res:// maps to the project
    /// directory. On iOS/Android, res:// files are packed inside the .pck bundle and
    /// cannot be accessed via System.IO.File. Godot's FileAccess handles this transparently.
    /// </summary>
    private static string ReadGodotResource(string resPath)
    {
        using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
            throw new System.IO.FileNotFoundException($"Godot resource not found: {resPath}");
        var text = file.GetAsText();
        GD.Print($"[ReadGodotResource] {resPath}: {text.Length} chars, starts with: {text[..System.Math.Min(80, text.Length)]}");
        return text;
    }
}
