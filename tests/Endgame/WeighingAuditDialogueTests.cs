using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using UnderWarden.Logic.Persistence;
using UnderWarden.Tests.Persistence;
using NUnit.Framework;

namespace UnderWarden.Tests.Endgame;

/// <summary>
/// Tests for the Weighing audit dialogue: registry parsing, content integrity, and
/// orchestrator event emission.
/// </summary>
[TestFixture]
public class WeighingAuditDialogueTests
{
    private static WeighingAuditRegistry LoadLiveRegistry()
    {
        var yamlPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "voice_lines", "weighing_audit.yaml");
        yamlPath = Path.GetFullPath(yamlPath);
        var yaml = File.ReadAllText(yamlPath);
        return WeighingAuditRegistry.LoadFromYaml(yaml);
    }

    // ── Registry: parse + content integrity ──────────────────────────────────

    [Test]
    public void LiveYaml_Loads_WithoutException()
    {
        Assert.That(() => LoadLiveRegistry(), Throws.Nothing);
    }

    [Test]
    public void Opening_HasSixPages_AllByUnderWarden()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetOpening();
        Assert.That(pages, Has.Count.EqualTo(6));
        Assert.That(pages.All(p => p.Speaker == "under_warden"), Is.True,
            "the opening is pure Under-Warden — no Guardian voice until the first rises");
    }

    [Test]
    public void WardenBeat_AllFourTiers_HaveTwoPages_WardenThenGuardian()
    {
        var reg = LoadLiveRegistry();
        foreach (GuardianTier tier in System.Enum.GetValues<GuardianTier>())
        {
            var pages = reg.GetGuardianBeat(GuardianId.WardenOfWardens, tier);
            Assert.That(pages, Has.Count.EqualTo(2),
                $"Warden {tier} beat should have 2 pages (Under-Warden reading + Guardian line)");
            Assert.That(pages[0].Speaker, Is.EqualTo("under_warden"),
                $"Warden {tier} page 0 should be the Under-Warden");
            Assert.That(pages[1].Speaker, Is.EqualTo("guardian"),
                $"Warden {tier} page 1 should be the Guardian");
        }
    }

    [Test]
    public void SavageWardenLine_Contains_WearThis()
    {
        // "Now wear this" earns the enrage/possession mechanic; must survive any future text edit.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.WardenOfWardens, GuardianTier.Savage);
        Assert.That(pages[1].Text, Does.Contain("wear this"),
            "the savage Warden's final line references its mechanic (TurnAllyHostile/enrage)");
    }

    [Test]
    public void OathkeeperBeat_AllFourTiers_HaveTwoPages()
    {
        var reg = LoadLiveRegistry();
        foreach (GuardianTier tier in System.Enum.GetValues<GuardianTier>())
        {
            var pages = reg.GetGuardianBeat(GuardianId.Oathkeeper, tier);
            Assert.That(pages, Has.Count.EqualTo(2), $"Oathkeeper {tier} beat should have 2 pages");
            Assert.That(pages[0].Speaker, Is.EqualTo("under_warden"));
            Assert.That(pages[1].Speaker, Is.EqualTo("guardian"));
        }
    }

    [Test]
    public void SavageOathkeeperLine_Contains_Geas()
    {
        // "There is no geas on us" is the payoff — the judgment acts where the living orcs cannot.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.Oathkeeper, GuardianTier.Savage);
        Assert.That(pages[1].Text, Does.Contain("geas"),
            "savage Oathkeeper references the geas payoff (finally unbound)");
    }

    [Test]
    public void AlliedOathkeeperLine_Contains_KingReference()
    {
        // "The king does not say such things aloud" — the Allied payoff for players who ran the orc questline.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.Oathkeeper, GuardianTier.Allied);
        Assert.That(pages[1].Text, Does.Contain("king"),
            "allied Oathkeeper carries Borrek's unspoken loyalty through the champion");
    }

    [Test]
    public void AuditorsBeat_AllFourTiers_HaveTwoPages()
    {
        var reg = LoadLiveRegistry();
        foreach (GuardianTier tier in System.Enum.GetValues<GuardianTier>())
        {
            var pages = reg.GetGuardianBeat(GuardianId.AuditorsOwn, tier);
            Assert.That(pages, Has.Count.EqualTo(2), $"Auditor's Own {tier} beat should have 2 pages");
            Assert.That(pages[0].Speaker, Is.EqualTo("under_warden"));
            Assert.That(pages[1].Speaker, Is.EqualTo("guardian"));
        }
    }

    [Test]
    public void AlliedAuditor_IsDisappointed_NotAlly()
    {
        // The tier-inversion: Allied = cold shoulder from boredom, not allegiance.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.AuditorsOwn, GuardianTier.Allied);
        Assert.That(pages[1].Text, Does.Contain("no fun"),
            "allied Auditor is dismissive — disappointed, not friendly");
        Assert.That(pages[1].Text, Does.Not.Contain("stand with you"),
            "allied Auditor does not offer alliance the way other allied Guardians do");
    }

    [Test]
    public void SavageAuditor_EndsOnTrailingDash_NoResolvingLine()
    {
        // The savage close intentionally breaks mid-surge. The trailing dash is the dismiss.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.AuditorsOwn, GuardianTier.Savage);
        string lastText = pages[1].Text;
        Assert.That(lastText, Does.EndWith("-"),
            "savage Auditor ends on a trailing dash — combat begins out of the break, no button");
    }

    [Test]
    public void SavageAuditor_Contains_NoOfficeDownHere()
    {
        // "there is no office down here" — the beast noting the cage doesn't reach this deep.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.AuditorsOwn, GuardianTier.Savage);
        Assert.That(pages[1].Text, Does.Contain("no office down here"),
            "savage Auditor surfaces the cage/contained framing payoff");
    }

    [Test]
    public void AssemblyBeat_AllFourTiers_HaveThreePages_WithHollowmark()
    {
        var reg = LoadLiveRegistry();
        foreach (GuardianTier tier in System.Enum.GetValues<GuardianTier>())
        {
            var pages = reg.GetGuardianBeat(GuardianId.AssemblyOfTheLost, tier);
            Assert.That(pages, Has.Count.EqualTo(3),
                $"Assembly {tier} beat has three pages: under_warden, guardian, hollowmark");
            Assert.That(pages[0].Speaker, Is.EqualTo("under_warden"));
            Assert.That(pages[1].Speaker, Is.EqualTo("guardian"));
            Assert.That(pages[2].Speaker, Is.EqualTo("hollowmark"),
                "Hollowmark is the third voice — the binding-cost detonation");
        }
    }

    [Test]
    public void AssemblyNeutral_UnderWarden_ContainsGriefCrack()
    {
        // "for the length of a single breath, the procedure thins" — brief crack, closes.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.AssemblyOfTheLost, GuardianTier.Neutral);
        Assert.That(pages[0].Text, Does.Contain("the procedure thins"),
            "Neutral grief crack: the procedure thins for one breath");
        Assert.That(pages[0].Text, Does.Contain("the breath ends"),
            "Neutral grief crack closes: 'the breath ends; the file closes over it'");
    }

    [Test]
    public void AssemblySavage_UnderWarden_GriefCrackDoesNotClose()
    {
        // Savage crack: longer, doesn't quite close; files his own crack as an anomaly.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.AssemblyOfTheLost, GuardianTier.Savage);
        Assert.That(pages[0].Text, Does.Contain("not able to be entirely cold"),
            "Savage crack: 'I find I am not able to be entirely cold about this account'");
        Assert.That(pages[0].Text, Does.Contain("Note that it is the only one"),
            "him filing his own crack as a procedural anomaly");
        // The Savage crack does not contain "the breath ends" — it doesn't close.
        Assert.That(pages[0].Text, Does.Not.Contain("the breath ends"),
            "Savage crack does not close the way the Neutral crack does");
    }

    [Test]
    public void AssemblySavage_Assembly_NamesAnikDirectly()
    {
        // Reserved for Savage only — the dead selves saying what Sasha suppresses.
        var reg = LoadLiveRegistry();
        var pages = reg.GetGuardianBeat(GuardianId.AssemblyOfTheLost, GuardianTier.Savage);
        Assert.That(pages[1].Text, Does.Contain("Anik"),
            "Savage Assembly names Anik — the thing Sasha never lets himself think");
        // Lower tiers must NOT name Anik — it must be earned by the vast Assembly's weight.
        foreach (var tier in new[] { GuardianTier.Allied, GuardianTier.Diminished, GuardianTier.Neutral })
        {
            var lowerPages = reg.GetGuardianBeat(GuardianId.AssemblyOfTheLost, tier);
            Assert.That(lowerPages[1].Text, Does.Not.Contain("Anik"),
                $"Assembly {tier} does not name Anik — reserved for Savage");
        }
    }

    [Test]
    public void HollowmarkLines_RefuseToExplain_NeverStateTheMechanism()
    {
        // The character rule: she tightens under load, never explains the binding as mechanism.
        // She says "not many left" and "I'm not telling you the number" — the player reads the rest.
        // The test pins what she REFUSES to say, not a word count (she may carry more weight at
        // Savage via the Marya substrate, but she never names the binding or turns it into a speech).
        var reg = LoadLiveRegistry();
        foreach (GuardianTier tier in System.Enum.GetValues<GuardianTier>())
        {
            var text = reg.GetGuardianBeat(GuardianId.AssemblyOfTheLost, tier)[2].Text;
            // She never explains the binding as mechanism ("the binding works by...", "it costs me...").
            Assert.That(text, Does.Not.Contain("the binding works"),
                $"Hollowmark {tier}: never explains the mechanism");
            Assert.That(text, Does.Not.Contain("it costs me"),
                $"Hollowmark {tier}: states a shape, not an account");
        }
        // Savage: the detonation surfaces the cost obliquely.
        var savage = reg.GetGuardianBeat(GuardianId.AssemblyOfTheLost, GuardianTier.Savage)[2].Text;
        Assert.That(savage, Does.Contain("Not many left"),
            "Savage Hollowmark surfaces the binding cost obliquely — 'Not many left now, Boss'");
        Assert.That(savage, Does.Contain("not telling you the number"),
            "she refuses to quantify — refusal IS the weight");
    }

    // ── The Debt — terms and resolutions ─────────────────────────────────────

    [Test]
    public void Debt_HasSixPages_ThreeSpeakers()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetDebt();
        Assert.That(pages, Has.Count.EqualTo(6),
            "narrator scene-set + 3 Lady pages + Anik + Hollowmark threshold");
        Assert.That(pages[0].Speaker, Is.EqualTo("narrator"));
        Assert.That(pages[1].Speaker, Is.EqualTo("debt"),  "Lady opens (first trim target)");
        Assert.That(pages[2].Speaker, Is.EqualTo("debt"),  "Lady sees Anik (first trim target)");
        Assert.That(pages[3].Speaker, Is.EqualTo("debt"),  "Lady: the choice offer (anchor — keep last)");
        Assert.That(pages[4].Speaker, Is.EqualTo("anik"),  "Anik surfaces through the claim");
        Assert.That(pages[5].Speaker, Is.EqualTo("hollowmark"), "Hollowmark threshold: refuses to choose");
    }

    [Test]
    public void DebtLady_IsWarm_NotCold()
    {
        // "It is not cold. That is the first wrong thing." — the Lady's register is the inversion of
        // every other voice in the audit. Pins the scene-set's key image.
        var reg = LoadLiveRegistry();
        var pages = reg.GetDebt();
        Assert.That(pages[0].Text, Does.Contain("not cold"),
            "scene-set establishes the Lady's warmth as the first wrong thing");
    }

    [Test]
    public void DebtLady_ChoiceOfferPage_ContainsAllThreeBranches()
    {
        var reg = LoadLiveRegistry();
        var choiceOffer = reg.GetDebt()[3].Text; // "So. Here is what is yours to choose..."
        Assert.That(choiceOffer, Does.Contain("by force"),  "Force / Theft branch present");
        Assert.That(choiceOffer, Does.Contain("stay"),      "Self / Swap branch present");
        Assert.That(choiceOffer, Does.Contain("turn from"), "Refuse branch present");
    }

    [Test]
    public void HollowmarkThreshold_RefusesToChoose_ForEitherSide()
    {
        var reg = LoadLiveRegistry();
        var holl = reg.GetDebt()[5].Text;
        // She won't argue for Force OR Swap — the line is "pick it because it's yours to pick."
        Assert.That(holl, Does.Contain("yours to pick"),
            "Hollowmark refuses to put her thumb on the scale even though Swap would free her");
        Assert.That(holl, Does.Not.Contain("choose the Swap"),
            "she does not advocate for the Swap even though it would free her");
    }

    [Test]
    public void Resolution_Swap_ThreePagesWithHollowmarkPayoff()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.Swap);
        Assert.That(pages, Has.Count.EqualTo(3), "Lady + Hollowmark + narrator closing");
        Assert.That(pages[0].Speaker, Is.EqualTo("debt"),       "Lady: tender, not disappointed");
        Assert.That(pages[1].Speaker, Is.EqualTo("hollowmark"), "Hollowmark: the payoff");
        Assert.That(pages[2].Speaker, Is.EqualTo("narrator"),   "closing narration");
    }

    [Test]
    public void HollowmarkSwap_ContainsThenIllStayToo()
    {
        // The most important line in the game. Pin it explicitly.
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.Swap);
        Assert.That(pages[1].Text, Does.Contain("Then I'll stay too"),
            "Hollowmark's Swap line — the binding payoff, the most important line in the game");
    }

    [Test]
    public void HollowmarkSwap_IsShort_DoesNotMonologue()
    {
        // "shorter is the character, especially here" — Hollowmark's voice fully breaks but stays short.
        // The Warden's Savage Under-Warden text (~90 words) is the longest single page in the audit;
        // Hollowmark's Swap line must be substantially shorter. A genuine monologue would be 60+ words.
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.Swap);
        string text = pages[1].Text;
        int wordCount = text.Split(new[] { ' ', '\n', '\t' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.That(wordCount, Is.LessThan(60),
            "Hollowmark does not monologue even at her most broken — short is the character");
    }

    [Test]
    public void Resolution_Theft_LadyDoesNotRage()
    {
        // "she is patient, and that is worse" — she is unhurried even as the claim is torn.
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.Theft);
        string ladyText = pages[0].Text;
        Assert.That(ladyText, Does.Contain("unhurried"),
            "Lady in Theft is patient, not raging — serenity is the horror");
        Assert.That(ladyText, Does.Contain("still a debt"),
            "the theft is acknowledged as incomplete: 'A debt taken by force is still a debt'");
    }

    [Test]
    public void Resolution_CleanAudit_ClaimSatisfiedByConductNotForce()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.CleanAudit);
        Assert.That(pages[0].Text, Does.Contain("account closes itself"),
            "clean audit: the claim is satisfied by conduct, not torn free");
    }

    [Test]
    public void Resolution_Refused_LadyHasNoHurry()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.LossRefused);
        Assert.That(pages[0].Text, Does.Contain("no hurry"),
            "refused: the Lady's patience is unbearable — she will simply wait");
    }

    [Test]
    public void AllFourResolutions_HaveNarratorClosing()
    {
        var reg = LoadLiveRegistry();
        foreach (var ending in new[]
        {
            EndingType.CleanAudit, EndingType.Swap,
            EndingType.Theft, EndingType.LossRefused,
        })
        {
            var pages = reg.GetResolution(ending);
            Assert.That(pages, Is.Not.Empty, $"{ending} resolution must be non-empty");
            Assert.That(pages[^1].Speaker, Is.EqualTo("narrator"),
                $"{ending} must end with narrator closing narration");
        }
    }

    // ── Combat-death resolutions ─────────────────────────────────────────────

    [Test]
    public void LossGuardians_TwoPages_UnderWardenAndNarrator()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.LossGuardians);
        Assert.That(pages, Has.Count.EqualTo(2), "short and final — curt, not padded");
        Assert.That(pages[0].Speaker, Is.EqualTo("under_warden"),
            "LossGuardians opens with the Under-Warden filing the hour");
        Assert.That(pages[1].Speaker, Is.EqualTo("narrator"));
    }

    [Test]
    public void LossGuardians_IsShort_FiledAndMoved()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.LossGuardians);
        Assert.That(pages[0].Text, Does.Contain("remains open"),
            "Under-Warden: the account remains open (will resume)");
    }

    [Test]
    public void LossDebt_TwoPages_LadyAndNarrator()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetResolution(EndingType.LossDebt);
        Assert.That(pages, Has.Count.EqualTo(2));
        Assert.That(pages[0].Speaker, Is.EqualTo("debt"),
            "LossDebt: the Lady is present — you died in her hall");
        Assert.That(pages[1].Speaker, Is.EqualTo("narrator"));
    }

    [Test]
    public void LossDebt_LadyIsGentle_AchesMoreThanLossGuardians()
    {
        var reg = LoadLiveRegistry();
        var lossDebt = reg.GetResolution(EndingType.LossDebt);
        // Lady is gentle even in your defeat — especially now.
        Assert.That(lossDebt[0].Text, Does.Contain("so close"),
            "Lady: 'So close, and so tired' — the closest failure, she receives you");
        // Narrator holds Anik nearby — that nearness is the ache.
        Assert.That(lossDebt[1].Text, Does.Contain("beside the one you came for"),
            "narrator: you are held near Anik but not yet free");
    }

    // ── Ally-fallback lines ───────────────────────────────────────────────────

    [Test]
    public void AllyFallback_FramingNarration_Present()
    {
        var reg = LoadLiveRegistry();
        var pages = reg.GetAllyFallbackFraming();
        Assert.That(pages, Has.Count.EqualTo(1));
        Assert.That(pages[0].Speaker, Is.EqualTo("narrator"));
        Assert.That(pages[0].Text, Does.Contain("step back"),
            "framing: they step back by will, not because a barrier stops them");
    }

    [Test]
    public void AllyFallback_ThreeGuardians_AreSolidarity_OneDismissal()
    {
        var reg = LoadLiveRegistry();
        // Warden, Oathkeeper, Assembly: solidarity / regret / letting go.
        var warden = reg.GetAllyFallback(GuardianId.WardenOfWardens);
        var oath = reg.GetAllyFallback(GuardianId.Oathkeeper);
        var assembly = reg.GetAllyFallback(GuardianId.AssemblyOfTheLost);
        Assert.That(warden[0].Text, Does.Contain("Go well"),
            "Warden fallback: institutional, closes with 'Go well'");
        Assert.That(oath[0].Text, Does.Contain("We would if we could"),
            "Oathkeeper fallback: the explicit 'we would if we could' beat");
        Assert.That(assembly[0].Text, Does.Contain("We'll be watching"),
            "Assembly fallback: dry dead-self voice, 'same as ever'");
    }

    [Test]
    public void AllyFallback_AuditorsOwn_IsDismissal_NotSolidarity()
    {
        // The Auditor's Own inverted character: allied out of boredom, dismisses rather than mourns.
        var reg = LoadLiveRegistry();
        var auditor = reg.GetAllyFallback(GuardianId.AuditorsOwn);
        Assert.That(auditor[0].Text, Does.Contain("any fun"),
            "Auditor's Own fallback is dismissal — 'You were never going to be any fun'");
        Assert.That(auditor[0].Text, Does.Not.Contain("would if we could"),
            "Auditor's Own does not get the solidarity beat");
    }

    // ── UI copy ───────────────────────────────────────────────────────────────

    [Test]
    public void UiCopy_AllThreeButtonLabels_Present()
    {
        var reg = LoadLiveRegistry();
        Assert.That(reg.GetUiText("ui.force_button"),  Is.EqualTo("Take her by force."));
        Assert.That(reg.GetUiText("ui.self_button"),   Is.EqualTo("Give yourself in her place."));
        Assert.That(reg.GetUiText("ui.refuse_button"), Is.EqualTo("Turn back. Carry the debt."));
    }

    [Test]
    public void UiCopy_ForceAndSwapHaveConfirmations_RefuseDoesNot()
    {
        var reg = LoadLiveRegistry();
        Assert.That(reg.GetUiText("ui.force_confirm"), Is.Not.Null,
            "Force is irreversible — requires confirmation");
        Assert.That(reg.GetUiText("ui.self_confirm"),  Is.Not.Null,
            "Swap is irreversible — requires confirmation");
        Assert.That(reg.GetUiText("ui.refuse_confirm"), Is.Null,
            "Refuse has no confirmation — it is recoverable (you climb back up)");
    }

    [Test]
    public void UiCopy_MissingKey_ReturnsNull()
    {
        var reg = LoadLiveRegistry();
        Assert.That(reg.GetUiText("ui.nonexistent"), Is.Null);
    }

    // ── Orchestrator: dialogue events emitted during Begin ───────────────────

    private static (GameState state, List<TurnEvent> events) ArenaBegin(
        GuardianTier wardenTier, WeighingAuditRegistry? auditReg = null)
    {
        var arena = WeighingArenaDefinition.Build();
        var start = arena.FirstAnchor("player_start")!.Value;
        var player = new Entity(0, "Player", start.X, start.Y, blocksMovement: true);
        player.Add(new Fighter(hp: 500, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 14, evasion: 0, damageMin: 5, damageMax: 8));
        arena.Map.RegisterEntity(player);

        var state = new GameState(player, new List<Entity>(), arena.Map, new SeededRandom(1337), 10_000)
        {
            IsDungeonMode = true,
            CurrentDepth = WeighingConstants.FinalFloorDepth,
            WeighingArena = arena,
            WeighingAudit = auditReg,
        };

        var audit = new AuditScorer.AuditResult(wardenTier, GuardianTier.Savage, GuardianTier.Savage, GuardianTier.Savage);
        var events = new List<TurnEvent>();
        WeighingOrchestrator.Begin(state, audit, swapAvailable: false, "hostile", 12, events);
        return (state, events);
    }

    [Test]
    public void Begin_WithNoRegistry_EmitsNoDialogueEvents()
    {
        var (_, events) = ArenaBegin(GuardianTier.Savage, auditReg: null);
        Assert.That(events.OfType<WeighingDialogueEvent>(), Is.Empty,
            "without a registry, no dialogue events — graceful absence of content");
    }

    [Test]
    public void Begin_WithRegistry_EmitsOpeningThenWardenBeat()
    {
        var reg = LoadLiveRegistry();
        var (_, events) = ArenaBegin(GuardianTier.Savage, auditReg: reg);

        var dialogueEvents = events.OfType<WeighingDialogueEvent>().ToList();
        Assert.That(dialogueEvents, Has.Count.EqualTo(2), "opening + warden beat");
        Assert.That(dialogueEvents[0].DialogueKey, Is.EqualTo("opening"));
        Assert.That(dialogueEvents[0].Pages, Has.Count.EqualTo(6));
        Assert.That(dialogueEvents[1].DialogueKey, Does.Contain("guardian_warden_of_wardens"));
        Assert.That(dialogueEvents[1].Pages, Has.Count.EqualTo(2));
    }

    [Test]
    public void Begin_AlliedWarden_EmitsAlliedBeat()
    {
        var reg = LoadLiveRegistry();
        var (_, events) = ArenaBegin(GuardianTier.Allied, auditReg: reg);

        var dialogueEvents = events.OfType<WeighingDialogueEvent>().ToList();
        Assert.That(dialogueEvents.Any(d => d.DialogueKey.Contains("allied")), Is.True,
            "an allied Warden beat is emitted");
        var beat = dialogueEvents.First(d => d.DialogueKey.Contains("allied"));
        Assert.That(beat.Pages[1].Text, Does.Contain("I stand with you"),
            "the allied Guardian line fires");
    }
}
