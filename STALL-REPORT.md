# STALL REPORT — broken-judge

**The line has stopped and is not restarting itself.** LOOP-PROCESS §1.1.4 ruling trigger: this report is the evidence.

- **lane** `art/lambda-212`
- **surface** `combined`
- **guard** `broken-judge`
- **written** 2026-09-13T22:26:16

## Why it stopped

the seat-level plant term tripped: 3 seats missed a live plant in one round (seats 1, 2, 3). That is not one seat having a bad day.
Ruled 2026-09-11: a live-plant miss voids the SEAT and the slot is re-drawn once. This is the case that outruns the re-draw.

## What was tried, round by round

`rank` is where the build placed in that round's blind shuffled deck, and `score` normalises it so decks of different sizes compare — 1.00 is first, 0.00 is last. `Δpic` is how far the delivered frame moved from the previous round: mean and worst cell, in luminance levels. `0.000 / 0` means the picture did not change at all.

| round | verdict | rank | score | best? | Δpic | build | the seat's own words |
|---|---|---|---|---|---|---|---|
| 1 | VOID | 3/4 | 0.33 |  | — | `f6e928a324a8` | Nothing in it is lit. The figure at (205–245, 0–30) carries no light; the dirt at the top-left and the dirt at the bottom-right are the same |

## The flip lists, verbatim

Void rounds do not appear here. §4: the plant was missed, so those findings are not read — they are kept in the verdict under `flip_list_withheld` and are not evidence.

## Where to look

Captures and transcripts, per round:

- round 1 — deck `/Users/rafehatfield/.claude/frame-critic/deck-cb5505c3d31a5c41`, transcript `.claude/skills/frame-critic/history/r001-art_lambda-212-transcript-seat1.txt`

## What is being asked for

A ruling. Not another round — the guard fired precisely because another round is the wrong move. Nothing installs to the phone while this stands.
