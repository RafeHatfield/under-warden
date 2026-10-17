# STALL REPORT — broken-judge

**The line has stopped and is not restarting itself.** LOOP-PROCESS §1.1.4 ruling trigger: this report is the evidence.

- **lane** `art/lambda-opening-212`
- **surface** `combined`
- **guard** `broken-judge`
- **written** 2026-10-08T13:22:55

## Why it stopped

the seat-level plant term tripped: 3 seats missed a live plant in one round (seats 2, 3, 5). That is not one seat having a bad day.
Ruled 2026-09-11: a live-plant miss voids the SEAT and the slot is re-drawn once. This is the case that outruns the re-draw.

## What was tried, round by round

`rank` is where the build placed in that round's blind shuffled deck, and `score` normalises it so decks of different sizes compare — 1.00 is first, 0.00 is last. `Δpic` is how far the delivered frame moved from the previous round: mean and worst cell, in luminance levels. `0.000 / 0` means the picture did not change at all.

| round | verdict | rank | score | best? | Δpic | build | the seat's own words |
|---|---|---|---|---|---|---|---|
| 2 | VOID | — | — |  | — | `9929d647c13e` |  |
| 3 | VOID | 3/4 | 0.33 |  | — | `9bd609f6ce00` | 3 is a flat, stamped tileset with no lighting at all. The grey wall mass at top-right (x 256–384, y 0–110) is the same featureless grey plat |

## The flip lists, verbatim

Void rounds do not appear here. §4: the plant was missed, so those findings are not read — they are kept in the verdict under `flip_list_withheld` and are not evidence.

## Where to look

Captures and transcripts, per round:

- round 2 — deck `?`, transcript `?`
- round 3 — deck `/Users/rafehatfield/.claude/frame-critic/deck-7ba0e5ce585b016f`, transcript `.claude/skills/frame-critic/history/r003-art_lambda-opening-212-transcript-seat1.txt`

## What is being asked for

A ruling. Not another round — the guard fired precisely because another round is the wrong move. Nothing installs to the phone while this stands.
