using UnderWarden.Logic.Core;
using UnderWarden.Presentation.Entities;
using UnderWarden.Presentation.Map;
using UnderWarden.Presentation.UI;
using Godot;

namespace UnderWarden.Presentation.Animation;

/// <summary>
/// Plays turn events as sequential animations.
/// Each event animates in order: move tweens, attack bumps, damage numbers, death fades.
/// Fires AnimationComplete when the full sequence is done.
///
/// Completion detection uses polling (CheckComplete called from GameController._Process)
/// instead of TweenCallback + Callable.From(delegate). The delegate pattern creates a
/// GCHandle on a non-GodotObject instance method that Godot 4.6 C# doesn't reliably
/// release — causing catastrophic memory growth during combat.
/// </summary>
public sealed class TurnAnimator
{
    /// <summary>
    /// Scales all animation durations. 1.0 = normal, 0.25 = fast (auto-explore).
    /// Clamped to 0.05–1.0 so animations never disappear entirely or run slower than normal.
    /// </summary>
    public float SpeedMultiplier
    {
        get => _speedMultiplier;
        set => _speedMultiplier = Math.Clamp(value, 0.05f, 1.0f);
    }
    private float _speedMultiplier = 1.0f;

    // Base durations — scaled by SpeedMultiplier at animation time.
    private float MoveDur => 0.15f * _speedMultiplier;
    private float AttackBumpDur => 0.08f * _speedMultiplier;
    private float AttackReturnDur => 0.08f * _speedMultiplier;
    private float DeathFadeDur => 0.3f * _speedMultiplier;

    private readonly Node _root;
    private readonly EntitySpriteManager _entitySprites;
    private readonly IMapRenderer _renderer;
    private VfxOverlay? _vfxOverlay;
    private bool _playing;
    // Stored so we can Kill() the previous tween before creating the next one.
    private Tween? _activeTween;
    // Tracks whether any TweenProperty calls were actually made during PlayTurn.
    // If all sprites are null, no tweeners are appended and the tween would hang forever.
    private bool _tweenHasSteps;

    public event Action? AnimationComplete;

    public TurnAnimator(Node root, EntitySpriteManager entitySprites, IMapRenderer renderer,
        VfxOverlay? vfxOverlay = null)
    {
        _root = root;
        _entitySprites = entitySprites;
        _renderer = renderer;
        _vfxOverlay = vfxOverlay;
    }

    /// <summary>Assign a VfxOverlay after construction (used during floor transitions).</summary>
    public void SetVfxOverlay(VfxOverlay? overlay) => _vfxOverlay = overlay;

    /// <summary>
    /// Poll from GameController._Process every frame. When the active tween has
    /// finished (IsRunning() returns false), fire AnimationComplete.
    /// No Callable.From, no GCHandle, no delegate — just a bool check per frame.
    /// </summary>
    public void CheckComplete()
    {
        if (!_playing || _activeTween == null) return;
        if (_activeTween.IsRunning()) return;

        Diag.Log("TurnAnimator: tween finished, firing AnimationComplete");
        _activeTween.Kill();
        TweenTracker.Killed();
        _activeTween = null;
        _playing = false;
        AnimationComplete?.Invoke();
    }

    /// <summary>
    /// Play all events from a turn result in sequence.
    /// If no animatable events, completes immediately without allocating a Tween.
    /// </summary>
    public void PlayTurn(TurnResult result)
    {
        if (_playing) return;

        // First pass: check whether there is anything to animate.
        bool hasAnimatable = false;
        foreach (var evt in result.Events)
        {
            if (evt is MoveEvent or AttackEvent or HealEvent or DeathEvent or ThrowEvent
                or SpellEvent or StatusAppliedEvent)
            {
                hasAnimatable = true;
                break;
            }
        }

        if (!hasAnimatable)
        {
            AnimationComplete?.Invoke();
            return;
        }

        _playing = true;

        Diag.Log($"TurnAnimator.PlayTurn: creating tween for {result.Events.Count} events");
        if (_activeTween != null) { _activeTween.Kill(); TweenTracker.Killed(); }
        var tween = _root.CreateTween();
        _activeTween = tween;
        TweenTracker.Created();
        tween.SetParallel(false);

        _tweenHasSteps = false;
        foreach (var evt in result.Events)
        {
            if (evt is MoveEvent or AttackEvent or HealEvent or DeathEvent or ThrowEvent
                or SpellEvent or StatusAppliedEvent)
                AppendEventAnimation(tween, evt);
        }

        // If all sprites were null (e.g., during floor transitions), no TweenProperty
        // calls were made and the tween has zero tweeners. An empty tween never finishes
        // and Godot logs "started with no Tweeners". Kill it and complete immediately.
        if (!_tweenHasSteps)
        {
            Diag.Log("TurnAnimator: tween has no tweeners, completing immediately");
            tween.Kill();
            TweenTracker.Killed();
            _activeTween = null;
            _playing = false;
            AnimationComplete?.Invoke();
            return;
        }

        // No TweenCallback — completion detected by CheckComplete() polling.
    }

    private void AppendEventAnimation(Tween tween, TurnEvent evt)
    {
        switch (evt)
        {
            case MoveEvent move:
                AnimateMove(tween, move);
                break;

            case AttackEvent attack:
                AnimateAttack(tween, attack);
                break;

            case HealEvent:
                tween.TweenInterval(0.1f * _speedMultiplier);
                _tweenHasSteps = true;
                break;

            case DeathEvent death:
                AnimateDeath(tween, death);
                break;

            case ThrowEvent throwEvt:
                AnimateThrow(tween, throwEvt);
                break;

            case SpellEvent spell:
                AnimateSpell(tween, spell);
                break;

            case StatusAppliedEvent status:
                AnimateStatusApplied(tween, status);
                break;
        }
    }

    private void AnimateSpell(Tween tween, SpellEvent spell)
    {
        var config = GetSpellVfxConfig(spell.SpellId);
        if (config == null || _vfxOverlay == null)
        {
            // Unknown spell or no VFX layer — small pause so the tween has a step.
            tween.TweenInterval(0.1f * _speedMultiplier);
            _tweenHasSteps = true;
            return;
        }

        _tweenHasSteps = true;

        switch (config.Shape)
        {
            case VfxShape.Area:
                AnimateAreaSpell(tween, spell, config);
                break;
            case VfxShape.Path:
                AnimatePathSpell(tween, spell, config);
                break;
            case VfxShape.Cone:
                AnimateDragonFart(tween, spell, config);
                break;
        }
    }

    private void AnimateAreaSpell(Tween tween, SpellEvent spell, SpellVfxConfig config)
    {
        // Travel phase: directional sprite from caster to target (e.g. fireball in flight).
        if (config.DirectionalSprites != null
            && spell.CasterPos.HasValue && spell.TargetPos.HasValue)
        {
            _vfxOverlay!.AppendTravelEffect(tween, spell.CasterPos.Value, spell.TargetPos.Value,
                config.DirectionalSprites, 0.05f * _speedMultiplier);
        }
        else if (spell.CasterPos.HasValue && spell.TargetPos.HasValue)
        {
            // Glyph fallback for area spells without directional sprites.
            _vfxOverlay!.AppendTravelEffect(tween, spell.CasterPos.Value, spell.TargetPos.Value,
                config.AreaColor, config.GlyphFallback, 0.05f * _speedMultiplier);
        }

        // Impact / area phase.
        if (spell.AffectedTiles?.Count > 0)
        {
            // Burst center: prefer explicit TargetPos, fall back to first affected tile.
            var burstCenter = spell.TargetPos ?? spell.AffectedTiles[0];

            if (config.DirectionalSprites != null)
            {
                // Directional burst (fireball) spreads outward from impact point.
                _vfxOverlay!.AppendDirectionalBurst(tween, burstCenter,
                    config.DirectionalSprites, 0.15f * _speedMultiplier);
            }
            else
            {
                // No directional sprites — plain area color flash.
                _vfxOverlay!.AppendAreaEffect(tween, spell.AffectedTiles, config.AreaColor,
                    config.Duration * _speedMultiplier);
            }
        }
    }

    private void AnimateDragonFart(Tween tween, SpellEvent spell, SpellVfxConfig config)
    {
        if (!spell.CasterPos.HasValue || spell.AffectedTiles == null || spell.AffectedTiles.Count == 0)
        {
            _vfxOverlay!.AppendAreaEffect(tween, spell.AffectedTiles ?? [],
                config.AreaColor, config.Duration * _speedMultiplier);
            return;
        }

        // Directional area: sprite per tile based on direction from caster.
        _vfxOverlay!.AppendDirectionalAreaEffect(tween, spell.CasterPos.Value,
            spell.AffectedTiles, config.DirectionalSprites!, config.Duration * _speedMultiplier);
    }

    private void AnimatePathSpell(Tween tween, SpellEvent spell, SpellVfxConfig config)
    {
        if (spell.AffectedTiles?.Count > 0)
        {
            float perTile = config.Duration * _speedMultiplier / spell.AffectedTiles.Count;

            if (config.CycleSprites != null)
            {
                _vfxOverlay!.AppendPathEffect(tween, spell.AffectedTiles,
                    config.CycleSprites, config.ImpactSprite, perTile);
            }
            else
            {
                // Glyph fallback for path spells without cycle sprites.
                _vfxOverlay!.AppendPathEffect(tween, spell.AffectedTiles,
                    config.AreaColor, perTile);
            }
        }
    }

    private void AnimateStatusApplied(Tween tween, StatusAppliedEvent status)
    {
        if (_vfxOverlay == null) return;
        var sprite = _entitySprites.GetSprite(status.TargetId);
        if (sprite == null) return;

        _tweenHasSteps = true;
        var (glyph, color) = GetStatusGlyph(status.EffectName);
        _vfxOverlay.AppendStatusIndicator(tween, sprite.Position, glyph, color, 0.2f * _speedMultiplier);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // VFX configuration — maps SpellId to visual parameters
    // ──────────────────────────────────────────────────────────────────────────

    private enum VfxShape
    {
        /// <summary>Parallel area flash. DirectionalSprites → burst from TargetPos.</summary>
        Area,
        /// <summary>Sequential path chain. CycleSprites → per-tile sprites.</summary>
        Path,
        /// <summary>Directional cone from caster. DirectionalSprites → per-tile by direction.</summary>
        Cone,
    }

    /// <summary>
    /// All visual parameters for a single spell effect.
    ///
    /// DirectionalSprites: 8-element octant array [E,SE,S,SW,W,NW,N,NE], or null.
    /// CycleSprites: sequential sprites for path effects (each tile cycles through them), or null.
    /// ImpactSprite: shown on the final path tile at impact scale, or null.
    /// AreaColor: ColorRect flash color used as background tint or glyph fallback color.
    /// GlyphFallback: ASCII character used when sprites are unavailable.
    /// Duration: base duration before SpeedMultiplier is applied.
    /// Shape: Area = burst, Path = sequential chain, Cone = directional area from caster.
    /// </summary>
    private sealed record SpellVfxConfig(
        string[]? DirectionalSprites,
        string[]? CycleSprites,
        string? ImpactSprite,
        Color AreaColor,
        string GlyphFallback,
        float Duration,
        VfxShape Shape);

    // Helper: build a res:// path for a single FX sprite.
    private static string FxPath(int n) =>
        $"res://src/Presentation/assets/sprites/fx/uf_FX_{n:D2}.png";

    // Helper: build an array of res:// paths for FX sprites from a list of numbers.
    private static string[] FxPaths(params int[] nums) =>
        Array.ConvertAll(nums, FxPath);

    private static SpellVfxConfig? GetSpellVfxConfig(string spellId) => spellId switch
    {
        "fireball" => new SpellVfxConfig(
            DirectionalSprites: FxPaths(81, 85, 84, 86, 83, 87, 82, 88),
            CycleSprites: null,
            ImpactSprite: null, // burst reuses the directional sprites
            AreaColor: new Color(1f, 0.5f, 0f),
            GlyphFallback: "*",
            Duration: 0.25f,
            Shape: VfxShape.Area),

        "lightning" or "lightning_bolt" => new SpellVfxConfig(
            DirectionalSprites: null,
            CycleSprites: FxPaths(61, 62, 63, 64),
            ImpactSprite: FxPath(59),
            AreaColor: new Color(1f, 1f, 0.3f),
            GlyphFallback: "~",
            Duration: 0.15f,
            Shape: VfxShape.Path),

        "dragon_fart" => new SpellVfxConfig(
            DirectionalSprites: FxPaths(122, 126, 125, 127, 124, 128, 123, 129),
            CycleSprites: null,
            ImpactSprite: null,
            AreaColor: new Color(0.4f, 0.85f, 0.3f),
            GlyphFallback: "%",
            Duration: 0.25f,
            Shape: VfxShape.Cone),

        _ => null,
    };

    private static (string Glyph, Color Color) GetStatusGlyph(string effectName) => effectName switch
    {
        "slowed"                    => ("~", Colors.Cyan),
        "poisoned" or "plague"      => ("*", Colors.Green),
        "regeneration"              => ("+", new Color(0.3f, 1f, 0.3f)),
        "sleep" or "sleeping"       => ("z", Colors.Gray),
        "confused" or "disoriented" => ("?", Colors.Purple),
        "blinded"                   => ("-", Colors.DarkGray),
        _                           => ("!", Colors.White),
    };

    private void AnimateMove(Tween tween, MoveEvent move)
    {
        var sprite = _entitySprites.GetSprite(move.ActorId);
        if (sprite == null) return;
        _tweenHasSteps = true;

        // Update ZIndex immediately to the destination tile so the entity renders above
        // tiles/entities at the target depth for the full duration of the move animation.
        sprite.ZIndex = _renderer.GetEntitySortOrder(move.ToX, move.ToY);

        var targetPos = _renderer.GridToScreenCenter(move.ToX, move.ToY);
        tween.TweenProperty(sprite, "position", targetPos, MoveDur)
             .SetEase(Tween.EaseType.Out)
             .SetTrans(Tween.TransitionType.Quad);
    }

    private void AnimateAttack(Tween tween, AttackEvent attack)
    {
        var sprite = _entitySprites.GetSprite(attack.ActorId);
        var targetSprite = _entitySprites.GetSprite(attack.TargetId);
        if (sprite == null) return;
        _tweenHasSteps = true;

        if (!attack.Hit)
        {
            // Miss — brief shake
            var origPos = sprite.Position;
            tween.TweenProperty(sprite, "position", origPos + new Vector2(4, -2), AttackBumpDur * 0.5f);
            tween.TweenProperty(sprite, "position", origPos, AttackBumpDur * 0.5f);
            return;
        }

        // Bump toward target
        Vector2 bumpDir = Vector2.Zero;
        if (targetSprite != null)
            bumpDir = (targetSprite.Position - sprite.Position).Normalized() * 8f;

        var startPos = sprite.Position;
        var bumpPos = startPos + bumpDir;

        // Step 1: bump forward
        tween.TweenProperty(sprite, "position", bumpPos, AttackBumpDur)
             .SetEase(Tween.EaseType.Out);

        // Step 2: return to start + flash target red (in parallel)
        tween.TweenProperty(sprite, "position", startPos, AttackReturnDur)
             .SetEase(Tween.EaseType.In);

        if (targetSprite != null)
        {
            if (attack.IsCritical)
            {
                // Brighter flash + brief scale pop for crits.
                tween.Parallel().TweenProperty(targetSprite, "modulate", new Color(3f, 0.1f, 0.1f), 0.2f * _speedMultiplier);
                tween.Parallel().TweenProperty(targetSprite, "scale", new Vector2(1.15f, 1.15f), 0.1f * _speedMultiplier);
                tween.TweenProperty(targetSprite, "modulate", Colors.White, 0.1f * _speedMultiplier);
                tween.Parallel().TweenProperty(targetSprite, "scale", Vector2.One, 0.1f * _speedMultiplier);
            }
            else
            {
                // Standard hit flash.
                tween.Parallel().TweenProperty(targetSprite, "modulate", new Color(2, 0.3f, 0.3f), AttackReturnDur);
                // Step 3: reset modulate back to white (sequential, after the return).
                tween.TweenProperty(targetSprite, "modulate", Colors.White, AttackReturnDur * 0.5f);
            }
        }
    }

    private void AnimateDeath(Tween tween, DeathEvent death)
    {
        var sprite = _entitySprites.GetSprite(death.ActorId);
        if (sprite == null) return;
        _tweenHasSteps = true;

        tween.TweenProperty(sprite, "modulate:a", 0.0f, DeathFadeDur)
             .SetEase(Tween.EaseType.In);
    }

    /// <summary>
    /// Animate a throw. Creates a temporary projectile label that travels from the thrower
    /// to the landing tile at PoC timing (50ms per tile).
    ///
    /// Projectile character by type:
    ///   Weapon → "/" (blade tumbling)
    ///   Potion → "*" (shatter on impact)
    ///   Junk   → "o" (generic object)
    ///
    /// The label is added to the animation root (scene-relative) and freed when the tween
    /// finishes its travel step. Impact flash follows for potion hits.
    /// </summary>
    private void AnimateThrow(Tween tween, ThrowEvent throwEvt)
    {
        var actorSprite = _entitySprites.GetSprite(throwEvt.ActorId);
        if (actorSprite == null) return;

        // Determine projectile glyph
        string glyph = throwEvt.ResultType switch
        {
            ThrowResultType.WeaponHit  or ThrowResultType.WeaponMiss  => "/",
            ThrowResultType.PotionShatter => "*",
            _ => "o",
        };

        // Timing: 50ms per tile (PoC reference), scaled by SpeedMultiplier
        var fromScreen = _renderer.GridToScreenCenter(throwEvt.ActorX, throwEvt.ActorY);
        var toScreen   = _renderer.GridToScreenCenter(throwEvt.LandX, throwEvt.LandY);

        // Approximate tile distance for timing
        int tileDist = Math.Max(1,
            Math.Abs(throwEvt.LandX - throwEvt.ActorX) + Math.Abs(throwEvt.LandY - throwEvt.ActorY));
        float travelDur = 0.05f * tileDist * _speedMultiplier;

        // Create projectile label in scene tree
        var projectile = new Label
        {
            Text = glyph,
            Position = fromScreen,
            ZIndex = 10,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(projectile);

        // Animate travel — InOut with linear transition = constant speed (PoC 50ms/tile)
        tween.TweenProperty(projectile, "position", toScreen, travelDur)
             .SetEase(Tween.EaseType.InOut)
             .SetTrans(Tween.TransitionType.Linear);
        _tweenHasSteps = true;

        // Free projectile after travel
        tween.TweenCallback(Callable.From(() => projectile.SafeFree()));

        // Impact flash for potion hit
        if (throwEvt.ResultType == ThrowResultType.PotionShatter && throwEvt.Hit)
        {
            var targetSprite = _entitySprites.GetSprite(throwEvt.TargetEntityId ?? -1);
            if (targetSprite != null)
            {
                tween.TweenProperty(targetSprite, "modulate", new Color(0.2f, 0.6f, 2.0f), 0.08f * _speedMultiplier);
                tween.TweenProperty(targetSprite, "modulate", Colors.White, 0.08f * _speedMultiplier);
            }
        }
    }
}
