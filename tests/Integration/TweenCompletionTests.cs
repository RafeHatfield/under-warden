using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace UnderWarden.Integration;

using UnderWarden.Presentation.Animation;
using UnderWarden.Presentation.Entities;
using UnderWarden.Presentation.UI;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Combat;

/// <summary>
/// Integration tests for TurnAnimator tween lifecycle.
/// Verifies that CheckComplete polling correctly detects tween
/// completion and fires AnimationComplete, and that tweens are
/// properly cleaned up (no leaks in TweenTracker).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TweenCompletionTests
{
    [TestCase]
    public async Task PlayTurn_NoAnimatableEvents_CompletesImmediately()
    {
        var root = new Node2D();
        var entityLayer = new Node2D();
        root.AddChild(entityLayer);

        var runner = ISceneRunner.Load(root, true);
        await runner.SimulateFrames(2);

        var entitySprites = new EntitySpriteManager(entityLayer, new UnderWarden.Presentation.Map.IsometricRenderer());
        var animator = new TurnAnimator(root, entitySprites);

        bool animComplete = false;
        animator.AnimationComplete += () => animComplete = true;

        // WaitEvent is not animatable — should fire immediately with no tween
        var result = new TurnResult
        {
            TurnNumber = 1,
            Events = new List<TurnEvent> { new WaitEvent { ActorId = 0 } },
            GameOver = false,
        };

        animator.PlayTurn(result);

        AssertBool(animComplete).IsTrue();

        await ISceneRunner.SyncProcessFrame;
    }

    [TestCase]
    public async Task PlayTurn_MissingSprite_CompletesWithoutCrash()
    {
        // When an entity has no sprite (e.g., during floor transition),
        // the animator should still complete without hanging or crashing.
        var root = new Node2D();
        var entityLayer = new Node2D();
        root.AddChild(entityLayer);

        var runner = ISceneRunner.Load(root, true);
        await runner.SimulateFrames(2);

        var entitySprites = new EntitySpriteManager(entityLayer, new UnderWarden.Presentation.Map.IsometricRenderer());
        var animator = new TurnAnimator(root, entitySprites);

        bool animComplete = false;
        animator.AnimationComplete += () => animComplete = true;

        int startTweens = TweenTracker.ActiveCount;

        // MoveEvent for entity ID 999 — no sprite registered for this ID
        var result = new TurnResult
        {
            TurnNumber = 1,
            Events = new List<TurnEvent>
            {
                new MoveEvent { ActorId = 999, FromX = 5, FromY = 5, ToX = 6, ToY = 5 },
            },
            GameOver = false,
        };

        animator.PlayTurn(result);

        // Tween was created (even though the sprite is missing, the tween itself exists)
        // Poll until it completes
        for (int i = 0; i < 60 && !animComplete; i++)
        {
            animator.CheckComplete();
            await ISceneRunner.SyncProcessFrame;
        }

        // Should complete (either immediately because tween had nothing to animate,
        // or after the tween's empty duration elapses)
        AssertBool(animComplete).IsTrue();

        // No tween leak
        AssertInt(TweenTracker.ActiveCount).IsEqual(startTweens);
    }

    [TestCase]
    public async Task TweenTracker_CountMatchesAfterMultipleAnimations()
    {
        var root = new Node2D();
        var entityLayer = new Node2D();
        root.AddChild(entityLayer);

        var runner = ISceneRunner.Load(root, true);
        await runner.SimulateFrames(2);

        var entitySprites = new EntitySpriteManager(entityLayer, new UnderWarden.Presentation.Map.IsometricRenderer());
        var animator = new TurnAnimator(root, entitySprites);

        int startCount = TweenTracker.ActiveCount;

        // Run 5 empty turns rapidly
        for (int turn = 0; turn < 5; turn++)
        {
            bool done = false;
            // Store the handler so we can unsubscribe the exact same delegate.
            // Creating a new lambda on unsubscribe would produce a different reference
            // and leave the original handler accumulated across turns.
            Action handler = () => done = true;
            animator.AnimationComplete += handler;

            var result = new TurnResult
            {
                TurnNumber = turn + 1,
                Events = new List<TurnEvent> { new WaitEvent { ActorId = 0 } },
                GameOver = false,
            };

            animator.PlayTurn(result);
            AssertBool(done).IsTrue();

            // Unsubscribe the stored reference, not a new lambda
            animator.AnimationComplete -= handler;
        }

        // No accumulated tweens
        AssertInt(TweenTracker.ActiveCount).IsEqual(startCount);

        await ISceneRunner.SyncProcessFrame;
    }
}
