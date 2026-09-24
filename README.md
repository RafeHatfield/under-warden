# The Under-Warden

Turn-based roguelike — Godot 4 + C#, deterministic ECS, data-driven YAML content, mobile-first (iOS/Android).

See [`docs/README.md`](docs/README.md) for the full documentation index.

---

## Running things

### Tests (no Godot required)

```bash
dotnet test --filter "Category!=Slow"   # fast suite (default)
dotnet test                             # full suite
```

### Scenario harness (balance)

```bash
dotnet run --project tools/Harness -- --scenario depth1_tuned
dotnet run --project tools/Harness -- --all --runs 50
```

### Bot-analysis pipeline

Generates a batch of enriched bot-run transcripts and runs the predicate analysis pipeline in one command.

```bash
./scripts/bot-analysis.sh
```

Output lands in `reports/bot-analysis/`:
- `findings.md` — human-readable: bug candidates, audit trail, system-trigger heatmap, mechanism blind-spots
- `aggregate.json` — structured data for tooling

**Options:**

| Flag | Default | Description |
|---|---|---|
| `--runs N` | 300 | Number of bot runs |
| `--seed N` | 1337 | Fixed base seed (regression mode — same games every run) |
| `--explore` | off | Use epoch second as base seed (fresh games each run) |
| `--floors N` | 10 | Floors per run |
| `--out DIR` | `reports/bot-analysis` | Output directory |

**Exit codes:** 0 = clean, 2 = bug candidates found, 1 = tool error.

Examples:

```bash
./scripts/bot-analysis.sh --runs 20                      # quick smoke
./scripts/bot-analysis.sh --runs 500                     # larger population
./scripts/bot-analysis.sh --explore                      # fresh games
./scripts/bot-analysis.sh --runs 300 --out reports/june  # named output
```

### LLM Player

Runs Claude Haiku as a player. Only calls the API at decision points (monster adjacent,
item underfoot, chest/mural nearby, staircase, low HP with potion) — bot-brain handles
exploration between them. Requires `ANTHROPIC_API_KEY` in the environment.

```bash
export ANTHROPIC_API_KEY=sk-ant-...

dotnet run --project tools/Harness -- --dungeon --llm-transcript /tmp/llm-runs \
  --player llm --persona reader --floors 3 --runs 1 --seed 1337
```

Transcript lands at `<dir>/run-<seed>.jsonl`. Analyse it:

```bash
dotnet run --project tools/Analyst -- \
  --transcript /tmp/llm-runs/run-1337.jsonl \
  --rubric config/rubric/v1.yaml
```

**Options:**

| Flag | Default | Description |
|---|---|---|
| `--persona reader\|system_explorer` | `reader` | Reader engages narrative; System Explorer triggers mechanics |
| `--floors N` | 10 | Floors per run |
| `--runs N` | 1 | Number of runs (each is a separate API session) |
| `--seed N` | random | Fixed seed for reproducibility |

**Cost:** ~$0.01–0.05 per 3-floor run at Haiku rates (~15–20% of turns reach the API).

### Visual bot mode (Godot, debug builds only)

Open the game in the Godot editor and use these hotkeys to watch the bot play live. The bot
runs the same `BotBrain` logic as the headless harness — same decisions, just rendered.
Useful for debugging: watch where it freezes, which items it ignores, how it navigates.

| Key | Action |
|-----|--------|
| **F4** | Toggle bot on / off (you get control back instantly) |
| **F5** | Cycle persona: balanced → cautious → aggressive → greedy → speedrunner |
| **F6** | Cycle speed: 1.0 s/turn → 0.5 → 0.25 → 0.1 → 0.0 (max, still rendered) |

Not available on iOS — debug builds only. The HUD shows the current persona and speed while
the bot is running.

For **batch runs with analysis** (headless, fast), use the bot-analysis pipeline above.

### Godot (visual game)

Open `UnderWarden.Presentation.sln` in the Godot editor, or export via the Godot CLI.

---

## Architecture

```
src/Logic/       — Pure C# game logic (no Godot). All tests and tools run against this.
src/Presentation/ — Godot scenes, nodes, rendering, input. Thin layer over Logic.
config/          — YAML content: monsters, items, scenarios, voice lines, rubric
tools/Harness/   — Balance pipeline CLI (scenario runs, dungeon soak, transcript generation)
tools/Analyst/   — LLM-testing analyst CLI (predicate checks, findings report)
tests/           — NUnit tests against the logic layer
scripts/         — Operational scripts
```

Balance pipeline: `Scenario YAML → C# harness → enriched JSONL transcripts → Analyst → findings.md`

LLM testing: `Bot or LLM Player → enriched JSONL transcripts → Analyst → findings.md`
