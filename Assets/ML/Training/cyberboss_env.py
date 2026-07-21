"""
CyberBoss headless Python training environment.

Simulates the CyberBoss arena combat loop without Unity. The boss (RL agent)
observes a 9-float BehaviorStatVector and selects one of 4 skills per step.
Player behaviour is generated stochastically from randomised style profiles so
the policy sees diverse input distributions across episodes.

Run training (entry point):
    python cyberboss_env.py --steps 2000000 --run-id cyberboss_v1

Export ONNX after training:
    python cyberboss_env.py --export-only --run-id cyberboss_v1

Observation layout (index → meaning) — LOCKED, matches BehaviorStatVector.cs:
    [0] DodgeDirectionBias       0=pure left, 0.5=neutral, 1=pure right
    [1] SkillUsageFrequency0     Dash uses/min ÷ 10, clamped 0–1
    [2] SkillUsageFrequency1     Parry uses/min ÷ 6, clamped 0–1
    [3] SkillUsageFrequency2     RangedBlast uses/min ÷ 8, clamped 0–1
    [4] SkillUsageFrequency3     BurstStrike uses/min ÷ 6, clamped 0–1
    [5] SkillUsageFrequency4     Barrier uses/min ÷ 4, clamped 0–1
    [6] AggressionScore          0.6×(timeInCloseRange/elapsedTime)
                                 + 0.4×(normalAttacksPerMin/MAX_NORMAL_ATTACKS_PER_MINUTE), 0–1
    [7] AverageEngagementRange   meanDistToBoss / maxArenaRange, 0–1
    [8] PositionalBias           1 − (meanDistFromCenter / arenaRadius), 0–1

Action mapping (4 actions — ShieldPhase removed, see below):
    0 = Charge          counters ranged play + burst-strike spam
    1 = ProjectileBurst counters barrier/passive turtling + parry-spam (a sudden
                        multi-projectile burst is much harder to react-parry than
                        a single telegraphed attack)
    2 = AoESlam         counters close-range aggression
    3 = TeleportStrike  counters dash-heavy play + general distance-keeping

ShieldPhase is intentionally NOT part of the RL action space. It used to be
action 3 (keyed on parry frequency), but that produced a stalemate in real
play: a player parrying without ever attacking would lock the boss into
picking a skill that deals zero damage, indefinitely. It is now a reactive
mechanic in Unity (BossShieldReactiveTrigger) that fires on the player's
attack events (normal attack, Burst Strike, Ranged Blast) with a probability
based on Ranged Blast frequency and AggressionScore — entirely independent of
this training environment, since it isn't a "turn" the policy takes.

Dependencies:
    pip install gymnasium stable-baselines3 torch onnx numpy
"""

from __future__ import annotations

import argparse
import os
import random
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
import gymnasium as gym
from gymnasium import spaces

# ---------------------------------------------------------------------------
# Constants — mirror Unity config defaults so the sim stays in sync
# ---------------------------------------------------------------------------

# Seconds of simulated game time per environment step.
# Set to BossConfig.SkillInterval (3 s) rounded down for tighter training loops.
STEP_DURATION: float = 2.0

# Fixed episode length: 30 steps × 2 s = 60 simulated seconds.
# Player can't die (2000 HP), so episodes always run to step 30 unless boss dies.
MAX_EPISODE_STEPS: int = 30

# Steps below this threshold have unreliable skill-frequency observations because
# the elapsed-time denominator is too small (uses/min spikes to 1.0 early).
# 5 steps × 2 s/step = 10 simulated seconds warm-up (spec requirement).
WARMUP_STEPS: int = 5

BOSS_MAX_HP: float = 200.0   # Reduced: random play kills boss in ~12 steps vs aggressive — forces counter-play
PLAYER_MAX_HP: float = 2000.0 # Effectively immortal — player can't die in 30 steps; focus is boss survival

# Per-skill uses/min ceilings — BehaviorTrackerConfig.MaxUsesPerMinutePerSkill
# Index: 0=Dash, 1=Parry, 2=RangedBlast, 3=BurstStrike, 4=Barrier
# Barrier lowered from 4.0 to 2.5: barrier_passive's baseline barrier rate is a
# fixed 3.5/min (see PlayerStyle.random()), which only reached ~0.875 of a 4.0
# ceiling — a soft signal that let Charge stay competitive with ProjectileBurst
# for barrier_passive (near 50/50 in training). At 2.5, the 3.5 baseline
# reliably clips to 1.0 even after -1 sigma jitter, matching how parry_heavy's
# baseline already reliably saturates its own (unchanged) 6.0 parry ceiling.
MAX_USES_PER_MINUTE = np.array([10.0, 6.0, 8.0, 6.0, 2.5], dtype=np.float32)

# Normalization ceiling for normal-attack rate, blended into AggressionScore.
# BehaviorTrackerConfig.MaxNormalAttacksPerMinute — higher than the 5 tracked
# skills' ceilings since no SkillCooldown gates the normal attack.
MAX_NORMAL_ATTACKS_PER_MINUTE: float = 30.0

CLOSE_RANGE_THRESHOLD: float = 4.0   # BehaviorTrackerConfig.CloseRangeThreshold
MAX_ARENA_RANGE: float = 20.0        # BehaviorTrackerConfig.MaxArenaRange
ARENA_RADIUS: float = 10.0           # BehaviorTrackerConfig.ArenaRadius
SAMPLE_INTERVAL: float = 0.1         # BehaviorTrackerConfig.SampleInterval
DASH_LATERAL_THRESHOLD: float = 0.3  # BehaviorTrackerConfig.DashLateralThreshold
# ParrySkillConfig.ParryWindowDuration — used to compute P(boss attack intercepted by parry)
PARRY_WINDOW_SEC: float = 0.4

N_BOSS_SKILLS: int = 4
N_OBS: int = 9

# ShieldPhase removed — see module docstring. Index 3 is now TeleportStrike
# (was 4 before the rework).
SKILL_NAMES = ("Charge", "ProjectileBurst", "AoESlam", "TeleportStrike")

# Expected dominant skill(s) per archetype — from README.md's counter-play table.
# Used by evaluate_policy() to flag whether the policy actually discriminates
# between archetypes, rather than just scoring well on the effectiveness heuristic.
EXPECTED_DOMINANT_SKILL: dict[str, tuple[int, ...]] = {
    "aggressive":      (2,),     # AoESlam — driven by obs[6], not obs[7] (Charge's driver)
    # Charge and TeleportStrike both legitimately counter ranged/distance-keeping
    # play by design (CLAUDE.md: "TeleportStrike... deliberately overlapping with
    # Charge's engagement-range driver... more than one valid counter per tendency
    # increases skill variety instead of strict 1:1"). Confirmed post-fix (once the
    # TeleportStrike dodge-bias scale bug was corrected and archetype bucketing was
    # de-contaminated): the trained policy splits between the two depending on
    # dodge_bias, which is sampled independently of archetype — that's the overlap
    # working as intended, not a discrimination failure.
    "ranged":          (0, 3),   # Charge or TeleportStrike
    # ShieldPhase's removal leaves "defensive" without a single confident
    # expectation — it's genuinely contested between ProjectileBurst (moderate
    # parry+barrier) and TeleportStrike (moderate dash+range) depending on jitter.
    "defensive":       (),
    "dash_heavy":      (3,),     # TeleportStrike (reindexed — was 4)
    "parry_heavy":     (1,),     # ProjectileBurst — now the sole parry-spam counter
    "barrier_passive": (1,),     # ProjectileBurst
    "balanced":        (),       # no dominant skill expected — generalisation check only
}

# Fixed observation vectors used to cross-check the exported ONNX model against
# the source SB3 policy (check_onnx_parity) and, separately, against Sentis
# inference in the Unity Editor (SentisInferenceManager.RunParityCheck).
# Field order: [dodge, dashFreq, parryFreq, rangedFreq, burstFreq, barrierFreq,
#               aggression, engagementRange, positionalBias]
# Keep this list identical to the C# _parityTestVectors array — see
# Assets/Scripts/ML/SentisInferenceManager.cs.
PARITY_TEST_VECTOR_NAMES = (
    "neutral", "pure_left_dash", "pure_right_dash", "aggressive_max",
    "ranged_max", "barrier_passive_max", "parry_heavy_max", "dash_heavy_max",
)
PARITY_TEST_VECTORS = (
    np.array([0.5, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.5, 0.5], dtype=np.float32),
    np.array([0.0, 0.9, 0.0, 0.0, 0.0, 0.0, 0.1, 0.3, 0.5], dtype=np.float32),
    np.array([1.0, 0.9, 0.0, 0.0, 0.0, 0.0, 0.1, 0.3, 0.5], dtype=np.float32),
    np.array([0.5, 0.1, 0.05, 0.1, 0.9, 0.05, 0.9, 0.1, 0.7], dtype=np.float32),
    np.array([0.5, 0.1, 0.05, 0.9, 0.05, 0.05, 0.05, 0.9, 0.4], dtype=np.float32),
    np.array([0.5, 0.05, 0.05, 0.2, 0.05, 0.9, 0.05, 0.4, 0.9], dtype=np.float32),
    np.array([0.5, 0.2, 0.9, 0.2, 0.1, 0.1, 0.3, 0.4, 0.6], dtype=np.float32),
    np.array([0.9, 0.9, 0.1, 0.2, 0.2, 0.05, 0.4, 0.4, 0.5], dtype=np.float32),
)

# Skill cooldowns in seconds — BossSkill*Config defaults
# Index: 0=Charge, 1=ProjectileBurst, 2=AoESlam, 3=TeleportStrike
SKILL_COOLDOWNS_SEC = np.array([7.0, 8.0, 9.0, 8.0], dtype=np.float32)

# Base damage boss deals to player per skill (from BossSkill*Config defaults).
# ProjectileBurst: 12 dmg × 3 projectiles × ~40% hit rate ≈ 15 average.
# (ShieldPhase's 0-damage entry removed along with the skill itself — all 4
# remaining skills deal nonzero damage, so step() no longer needs a
# zero-damage special case.)
SKILL_BASE_DAMAGE = np.array([20.0, 15.0, 25.0, 22.0], dtype=np.float32)

# Reward redesign: counter-play is the ONLY signal.
# Damage rewards are disabled — player HP is a dummy, boss survival drives behaviour.
# Aggressive archetypes can kill the boss in ~26 steps without counter-play,
# so the policy must learn to read observations and pick counters to survive.
REWARD_DAMAGE_DEALT_SCALE: float = 0.0   # disabled — player is effectively immortal
REWARD_DAMAGE_TAKEN_SCALE: float = 0.0   # disabled
REWARD_COUNTER_BONUS: float = 1.5        # per-step bonus: skill matches dominant vulnerability
REWARD_EFFECTIVENESS_SCALE: float = 2.0  # dense per-step signal: eff × scale (primary driver)

# Windowed variety penalty: penalizes repeating a skill that also appears among
# the trailing VARIETY_WINDOW_SIZE previous picks, scaling with how many times
# it repeats. Replaces the old single-previous-skill check (0.1 flat penalty),
# which was far too weak to compete with up to ~3.5/step from matching the
# "correct" counter — since player behaviour stats barely move within a fight,
# the old penalty let the policy lock onto 1-2 skills for an entire episode.
#
# IMPORTANT — structural gotcha discovered via training (v5_shieldrework run):
# a windowed repeat penalty mathematically rewards maximal rotation over
# commitment, regardless of window size: with N actions, spreading picks across
# all N always minimizes the repeat count more than committing to 1 does (a
# strict period-4 rotation across 4 actions with window<4 evades this penalty
# 100% of the time, at ANY window size below N_BOSS_SKILLS). At 0.3/repeat this
# fully dominated the ~1-2 point effectiveness/counter-bonus gap between the
# correct skill and the others (a gap this rework also shrunk, by adding
# cross-skill overlap per the #3/#6 design changes) — the trained policy
# converged to an exact ~25/25/25/25 rotation across all 4 skills for every
# archetype, ignoring the observation entirely. The penalty can't be eliminated
# structurally without redesigning the mechanism, so it's tuned low enough
# (0.05/repeat, max 0.15/step at this window size) that it nudges against
# multi-episode single-skill domination without being able to outweigh a
# genuine multi-point correctness gap.
REWARD_VARIETY_PENALTY_PER_REPEAT: float = 0.05
VARIETY_WINDOW_SIZE: int = 3             # compare current pick against this many previous picks

# Softmax temperature for cooldown-substitution sampling (_best_ready_skill).
# A strict argmax over ready skills' effectiveness made substitution fully
# deterministic for any player whose behaviour stats are roughly stable within
# a fight (most real fights, since these stats are slow-moving running
# averages) — the boss would lock onto its raw top pick plus one fixed
# "runner-up" substitute for the whole fight, with the 3rd/4th skill only
# firing if both of those happened to be on cooldown simultaneously. Sampling
# by softmax keeps better counters more likely without making them a lock, so
# substitution still varies across a fight even against a completely steady
# playstyle. Lower = sharper (closer to argmax); higher = closer to uniform
# random. 0.2 keeps the best-scoring ready skill winning roughly 55-65% of the
# time in typical 2-3 point effectiveness gaps, per hand-computed examples in
# README.md's "Ranking-based substitution" section.
# Must match SubstitutionSoftmaxTemperature in BossRLAgent.cs exactly.
SUBSTITUTION_SOFTMAX_TEMPERATURE: float = 0.2

REWARD_ALIVE_BONUS: float = 0.0
REWARD_WIN: float = 0.0                  # player can't die — unused
REWARD_LOSS: float = -2.0               # boss dying is penalized; creates survival pressure

# Default output path for the exported ONNX model
ONNX_OUTPUT_PATH: str = str(
    Path(__file__).parent.parent / "Models" / "BossBrain.onnx"
)

# SB3 checkpoint directory
CHECKPOINT_DIR: str = str(Path(__file__).parent / "checkpoints")

# Fixed RNG seed used for all reproducible evaluations (checkpoint sweeps, held-out evals).
EVAL_SEED: int = 42


# ---------------------------------------------------------------------------
# Player style — stochastic player profile randomised each episode
# ---------------------------------------------------------------------------

# Seven named archetypes cover the training distribution.
# The policy must learn to counter all of them.
_ARCHETYPES = (
    "aggressive",       # high aggression, burst-strike heavy, close range
    "ranged",           # low aggression, ranged-blast heavy, long range
    "defensive",        # parry-heavy, dash-heavy, mid-range
    "dash_heavy",       # high dash rate, mixed range
    "parry_heavy",      # high parry rate, mid range
    "barrier_passive",  # high barrier rate, stays centre, low aggression
    "balanced",         # no dominant tendency — tests generalisation
)


@dataclass
class PlayerStyle:
    """
    Fixed-per-episode behaviour profile for the simulated player.

    All rate values are in uses/minute (not normalised). The environment
    converts them to normalised frequencies via MAX_USES_PER_MINUTE before
    building the observation vector.
    """
    aggression: float           # fraction of step time spent in close range [0, 1]
    skill_rates: np.ndarray     # uses/min per player skill, shape (5,)
    dodge_bias: float           # 0 = always dashes left, 0.5 = neutral, 1 = always right
    mean_dist_normalized: float # AverageEngagementRange target [0, 1]
    mean_pos_bias: float        # PositionalBias target [0, 1]
    normal_attack_rate: float   # uses/min, blended into AggressionScore
    archetype: str = ""         # the label random() actually sampled — evaluate_policy()
                                 # buckets by this directly instead of re-deriving it from
                                 # thresholds on the (jittered) sampled stats. The old
                                 # threshold-based re-classifier had several undocumented
                                 # collisions (e.g. defensive's parry baseline always
                                 # exceeded the parry_heavy threshold, so real defensive
                                 # episodes were silently bucketed as parry_heavy; balanced
                                 # bled into ranged; dash_heavy bled into aggressive) that
                                 # contaminated the per-archetype validation table without
                                 # affecting training (the network never sees this field).

    @staticmethod
    def random(rng: np.random.Generator) -> "PlayerStyle":
        """Sample a random player archetype with per-field jitter."""
        archetype = rng.choice(_ARCHETYPES)

        if archetype == "aggressive":
            aggression = rng.uniform(0.6, 0.95)
            rates = [3.0, 0.5, 1.0, 5.0, 0.5]
            mean_dist = rng.uniform(0.05, 0.25)
            pos_bias = rng.uniform(0.4, 0.8)
            normal_attack_rate = rng.uniform(20.0, 30.0)

        elif archetype == "ranged":
            aggression = rng.uniform(0.0, 0.2)
            rates = [3.5, 0.5, 6.0, 0.5, 0.5]
            mean_dist = rng.uniform(0.5, 0.9)
            pos_bias = rng.uniform(0.3, 0.6)
            normal_attack_rate = rng.uniform(2.0, 5.0)

        elif archetype == "defensive":
            aggression = rng.uniform(0.1, 0.35)
            rates = [5.0, 4.5, 2.5, 1.0, 2.0]
            mean_dist = rng.uniform(0.3, 0.55)
            pos_bias = rng.uniform(0.5, 0.85)
            normal_attack_rate = rng.uniform(8.0, 14.0)

        elif archetype == "dash_heavy":
            aggression = rng.uniform(0.3, 0.65)
            rates = [8.0, 1.0, 2.0, 2.5, 0.5]
            mean_dist = rng.uniform(0.2, 0.6)
            pos_bias = rng.uniform(0.3, 0.7)
            normal_attack_rate = rng.uniform(10.0, 16.0)

        elif archetype == "parry_heavy":
            aggression = rng.uniform(0.2, 0.5)
            rates = [2.0, 5.0, 2.0, 2.0, 1.0]
            mean_dist = rng.uniform(0.2, 0.5)
            pos_bias = rng.uniform(0.4, 0.7)
            normal_attack_rate = rng.uniform(6.0, 12.0)

        elif archetype == "barrier_passive":
            aggression = rng.uniform(0.0, 0.15)
            rates = [1.0, 0.5, 2.5, 0.5, 3.5]
            mean_dist = rng.uniform(0.3, 0.6)
            pos_bias = rng.uniform(0.65, 0.95)  # huddles centre under barrier
            normal_attack_rate = rng.uniform(0.0, 4.0)

        else:  # balanced
            aggression = rng.uniform(0.2, 0.6)
            rates = [3.0, 2.0, 3.0, 2.0, 1.5]
            mean_dist = rng.uniform(0.25, 0.65)
            pos_bias = rng.uniform(0.3, 0.7)
            normal_attack_rate = rng.uniform(8.0, 16.0)

        # Clip each rate to its ceiling, then apply independent Gaussian jitter.
        noisy_rates = np.array(rates, dtype=np.float32)
        noisy_rates += rng.normal(0.0, 0.4, size=5).astype(np.float32)
        noisy_rates = np.clip(noisy_rates, 0.0, MAX_USES_PER_MINUTE)

        jitter = float(rng.normal(0.0, 0.04))
        dodge_bias = float(np.clip(rng.uniform(0.05, 0.95) + jitter, 0.0, 1.0))

        noisy_normal_attack_rate = float(np.clip(
            normal_attack_rate + rng.normal(0.0, 1.5),
            0.0, MAX_NORMAL_ATTACKS_PER_MINUTE,
        ))

        return PlayerStyle(
            aggression=float(np.clip(aggression + rng.normal(0.0, 0.04), 0.0, 1.0)),
            skill_rates=noisy_rates,
            dodge_bias=dodge_bias,
            mean_dist_normalized=float(
                np.clip(mean_dist + rng.normal(0.0, 0.04), 0.01, 0.99)
            ),
            mean_pos_bias=float(
                np.clip(pos_bias + rng.normal(0.0, 0.04), 0.01, 0.99)
            ),
            normal_attack_rate=noisy_normal_attack_rate,
            archetype=str(archetype),  # rng.choice returns numpy.str_; normalize to plain str
        )


# ---------------------------------------------------------------------------
# CyberBoss gymnasium environment
# ---------------------------------------------------------------------------


class CyberBossEnv(gym.Env):
    """
    Headless combat simulation for CyberBoss PPO training.

    Each episode:
        - A random PlayerStyle is drawn. This represents the player's true
          behaviour throughout the fight.
        - Accumulators match PlayerBehaviorTracker.cs exactly (elapsed_time,
          skill_use_counts, etc.) so the observation vector mirrors the Unity
          runtime vector field-for-field.
        - Each step represents STEP_DURATION seconds of simulated combat.
        - The boss (agent) picks one of 4 skills (ShieldPhase excluded — see
          module docstring). If the chosen skill is on cooldown the environment
          substitutes the most-ready alternative — the policy should learn to
          avoid this by respecting cooldowns.
        - Reward drives counter-play: high when the selected skill matches the
          player's dominant vulnerability; penalised for skill spam.

    Observation (9 floats, float32, all in [0, 1]):
        Matches BehaviorStatVector field order — DO NOT reorder.
    """

    metadata = {"render_modes": []}

    def __init__(self) -> None:
        super().__init__()

        self.observation_space = spaces.Box(
            low=0.0, high=1.0, shape=(N_OBS,), dtype=np.float32
        )
        self.action_space = spaces.Discrete(N_BOSS_SKILLS)

        # Episode-level state — initialised in reset()
        self._step_num: int = 0
        self._boss_hp: float = BOSS_MAX_HP
        self._player_hp: float = PLAYER_MAX_HP
        self._skill_cooldowns: np.ndarray = np.zeros(N_BOSS_SKILLS, dtype=np.float32)
        # Trailing window of previous skill picks (most recent last), capped at
        # VARIETY_WINDOW_SIZE — see _compute_reward.
        self._recent_skills: list[int] = []
        self._player_style: PlayerStyle | None = None
        self._obs: np.ndarray = np.full(N_OBS, 0.5, dtype=np.float32)

        # Player accumulators — mirror PlayerBehaviorTracker private fields
        self._elapsed_time: float = 0.0
        self._time_in_close_range: float = 0.0
        self._skill_use_counts: np.ndarray = np.zeros(5, dtype=np.int32)
        self._normal_attack_count: int = 0
        self._range_sample_sum: float = 0.0
        self._range_sample_count: int = 0
        self._pos_sample_sum: float = 0.0
        self._pos_sample_count: int = 0
        self._left_dash_count: int = 0
        self._right_dash_count: int = 0

        # Seeded RNG — reset() re-seeds via gymnasium's seed mechanism
        self._rng: np.random.Generator = np.random.default_rng()

    # ------------------------------------------------------------------
    # gymnasium interface
    # ------------------------------------------------------------------

    def reset(
        self,
        *,
        seed: int | None = None,
        options: dict[str, Any] | None = None,
    ) -> tuple[np.ndarray, dict]:
        super().reset(seed=seed)
        if seed is not None:
            self._rng = np.random.default_rng(seed)

        self._step_num = 0
        self._boss_hp = BOSS_MAX_HP
        self._player_hp = PLAYER_MAX_HP
        self._skill_cooldowns[:] = 0.0
        self._recent_skills = []
        self._player_style = PlayerStyle.random(self._rng)

        # Reset accumulators — mirrors PlayerBehaviorTracker.ResetStats()
        self._elapsed_time = 0.0
        self._time_in_close_range = 0.0
        self._skill_use_counts[:] = 0
        self._normal_attack_count = 0
        self._range_sample_sum = 0.0
        self._range_sample_count = 0
        self._pos_sample_sum = 0.0
        self._pos_sample_count = 0
        self._left_dash_count = 0
        self._right_dash_count = 0

        # Default observation matches PlayerBehaviorTracker defaults:
        # DodgeDirectionBias=0.5 (neutral), frequencies=0, range=0.5, pos=0.5
        self._obs = np.array(
            [0.5, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.5, 0.5], dtype=np.float32
        )
        return self._obs.copy(), {}

    def step(
        self, action: int
    ) -> tuple[np.ndarray, float, bool, bool, dict]:
        assert self.action_space.contains(action), f"Invalid action: {action}"

        self._step_num += 1

        # 1. Simulate player behaviour for this step's time window.
        self._simulate_player_step()

        # 2. Compute the current BehaviorStatVector observation.
        obs = self._compute_obs()
        self._obs = obs

        # 3. Resolve cooldown: if chosen skill is on cooldown, substitute the
        #    ready skill that's the best genuine counter for this observation.
        raw_action = action  # policy's actual choice, before substitution
        if self._skill_cooldowns[action] > 0.0:
            action = self._best_ready_skill(obs)

        # 4. Trigger the selected skill's cooldown.
        self._skill_cooldowns[action] = SKILL_COOLDOWNS_SEC[action]

        # 5. Tick all cooldowns by step duration.
        self._skill_cooldowns = np.maximum(0.0, self._skill_cooldowns - STEP_DURATION)

        # 6. Compute counter-play effectiveness twice: once for the skill that
        #    actually fires (drives real damage — "what physically happens in
        #    sim"), and once for the policy's raw request (drives reward).
        #    These diverge whenever cooldown substitution kicks in. SB3's
        #    rollout buffer credits whatever reward step() returns to
        #    raw_action (the action it actually sampled) — crediting it with
        #    eff computed from a silently-substituted different skill taught
        #    the policy a corrupted action->reward association and was
        #    confirmed (via ml-agents-specialist diagnostic) to be why
        #    ProjectileBurst never converged despite objectively being the
        #    best counter for parry_heavy/barrier_passive: cooldown
        #    substitution diluted its true reward margin toward the average
        #    of whatever it kept getting substituted for.
        eff = self._compute_effectiveness(action, obs)
        raw_eff = self._compute_effectiveness(raw_action, obs)

        # 7. Resolve damage exchange. All 4 remaining skills deal nonzero base
        #    damage (ShieldPhase's 0-damage case was removed along with the skill).
        base = SKILL_BASE_DAMAGE[action]
        # Damage scales from 50% (no counter) to 150% (perfect counter).
        damage_to_player = base * (0.5 + eff)
        damage_to_boss = self._compute_player_retaliation(eff, base)

        self._player_hp = max(0.0, self._player_hp - damage_to_player)
        self._boss_hp = max(0.0, self._boss_hp - damage_to_boss)

        # 8. Compute reward — credited to raw_action/raw_eff (see step 6).
        reward = self._compute_reward(raw_action, damage_to_player, damage_to_boss, raw_eff, obs)

        # 9. Check terminal conditions.
        player_dead = self._player_hp <= 0.0
        boss_dead = self._boss_hp <= 0.0
        timeout = self._step_num >= MAX_EPISODE_STEPS

        terminated = player_dead or boss_dead
        truncated = timeout and not terminated

        if player_dead:
            reward += REWARD_WIN
        if boss_dead:
            reward += REWARD_LOSS

        # Append AFTER reward computation — _compute_reward's windowed penalty
        # compares the current pick against picks strictly before this one.
        # Tracks raw_action (not the substituted action), consistent with the
        # credit-assignment fix above: the penalty should discourage the
        # policy from repeatedly wanting the same skill, regardless of what
        # cooldown forced it to actually execute.
        self._recent_skills.append(raw_action)
        if len(self._recent_skills) > VARIETY_WINDOW_SIZE:
            self._recent_skills.pop(0)

        info: dict = {
            "boss_hp_norm": self._boss_hp / BOSS_MAX_HP,
            "player_hp_norm": self._player_hp / PLAYER_MAX_HP,
            "skill_used": action,
            # Raw policy choice BEFORE cooldown substitution. Distinguishing this
            # from "skill_used" matters: a skill with a long cooldown (AoESlam,
            # 9s) gets substituted away often regardless of what the policy
            # wants, which can make a badly collapsed policy (e.g. "always want
            # AoESlam") look artificially diverse in the post-substitution
            # skill_used histogram — the cooldown clock manufactures apparent
            # variety that has nothing to do with the policy reading the
            # observation. evaluate_policy() must track both.
            "raw_action": raw_action,
            "effectiveness": float(eff),
            # Effectiveness of the policy's raw request, not the substituted
            # skill that actually executed. evaluate_policy's avg_eff gate uses
            # this — the executed-skill "effectiveness" above gets diluted
            # toward a near-uniform average across all 4 skills by cooldown
            # substitution regardless of how well the policy discriminates
            # (confirmed empirically: avg_eff plateaued at ~0.335 whether
            # trained for 200k or 2M steps), so it understates genuine
            # decision quality. Mirrors the raw_action credit-assignment fix
            # applied to reward in step 8 above, applied here to the metric.
            "raw_effectiveness": float(raw_eff),
            "step": self._step_num,
        }
        return obs.copy(), float(reward), terminated, truncated, info

    # ------------------------------------------------------------------
    # Player simulation — mirrors PlayerBehaviorTracker accumulator logic
    # ------------------------------------------------------------------

    def _simulate_player_step(self) -> None:
        """
        Advance all player accumulators by STEP_DURATION seconds.
        Uses the episode's PlayerStyle as the stochastic driver.
        """
        style = self._player_style
        dt = STEP_DURATION

        self._elapsed_time += dt

        # Aggression: fraction of time spent within CLOSE_RANGE_THRESHOLD.
        self._time_in_close_range += style.aggression * dt

        # Normal-attack rate: Poisson arrivals, same treatment as the 5 tracked skills.
        normal_attack_rate_per_sec = style.normal_attack_rate / 60.0
        expected_normal_attacks = normal_attack_rate_per_sec * dt
        self._normal_attack_count += int(self._rng.poisson(max(0.0, expected_normal_attacks)))

        # Range & position samples: sample_interval = 0.1 s → 20 samples per step.
        n_samples = max(1, int(round(dt / SAMPLE_INTERVAL)))
        mean_boss_dist = style.mean_dist_normalized * MAX_ARENA_RANGE
        mean_dist_from_centre = (1.0 - style.mean_pos_bias) * ARENA_RADIUS

        for _ in range(n_samples):
            dist_noise = float(self._rng.normal(0.0, mean_boss_dist * 0.1 + 0.5))
            self._range_sample_sum += max(0.0, mean_boss_dist + dist_noise)
            self._range_sample_count += 1

            pos_noise = float(self._rng.normal(0.0, mean_dist_from_centre * 0.1 + 0.2))
            self._pos_sample_sum += max(0.0, mean_dist_from_centre + pos_noise)
            self._pos_sample_count += 1

        # Skill uses: Poisson arrivals for each skill.
        for skill_idx in range(5):
            rate_per_sec = style.skill_rates[skill_idx] / 60.0
            expected_uses = rate_per_sec * dt
            uses = int(self._rng.poisson(max(0.0, expected_uses)))
            self._skill_use_counts[skill_idx] += uses

            # Classify dash directions (skill_idx == 0 is Dash).
            if skill_idx == 0:
                for _ in range(uses):
                    # Draw a dash direction. dodge_bias = P(rightward dash).
                    dot = float(self._rng.uniform(-1.0, 1.0))
                    if dot > DASH_LATERAL_THRESHOLD:
                        # Bias toward right proportionally.
                        if self._rng.random() < style.dodge_bias:
                            self._right_dash_count += 1
                        else:
                            self._left_dash_count += 1
                    elif dot < -DASH_LATERAL_THRESHOLD:
                        if self._rng.random() < (1.0 - style.dodge_bias):
                            self._left_dash_count += 1
                        else:
                            self._right_dash_count += 1
                    # else: forward/back dash — excluded from DodgeDirectionBias

    # ------------------------------------------------------------------
    # Observation — mirrors PlayerBehaviorTracker.GetCurrentVector()
    # ------------------------------------------------------------------

    def _compute_obs(self) -> np.ndarray:
        """
        Build the 9-float BehaviorStatVector from current accumulators.
        All normalization matches PlayerBehaviorTracker.cs exactly.
        """
        obs = np.empty(N_OBS, dtype=np.float32)

        # [0] DodgeDirectionBias
        total_lateral = self._left_dash_count + self._right_dash_count
        obs[0] = (
            float(self._right_dash_count) / total_lateral
            if total_lateral > 0
            else 0.5
        )

        # [1]–[5] SkillUsageFrequency per skill
        elapsed_min = self._elapsed_time / 60.0
        for i in range(5):
            if elapsed_min > 0.0 and MAX_USES_PER_MINUTE[i] > 0.0:
                uses_per_min = self._skill_use_counts[i] / elapsed_min
                obs[1 + i] = float(np.clip(uses_per_min / MAX_USES_PER_MINUTE[i], 0.0, 1.0))
            else:
                obs[1 + i] = 0.0

        # [6] AggressionScore — blends proximity with normal-attack rate; see
        # PlayerBehaviorTracker.ComputeAggressionScore for the mirrored formula.
        if self._elapsed_time > 0.0:
            proximity_fraction = np.clip(self._time_in_close_range / self._elapsed_time, 0.0, 1.0)
            attack_rate_norm = (
                np.clip((self._normal_attack_count / elapsed_min) / MAX_NORMAL_ATTACKS_PER_MINUTE, 0.0, 1.0)
                if elapsed_min > 0.0
                else 0.0
            )
            obs[6] = float(np.clip(proximity_fraction * 0.6 + attack_rate_norm * 0.4, 0.0, 1.0))
        else:
            obs[6] = 0.0

        # [7] AverageEngagementRange
        if self._range_sample_count > 0 and MAX_ARENA_RANGE > 0.0:
            mean_dist = self._range_sample_sum / self._range_sample_count
            obs[7] = float(np.clip(mean_dist / MAX_ARENA_RANGE, 0.0, 1.0))
        else:
            obs[7] = 0.5  # neutral default before boss position is tracked

        # [8] PositionalBias
        if self._pos_sample_count > 0 and ARENA_RADIUS > 0.0:
            mean_from_centre = self._pos_sample_sum / self._pos_sample_count
            obs[8] = float(np.clip(1.0 - (mean_from_centre / ARENA_RADIUS), 0.0, 1.0))
        else:
            obs[8] = 0.5  # neutral default

        return obs

    # ------------------------------------------------------------------
    # Counter-play effectiveness
    # ------------------------------------------------------------------

    def _compute_effectiveness(self, skill: int, obs: np.ndarray) -> float:
        """
        Returns a counter-play effectiveness value in [0, 1] for the given
        boss skill against the current player observation.

        Skill-frequency observations [1]-[5] are DAMPENED (not hard-zeroed) for
        the first WARMUP_STEPS steps via _warmup_ramp(), because the
        elapsed-time denominator is too small to be meaningful that early (a
        single early skill use can spike uses/min to the clip ceiling).

        A hard 0/1 gate was tried first and caused a worse problem than the one
        it solved: ProjectileBurst's effectiveness formula uses ONLY gated
        terms (barrier + parry frequency, both in [1]-[5]), while Charge and
        AoESlam each keep at least one non-gated term (obs[7], obs[6]). Hard-
        zeroing ProjectileBurst to exactly 0 for the first 5 of every 30 steps
        gave Charge/AoESlam a structural training advantage from step 1 that
        ProjectileBurst never got — in practice this trained a policy that
        picked ProjectileBurst 0-1% of the time for EVERY archetype, including
        parry_heavy and barrier_passive where it should dominate. The smooth
        ramp keeps the spike-suppression benefit while still giving every
        skill *some* nonzero signal from step 1.
        """
        warmup_ramp = min(1.0, self._step_num / WARMUP_STEPS)

        if skill == 0:  # Charge — counters ranged play, plus burst-strike spam (hard
                        # to aim a targeted burst while the boss is closing distance)
            range_eff = float(obs[7])
            burst_eff = float(obs[4]) * warmup_ramp
            return float(np.clip(range_eff * 0.7 + burst_eff * 0.3, 0.0, 1.0))

        if skill == 1:  # ProjectileBurst — counters barrier/passive turtling AND
                        # parry-spam (a sudden multi-projectile burst is much harder
                        # to react-parry than a single telegraphed attack).
                        # PositionalBias dropped: it was a proxy that only held up
                        # because barrier_passive's synthetic archetype happened to
                        # bundle center-hugging with barrier use, not an independent
                        # signal for passivity.
            barrier_eff = float(obs[5]) * warmup_ramp
            parry_eff   = float(obs[2]) * warmup_ramp
            return float(np.clip((barrier_eff + parry_eff) * 0.5, 0.0, 1.0))

        if skill == 2:  # AoESlam — counters close aggression
            return float(obs[6])

        if skill == 3:  # TeleportStrike — counters dash-heavy play, plus general
                        # distance-keeping (deliberately overlaps with Charge's
                        # obs[7] driver — more than one valid counter per player
                        # tendency increases skill variety instead of a strict 1:1
                        # mapping).
            dash_eff = float(obs[1]) * warmup_ramp
            # Predictable dodge direction amplifies effectiveness.
            bias_deviation = abs(float(obs[0]) - 0.5) * 2.0
            range_eff = float(obs[7])
            return float(np.clip(dash_eff * 0.5 + bias_deviation * 0.3 + range_eff * 0.2, 0.0, 1.0))

        return 0.0

    def _compute_player_retaliation(
        self, boss_effectiveness: float, boss_base_damage: float = 0.0
    ) -> float:
        """
        Damage the player deals to the boss this step.

        Includes two sources:
        1. Active damage (melee + ranged + burst), disrupted by boss counter-play.
        2. Parry-reflect: P(boss attack lands during parry window) × boss damage.
           DamageHandler.TakeDamage() routes full damage back to boss during parry.
           (ShieldPhase used to pass boss_base_damage=0 here to suppress reflect —
           it's no longer part of this environment; see module docstring.)

        When the boss counter-plays perfectly (effectiveness=1.0), the player
        is disrupted and deals only 25% of their normal output. When the boss
        whiffs (effectiveness=0.0), the player deals full damage.
        """
        style = self._player_style
        aggression_dmg = style.aggression * 12.0 * STEP_DURATION
        burst_dmg = (style.skill_rates[3] / 60.0) * STEP_DURATION * 20.0
        ranged_dmg = (style.skill_rates[2] / 60.0) * STEP_DURATION * 10.0
        base_dps = aggression_dmg + burst_dmg + ranged_dmg

        disruption = 1.0 - boss_effectiveness * 0.75
        retaliation = base_dps * disruption

        # Parry-reflect: P(at least one parry window overlaps the boss attack this step).
        # Probability ≈ parry_rate_per_sec × parry_window_duration, clamped to [0, 1].
        # Source: DamageHandler.cs:99-104 — full damage reflects when IsParryActive.
        parry_rate_per_sec = style.skill_rates[1] / 60.0
        p_reflect = min(1.0, parry_rate_per_sec * PARRY_WINDOW_SEC)
        retaliation += p_reflect * boss_base_damage

        return float(retaliation)

    # ------------------------------------------------------------------
    # Reward
    # ------------------------------------------------------------------

    def _compute_reward(
        self,
        action: int,
        dmg_to_player: float,
        dmg_to_boss: float,
        eff: float,
        obs: np.ndarray,
    ) -> float:
        reward = REWARD_ALIVE_BONUS

        # Damage rewards are disabled (scales = 0.0); these lines evaluate to 0
        # but are kept so the structure mirrors the Unity reward rationale.
        #
        # WARNING if either scale above is ever made nonzero: dmg_to_player/dmg_to_boss
        # are computed in step() from the EXECUTED (post-cooldown-substitution) action,
        # not raw_action, even though `action` here is raw_action (see step()'s credit-
        # assignment fix). Reintroducing nonzero damage reward without also recomputing
        # damage from raw_action would silently reintroduce the exact substitution-
        # diluted credit-assignment bug this fix was written to solve — just for the
        # damage terms instead of the effectiveness term. Currently inert only because
        # both scales are 0.0.
        reward += (dmg_to_player / PLAYER_MAX_HP) * REWARD_DAMAGE_DEALT_SCALE
        reward -= (dmg_to_boss / BOSS_MAX_HP) * REWARD_DAMAGE_TAKEN_SCALE

        # Counter-play bonus: discrete signal for matching the dominant vulnerability.
        if action == self._dominant_counter(obs):
            reward += REWARD_COUNTER_BONUS

        # Dense effectiveness reward: continuous gradient toward any high-eff skill.
        # No warmup suppression needed: _compute_effectiveness and _dominant_counter
        # already return 0 for frequency-based skills (1-4) during warmup, and
        # AoESlam/Charge effectiveness (obs[6]/obs[7]) are reliable from step 1.
        reward += eff * REWARD_EFFECTIVENESS_SCALE

        # Windowed variety penalty: -REWARD_VARIETY_PENALTY_PER_REPEAT for each of
        # the trailing VARIETY_WINDOW_SIZE previous picks that match this one.
        # Escalates the more a skill dominates recent picks (0/0.3/0.6/0.9 for
        # 0/1/2/3 matches) instead of only checking the immediately-previous pick —
        # a flat single-step check was too weak to counteract locking onto one
        # "objectively correct" skill for an entire episode (see #diversity).
        repeat_count = sum(1 for s in self._recent_skills if s == action)
        reward -= repeat_count * REWARD_VARIETY_PENALTY_PER_REPEAT

        return reward

    def _dominant_counter(self, obs: np.ndarray) -> int:
        """
        Returns the boss skill index that best counters the player's
        current dominant behaviour.

        Score each skill by its raw counter signal, then pick the highest.
        Skill-frequency signals are dampened (not hard-zeroed) during warm-up
        via _warmup_ramp() — see _compute_effectiveness's docstring for why a
        hard 0/1 gate structurally disadvantaged ProjectileBurst (both of its
        terms are frequency-based, so hard-zeroing gave Charge/AoESlam — each
        with a non-gated term — an unfair training advantage from step 1).
        """
        warmup_ramp = min(1.0, self._step_num / WARMUP_STEPS)

        # All 4 rows' weights sum to 1.0 — kept deliberately identical to the
        # corresponding _compute_effectiveness weights (single source of truth).
        # Teleport previously used different weights here (0.5+0.6+0.4=1.5
        # ceiling) than in _compute_effectiveness (0.5+0.3+0.2=1.0 ceiling) —
        # that scale mismatch let Teleport win argmax whenever dodge-bias
        # happened to be extreme, REGARDLESS of archetype (dodge_bias is
        # sampled independently of archetype, so this affected ~half of all
        # episodes across every archetype, not just dash-heavy ones).
        #
        # bias_deviation must carry the same *2.0 normalization as
        # _compute_effectiveness's bias_deviation term (abs(obs[0]-0.5) maxes
        # at 0.5, so without *2.0 this row's dodge-bias contribution tops out
        # at 0.15 instead of the intended 0.3 ceiling) — devil's-advocate found
        # this had silently reintroduced a smaller version of the exact bug the
        # comment above describes fixing: the discrete +1.5 counter bonus and
        # the dense eff reward disagreed about how much dodge-bias should
        # matter for players with strong lateral bias but moderate dash rate.
        scores = np.array([
            obs[7] * 0.7 + obs[4] * warmup_ramp * 0.3,                     # 0: Charge vs ranged + burst spam
            (obs[5] * warmup_ramp + obs[2] * warmup_ramp) * 0.5,          # 1: Burst vs barrier-turtle + parry-spam
            obs[6],                                                        # 2: AoE vs aggressive
            obs[1] * warmup_ramp * 0.5
                + abs(obs[0] - 0.5) * 2.0 * 0.3 + obs[7] * 0.2,           # 3: Teleport vs dash + range
        ], dtype=np.float32)

        return int(np.argmax(scores))

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _best_ready_skill(self, obs: np.ndarray) -> int:
        """
        Returns a ready skill (cooldown <= 0), sampled with probability
        weighted by counter-play effectiveness against the current
        observation — a softmax over the same _compute_effectiveness()
        formula reward is built on, not a strict argmax, and not raw
        remaining cooldown time.

        A strict argmax was tried first and made substitution fully
        deterministic for any player whose behaviour stats are roughly stable
        within a fight (most real fights, since these stats are slow-moving
        running averages) — the boss locked onto its raw top pick plus one
        fixed "runner-up" substitute for the whole fight, with the 3rd/4th
        skill only firing if both of those happened to be on cooldown at
        once. Softmax sampling keeps better counters more likely without
        making them a guarantee, so substitution still varies across a fight
        even against a completely steady playstyle.

        Falls back to argmin(remaining_seconds) only if no skill is currently
        ready (rare — requires all 4 simultaneously mid-cooldown), so a skill
        still always fires this step, matching the original always-execute
        contract.

        Mirrors BossRLAgent.ApplyCooldownSubstitution() in Unity, which ports
        this exact formula to C# (BossCounterEffectiveness.cs) and evaluates it
        against the same 9-float observation vector — not the model's own
        output scores. _compute_effectiveness() is a pure function of
        (skill_index, obs) with no RNG or training-only state, so it can be
        replicated exactly rather than approximated. Keep both in sync if this
        formula or SUBSTITUTION_SOFTMAX_TEMPERATURE ever change — see
        README.md "Ranking-based substitution."
        """
        ready = [i for i in range(N_BOSS_SKILLS) if self._skill_cooldowns[i] <= 0.0]
        if ready:
            scores  = np.array([self._compute_effectiveness(i, obs) for i in ready])
            weights = np.exp(scores / SUBSTITUTION_SOFTMAX_TEMPERATURE)
            probs   = weights / weights.sum()
            return int(self._rng.choice(ready, p=probs))
        return int(np.argmin(self._skill_cooldowns))


# ---------------------------------------------------------------------------
# Validation helpers — run after training to check counter-play is learned
# ---------------------------------------------------------------------------

def evaluate_policy(model, n_episodes: int = 200, verbose: bool = True, seed: int | None = None) -> dict:
    """
    Evaluate counter-play effectiveness and boss survival rate per archetype.

    Primary pass gate: overall avg_eff >= 0.35.
    A random policy achieves avg_eff ~0.15-0.20; 0.35 confirms the policy is
    actively counter-playing, not guessing.

    avg_eff is computed from each step's raw_effectiveness (the policy's
    requested action's effectiveness), not the executed/substituted skill's.
    Cooldown substitution forces each archetype's executed skill mix toward
    near-uniform regardless of how well the policy discriminates, which
    dilutes executed-effectiveness toward each archetype's average across all
    4 skills — understating genuine decision quality. raw_effectiveness
    measures what the policy actually chose to do, mirroring the raw_action
    credit-assignment fix already applied to reward in step().

    Survival rate is logged but is NOT the gate: the aggressive archetype deals
    ~530 total damage over 30 steps even with perfect AoESlam use (cooldown forces
    24 non-AoESlam steps at near-full damage), making BOSS_MAX_HP=200 structurally
    unsurvivable regardless of policy quality. avg_eff correctly measures
    counter-play without depending on the HP budget.
    """
    from stable_baselines3 import PPO  # noqa: F401 — ensure import works

    env = CyberBossEnv()
    archetype_survived: dict[str, list[bool]] = {a: [] for a in _ARCHETYPES}
    archetype_eff: dict[str, list[float]] = {a: [] for a in _ARCHETYPES}
    archetype_skill_counts: dict[str, np.ndarray] = {
        a: np.zeros(N_BOSS_SKILLS, dtype=np.int64) for a in _ARCHETYPES
    }
    # Raw policy choice BEFORE cooldown substitution — see step()'s "raw_action"
    # comment. A skill with a long cooldown (AoESlam, 9s) gets substituted away
    # often regardless of what the policy actually wants; tracking only the
    # post-substitution "skill_used" can make a badly collapsed policy (e.g.
    # "always want AoESlam" for every input) look artificially diverse, since
    # the cooldown clock — not the observation — ends up driving the apparent
    # variety. This histogram is the only way to see what the policy itself
    # actually prefers.
    archetype_raw_action_counts: dict[str, np.ndarray] = {
        a: np.zeros(N_BOSS_SKILLS, dtype=np.int64) for a in _ARCHETYPES
    }

    # Seed the env RNG once at the start; subsequent resets continue the same sequence.
    first_reset_seed = seed
    for _ in range(n_episodes):
        obs, _ = env.reset(seed=first_reset_seed)
        first_reset_seed = None  # only seed the very first reset
        style = env._player_style
        # Bucket by the archetype random() actually sampled, not a re-derived
        # threshold guess. The previous threshold-based re-classifier had
        # several undocumented collisions found by devil's-advocate review —
        # defensive's parry baseline (4.5) always exceeded the parry_heavy
        # threshold (3.5) so real defensive episodes were silently counted as
        # parry_heavy; balanced bled into ranged; dash_heavy bled into
        # aggressive — contaminating the per-archetype validation table with
        # blended samples. style.archetype is set directly at sample time in
        # PlayerStyle.random(), so this bucketing is now exact.
        archetype = style.archetype

        ep_eff: list[float] = []
        done = False
        while not done:
            action, _ = model.predict(obs, deterministic=True)
            obs, _, terminated, truncated, info = env.step(int(action))
            ep_eff.append(info["raw_effectiveness"])
            archetype_skill_counts[archetype][info["skill_used"]] += 1
            archetype_raw_action_counts[archetype][info["raw_action"]] += 1
            done = terminated or truncated

        boss_survived = info["boss_hp_norm"] > 0.0
        archetype_survived[archetype].append(boss_survived)
        if ep_eff:
            archetype_eff[archetype].append(float(sum(ep_eff) / len(ep_eff)))

    overall_survived = sum(v for s in archetype_survived.values() for v in s)
    overall_total = sum(len(s) for s in archetype_survived.values())
    overall_survival_rate = overall_survived / max(1, overall_total)

    all_eff = [e for es in archetype_eff.values() for e in es]
    overall_avg_eff = sum(all_eff) / max(1, len(all_eff))

    # Per-archetype skill-choice distribution — a stronger check than avg_eff alone.
    # avg_eff can look healthy even if the policy leans on 1-2 skills that score
    # decently everywhere; this confirms it actually discriminates between archetypes
    # and matches the hand-authored counter-play table in README.md.
    #
    # Gates on RAW pre-substitution action, not post-substitution "skill_used" —
    # a long-cooldown skill (AoESlam, 9s) gets substituted away often regardless
    # of what the policy wants, which can make a collapsed policy ("always want
    # AoESlam" for every input) look artificially diverse in skill_used alone.
    # Both histograms are printed; only raw_action drives the pass/fail gate.
    discrimination_ok = True
    if verbose:
        print(f"\n=== Raw policy choice per archetype, pre-cooldown-substitution ({n_episodes} episodes) ===")
        for arch in _ARCHETYPES:
            counts = archetype_raw_action_counts[arch]
            total = counts.sum()
            if total == 0:
                continue
            pct = counts / total
            top_idx = int(np.argmax(counts))
            expected = EXPECTED_DOMINANT_SKILL[arch]
            dist_str = "  ".join(
                f"{SKILL_NAMES[i]}={pct[i]:.0%}" for i in range(N_BOSS_SKILLS)
            )
            if expected:
                matched = top_idx in expected
                discrimination_ok &= matched
                flag = "OK" if matched else "MISMATCH"
                expected_names = "/".join(SKILL_NAMES[i] for i in expected)
                print(f"  {arch:20s}  top={SKILL_NAMES[top_idx]:15s} expected={expected_names:25s} [{flag}]")
            else:
                print(f"  {arch:20s}  top={SKILL_NAMES[top_idx]:15s} (no expected skill — generalisation check)")
            print(f"  {'':20s}  {dist_str}")
        if discrimination_ok:
            print("  PASS: every archetype's top RAW policy choice matches the expected counter.")
        else:
            print("  WARNING: at least one archetype's top RAW choice does not match the "
                  "expected counter (see MISMATCH rows above). The policy may have "
                  "collapsed onto a subset of skills rather than truly discriminating "
                  "player styles.")

        print(f"\n=== Effective (post-cooldown-substitution) skill usage per archetype ===")
        for arch in _ARCHETYPES:
            counts = archetype_skill_counts[arch]
            total = counts.sum()
            if total == 0:
                continue
            pct = counts / total
            dist_str = "  ".join(
                f"{SKILL_NAMES[i]}={pct[i]:.0%}" for i in range(N_BOSS_SKILLS)
            )
            print(f"  {arch:20s}  {dist_str}")
        print("  (This is what actually happens in-game after cooldown substitution —"
              " informational only, NOT the discrimination gate. A near-even spread"
              " here does not by itself mean the policy is discriminating; check the"
              " raw-choice table above.)")

    if verbose:
        print(f"\n=== Evaluation ({n_episodes} episodes) ===")
        for arch in _ARCHETYPES:
            s = archetype_survived[arch]
            e = archetype_eff[arch]
            if s:
                rate = sum(s) / len(s)
                avg_eff = sum(e) / len(e) if e else 0.0
                print(f"  {arch:20s}  survival = {rate:.0%}  avg_eff = {avg_eff:.2f}  (n={len(s)})")
        print(f"  {'OVERALL':20s}  survival = {overall_survival_rate:.0%}  avg_eff = {overall_avg_eff:.3f}")
        gate_pass = overall_avg_eff >= 0.35
        if gate_pass:
            print("  PASS: avg_eff >= 0.35 — policy is actively counter-playing.")
        else:
            print(f"  WARNING: avg_eff = {overall_avg_eff:.3f} < 0.35. Tune reward weights before export.")

    return {
        "overall": overall_survival_rate,
        "overall_avg_eff": overall_avg_eff,
        "by_archetype": archetype_survived,
        "skill_distribution": archetype_skill_counts,
        "raw_action_distribution": archetype_raw_action_counts,
        "discrimination_ok": discrimination_ok,
    }


# ---------------------------------------------------------------------------
# Gradient norm callback — logs pre-clip norm alongside SB3's built-in metrics
# ---------------------------------------------------------------------------

class GradNormCallback:
    """
    Patches the PPO optimizer's step() to log the pre-clip L2 gradient norm.

    SB3's PPO clips at max_grad_norm=0.5 by default but never surfaces the
    pre-clip value. Tracking it reveals whether gradients are consistently
    hitting the clip boundary (too-large updates) or consistently tiny
    (no effective learning signal).

    Usage: pass an instance in the callbacks list to model.learn().
    """

    def __init__(self) -> None:
        self._orig_step: Any = None
        # Minimal BaseCallback duck-typing for SB3's callback list
        self.n_calls: int = 0
        self.num_timesteps: int = 0
        self.locals: dict = {}
        self.globals: dict = {}
        self.logger: Any = None
        self.parent: Any = None
        self.model: Any = None

    def init_callback(self, model: Any) -> None:
        self.model = model
        self.logger = model.logger
        optimizer = model.policy.optimizer
        orig_step = optimizer.step
        logger_ref = self.logger
        model_ref = model

        def _logging_step(closure: Any = None) -> Any:
            total_norm = 0.0
            for p in model_ref.policy.parameters():
                if p.grad is not None:
                    total_norm += p.grad.data.norm(2).item() ** 2
            logger_ref.record("train/grad_norm_pre_clip", total_norm ** 0.5)
            return orig_step(closure)

        optimizer.step = _logging_step
        self._orig_step = orig_step

    def on_training_start(self, locals_: dict, globals_: dict) -> None:
        pass

    def on_rollout_start(self) -> None:
        pass

    def on_step(self) -> bool:
        return True

    def on_rollout_end(self) -> None:
        pass

    def on_training_end(self) -> None:
        if self._orig_step is not None:
            self.model.policy.optimizer.step = self._orig_step

    def update_locals(self, locals_: dict) -> None:
        """Required by CallbackList.update_locals(), called every rollout step."""
        self.locals.update(locals_)
        self.update_child_locals(locals_)

    def update_child_locals(self, locals_: dict) -> None:
        """No sub-callbacks to propagate to — matches BaseCallback's no-op default."""
        pass


# ---------------------------------------------------------------------------
# Checkpoint sweep — evaluate all step checkpoints on a seeded held-out env
# ---------------------------------------------------------------------------

def eval_checkpoints(
    run_id: str,
    output_path: str = ONNX_OUTPUT_PATH,
    n_episodes: int = 200,
) -> dict[int, dict]:
    """
    Evaluate every step checkpoint for a run on a fixed seeded environment,
    rank by avg_eff, and export the best checkpoint as BossBrain.onnx.

    All checkpoints are evaluated with EVAL_SEED so results are directly
    comparable across steps — eliminates evaluation variance as a confounder
    when choosing the best checkpoint.

    Returns a dict of {step: {"path": str, "avg_eff": float}}.
    """
    import glob

    try:
        from stable_baselines3 import PPO
    except ImportError as exc:
        raise SystemExit("pip install stable-baselines3") from exc

    pattern = os.path.join(CHECKPOINT_DIR, run_id, "BossBrain_*_steps.zip")
    checkpoint_files = sorted(
        glob.glob(pattern),
        key=lambda p: int(os.path.basename(p).split("_")[1]),
    )

    if not checkpoint_files:
        raise FileNotFoundError(
            f"No step checkpoints found at {pattern}. "
            f"Run training first: python cyberboss_env.py --run-id {run_id}"
        )

    print(f"\n[Sweep] Evaluating {len(checkpoint_files)} checkpoints for {run_id} "
          f"({n_episodes} episodes each, seed={EVAL_SEED})\n")

    results: dict[int, dict] = {}
    for ckpt_path in checkpoint_files:
        steps = int(os.path.basename(ckpt_path).split("_")[1])
        model = PPO.load(ckpt_path)
        eval_result = evaluate_policy(model, n_episodes=n_episodes, verbose=False, seed=EVAL_SEED)
        avg_eff = eval_result["overall_avg_eff"]
        results[steps] = {"path": ckpt_path, "avg_eff": avg_eff}
        print(f"  {steps:>9,} steps  avg_eff = {avg_eff:.3f}")

    best_steps = max(results, key=lambda s: results[s]["avg_eff"])
    best = results[best_steps]
    print(f"\n  Best checkpoint: {best_steps:,} steps  avg_eff = {best['avg_eff']:.3f}")

    print(f"\n[Sweep] Exporting best checkpoint to {output_path} ...")
    best_model = PPO.load(best["path"])
    export_onnx(best_model, output_path)
    return results


# ---------------------------------------------------------------------------
# Training entry point
# ---------------------------------------------------------------------------

def train(total_steps: int = 2_000_000, run_id: str = "cyberboss_v1") -> Any:
    """
    Train the BossBrain PPO policy using stable-baselines3.

    Hyperparameters match cyberboss_config.yaml (ML-Agents format reference).
    Returns the trained SB3 model.
    """
    try:
        from stable_baselines3 import PPO
        from stable_baselines3.common.env_util import make_vec_env
        from stable_baselines3.common.callbacks import CheckpointCallback, CallbackList
    except ImportError as exc:
        raise SystemExit(
            "stable-baselines3 is required: pip install stable-baselines3"
        ) from exc

    os.makedirs(CHECKPOINT_DIR, exist_ok=True)

    # Vectorise with 8 parallel environments for faster data collection.
    vec_env = make_vec_env(CyberBossEnv, n_envs=8)

    model = PPO(
        policy="MlpPolicy",
        env=vec_env,
        learning_rate=3.0e-4,
        n_steps=256,         # 256×8=2048 per rollout: ~68 episodes, better advantage estimates
        batch_size=512,
        n_epochs=10,         # more gradient steps per rollout
        gamma=0.99,
        gae_lambda=0.95,
        clip_range=0.2,
        # Raised from 5e-3 after the v7 retrain's raw-action histogram (see
        # step()'s raw_action tracking) showed the policy had collapsed to
        # near-total confidence on AoESlam regardless of input (logits ~0 vs
        # ~-10 for every other action, for nearly every test observation) —
        # classic premature convergence. Stronger entropy pressure keeps the
        # policy exploring long enough to discover archetype-specific payoffs
        # instead of settling on whichever action looks safest early.
        ent_coef=2.0e-2,
        verbose=1,
        policy_kwargs={
            "net_arch": [128, 128],  # 2 hidden layers, 128 units — config network_settings
            "activation_fn": __import__("torch").nn.Tanh,
        },
    )

    checkpoint_cb = CheckpointCallback(
        save_freq=200_000 // 8,  # per-env steps; total = save_freq × n_envs
        save_path=os.path.join(CHECKPOINT_DIR, run_id),
        name_prefix="BossBrain",
    )

    grad_norm_cb = GradNormCallback()

    print(f"[Train] Starting PPO training: {total_steps:,} total steps, run_id={run_id}")
    model.learn(
        total_timesteps=total_steps,
        callback=CallbackList([checkpoint_cb, grad_norm_cb]),
    )

    save_path = os.path.join(CHECKPOINT_DIR, run_id, "BossBrain_final")
    model.save(save_path)
    print(f"[Train] Model saved to {save_path}")

    vec_env.close()
    return model


# ---------------------------------------------------------------------------
# ONNX export
# ---------------------------------------------------------------------------

def export_onnx(model: Any, output_path: str = ONNX_OUTPUT_PATH) -> None:
    """
    Export the trained SB3 PPO policy to ONNX for Unity Sentis.

    Input tensor  : "obs_0"            shape (batch, 9), dtype float32
    Output tensor : "discrete_actions" shape (batch, 4), dtype float32 (logits)

    Part 9 (SentisInferenceManager) reads the output and takes argmax to get
    the skill index. Do NOT apply softmax here — Sentis reads raw logits.
    """
    try:
        import torch
        import torch.nn as nn
        import onnx
    except ImportError as exc:
        raise SystemExit(
            "torch and onnx are required: pip install torch onnx"
        ) from exc

    class _PolicyInferenceWrapper(nn.Module):
        """
        Wraps the SB3 ActorCriticPolicy to output only the action logits.
        The value head is discarded — Sentis only needs the actor output.
        """

        def __init__(self, policy: Any) -> None:
            super().__init__()
            self.features_extractor = policy.features_extractor
            self.mlp_extractor = policy.mlp_extractor
            self.action_net = policy.action_net

        def forward(self, obs: "torch.Tensor") -> "torch.Tensor":
            features = self.features_extractor(obs)
            latent_pi, _ = self.mlp_extractor(features)
            return self.action_net(latent_pi)

    policy = model.policy
    policy.eval()

    wrapper = _PolicyInferenceWrapper(policy)
    wrapper.eval()

    dummy_input = __import__("torch").zeros(1, N_OBS, dtype=__import__("torch").float32)

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)

    __import__("torch").onnx.export(
        wrapper,
        dummy_input,
        output_path,
        export_params=True,
        opset_version=11,       # Sentis-compatible opset
        do_constant_folding=True,
        input_names=["obs_0"],
        output_names=["discrete_actions"],
        dynamic_axes={
            "obs_0":            {0: "batch"},
            "discrete_actions": {0: "batch"},
        },
        dynamo=False,           # Legacy exporter: avoids dynamo/onnxscript cp1252 crash
    )

    # Verify the exported model is structurally valid.
    onnx_model = onnx.load(output_path)
    onnx.checker.check_model(onnx_model)

    print(f"[Export] ONNX model written to {output_path}")
    print(f"[Export] Input  : obs_0           shape (batch, {N_OBS})")
    print(f"[Export] Output : discrete_actions shape (batch, {N_BOSS_SKILLS})")
    print("[Export] Part 9: argmax(discrete_actions) -> skill index 0-3")


def check_onnx_parity(
    run_id: str = "cyberboss_v1",
    onnx_path: str = ONNX_OUTPUT_PATH,
    n_random: int = 50,
) -> bool:
    """
    Confirms the exported ONNX graph picks the same skill (argmax) as the
    source SB3 policy, for the fixed PARITY_TEST_VECTORS plus n_random random
    observations.

    This only validates the PyTorch -> ONNX export step (torch.onnx.export in
    export_onnx()). It does NOT validate ONNX -> Sentis — Sentis is Unity/C#-only
    and can't be driven from this script. After this passes, open the boss
    GameObject in the Unity Editor (Play mode), right-click SentisInferenceManager
    in the Inspector, choose "Run Parity Check", and diff its printed skill
    indices against the ones printed here for the same vector names.
    """
    try:
        from stable_baselines3 import PPO
        import onnxruntime as ort
    except ImportError as exc:
        raise SystemExit(
            "pip install onnxruntime (stable-baselines3 already required for training)"
        ) from exc

    checkpoint_path = os.path.join(CHECKPOINT_DIR, run_id, "BossBrain_final.zip")
    if not os.path.exists(checkpoint_path):
        raise FileNotFoundError(
            f"No checkpoint found at {checkpoint_path}. "
            f"Run training first: python cyberboss_env.py --run-id {run_id}"
        )
    if not os.path.exists(onnx_path):
        raise FileNotFoundError(
            f"No ONNX export found at {onnx_path}. "
            f"Export first: python cyberboss_env.py --export-only --run-id {run_id}"
        )

    model = PPO.load(checkpoint_path)
    session = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])

    rng = np.random.default_rng(EVAL_SEED)
    names = list(PARITY_TEST_VECTOR_NAMES)
    vectors = list(PARITY_TEST_VECTORS)
    for i in range(n_random):
        vectors.append(rng.uniform(0.0, 1.0, size=N_OBS).astype(np.float32))
        names.append(f"random_{i}")

    print(f"\n[Parity] Comparing SB3 policy vs {onnx_path} on {len(vectors)} vectors\n")
    mismatches = 0
    for name, obs in zip(names, vectors):
        obs = np.asarray(obs, dtype=np.float32)
        torch_action, _ = model.predict(obs, deterministic=True)
        onnx_logits = session.run(["discrete_actions"], {"obs_0": obs.reshape(1, -1)})[0]
        onnx_action = int(np.argmax(onnx_logits[0]))
        match = int(torch_action) == onnx_action
        mismatches += 0 if match else 1
        if name in PARITY_TEST_VECTOR_NAMES:
            flag = "OK" if match else "MISMATCH"
            print(f"  {name:20s}  torch={SKILL_NAMES[int(torch_action)]:15s} "
                  f"onnx={SKILL_NAMES[onnx_action]:15s} [{flag}]")
        elif not match:
            print(f"  {name:20s}  torch={SKILL_NAMES[int(torch_action)]:15s} "
                  f"onnx={SKILL_NAMES[onnx_action]:15s} [MISMATCH]")

    print(f"\n[Parity] {len(vectors) - mismatches}/{len(vectors)} matched.")
    if mismatches == 0:
        print("[Parity] PASS — ONNX export preserves policy behaviour.")
    else:
        print(f"[Parity] FAIL — {mismatches} mismatches. Do not wire this ONNX "
              "export into Part 9 until resolved (check opset_version, "
              "do_constant_folding, and whether export_onnx was re-run against "
              "a stale checkpoint).")
    return mismatches == 0


def load_and_export(run_id: str, output_path: str = ONNX_OUTPUT_PATH) -> None:
    """Load a previously saved checkpoint and export to ONNX."""
    try:
        from stable_baselines3 import PPO
    except ImportError as exc:
        raise SystemExit("pip install stable-baselines3") from exc

    checkpoint_path = os.path.join(CHECKPOINT_DIR, run_id, "BossBrain_final.zip")
    if not os.path.exists(checkpoint_path):
        raise FileNotFoundError(
            f"No checkpoint found at {checkpoint_path}. "
            f"Run training first: python cyberboss_env.py --run-id {run_id}"
        )

    model = PPO.load(checkpoint_path, env=CyberBossEnv())
    evaluate_policy(model)
    export_onnx(model, output_path)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="CyberBoss BossBrain PPO trainer")
    parser.add_argument("--steps", type=int, default=2_000_000,
                        help="Total training timesteps (default: 2 000 000)")
    parser.add_argument("--run-id", type=str, default="cyberboss_v1",
                        help="Run identifier used for checkpoint paths")
    parser.add_argument("--export-only", action="store_true",
                        help="Skip training; load existing checkpoint and export ONNX")
    parser.add_argument("--eval-checkpoints", action="store_true",
                        help="Evaluate all step checkpoints on a seeded env, export the best")
    parser.add_argument("--check-parity", action="store_true",
                        help="Compare the exported ONNX model's argmax choice against the "
                             "source SB3 checkpoint on fixed + random vectors")
    parser.add_argument("--output", type=str, default=ONNX_OUTPUT_PATH,
                        help=f"ONNX output path (default: {ONNX_OUTPUT_PATH})")
    args = parser.parse_args()

    if args.check_parity:
        ok = check_onnx_parity(args.run_id, args.output)
        raise SystemExit(0 if ok else 1)
    elif args.eval_checkpoints:
        eval_checkpoints(args.run_id, args.output)
    elif args.export_only:
        load_and_export(args.run_id, args.output)
    else:
        trained_model = train(total_steps=args.steps, run_id=args.run_id)
        print("\n[Validate] Running post-training evaluation...")
        results = evaluate_policy(trained_model)
        if results["overall_avg_eff"] >= 0.35:
            print("[Validate] avg_eff threshold met. Exporting ONNX.")
            export_onnx(trained_model, args.output)
        else:
            print(
                f"[Validate] avg_eff = {results['overall_avg_eff']:.3f} < 0.35. "
                "Model saved but NOT exported. Tune reward weights or increase "
                "training steps, then re-run with --export-only."
            )
