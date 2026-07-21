# CyberBoss — BossBrain PPO Training

Headless Python training environment for the CyberBoss RL boss policy.
No Unity Editor is required for training. The output is a single `.onnx`
file loaded at runtime by Unity Sentis (Part 9).

## Prerequisites

Python 3.10+. Use a **virtual environment** — do not install into a conda base env (runtime DLL conflicts on Windows).

```
cd Assets/ML/Training
python -m venv venv

# Windows
.\venv\Scripts\pip.exe install torch --index-url https://download.pytorch.org/whl/cpu
.\venv\Scripts\pip.exe install gymnasium stable-baselines3 onnx numpy
.\venv\Scripts\pip.exe install onnxruntime  # only needed for --check-parity
```

The two-step install is required on Windows: PyTorch's CPU wheel must be fetched from its own index; the other packages come from PyPI.

## Quick start

### Train from scratch (2 M steps, ~20–40 min on CPU; faster with GPU)

```
cd Assets/ML/Training
.\venv\Scripts\python.exe cyberboss_env.py --steps 2000000 --run-id cyberboss_v1
```

The script:
1. Trains PPO on the headless combat sim.
2. Runs a validation sweep (200 episodes across all player archetypes).
3. If avg_eff >= 0.35, exports `Assets/ML/Models/BossBrain.onnx` automatically.

### Find and export the best checkpoint across the run

```
.\venv\Scripts\python.exe cyberboss_env.py --eval-checkpoints --run-id cyberboss_v1
```

Evaluates every 200k-step checkpoint on a fixed seed-42 environment, ranks by
avg_eff, and exports the best one. The final checkpoint is not always the best.

### Resume / export from an existing checkpoint

```
python Assets/ML/Training/cyberboss_env.py \
    --export-only \
    --run-id cyberboss_v1 \
    --output Assets/ML/Models/BossBrain.onnx
```

### Override output path

```
python Assets/ML/Training/cyberboss_env.py \
    --steps 2000000 \
    --run-id cyberboss_v2 \
    --output Assets/ML/Models/BossBrain_v2.onnx
```

## Validation thresholds (check before exporting)

The evaluation suite tests the policy against 7 player archetypes:

| Archetype        | Expected dominant boss skill       |
|------------------|------------------------------------|
| aggressive       | AoESlam (2)                        |
| ranged           | Charge (0) or TeleportStrike (3) — both are deliberate overlapping counters (see below), not a strict single answer |
| defensive        | No hard gate, but now has a clean bucket post-fix — `cyberboss_v11_da_fixes` showed ProjectileBurst dominant (55%) over TeleportStrike (36%) |
| dash_heavy       | TeleportStrike (3)                 |
| parry_heavy      | ProjectileBurst (1)                |
| barrier_passive  | ProjectileBurst (1) — `cyberboss_v11_da_fixes` showed this as a near-tie with Charge (34% vs 33%), weaker than parry_heavy's separation; not yet investigated further |
| balanced         | Mixed — no dominant pattern        |

`EXPECTED_DOMINANT_SKILL` values are tuples specifically so an archetype can have more than one
valid counter — `"ranged": (0, 3)` means either Charge or TeleportStrike counts as a match, not
that the policy must prefer one specifically. Add to the tuple (rather than picking one) whenever
an archetype's two competing counters are both intentional design overlap; leave it as a single
value when one skill is supposed to clearly dominate.

ShieldPhase is **not** in this table — it's no longer an RL-selectable skill.
See "Reactive Shield Phase" below.

**Fixed — archetype bucketing in `evaluate_policy()` no longer re-derives the
label from thresholds.** It used to guess the archetype from the (jittered)
sampled stats via an if/elif chain, which had several undocumented collisions
found by adversarial review: `defensive`'s own parry baseline (4.5) always
exceeded the `parry_heavy` threshold (3.5), so every genuinely-defensive
episode was silently counted as `parry_heavy`; `balanced` bled into `ranged`;
`dash_heavy` bled into `aggressive`. Net effect: 3 of the archetypes with a
defined expectation (`parry_heavy`, `ranged`, `aggressive`) had their reported
percentages contaminated by other archetypes' episodes, and `defensive`'s
bucket was almost entirely spillover rather than genuine defensive episodes
(hence its tiny `n=` count, 2-6 out of 200, and inconsistent "top skill" across
runs). Fixed by adding `PlayerStyle.archetype` — the label `random()` actually
sampled, set once at sample time — and having `evaluate_policy()` bucket by
that field directly instead of re-guessing it. `cyberboss_v10_full_fix`'s
validation table predates this fix, so its `parry_heavy`/`ranged`/`aggressive`
rows are directionally trustworthy (the formula margins are large) but not
exactly clean; re-run `evaluate_policy()` (or retrain) to get uncontaminated
numbers, including a first real read on `defensive`.

**Pass gate:** overall avg_eff >= 0.35 across all archetypes (a random policy
scores ~0.15–0.20; 0.35 confirms the policy is actively counter-playing).

`evaluate_policy()` (run automatically after training, or via `--export-only`)
also prints a **per-archetype skill distribution** and flags whether each
archetype's most-picked skill matches the expected counter above. This catches
a case avg_eff alone can miss: the policy scoring decently everywhere while
actually leaning on 1-2 skills instead of truly discriminating player styles.

### Credit-assignment fix: raw action vs. executed (substituted) action

`step()` resolves cooldowns by silently substituting the best-ready skill
whenever the policy's requested action is on cooldown (`raw_action` vs.
`action`). Two related bugs were found and fixed here (see `cyberboss_v9_credit_fix`
and `cyberboss_v10_full_fix` in `checkpoints/`):

1. **Reward was computed from the substituted skill but credited to the raw
   action SB3 actually sampled.** Whenever substitution kicked in — which
   happened to ProjectileBurst ~73% of the time in isolated testing — the
   policy learned a corrupted action→reward association. This was confirmed
   (via an `ml-agents-specialist` diagnostic) to be the reason ProjectileBurst
   never converged above ~0-1% raw-pick rate for any archetype across 6
   consecutive full training runs, despite objectively being the best counter
   for parry_heavy/barrier_passive/defensive by the environment's own reward
   formula. Fix: `step()` now computes a separate `raw_eff` from `raw_action`
   and passes `raw_action`/`raw_eff` into `_compute_reward()` and the windowed
   variety-penalty history (`_recent_skills`), while damage/cooldowns still use
   the actually-executed substituted skill.
2. **`evaluate_policy()`'s `avg_eff` gate had the same dilution problem as a
   metric, not just as a training signal.** Even after fix #1, cooldown
   substitution forces each archetype's *executed* skill mix toward
   near-uniform (~25% each) regardless of how well the policy has learned to
   discriminate, which caps `avg_eff` near each archetype's *average*
   effectiveness across all 4 skills rather than reflecting genuine decision
   quality (confirmed empirically: avg_eff plateaued at ~0.335 whether trained
   for 200k or 2M steps). Fix: `info["raw_effectiveness"]` (computed from
   `raw_action`) is what `evaluate_policy()` now averages into `avg_eff`,
   mirroring fix #1 applied to the metric instead of the reward.

A third, smaller fix was needed alongside these: `barrier_passive`'s barrier-
rate ceiling (`MAX_USES_PER_MINUTE[4]`) was lowered from 4.0 to 2.5, since its
fixed 3.5/min baseline only reached ~0.875 of the old 4.0 ceiling — a soft
signal that left Charge competitive with ProjectileBurst for that archetype.
This in turn required loosening the re-classifier's `barrier_passive` threshold
(now `MAX_USES_PER_MINUTE[4] - 0.3` instead of a hardcoded `2.5`) since the
old hardcoded check silently stopped matching once the ceiling itself became
2.5. `BehaviorTrackerConfig.cs`'s `_maxUsesPerMinutePerSkill` default must stay
in sync with this value (see CLAUDE.md's cross-part contract table).

### Second devil's-advocate round: TeleportStrike scale + archetype bucketing (`cyberboss_v11_da_fixes`)

A follow-up adversarial review of the fixes above found two more issues, both fixed:

1. **`_dominant_counter()`'s TeleportStrike row was still half-wrong** — the
   `bias_deviation` term needs the same `*2.0` normalization
   `_compute_effectiveness` uses (`abs(obs[0]-0.5)` maxes at 0.5, not 1.0), but
   that multiplier was missing here, capping dodge-bias's contribution to the
   discrete counter-bonus at half of what the dense reward actually gives it.
   This was a smaller-scale survival of the exact scale-mismatch bug the
   surrounding comment already describes fixing once. Fixed by adding the
   missing `* 2.0`.
2. **Archetype bucketing in `evaluate_policy()` was contaminated in more places
   than the one documented `defensive`/`parry_heavy` collision** — `balanced`
   episodes were bleeding into `ranged`, and `dash_heavy` episodes into
   `aggressive`, via if/elif threshold collisions in the old re-classifier.
   Fixed at the root: `PlayerStyle` now carries an `archetype: str` field set
   directly by `random()`, and `evaluate_policy()` buckets by that instead of
   re-deriving a guess from the (jittered) sampled stats. This is a diagnostic-
   only fix — training never saw archetype labels either way — but it means
   `cyberboss_v10_full_fix`'s validation table (and all runs before it) has
   some cross-contamination in its `aggressive`/`ranged`/`parry_heavy` rows;
   `cyberboss_v11_da_fixes` is the first run with clean bucketing, including a
   `defensive` bucket with a real sample count for the first time.

Retraining with both fixes (`cyberboss_v11_da_fixes`) surfaced a result worth
noting rather than chasing: with TeleportStrike's dodge-bias term now correctly
weighted, `ranged` shifted from a clean Charge preference to a genuine split
between Charge and TeleportStrike (52%/42%). This isn't a discrimination
failure — dodge_bias is sampled independently of archetype, and CLAUDE.md
already documents TeleportStrike and Charge as *deliberately* overlapping
counters for distance-keeping play, for skill-variety reasons. Confirmed with
the user and reflected in `EXPECTED_DOMINANT_SKILL["ranged"] = (0, 3)` (both
count as a pass) rather than re-tuning weights to force a single answer.
`barrier_passive` also came out closer than before (Charge 34% vs.
ProjectileBurst 33%) — that one was **not** confirmed as intentional overlap
(CLAUDE.md documents ProjectileBurst as barrier_passive's specific counter,
with no Charge overlap called out), so it's logged as a known, open weakness
rather than re-gated. `avg_eff = 0.576` for this run, comfortably clearing the
0.35 gate.

### Ranking-based substitution (`cyberboss_v12_ranked_substitution` or later)

Cooldown substitution previously ranked candidates by remaining cooldown time
only — whichever skill recharges soonest fires, with zero regard for how well
it actually counters the current player. Both sides now rank substitutes by
genuine counter-play quality instead:

- **Python:** `_best_ready_skill(obs)` ranks all currently-ready skills by
  `_compute_effectiveness(i, obs)` (the same formula reward is built on) and
  returns the highest-scoring one. Falls back to
  `argmin(remaining_seconds)` only if no skill is ready at all (rare), so a
  skill still always fires every step.
- **Unity:** `BossRLAgent.ApplyCooldownSubstitution(policyChoice, obs)` does
  the identical ranking via `BossCounterEffectiveness.Compute` — a hand-ported
  C# copy of `_compute_effectiveness()`. Both are pure functions of
  `(skill_index, obs)` with no RNG or hidden state, so this is an exact port,
  not an approximation — keep the two in sync if the formula ever changes.

**One deliberate, accepted divergence:** Python's `_compute_effectiveness`
dampens the frequency-based terms (obs[1]-[5]) for the first `WARMUP_STEPS` of
a training episode via `warmup_ramp`, to counteract early-episode statistical
noise. `BossCounterEffectiveness.Compute` does **not** replicate this — there's
no clean Unity-side analog for "steps into a training episode," and this
formula is only used for the substitution fallback path (not primary reward
shaping), so a small early-fight divergence here was accepted rather than
built out. If this ever needs to change, `BossRLAgent` would need to track
elapsed-fight-time and compute a matching ramp.

Retrain after this change to confirm nothing regressed in the discrimination
table — substitution scenarios are common enough (cooldowns 3.5-4.5x the
decision interval) that changing what fires during them can shift the
executed-skill distribution meaningfully, even though the raw-preference
training signal itself (`raw_action`/`raw_eff`) is untouched by this change.

#### Follow-up: argmax ranking collapsed real-fight variety (`cyberboss_v12_ranked_substitution` -> softmax)

`cyberboss_v12_ranked_substitution` (strict argmax ranking, as described above)
trained fine and passed validation (`avg_eff = 0.587`), but in-game playtesting
found it noticeably *less* varied than the old timer-based substitution: the
boss would settle onto 2, occasionally 3, of its 4 skills for an entire fight
and essentially never show the 4th unless the player's playstyle actually
changed.

The cause: real player behavior stats are slow-moving running averages that
barely shift once a fight settles into a rhythm, so the observation vector is
roughly static for most of a fight. A strict argmax over a static observation
is deterministic — the "best ready substitute" for a given player is always
the *same* skill, every time the raw pick is on cooldown. So the boss locks
onto raw-top-pick + one fixed runner-up, and the other two skills only fire if
both of those happen to be on cooldown simultaneously. The old timer-based
substitution didn't have this problem, ironically *because* it wasn't
observation-driven — it rotated through skills based on cooldown/usage
history, which naturally cycles through all 4 over time regardless of how
static the player's stats are. Aggregate validation stats never caught this
because they average across 200 *different* randomly-sampled players per
run — the per-player concentration this caused is invisible in an average
across many different players.

Fix: both `_best_ready_skill(obs)` (Python) and
`BossRLAgent.ApplyCooldownSubstitution(policyChoice, obs)` (Unity) now
**sample** among ready skills with probability proportional to
`exp(effectiveness / SUBSTITUTION_SOFTMAX_TEMPERATURE)` — a softmax — instead
of always taking the single highest-scoring one. `SUBSTITUTION_SOFTMAX_TEMPERATURE`
(Python) / `SubstitutionSoftmaxTemperature` (C#) = `0.2` on both sides, tuned so
the best-scoring ready skill still wins roughly 55-65% of the time for a
typical 2-3 point effectiveness gap, while leaving real, non-negligible odds
for the others. Better counters stay more likely to be picked; they're just no
longer a guarantee, so a steady playstyle still eventually sees all 4 skills
across a fight instead of getting stuck on 2.

### Sentis/ONNX export parity check

```
.\venv\Scripts\python.exe cyberboss_env.py --check-parity --run-id cyberboss_v1
```

Loads the SB3 checkpoint and the exported `.onnx`, runs both on 8 fixed
vectors (one per archetype extreme) plus 50 random ones, and confirms they
pick the same skill. This only validates PyTorch -> ONNX export — it can't
reach Sentis (Unity/C#-only). After it passes, enter Play mode in Unity,
right-click `SentisInferenceManager` in the Inspector, choose **Run Parity
Check**, and diff its printed skill names against this script's output for
the same 8 named vectors (`neutral`, `pure_left_dash`, `pure_right_dash`,
`aggressive_max`, `ranged_max`, `barrier_passive_max`, `parry_heavy_max`,
`dash_heavy_max`). A mismatch means Sentis is interpreting the ONNX graph
differently from onnxruntime/PyTorch — do not ship that export.

Survival rate is logged but is **not** the gate. The aggressive archetype deals
~530 total damage over 30 steps even with perfect counter-play (cooldown forces
24 non-AoESlam steps at near-full damage), making BOSS_MAX_HP=200 structurally
unsurvivable regardless of policy quality. avg_eff correctly measures
counter-play without depending on the HP budget.

If the gate fails (avg_eff < 0.35):
- All archetypes near 0.15–0.20: reward signal is too weak — increase
  `REWARD_EFFECTIVENESS_SCALE` or `REWARD_COUNTER_BONUS` (currently 1.5)
  in `cyberboss_env.py`.
- One archetype high, others near 0: policy collapsed onto one skill —
  increase `REWARD_VARIETY_PENALTY_PER_REPEAT` (currently 0.05 — lowered from an
  earlier 0.3 after that value caused the exact all-skills-equal-split exploit
  described below) in `cyberboss_env.py`.
- One specific skill index stuck near 0% raw-pick rate for every archetype,
  including archetypes where it's the objectively best counter: check whether
  cooldown substitution is diluting its credit assignment — see "Credit-
  assignment fix" below before assuming this is a reward-weight problem.
- **Every archetype shows an ~equal split across all 4 skills** (e.g. ~25%
  each, regardless of player style): the policy found the windowed-penalty
  exploit — a strict rotation across all N actions structurally evades any
  window smaller than N, at any window size. This is what happened in the
  first post-#5 retrain at `REWARD_VARIETY_PENALTY_PER_REPEAT=0.3`. Decrease
  the constant (it's a nudge against long single-skill domination, not a hard
  anti-repeat rule) rather than increasing the window size, which doesn't
  fix the underlying exploit.

## Reactive Shield Phase (no longer RL-selectable)

ShieldPhase used to be action 3, picked whenever parry frequency (`obs[2]`)
was high — but that produced a real stalemate: a player parrying without
ever attacking would lock the boss into repeatedly picking a skill that
deals zero damage, forever. It's been removed from the RL action space
entirely (`N_BOSS_SKILLS` is now 4) and replaced with a reactive Unity-side
mechanic:

- `BossShieldReactiveTrigger` (`Assets/Scripts/Boss/BossShieldReactiveTrigger.cs`)
  listens for the player's attack events (normal attack, Burst Strike, Ranged
  Blast — not Dash/Parry/Barrier, since those aren't damage output).
- On each qualifying attack, it rolls `P = clamp01(w1 * rangedBlastFreq + w2 *
  aggressionScore)` (weights in `BossShieldReactiveTriggerConfig`, defaults
  0.6/0.4) and triggers Shield if the roll succeeds and it's off cooldown.
- This training environment has **no representation of Shield at all** — it's
  a deterministic reactive system orthogonal to what the 4-skill policy learns,
  the same way UI/VFX aren't modeled here.

## Windowed variety penalty

Player behavior stats barely move within a single fight, so whichever skill
scored highest at minute 1 usually still scores highest at minute 3 — the old
flat "-0.1 if same as the immediately-previous pick" penalty was far too weak
to compete with up to ~3.5/step for matching the "correct" counter, so the
policy would lock onto 1-2 skills for an entire episode. `_compute_reward` now
checks the trailing `VARIETY_WINDOW_SIZE` (3) previous picks and subtracts
`REWARD_VARIETY_PENALTY_PER_REPEAT` (0.3) for each one that matches the current
pick — escalating from 0 to 0.9 the more a skill has recently dominated.

## AggressionScore now includes normal-attack rate

`obs[6]` used to be pure proximity (`timeInCloseRange / elapsedTime`). It now
blends in normal-attack rate (the player's basic melee attack — distinct from
the 5 tracked skills): `0.6 * proximityFraction + 0.4 * attackRateNormalized`,
where `attackRateNormalized = (attacks/min) / MAX_NORMAL_ATTACKS_PER_MINUTE`
(ceiling 30/min — higher than the 5 tracked skills since no `SkillCooldown`
gates the normal attack). Standing close without ever swinging no longer
reads as full aggression. Each archetype's `normal_attack_rate` range in
`PlayerStyle.random()`: aggressive 20–30, dash_heavy 10–16, balanced 8–16,
defensive 8–14, parry_heavy 6–12, ranged 2–5, barrier_passive 0–4.

## ONNX model contract (Part 9 must know this)

| Property         | Value                        |
|------------------|------------------------------|
| Input tensor     | `obs_0`                      |
| Input shape      | `(batch, 9)` — float32       |
| Output tensor    | `discrete_actions`           |
| Output shape     | `(batch, 4)` — float32 logits|
| Inference        | `argmax(discrete_actions[0])`|
| ONNX opset       | 11                           |

The output is raw logits (pre-softmax). `SentisInferenceManager` calls
`argmax` on the output to get the skill index 0–3
(0=Charge, 1=ProjectileBurst, 2=AoESlam, 3=TeleportStrike — ShieldPhase is
not in this list, see "Reactive Shield Phase" above).

Observation index → BehaviorStatVector field (LOCKED):

```
[0] DodgeDirectionBias
[1] SkillUsageFrequency0  (Dash)
[2] SkillUsageFrequency1  (Parry)
[3] SkillUsageFrequency2  (RangedBlast)
[4] SkillUsageFrequency3  (BurstStrike)
[5] SkillUsageFrequency4  (Barrier)
[6] AggressionScore
[7] AverageEngagementRange
[8] PositionalBias
```

## Checkpoints

Saved to `Assets/ML/Training/checkpoints/<run-id>/`.
The final model is `BossBrain_final.zip` (SB3 format).

## File layout

```
Assets/
  ML/
    Training/
      cyberboss_config.yaml   ML-Agents-format hyperparameter reference
      cyberboss_env.py        Gym environment + training + ONNX export
      README.md               This file
    Models/
      BossBrain.onnx          Exported model — drag into Unity Inspector
  Scripts/
    ML/
      BehaviorStatVector.cs   Shared observation struct (DO NOT modify)
      BossRLAgent.cs          Unity component — skill selection + obs collection
      SentisInferenceManager.cs  ONNX inference (4-output argmax)
    Boss/
      BossShieldReactiveTrigger.cs        Reactive Shield trigger (not RL-driven)
      Configs/
        BossShieldReactiveTriggerConfig.cs  Trigger probability weights
```

## Hyperparameters (cyberboss_config.yaml)

Matches the SB3 training run:

| Parameter          | Value   | Notes                                                    |
|--------------------|---------|----------------------------------------------------------|
| trainer_type       | ppo     |                                                          |
| learning_rate      | 3e-4    |                                                          |
| batch_size         | 512     |                                                          |
| buffer_size        | 2048    | n_steps=256 × n_envs=8                                  |
| beta (ent_coef)    | 5e-3    | Keeps exploration alive early in training                |
| epsilon (clip)     | 0.2     |                                                          |
| lambd              | 0.95    | GAE lambda                                               |
| num_epoch          | 10      |                                                          |
| gamma              | 0.99    |                                                          |
| hidden_units       | 128     | Two layers — keeps model small for Sentis                |
| num_layers         | 2       |                                                          |
| normalize obs      | false   | Obs already in [0,1]; SB3 MlpPolicy does NOT normalise  |
| max_steps          | 2000000 |                                                          |
| time_horizon       | 256     | n_steps value                                            |

## Part 9 wiring checklist

After copying `BossBrain.onnx` into `Assets/ML/Models/`:

1. Add `SentisInferenceManager` MonoBehaviour to the boss GameObject.
2. Drag `BossBrain.onnx` into `SentisInferenceManager._onnxModel` field.
3. In `BossRLAgent`, set `_useRLPolicy = true` and wire `_sentisManager`.
4. `BossController.SkillSelectionLoop` already indexes into
   `_bossSkills.SelectableSkills` (not `AllSkills`) — the 4-skill subset that
   matches the ONNX action space (ShieldPhase excluded). No change needed here.
5. Add `BossShieldReactiveTrigger` to the boss GameObject alongside `BossSkills`,
   and assign a `BossShieldReactiveTriggerConfig` asset — this is what makes
   ShieldPhase fire at all now that it's excluded from turn-based selection.
6. **Critical:** Call `_bossRLAgent.OnEpisodeBegin()` (which calls `PlayerBehaviorTracker.ResetStats()`)
   at the start of every new fight — in `GameOverScreen.Respawn()` or wherever the game loop
   resets. Without this, fight 2 begins with fight 1's behavior stats still accumulated:
   the boss will immediately pick skills based on the *previous* fight's player archetype.
   Already wired as of this writing (`GameOverScreen.Restart()` -> `BossController.NotifyFightStart()`
   -> `BossRLAgent.OnEpisodeBegin()`), confirmed by devil's-advocate review — currently redundant
   with the full scene reload `Restart()` also does, but is the only safety net if a future
   "rematch without reload" feature removes that reload.
7. `BossRLAgent.ApplyCooldownSubstitution()` ranks cooldown-substitute skills by
   counter-play effectiveness (`BossCounterEffectiveness.Compute`), not by
   remaining cooldown time — see "Ranking-based substitution" below. It falls
   back to `SelectableSkillCooldownDurations`-based argmin(remaining seconds)
   only in the rare case no skill is currently ready at all.
8. If `_useRLPolicy` is true but `_sentisManager` isn't wired, `BossRLAgent`
   silently falls back to the Part 1-8 random picker with **no visible
   difference in behavior** — the random picker draws from the same 4-skill
   pool, so a boss "adapting" could just be this failure mode. `BossRLAgent.Start()`
   now logs an explicit error in this case; check the Console for it before
   trusting any playtest as evidence the RL policy is actually running.
9. Verify in a WebGL build: inference must use CPU backend (not GPU).
   - Sentis 2.x (package com.unity.sentis ≥ 2.0): `new Worker(model, BackendType.CPU)`
   - Sentis 1.x (package com.unity.sentis 1.x): `WorkerFactory.CreateWorker(BackendType.CPU, model)`
   Check `Packages/manifest.json` for `com.unity.sentis` version to confirm which API to use.
