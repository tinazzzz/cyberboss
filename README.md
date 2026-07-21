# CyberBoss

A 3D isometric cyberpunk boss fight where the boss learns how you play.

Built with Unity 3D (URP) and Unity ML-Agents, CyberBoss pits you against a single
boss in a neon-lit arena. As the fight progresses the boss tracks your behavior —
how aggressively you engage, which skills you lean on, where you position yourself —
and feeds that live stat vector into a trained reinforcement-learning policy. The
longer you fight, the better it reads you.

**WebGL playable in the browser** — no install required.

---

## Gameplay

You have five skills mapped to keyboard shortcuts:

| Skill | Effect | Boss counter |
|---|---|---|
| **Dash** | Quick directional dodge with invincibility frames | Teleport Strike (appears behind you) |
| **Parry** | Timed block that reflects projectiles back | Projectile Burst (harder to parry volleys) |
| **Ranged Blast** | Charge-based hitscan shots (3 charges) | Charge (closes distance fast) |
| **Burst Strike** | Energy-gated AoE slam around the player | AoE Slam (punishes close aggression) |
| **Barrier** | Absorbs the next hit, then shatters | Projectile Burst (punishes turtling) |

The boss has four selectable skills — **Charge**, **Projectile Burst**, **AoE Slam**,
and **Teleport Strike** — plus a reactive **Shield Phase** that fires when you attack,
weighted by how often you use ranged moves and how aggressively you pressure it.

Early in the fight the boss picks skills at random. As it accumulates data on your
style, the trained RL policy takes over and begins exploiting your patterns.

---

## Adaptive Boss — How It Works

A 9-float behavior stat vector is computed live from your actions:

- **Dodge direction bias** — do you always dash the same way?
- **Skill usage frequencies** — how often each of your five skills fires per minute
- **Aggression score** — blended from time spent in close range and normal attack rate
- **Average engagement range** — how far you keep from the boss on average
- **Positional bias** — do you hug the arena center or hug the walls?

This vector is the observation input for a PPO policy trained with Unity ML-Agents.
The policy maps it to one of four boss skills, selecting whichever is most likely to
counter your current playstyle. At runtime the ONNX model is loaded via Unity Sentis
and runs inference locally — no server required, fully WebGL compatible.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Engine | Unity 3D + URP (Universal Render Pipeline) |
| Camera | Cinemachine — fixed isometric rig |
| Input | Unity New Input System |
| RL Training | Unity ML-Agents Toolkit (PPO, headless Python) |
| Runtime Inference | ONNX + Unity Sentis |
| Deployment | WebGL |

---

## Project Structure

```
Assets/
  Scripts/
    Player/         PlayerController, PlayerSkills, PlayerBehaviorTracker
    Boss/           BossController, BossSkills, BossRLAgent
    Combat/         HealthSystem, HitboxManager, DamageHandler
    UI/             HUDController, CooldownUI, GameOverScreen
    ML/             BehaviorStatVector, SentisInferenceManager
    Skills/         ISkill, per-skill implementations
  Scenes/
    CyberArena.unity
  VFX/              Particle systems and post-processing profiles
  ML/
    Training/       Python training environment and ML-Agents config
    Models/         Exported .onnx files (not tracked in Git)
```

---

## Running Locally

**Requirements:** Unity 6 (tested on 6000.x), URP package, Cinemachine 3.x,
ML-Agents package, Unity Sentis (InferenceEngine) package.

1. Clone the repo and open the project in Unity.
2. Open `Assets/Scenes/CyberArena.unity`.
3. Run `CyberBoss/Setup Boss` and `CyberBoss/Setup Polish` from the menu bar
   to wire scene components (idempotent — safe to run multiple times).
4. Press Play.

The boss ships with a pre-trained `BossBrain.onnx` model. If you want to retrain:

```bash
cd Assets/ML/Training
pip install mlagents torch numpy
python cyberboss_env.py
```

Export the resulting model to `Assets/ML/Models/BossBrain.onnx`, then run
`CyberBoss/Setup RL Boss` in the Unity Editor to wire it in.

---

## WebGL Build

1. Switch platform to WebGL in Build Settings.
2. Set **Compression Format** to Brotli.
3. Build — the output folder can be deployed to any static host (GitHub Pages, itch.io).

No compute shaders or async GPU readback are used. The Sentis inference pass runs
on the CPU to stay within WebGL constraints.

---

## License

MIT — see `LICENSE` for details.
