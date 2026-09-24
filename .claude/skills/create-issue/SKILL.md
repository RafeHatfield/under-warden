---
name: create-issue
description: Use when creating, filing, or opening a GitHub issue or ticket for The Under-Warden — covers the required thread label and milestone, the optional type:bug label, sub-issue structure, and body discipline. Also use when correcting an existing issue's labels or milestone.
---

# Issue / ticket creation contract

All work tracking lives in GitHub Issues + the "Catacombs of Yarl — Release"
Project board. When creating any issue, follow this exactly. Do not
reintroduce the taxonomy this replaces.

**Type — one optional label, `type:bug`.**

```bash
gh issue create --repo RafeHatfield/under-warden \
  --title "…" --body "…" --milestone "…" --label "thread:<name>"
# add --label "type:bug" when the issue is a defect
```

Apply `type:bug` when current behaviour is **wrong relative to intent** — a
regression, a contract the code does not honour, or output that is incorrect
rather than merely coarse or unfinished. Everything else carries no type label;
"work to be done" is the default and does not need marking. Do not classify
feature-vs-task — that distinction has never changed what happens next.

**Do not use native issue Type.** GitHub issue Types are an organisation-level
feature and `RafeHatfield` is a User account, so `gh issue create --type` fails
with `type "Bug" not found` and the field cannot be set by any route. An earlier
version of this contract mandated native Type and deleted the `type:*` labels in
its favour; the Type was never actually available, which left 33 issues with no
kind-signal at all. If the repo ever moves under an organisation, revisit.

**`type:idea`** marks a backlog item that is not committed. `type:story` was
retired — zero uses across 33 issues, and epic→story→task structure is carried by
native sub-issues (`--parent`), or a `parent: #N` body note plus post-hoc linking
if this gh version lacks `--parent`.

**Thread — apply exactly one `thread:*` label** (`thread:foundation|voice|
launch|combat|spine|art`). Do **not** attempt to set the Project "Thread"
field yourself — `gh issue create` cannot write Project fields. A workflow
(`.github/workflows/set-thread-field.yml`) reads the label and populates the
field automatically. Your only obligation is the correct label; the field
follows. Every issue must carry exactly one thread label so the workflow can
resolve it.

**Milestone — assign one** where the work maps to `M1–M7`. Leave unset only
when the roadmap genuinely provides no home (e.g. most art-conformance work),
and flag that rather than guessing.

**Meta labels** as applicable: `exit-gate`, `blocked`, `needs-ruling`.

**Cross-cutting work files to Foundation.** If an issue spans threads, it
carries `thread:foundation` and names the coordination in its body — never a
second thread label, and never a duplicate issue under the other thread.
Reference the Foundation issue number from dependent threads instead.

**Body discipline:** current factual state only — no historical narrative.
2–5 lines: current state, exit condition, and a link to the relevant
`ROADMAP_release_2026-07.md` section or rubric file. Gate *definitions* live
in the roadmap; issues carry live *state*. Do not duplicate definitions.
