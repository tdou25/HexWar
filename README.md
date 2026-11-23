# HexWar – stripped-down HOI4-style combat RTS + RL environment

## Run it in Unity (2022.3 LTS or Unity 6)
1. Create any new Unity project (2D or 3D) and copy `Assets/Scripts` into it.
2. Open any scene (an empty one is fine) and press Play. `GameBootstrap` creates itself, builds the camera, map, and HUD.
   You are Grey vs. a scripted Red bot. Optionally add a `GameBootstrap` component to pick another `Mode`.

Controls: click a division → click an adjacent tile (move/attack). Right-click/X cancels. Space pauses, 1-5 speed, R restarts.
One order per game hour (the last one you queue before the hour ticks is issued).

## Train an agent
1. Package Manager → install **ML Agents** (`com.unity.ml-agents`; `Game.Agents` only compiles when it's present).
   `pip install mlagents` with the matching release.
2. `mlagents-learn config/strategy_selfplay.yaml --run-id=hex1` then press Play (empty scene works), or build and pass
   `--env=Build/HexWar --num-envs=8 --time-scale=20 --no-graphics`.
   Warm-up against the scripted bot: run with `-mode TrainVsScripted` and `config/strategy_vs_scripted.yaml`.
3. Drop the resulting `.onnx` into `GameBootstrap.PolicyModel` and use `HumanVsAgent` / `AgentVsScripted`.

## Fast headless check (no Unity needed)
`cd Tools/Headless && dotnet run -c Release -- 300` runs self-tests (hex symmetry, map symmetry, observation
symmetry) and prints balance stats (~150k steps/s). `dotnet run -c Release -- 1 log` prints the event log of one game.

## Rules (all constants in `Assets/Scripts/Core/Config.cs`)
* 10x10 flat-top hex map, odd columns shoved down: N, NE, SE, S, SW, NW. Grey = columns 0-4, Red = columns 5-9. 180° rotation-symmetric.
* 5 victory points/side: command center at the back (Grey (0,4), Red (9,5)) + 4 outposts. Win = hold all 4 enemy outposts, or 2 outposts + the enemy command center
  (or eliminate the enemy). Day 60 (1440 hours) = draw. Defenders on their own VP tile get +25% defense, +10% attack.
* 14 divisions per side (10 infantry, 4 tanks), max 3 per tile (same team only). 1 step = 1 hour.
* Each division: **supply** (true health; never refills; drained by damage taken and by combat, faster when attacking; 0 = destroyed)
  and **organization** (cohesion; 0 = broken; refills slowly out of combat). Broken defenders retreat to a friendly tile; broken attackers disengage.
  A broken division with every neighbouring tile enemy-owned is destroyed (encirclement).
* Movement and battles happen on tile *edges*: an order into an enemy-held tile starts an edge battle (attackers vs. every division in that tile);
  an order into an empty tile is a slow crossing (infantry 36 h/tile, tanks 18 h/tile). The tile flips owner when you arrive. Frontline = owner boundary.
* HOI-style pinning: a division being attacked can still attack a *different* neighbouring tile, but it can't counter-attack the tile that is attacking it,
  and it can't complete a move (including advancing after a win) while its own tile is under attack: progress stalls at 95% until the pressure stops.
* Damage per firing division: `blend(soft, hard by target hardness) × (0.25 + 0.75·BT/(BT+DEF)) × supply factor × roll`.

## RL interface
* Observation (1635 floats, always from the acting team's view; Red's view is rotated 180° so one policy plays both sides):
  board `10×10×(3+8)` (3 division-id slots + owner, outpost, capital, attacked-by-enemy, attacked-by-me, my/enemy count, frontline),
  own `14×19`, enemy `14×19` division matrices, 3 globals. `GameSim.FillBoard/FillDivisionMatrix` expose the tensors directly.
* Action: Discrete(99) = division(14) × command(7: cancel, 6 directions) + no-op, invalid actions masked.
* Reward (zero-sum): battle won ±1, division destroyed ±25, outpost ±100, command center ±200, victory ±1000; agents multiply by `RewardScale=0.01`.

## Python training without Unity (`python/`)
`Tools/Server` (C#, net6.0) hosts N games running the *same* `GameSim` and talks to Python over a binary pipe, so there is one rule set for Unity and training.
```
conda activate mlagents          # needs torch + numpy only
cd python
python train.py --run mine --envs 32 --total-steps 20000000     # PPO + opponent mixing (self / past snapshots / randomised rusher)
python plot.py runs/mine                                        # redraw runs/mine/training_curve.png from the CSVs
python evaluate.py runs/mine/latest.pt --games 200              # win/draw/loss vs the default rusher, both seats
```
Training also writes `runs/<name>/training_curve.png` (episode return and win-rate vs timesteps, refreshed every 50 iterations), plus `episodes.csv` and `metrics.csv`.
`hexwar.HexWarVec` is a plain vectorised env (obs [N,2,1635], mask [N,2,99], action -1 = scripted bot) if you want to plug in another algorithm.
The server rebuilds automatically on first use (`dotnet build -c Release` in `Tools/Server`). Checkpoints are PyTorch `.pt` files; they are not ONNX/ML-Agents compatible.

## Watch a trained network play in Unity
```
cd python && python export_policy.py runs/mine/latest.pt      # writes Battle of Hexagrad/Assets/StreamingAssets/policy.bytes
```
In Unity: add an empty GameObject with the `GameBootstrap` component, set **Mode** to `PolicyVsScripted` (Grey = network, Red = scripted bot; spectator HUD: Space pause, 1-5 speed, R restart)
or `PolicyVsHuman` (Grey = network, you are Red). `PolicyFile` can point to another file (absolute, or relative to StreamingAssets).
The network runs in plain C# (`Core/MlpPolicy.cs`), greedy over valid actions; no ML-Agents/ONNX needed. Re-export after every new checkpoint.
