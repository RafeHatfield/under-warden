using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.Bot;

/// <summary>
/// Godot Node that drives the player's turns autonomously via BotBrain.
/// Ticks on a configurable timer; on each tick, if the game is waiting for player input,
/// decides an action and submits it via GameController.SubmitBotAction().
///
/// Architecture contract:
/// - All game logic stays in BotBrain (Logic layer). This node is purely a Godot wiring.
/// - Bot actions enter the same PlayerAction → TurnController.ProcessTurn pipeline as human taps.
/// - Bot does NOT drive possessed hosts (ControlledEntity != Player → Disable() is called).
/// - Release builds: never instantiated (caller gates on OS.IsDebugBuild()).
///
/// Usage:
///   var driver = new BotPlayerDriver();
///   AddChild(driver);
///   driver.Initialize(controller, state);
///   driver.Enable();       // Bot starts playing
///   driver.Disable();      // Human regains control
///   driver.SetPersona("aggressive");  // Switch persona mid-session
/// </summary>
public sealed partial class BotPlayerDriver : Node
{
    // Configurable turn delay. Higher = slower and easier to watch.
    public float TurnDelaySeconds { get; set; } = 0.25f; // 4 turns/sec

    public BotPersonaConfig Persona { get; private set; } = BotPersonaRegistry.Get("balanced");
    public bool Enabled { get; private set; }

    /// <summary>
    /// Fired when Enabled changes. True = bot just enabled, False = bot just disabled.
    /// </summary>
    public event Action<bool>? EnabledChanged;

    private GameController? _controller;
    private GameState? _state;
    private BotBrain? _brain; // one instance per Enable() call
    private Godot.Timer? _timer;

    // Called by Godot when this node enters the scene tree.
    public override void _Ready()
    {
        _timer = new Godot.Timer();
        _timer.OneShot = false;
        _timer.WaitTime = TurnDelaySeconds;
        _timer.Timeout += OnTimerTick;
        AddChild(_timer);
    }

    /// <summary>
    /// Attach this driver to an active game session. Must be called before Enable().
    /// Call again when the player transitions to a new floor.
    /// </summary>
    public void Initialize(GameController controller, GameState state)
    {
        _controller = controller;
        _state = state;
    }

    /// <summary>
    /// Set the active bot persona. Change takes effect on the next tick.
    /// Creates a fresh BotBrain instance (resets stuck state) on persona change.
    /// </summary>
    public void SetPersona(string name)
    {
        Persona = BotPersonaRegistry.Get(name);
        if (Enabled)
        {
            // Reset brain to pick up the new persona
            _brain = new BotBrain(Persona);
        }
    }

    /// <summary>
    /// Enable the bot driver. Starts the tick timer and creates a fresh BotBrain instance.
    /// Stuck detection is per-instance — resets cleanly on each Enable() call.
    /// </summary>
    public void Enable()
    {
        if (Enabled) return;
        Enabled = true;
        _brain = new BotBrain(Persona);

        if (_timer != null)
        {
            _timer.WaitTime = TurnDelaySeconds;
            _timer.Start();
        }

        EnabledChanged?.Invoke(true);
        GD.Print($"[BotPlayerDriver] Enabled (persona={Persona.Name}, delay={TurnDelaySeconds}s)");
    }

    /// <summary>
    /// Disable the bot driver. Stops the timer, clears the BotBrain instance.
    /// Human input is fully restored after this call.
    /// </summary>
    public void Disable()
    {
        if (!Enabled) return;
        Enabled = false;
        _brain = null;
        _timer?.Stop();
        EnabledChanged?.Invoke(false);
        GD.Print("[BotPlayerDriver] Disabled");
    }

    // ── Timer tick ────────────────────────────────────────────────────────────

    private void OnTimerTick()
    {
        if (!Enabled) return;
        if (_controller == null || _state == null || _brain == null) return;

        // Disable when game is over — never submit actions to a dead game
        if (_state.IsGameOver)
        {
            Disable();
            return;
        }

        if (_controller.Phase != GameController.GamePhase.WaitingForInput) return;

        // Bot does not drive possessed hosts — possession is a human feature
        if (!ReferenceEquals(_state.ControlledEntity, _state.Player))
        {
            Disable();
            return;
        }

        // Update timer delay in case TurnDelaySeconds was changed via F6
        if (_timer != null && Math.Abs(_timer.WaitTime - TurnDelaySeconds) > 0.001)
            _timer.WaitTime = TurnDelaySeconds;

        // Only pass visible monsters to BotBrain — prevents path-planning through fog.
        // The headless harnesses use all monsters (arenas have full visibility).
        var visibleAlive = _state.Monsters
            .Where(m => _state.Map.IsVisible(m.X, m.Y) && m.Get<Fighter>()?.IsAlive == true)
            .ToList();

        // Floor-clear → Descend: after BotBrain returns None/Wait and floor is clear
        if (visibleAlive.Count == 0 && _state.AliveMonsters.Count == 0 && _state.IsFloorClear)
        {
            // Check if player is on the stair
            if (_state.PlayerOnStairDown)
            {
                _controller.SubmitBotAction(PlayerAction.Descend);
                return;
            }

            // Stair exists but player isn't on it — auto-explore to navigate to stair
            if (_state.StairDown != null)
            {
                // Use StartAutoExplore rather than SubmitBotAction — auto-explore is handled
                // as a controller mode, not a single PlayerAction. It activates the
                // AutoExploreSystem which navigates turn-by-turn until the stair is reached.
                _controller.StartAutoExplore();
                return;
            }
        }

        // No visible enemies and floor not clear → auto-explore to reveal more
        if (visibleAlive.Count == 0 && !_state.IsFloorClear)
        {
            // Don't restart auto-explore if it's already active
            if (!_controller.IsAutoExploreActive)
                _controller.StartAutoExplore();
            return;
        }

        // Decide via BotBrain
        var botAction = _brain.Decide(
            _state.Player,
            _state.PlayerFighter,
            _state.PlayerInventory,
            visibleAlive,
            _state.Map,
            context: null,
            floorItems: _state.FloorItems);

        // AbortRun: disable — bot is stuck, hand back to human
        if (botAction.Type == BotAction.ActionType.AbortRun)
        {
            GD.Print("[BotPlayerDriver] AbortRun — bot stuck, disabling");
            Disable();
            return;
        }

        // Convert to PlayerAction with A* pathing for MoveToward
        var playerAction = BotActionConverter.ToPlayerActionWithPathing(botAction, _state);
        _controller.SubmitBotAction(playerAction);
    }
}
