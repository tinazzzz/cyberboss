# CyberBoss

A 3D isometric cyberpunk boss fight where the boss learns how you play.

Built with Unity 3D (URP) and Unity ML-Agents, CyberBoss pits you against a single
boss in a neon-lit arena. As the fight progresses the boss tracks your behavior (how aggressively you engage, which skills you lean on, where you position yourself)
and feeds that live stat vector into a trained reinforcement-learning policy. The
longer you fight, the better it reads you.

**WebGL playable in the browser**: https://tnazzzz.itch.io/cyberboss

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

---

## How the Adaptive Boss Works

While you fight, `PlayerBehaviorTracker` computes a live 9-value stat vector —
dodge direction bias, per-skill usage frequency (dash/parry/ranged/burst/barrier),
aggression, average engagement range, and positional bias — all normalized 0–1
and updated continuously, no reset between engagements.

That vector is the observation input to a policy trained with Unity ML-Agents
(PPO) in a headless Python simulation against scripted player archetypes
(aggressive, ranged, turtling, etc.). The trained policy picks among four boss
skills — Charge, Projectile Burst, AoE Slam, Teleport Strike — each mapped to
counter a distinct playstyle signal in the vector.

The trained network is exported to ONNX and runs locally in-browser via Unity
Sentis — no server, no retraining at runtime. The "learning" is per-session
adaptation: the boss reads your evolving stat vector each decision tick and
leans harder into whichever counter your playstyle is currently exposing.

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

## Deployment

### Build

Open the project in Unity, then use the menu bar:

**CyberBoss → Build WebGL**

This applies all WebGL Player Settings (Brotli compression, decompression fallback,
.NET Standard, data caching) and builds to `WebGL-Build/` at the project root.

Alternatively: **File → Build Settings**, switch platform to WebGL, click **Build**,
and point it at the `WebGL-Build/` folder manually.

The `WebGL-Build/` folder is excluded from Git — never commit it.

### Deploy to itch.io

1. Install [butler](https://itch.io/docs/butler/) (itch.io's CLI upload tool).
2. Log in once with `butler login` (opens browser for OAuth).
3. From the project root, run the deploy script:

**Windows:**
```bat
deploy-itch.bat
```

**Mac / Linux:**
```bash
chmod +x deploy-itch.sh
./deploy-itch.sh
```

Both scripts push the `WebGL-Build/` folder to the `html5` channel on itch.io:

```
butler push WebGL-Build tinazzzz/cyberboss:html5
```

Run this command again for every subsequent update — butler uploads only the diff.

### Live URL

https://tinazzzz.itch.io/cyberboss (live once published on itch.io)

---
