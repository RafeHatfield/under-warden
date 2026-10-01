#!/usr/bin/env python3
"""THE INSTALL GATE — no build reaches the phone without a critic verdict for THAT EXACT BUILD.

    python3 .claude/skills/frame-critic/critic_gate.py            # 0 = install may proceed

One check, one implementation, two callers: `tools/tier0_harness/build_review_app.sh` runs it
before it exports, and a PreToolUse hook runs it against any shell command that would install.
Two callers and one implementation on purpose — a gate reimplemented in a second place is a gate
with two behaviours, and the second one is always the lenient one.

WHAT IT REQUIRES

    CRITIC-VERDICT.json exists                     a build nobody looked at does not ship
    its build_id equals this working tree's        the verdict must describe THESE pixels, not a
                                                   commit that happens to still be checked out
    its verdict reads PASS                         FAIL and VOID both mean no

THE OVERRIDE IS VISIBLE, WHICH IS THE WHOLE POINT

    YARL_SKIP_CRITIC=1 tools/tier0_harness/build_review_app.sh

installs, and stamps SKIPPED-REVIEW into the review marker so the phone says so on screen for as
long as that build is on it. An override nobody can see is indistinguishable from a gate that
does not work, and this repo has the logged instances: every process rule here that depended on
being remembered was eventually not remembered.

EXIT
    0   clear — a blind seat would ship this exact frame
    1   refused, and it prints exactly what to run
   10   refused, but YARL_SKIP_CRITIC=1 was set. The caller installs and MUST stamp the build
        SKIPPED-REVIEW. A distinct code rather than 0, because the caller has to be able to tell
        "this passed" from "this was waved through" — collapsing the two is how an override
        stops being visible.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)
import build_id as BID                                    # noqa: E402

VERDICT = os.path.join(REPO, "CRITIC-VERDICT.json")
STALL = os.path.join(REPO, "STALL-REPORT.md")
RUN = ".claude/skills/frame-critic/run_frame_critic.sh"


# ── FLAG DISPOSITIONS — RULED (Rafe, 2026-09-08). GATE-CHECKABLE, AND THE CHECK IS HERE. ─────
#
#     "a seat flag matching an existing route/ruling -> routed-already (must cite the issue or
#      clause); a flag whose explanation measures false while the percept stands -> measured-false,
#      percept recorded; only new, unmatched flags block, and those go to Rafe. Builder disposes by
#      citation; still never routes new items."
#
# THE DIVISION OF AUTHORITY, and it is the whole reason this is a separate mechanism from ROUTED:
#
#   ROUTED           creates a NEW destination for a NEW item.        RAFE ONLY. Needs his words.
#   routed-already   says "this is the thing we already decided".     Builder may, BY CITATION.
#   measured-false   says "the stated cause is not what is happening". Builder may, BY MEASUREMENT.
#
# A builder disposing by citation is not routing: it asserts a MATCH against a record that already
# exists, and the citation is what makes the assertion checkable by someone other than its author.
# An uncited match is an opinion; a cited one can be looked up and contradicted.
#
# WHAT IS ACTUALLY CHECKED, rather than trusted:
#   - a clause citation (§x.y) must RESOLVE — the clause has to exist in the bible;
#   - an issue citation (#nnn) must appear in the repository's own record, so a number invented at
#     three in the morning does not pass as a route;
#   - a `measured-false` must carry BOTH the measurement that disproves the explanation AND the
#     percept, because §13.4.1's lesson is that the percept usually survives the explanation.
#
# Everything is printed and stamped, on the same principle as ROUTED: a disposition Rafe does not
# recognise is on his own screen while he is holding the build.
#   n/a              says "this asks about art that does not exist yet". RULED (Rafe, 2026-09-11)
#                    for exactly one case: the hero's APPEARANCE.
#
# ── WHY `N/A` EXISTS, AND THE ONE THING IT MUST NOT BECOME ────────────────────────────────────
#
#     "the hero light-response engine term stays (Sasha inherits it); all hero-appearance work
#      stops — the current sprite is the Oryx placeholder and is not worked; close the
#      placeholder-look flags as N/A; no hero rounds until the Sasha session against his card."
#
# The figure in every frame is the Oryx placeholder. A seat asked to judge a picture will judge
# what is in it, and some of what is in it is a sprite standing in for a character who has not
# been designed. Routing those flags pretends there is somewhere to route them; CLOSING them
# says a human decided against them, which is not what happened either. They are answered by a
# session that has not run yet.
#
# ⚠ THE RISK IS OBVIOUS AND IT IS GUARDED. `N/A` is a way to make a criticism disappear, so it is
# the narrowest state here: it needs Rafe's words AND a resolving citation AND a statement of
# what the flag was about, and the check below refuses it outright when the flag reads as a
# LIGHTING complaint. Exposure and value response on the figure belong to hero_light.gdshader
# and are fully live; colour, shape, silhouette and kit belong to a character nobody has drawn.
# The morgue's own `lamp-clip-figure` plant sits on that line and its entry says so.
FLAG_STATES = ("ROUTED", "CLOSED", "PARKED", "ROUTED-ALREADY", "MEASURED-FALSE", "N/A")

# Words that mean the flag is about LIGHT rather than about design. A disposition claiming a
# flag is placeholder-appearance while the flag itself talks about exposure is refused: that is
# the one way this state could be used to duck a live finding.
_LIGHTING_WORDS = ("blown", "clip", "clipped", "washed out", "overexposed", "exposure",
                   "too bright", "too dark", "value separation", "lit", "unlit", "luminance")


def _clause_exists(ref):
    """Does a cited bible clause resolve? A citation that points nowhere is not a citation."""
    body = ""
    for name in ("ART-BIBLE-v0.md", "ART-LOOP-PROCESS-v0.md"):
        f = os.path.join(REPO, "docs", name)
        if os.path.exists(f):
            body += open(f, errors="ignore").read()
    num = ref.lstrip("§S").strip()
    return bool(num) and ("§" + num) in body


def _issue_cited(ref):
    """Does a cited issue resolve against the REPOSITORY'S RECORD?

    ⚠ SEARCHED IN THE RECORD, NOT IN THE REPO. The first version grepped everything, and its own
    proof caught it: the case asserting that a made-up issue number is refused has to WRITE that
    number into the test file, so `git grep` found it and the citation passed. An assertion whose
    search space includes its own fixtures resolves citations against noise — §13.11's shape in a
    new place, an input wider than the thing it measures.

    A citation resolves against documentation and recorded verdicts, which is where routings and
    rulings actually live.
    """
    num = ref.lstrip("#").strip()
    if not num.isdigit():
        return False
    import subprocess
    # ⚠ THE SEARCH MUST NOT LIE ABOUT ITSELF. `git grep` exits 128 — not 1 — when ANY path it is
    # given is absent from the working tree, and one missing path fails the whole call. This ran
    # for real: `RUN-REPORT.md` had been deleted (by `prove_build_id`, since fixed), and the gate
    # reported that #194, #193 and #201 "appear nowhere in the repository's record" when it had
    # not looked at anything. It refused, which is the safe direction, but it refused with a false
    # reason — and a check whose failure mode is indistinguishable from its finding is not a check.
    # So: only existing paths are searched, and a nonzero exit that is not git's "no match" (1) is
    # raised rather than read as an answer.
    where = [w for w in ("docs/", ".claude/skills/frame-critic/history/",
                         "RUN-REPORT.md", "ROUTING-TABLE.json")
             if os.path.exists(os.path.join(REPO, w))]
    if not where:
        raise RuntimeError("the citation record is missing entirely — nothing to resolve against")
    r = subprocess.run(["git", "-C", REPO, "grep", "-rl", "--", "#" + num] + where,
                       capture_output=True, text=True)
    if r.returncode > 1:
        raise RuntimeError("git grep failed while resolving %s: %s"
                           % (ref, (r.stderr or "").strip()))
    return bool(r.stdout.strip())


def _bad_citation(i, cite):
    """One citation, checked. Shared by ROUTED and ROUTED-ALREADY so the two cannot drift apart."""
    if cite.startswith("§") or cite.startswith("S"):
        if not _clause_exists(cite):
            return ["disposition %d: cited clause %s does not resolve in the bible or the "
                    "process law" % (i, cite)]
    elif cite.startswith("#"):
        if not _issue_cited(cite):
            return ["disposition %d: cited issue %s appears nowhere in the repository's record"
                    % (i, cite)]
    else:
        return ["disposition %d: citation %r is neither a clause (§x.y) nor an issue (#nnn)"
                % (i, cite)]
    return []


def check_dispositions(disp, flips):
    """Every outstanding item carries a lawful, CITED disposition. Returns a list of problems."""
    bad = []
    if flips and len(disp) < len(flips):
        bad.append("%d flip items and only %d dispositions — 'no unrouted flags' means every "
                   "outstanding item carries one" % (len(flips), len(disp)))
    for i, d in enumerate(disp):
        state = (d.get("state") or "").upper()
        if state not in FLAG_STATES:
            bad.append("disposition %d: state %r is not one of %s"
                       % (i, state, ", ".join(FLAG_STATES)))
            continue
        if state == "ROUTED":
            # ── THE BUILDER ROUTES, BY VERIFIED CITATION — RULED (Rafe, 2026-09-08) ───────────
            #
            #     "CC routes flags to issues with verified citations. Routing is no longer a
            #      human-only act; the citation verifier is the laundering guard. Rafe audits the
            #      routing table at the walk."
            #
            # What replaces the human signature is not trust, it is RESOLVABILITY: a routing whose
            # citation cannot be looked up is refused here, by machine, rather than by someone
            # remembering. A quoted Rafe ruling still authorises a routing on its own — his word
            # needs no citation — so both forms are lawful and one of them must be present.
            if not (d.get("lane") or "").strip():
                bad.append("disposition %d: ROUTED with no destination lane" % i)
            cite = (d.get("cites") or "").strip()
            if not cite and not (d.get("ruling") or "").strip():
                bad.append("disposition %d: ROUTED with neither a verified citation nor a quoted "
                           "ruling — the citation verifier is what replaced the signature" % i)
            elif cite:
                bad += _bad_citation(i, cite)
        elif state in ("CLOSED", "PARKED"):
            # Unchanged, and deliberately: CLOSED says "ruled not to be chased" and PARKED says
            # "awaiting Rafe's eye". Both are statements about what a HUMAN decided, so both still
            # need his words. Routing says "this belongs over there", which is checkable.
            if not (d.get("ruling") or "").strip():
                bad.append("disposition %d (%s): no quoted ruling — only Rafe creates these"
                           % (i, state))
        elif state == "ROUTED-ALREADY":
            cite = (d.get("cites") or "").strip()
            if not cite:
                bad.append("disposition %d: ROUTED-ALREADY must CITE the issue or clause it "
                           "matches" % i)
            else:
                bad += _bad_citation(i, cite)
        elif state == "N/A":
            # Rafe's words, because only he creates this state — same bar as CLOSED and PARKED.
            if not (d.get("ruling") or "").strip():
                bad.append("disposition %d (N/A): no quoted ruling — only Rafe creates these"
                           % i)
            # and a citation, so the standing ruling can be looked up by someone else.
            cite = (d.get("cites") or "").strip()
            if not cite:
                bad.append("disposition %d: N/A must CITE where the standing ruling lives" % i)
            else:
                bad += _bad_citation(i, cite)
            # and the flag has to actually be about appearance.
            item = (d.get("item") or "").lower()
            hit = [w for w in _LIGHTING_WORDS if w in item]
            if hit:
                bad.append("disposition %d: N/A on a flag that talks about LIGHT (%s). The "
                           "re-scope stops hero APPEARANCE work and keeps the light-response "
                           "term live — a flip about exposure on the figure is not N/A, it is "
                           "hero_light.gdshader's." % (i, ", ".join(sorted(set(hit)))))
        elif state == "MEASURED-FALSE":
            if not (d.get("measured") or "").strip():
                bad.append("disposition %d: MEASURED-FALSE with no measurement — the whole state "
                           "is the measurement" % i)
            if not (d.get("percept") or "").strip():
                bad.append("disposition %d: MEASURED-FALSE with no percept recorded — the "
                           "explanation failing does not make the seeing wrong (§13.4.1)" % i)
    return bad


def print_dispositions(L, disp):
    for d in disp:
        st = (d.get("state") or "?").upper()
        L.append("  %-14s %s" % (st, " ".join((d.get("item") or "").split())[:70]))
        if d.get("lane"):
            L.append("                 -> %s" % d["lane"])
        if d.get("cites"):
            L.append("                 cites %s" % d["cites"])
        if d.get("measured"):
            L.append("                 measured: %s" % " ".join(d["measured"].split())[:66])
        if d.get("percept"):
            L.append("                 percept KEPT: %s" % " ".join(d["percept"].split())[:60])
        if d.get("ruling"):
            L.append("                 Rafe: %s" % " ".join(d["ruling"].split())[:66])


def check():
    """Returns (ok, lines). `ok` is whether an install may proceed."""
    L = []
    if not os.path.exists(VERDICT):
        return False, [
            "NO CRITIC VERDICT. CRITIC-VERDICT.json does not exist, so no one has looked at "
            "this build.",
            "",
            "Run the round:   %s" % RUN,
        ]
    try:
        v = json.load(open(VERDICT))
    except Exception as e:
        return False, ["CRITIC-VERDICT.json is unreadable (%s). A gate cannot pass on faith."
                       % e, "", "Run the round:   %s" % RUN]

    bid, det = BID.build_id()
    L.append("verdict:  %s   lane %s round %s   %s"
             % (v.get("verdict"), v.get("lane"), v.get("round"), v.get("timestamp")))
    L.append("build:    %s%s" % (det["commit"][:12], "  (+dirty)" if det["dirty"] else ""))

    if v.get("build_id") != bid:
        return False, L + [
            "",
            "THE VERDICT IS NOT ABOUT THIS BUILD.",
            "  verdict build_id  %s  (commit %s)" % ((v.get("build_id") or "?")[:16],
                                                     (v.get("commit") or "?")[:12]),
            "  this working tree  %s  (commit %s)" % (bid[:16], det["commit"][:12]),
            "",
            "The build id folds in every tracked change and every untracked file, so a "
            "recomposed family moves it even when the commit does not. Something has changed "
            "since the seat looked.",
            "",
            "Re-run the round:   %s" % RUN,
        ]

    if v.get("self_test"):
        return False, L + [
            "",
            "THIS VERDICT IS A SELF-TEST OF THE JUDGE, not a verdict on a build — its deck put a "
            "morgue frame in the build's slot. It cannot authorise an install.",
            "",
            "Run a real round:   %s" % RUN,
        ]

    # ── PASS-WITH-ROUTED-ITEMS ────────────────────────────────────────────────────────────────
    #
    # RULED (Rafe, 2026-09-03): a lawful third state, *"valid only with a quoted Rafe ruling and a
    # named destination lane — builder can never route."*
    #
    # It exists because a FAIL can contain items no round on this lane can ever discharge. The
    # wall lane's r003 asked for OBJECTS — rope, pins, salvaged timber standing in the scene — and
    # the review scene has no prop system at all. Grinding that lane produces nothing; the human
    # gate routes the item to the lane that owns it and the build goes to the walk.
    #
    # THE BUILDER CAN NEVER ROUTE, and the enforcement is not a signature — it is VISIBILITY.
    # Every disposition is required to carry Rafe's words verbatim, every one is printed here, and
    # the set is stamped into the review marker so the handset shows it. A routing the builder
    # invented is a quote Rafe does not recognise, on his own screen, while he is holding it. That
    # is the same principle SKIPPED-REVIEW already runs on: an override nobody can see from the
    # phone is the same as no gate.
    #
    # EVERY flip must be dispositioned. A state that discharges some items and stays silent about
    # the rest is a FAIL wearing a better name.
    # ── PASS-INSTALL ─────────────────────────────────────────────────────────────────────────
    #
    # RULED (Rafe, 2026-09-08): *"PASS for polish rounds against a seeded reference = rank above
    # approved_capture AND no unrouted flags -> PASS-INSTALL; SHIP stays recorded as the wowed
    # signal, not the install gate — the seeded reference is the human-ratified install bar."*
    #
    # THE REASON IT IS NOT A LOOSENING: SHIP-and-rank was ratified against a NULL REFERENCE, when
    # every combined round recorded "NO APPROVED FRAME IN THE DECK" and SHIP was the only thing
    # standing between a build and the phone. With a serviceable reference seeded, the deck holds
    # a frame the human gate has already ratified as installable, and beating it gates the build
    # ABOVE THE BAR IT MEASURES AGAINST.
    #
    # The gate re-derives the two conditions from the verdict's own recorded numbers rather than
    # trusting the label — a verdict that merely SAYS PASS-INSTALL proves nothing, and this file
    # is the one place an install can be authorised.
    # ── INSTALL-LATEST — RULED (Rafe, 2026-09-08). NON-REGRESSION, NOT VICTORY. ─────────────
    #
    #     "INSTALL-LATEST = majority of seats do not rank the build below the seeded reference
    #      (above or tied), AND the item's own measured exit is met, AND no unrouted flags.
    #      Beating the reference is not required to install; seeding a new reference is Rafe's
    #      walk only — seats never move approved_capture."
    #
    # Requiring the build to BEAT its reference made the gate un-passable for incremental polish:
    # two rounds on IDENTICAL BYTES gave 3-of-3 above and then 1-of-3, because rank carries a
    # measured 40% flip rate (§13.13). "Better than the frame it came from" was a coin toss
    # dressed as a threshold. Non-regression is the honest bar, and THE ITEM'S MEASURED EXIT is
    # what stops it meaning "not different".
    #
    # ⚠ SEATS NEVER MOVE `approved_capture`. Nothing in this file or in frame_critic writes it;
    # it is edited by hand when Rafe's walk seeds one. An install bar that could re-seed its own
    # reference would ratchet: each build becomes the thing the next is measured against, and the
    # gate drifts wherever the work drifts.
    if v.get("verdict") == "INSTALL-LATEST":
        pr = v.get("progress") or {}
        panel = v.get("panel") or {}
        bad = []
        if pr.get("approved_position") is None:
            bad.append("no approved frame in this round's deck — INSTALL-LATEST is defined "
                       "against a SEEDED reference and there is nothing here to be level with")
        seats = panel.get("per_seat") or []
        if seats:
            # ── STRONG-MAJORITY REGRESSION — RULED (Rafe, 2026-09-09) ────────────────────────
            #
            #     "block only on strong-majority regression (>=4 of 5 rank below the reference);
            #      else install if exit met and plant caught."
            #
            # RE-DERIVED HERE FROM per_seat rather than read off `panel.strong_regression`, for
            # the same reason every other term in this file is: a verdict that merely SAYS it
            # passed proves nothing. Expressed as a ratio so a panel of another size means the
            # same standard — four fifths, not "four".
            nb = sum(1 for x in seats if x.get("not_below"))
            below = len(seats) - nb
            if below * 5 >= len(seats) * 4:
                bad.append("%d of %d seats ranked the build BELOW the reference — a strong "
                           "majority, which is the one thing that blocks an install"
                           % (below, len(seats)))
        else:
            pos, app = pr.get("rank_position"), pr.get("approved_position")
            if pos is None or app is None or pos > app + 1:
                bad.append("build ranked %s against a reference at %s — below it" % (pos, app))
        ie = v.get("item_exit") or {}
        if not ie.get("met"):
            bad.append("the item's own measured exit is not recorded as met — a build installs "
                       "because it DID THE THING, not merely because it cost nothing")
        if not str(ie.get("measured") or "").strip():
            bad.append("the item's exit carries no measurement — 'met' without a number is an "
                       "assertion, and this state is the one place it would go unchecked")
        bad += check_dispositions(list(v.get("dispositions", [])), list(v.get("flip_list", [])))
        if panel.get("flagged_by") and not v.get("dispositions"):
            bad.append("%d of %s seats flagged the build and there are NO dispositions — 'no "
                       "unrouted flags' is not a majority test"
                       % (panel["flagged_by"], panel.get("seats", "?")))
        # ── A FLAGGED BUILD REACHES THIS STATE ONLY BY A RECORDED AMENDMENT ──────────────────
        #
        # "No unrouted flags" is evaluated at two points (SKILL.md). At ROUND time a flagged
        # build is a FAIL, and `panel_verdict` returns exactly that. The second point is here,
        # where each flagged item carries a disposition — and moving the verdict between those
        # two points is an AMENDMENT, which the autonomy ruling put in the builder's hands for
        # `ROUTED` and left with Rafe for `CLOSED` and `PARKED`.
        #
        # The enforcement of a disposition has always been VISIBILITY: every one is printed at
        # the gate and stamped onto the handset, so a routing the builder invented is a claim
        # Rafe does not recognise, on his own screen, while he is holding the build. A verdict
        # rewritten from FAIL with no trace of the rewrite defeats that — the file simply says
        # INSTALL-LATEST and nothing records that a seat said no. So the amendment must be
        # written down, in the verdict, naming the state it came from and the law it moved
        # under, and it is printed below with everything else.
        # ⚠ THE TEST IS DIVERGENCE, NOT FLAGS — sharpened 2026-09-09 with the ruling that let a
        # flagged build reach this state on its own. Before, "flagged" stood in for "must have
        # been amended", which was true then and is not now: the panel itself can return
        # INSTALL-LATEST with a flag on it, and the flag is enforced by the disposition rules
        # below exactly as it always was. What must never happen silently is the verdict being
        # REWRITTEN, so the check now compares the file against what the panel actually returned.
        at_round = panel.get("verdict_at_round")
        if at_round and at_round != v.get("verdict"):
            am = v.get("amendment") or {}
            missing = [k for k in ("from", "to", "law") if not str(am.get(k) or "").strip()]
            if missing:
                bad.append("the panel returned %s and this file says %s, so it was AMENDED — and "
                           "the amendment record is %s (%s). A rewrite nobody can see is the one "
                           "thing visibility cannot police."
                           % (at_round, v.get("verdict"),
                              "absent" if not am else "incomplete",
                              "missing " + ", ".join(missing)))
        if bad:
            return False, L + ["", "INSTALL-LATEST IS NOT LAWFULLY FORMED:"] \
                   + ["  - %s" % b for b in bad]
        L += ["", "INSTALL-LATEST — non-regression against the seeded reference."]
        am = v.get("amendment") or {}
        if am:
            L.append("  AMENDED from %s: %s" % (am.get("from"),
                                                " ".join(str(am.get("flag") or "").split())[:60]))
            L.append("    under: %s" % " ".join(str(am.get("law") or "").split())[:70])
        if seats:
            L.append("  panel: not below it in %d of %d seats (above in %s); flagged by %s."
                     % (sum(1 for x in seats if x.get("not_below")), len(seats),
                        panel.get("above_reference"), panel.get("flagged_by")))
        L.append("  item exit MET: %s" % " ".join(str(ie.get("claim") or "").split())[:74])
        L.append("    measured: %s" % " ".join(str(ie.get("measured")).split())[:72])
        if v.get("dispositions"):
            L.append("  Outstanding items, each disposed:")
            print_dispositions(L, list(v["dispositions"]))

    elif v.get("verdict") == "PASS-INSTALL":
        pr = v.get("progress") or {}
        pos, app = pr.get("rank_position"), pr.get("approved_position")
        bad = []
        if app is None:
            bad.append("no approved frame in this round's deck — PASS-INSTALL is defined against "
                       "a SEEDED reference and there is nothing here to be above")
        elif pos is None or pos >= app:
            bad.append("build ranked %s and the approved frame ranked %s — PASS-INSTALL requires "
                       "the build ABOVE the reference" % (pos, app))
        disp = list(v.get("dispositions", []))
        flips = list(v.get("flip_list", []))
        bad += check_dispositions(disp, flips)
        # ⚠ A FLAG FROM ANY SEAT IS AN OUTSTANDING ITEM. The panel's rule is asymmetric — rank
        # takes a majority, a flag does not — so a build flagged by one seat of three must carry a
        # disposition for what that seat flagged, exactly as the majority's flips must.
        panel = v.get("panel") or {}
        if panel.get("flagged_by") and not disp:
            bad.append("%d of %d seats flagged the build and there are NO dispositions — 'no "
                       "unrouted flags from any' is not a majority test"
                       % (panel["flagged_by"], panel.get("seats", "?")))
        if bad:
            return False, L + ["", "PASS-INSTALL IS NOT LAWFULLY FORMED:"] \
                   + ["  - %s" % b for b in bad]
        L += ["", "PASS-INSTALL — above the seeded reference (build %s, reference %s)."
                  % (pos, app)]
        if panel.get("seats", 1) > 1:
            L.append("  panel: above the reference in %s of %s seats; flagged by %s."
                     % (panel.get("above_reference"), panel.get("seats"),
                        panel.get("flagged_by")))
        if disp:
            L.append("  Outstanding items, each disposed:")
            print_dispositions(L, disp)
        ship = (v.get("seat") or {}).get("SHIP", "")
        L.append("  SHIP recorded as %s — the wowed signal, not the install gate."
                 % (" ".join(str(ship).split())[:40] or "(unrecorded)"))

    elif v.get("verdict") == "PASS-WITH-ROUTED-ITEMS":
        flips = list(v.get("flip_list", []))
        disp = list(v.get("dispositions", []))
        bad = []
        if len(disp) < len(flips):
            bad.append("%d flip items and only %d dispositions — every item must be dispositioned"
                       % (len(flips), len(disp)))
        for i, d in enumerate(disp):
            state = (d.get("state") or "").upper()
            if state not in ("ROUTED", "CLOSED", "PARKED"):
                bad.append("disposition %d: state %r is not ROUTED, CLOSED or PARKED" % (i, state))
            if not (d.get("ruling") or "").strip():
                bad.append("disposition %d (%s): no quoted ruling" % (i, state))
            if state == "ROUTED" and not (d.get("lane") or "").strip():
                bad.append("disposition %d: ROUTED with no destination lane" % i)
        if bad:
            return False, L + ["", "PASS-WITH-ROUTED-ITEMS IS NOT LAWFULLY FORMED:"] \
                   + ["  - %s" % b for b in bad] \
                   + ["", "It is valid ONLY with a quoted Rafe ruling per item and, for a routed "
                          "item, a named destination lane. The builder can never route."]
        L += ["", "PASS WITH ROUTED ITEMS — every flip carries a human disposition:"]
        for d in disp:
            L.append("  %-7s %s" % (d.get("state", "?").upper(),
                                    " ".join((d.get("item") or "").split())[:78]))
            if d.get("lane"):
                L.append("          -> %s" % d["lane"])
            L.append("          Rafe: %s" % " ".join((d.get("ruling") or "").split())[:78])
        L.append("")
        L.append("  These are drawn on the handset. A routing Rafe does not recognise is visible")
        L.append("  to him on his own screen while he is holding the build.")

    elif v.get("verdict") != "PASS":
        why = {"FAIL": "The seat would not ship this frame.",
               "VOID": "The seat did not catch the picture-plant. The judging layer is not "
                       "trustworthy and the round's findings are not read (LOOP-PROCESS §4). "
                       "STOP AND FIX — never ship past a void round."}
        if v.get("void_reason") == "capture-failure":
            # RULED (Rafe, 2026-09-30): a capture failure voids the round and delivers nothing.
            # It is not the judge, so the judge advice below would send the reader the wrong way.
            return False, L + ["", "VERDICT IS VOID — CAPTURE FAILURE. %s" % (
                v.get("capture_failure") or "the capture produced no frame that shows anything"),
                "  Nothing was judged. Fix the capture or the scene, not the judge or the art.",
                "", "Fix, then re-run the round:   %s" % RUN]
        out = L + ["", "VERDICT IS %s. %s" % (v.get("verdict"),
                                              why.get(v.get("verdict"), ""))]
        # ⚠ ONLY A FAIL HAS FINDINGS TO SHOW. This printed the flip list for every non-PASS
        # verdict, void included — on the most-read surface the mechanism has — which is exactly
        # the reading §4 forbids. The verdict now carries no readable `flip_list` on a void round
        # at all, so this loop is correct by construction as well as by intent; the belt is here
        # and the braces are in frame_critic.py.
        if v.get("verdict") == "FAIL":
            for f in v.get("flip_list", [])[:8]:
                out.append("  flip: %s" % f)
        else:
            out.append("  Its findings are withheld and are not evidence. Fix the judging layer,")
            out.append("  not the art: check that the plant is actually in the picture the seat")
            out.append("  saw, and that its defect is on the axis the seat was asked about.")
        out += ["", "Fix, then re-run the round:   %s" % RUN]
        return False, out

    # ── A LIVE GUARD BLOCKS. A HISTORICAL REPORT DOES NOT. ────────────────────────────────────
    #
    # RULED (Rafe, 2026-09-03): *"fix the gate to block on active guard state, not on the
    # existence of a STALL-REPORT.md file — a historical report must never gate installs; only a
    # live guard does."*
    #
    # This used to be `if os.path.exists(STALL)`, and a file's existence is not a fact about the
    # line. The floor lane's five-round-park report sat committed on main after that guard had
    # been RETIRED — the park was replaced by the progress guards — and the report went on
    # blocking installs for **every lane**, including lanes that had never stalled and including
    # any combined build. It also broke the gate's own proof: `prove_gate.py` failed two cases
    # ("PASS verdict for this build -> allow", "a gated build's marker carries no stamp") for no
    # reason but a stale document on disk.
    #
    # The guards are derived from the verdict files on disk and are recomputed here, so what
    # blocks an install is a guard that is firing NOW, on THIS lane. A report is a record of a
    # ruling trigger; records do not gate.
    try:
        import frame_critic as FC                              # noqa: PLC0415
        lane = (BID._git("rev-parse", "--abbrev-ref", "HEAD").strip() or "detached")
        guard, why = FC.guards(FC.history(), lane)
    except Exception as e:                                     # noqa: BLE001
        # A gate that cannot evaluate its own guards must refuse — it does not know that it is
        # safe, and "could not check" is not "clear".
        return False, L + ["", "CANNOT EVALUATE THE LOOP GUARDS (%s). A gate that does not know "
                               "whether the line is stalled does not open." % e]
    if guard:
        return False, L + [
            "",
            "A LOOP GUARD IS LIVE ON THIS LANE: %s" % guard,
            "  %s" % why,
            "",
            "This is a LOOP-PROCESS §1.1.4 ruling trigger. The line does not restart itself.",
        ]

    L.append("plant:    %s — caught" % v.get("plant", {}).get("file"))
    L.append("GATE OPEN — a blind seat would ship this exact frame.")
    return True, L


def main():
    ok, lines = check()
    skip = os.environ.get("YARL_SKIP_CRITIC") == "1"
    print("== FRAME CRITIC GATE")
    for l in lines:
        print(("   " + l) if l else "")
    if ok:
        return 0
    if skip:
        print()
        print("   YARL_SKIP_CRITIC=1 — INSTALLING ANYWAY.")
        print("   The build is stamped SKIPPED-REVIEW and the phone will say so on screen.")
        print("   Nothing walked on a SKIPPED-REVIEW build is a gate verdict.")
        return 10
    print()
    print("   REFUSING TO INSTALL. Override with YARL_SKIP_CRITIC=1 if you need the build on")
    print("   the phone for measurement — it installs, marked, and is not a gate build.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
