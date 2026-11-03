# Handoff — 2026-11-03

Supersedes the 2026-08-06 handoff. Covers what is known to work, what the next session should
do first, and what is still open.

## Where things stand

**This repo is new.** It is `shamsulhakimee/FYP_VR_DigitalTwin_RISAbot` (public, only remote
`origin`), and it began as a copy of the old `eemrull/RISA-bot`. Development happens on `dev`.
The old repo's branches (`main`/`test`/`refactor-test`) and `company` remotes no longer apply.
`dev` is at `4b705fa` (pushed; the doc corrections below). `main` is still at `76d197c`.

**The robot and the app work together. This is the baseline to protect.** The user confirmed
this on 2026-11-03 against the current version:

- The laptop joins the robot's `RISABot` access point, and the installed **RISA-Studio
  v2.0.0** discovers the robot and connects.
- From the app, the robot can be switched onto another internet WiFi. The laptop then joins
  the same network and reconnects.
- The stack (`risabot.service` → `bringup.launch.py`) can be started from the app.

The robot is headless, so the app is how it is operated. **Don't change anything in the
connection path** without asking first. That means the portal (`app.py` and the
hostapd/dnsmasq units), `ApiClient.cs`, discovery/pairing, WiFi setup and launch start/stop.

**The robot does not pull from this repo yet.** Its checkout at
`~/risabotcar_ws/src/RISA-bot` was cloned from the old repository. Fixing that is task 1.

**The project has moved into its FYP phase.** RISA-Bot is now the platform for the user's final
year project: *Design and Development of an Interactive VR Digital Twin for Autonomous Mobile
Robot Navigation based on ROS 2*. All planning lives in `FYP1 Data/Planning/`; see "Session log"
below.

## Session log — 2026-11-03

### 1. Repository and documentation (committed and pushed: `4b705fa`)

- **Docs corrected against the code:**
  - `ARCHITECTURE.md` state table rewritten from `auto_driver.py`. E-stop comes before the
    traffic light; `FINISHED` is disabled (`if False:`); `PARALLEL_PARK` /
    `PERPENDICULAR_PARK` are never selected; `LANE_RECOVERY` stops in place.
  - `CLAUDE.md`: `setup_autostart.sh` *enables and starts* `risabot.service`.
  - `README.md`: WPF, not MAUI; Quick Start points to the installer; clones this repo.
  - `RISA_Bot_System_Context.md`: priority list, the paramiko scripts instead of a non-existent
    `fleet.py`, boom gate = LiDAR + camera, tunnel = RANSAC.
- **`CLAUDE.md` now describes this repo only:** new "Repository and branches" section, the
  baseline-protection rule, and the warning about the robot's checkout pointing at the old
  repo.
- **This handoff was rewritten**, with the robot-repo procedure as Task 1 below.
- **The user runs the installed RISA-Studio v2.0.0.** A local `dotnet build` does not replace
  it.

### 2. FYP planning: everything is in `FYP1 Data/Planning/`

**`FYP1 Data/` is gitignored** (the user's choice), so none of the planning is in git. It
exists only on this PC, synced by OneDrive. Do not assume a clone has it.

| File | What it is |
| --- | --- |
| `RISA-Bot_Digital_Twin_Plan_v2.md` | **The written project plan, v2.1.** Phases 0–6, architecture, targets, decisions; cited against the citation Excel |
| `RISA-Bot_Digital_Twin_Plan_v2.ipynb` | Visual version: diagrams (twin levels, phase gate, architecture, goal sequence, bicycle model), illustrative result plots, live citation cards |
| `FYP1_Progress.ipynb` | FYP 1 tracker: Gantt chart, weekly milestones, Ch 1/Ch 2 guides with worked examples, measurement guide, supervisor log |
| `FYP1_Gantt.xlsx` | **The schedule's single source of truth.** Day-by-day Gantt; bars coloured by status. The progress notebook reads its tasks from here |
| `scripts/` | Generators and helpers: `build_notebooks.py`, `run_nb.py`, `fyp_citations.py`, `fyp_figures.py`, `fyp_schedule.py`, `make_gantt_xlsx.py`, `verify_gantt.ps1` |

The original `AMR Digital Twin + VR Research Plan.docx` is kept for reference. The old
week-1–5 planning notebook was deleted at the user's request.

**Project decisions (plan v2.1, all confirmed by the user):**

| # | Decision |
| --- | --- |
| D2 | **Robot is the master, as today.** Driving runs on the robot. Gazebo, the Twin Manager and the VR bridge run on the laptop; the VR app runs standalone on a **Meta Quest 2**. **Nav2 is added on the robot**; the twin sends goals, never velocities |
| D3 | Phase 4 (predictive twin) = minimum version: one Gazebo trial per goal before it is sent. Continuous re-prediction is a stretch goal |
| D4 | Phase 6 (VLM / scene understanding) = future work |
| D5 | **Gazebo Harmonic**, with ROS 2 Humble via the OSRF `ros-humble-ros-gzharmonic` packages (not Humble's default pairing) |
| D8 | **True twin**: automatic data flow both ways. The robot gets three new, *default-off* parts in FYP 2: Nav2, a separate `twin.launch.py`, and a `TWIN_NAV` state in `auto_driver` that passes Nav2 output through the existing `cmd_safety_controller`. The competition `bringup.launch.py` and the app path stay untouched |

Facts the plan rests on, verified in the code this session:
- There is no Nav2 and no URDF in the repo.
- The drive is a car-like bicycle model (`wheel_base 0.14`, `steering_max_deg 50`).
- `cmd_safety_controller` has `cmd_timeout 0.35` and `max_linear_speed 0.30`.
- No `RMW_IMPLEMENTATION` or `ROS_DOMAIN_ID` is set anywhere, so the robot runs Fast DDS on
  domain 0.

**FYP 1 calendar (from the user):**
- Week 6 = 2–8 Nov; week 7 = 9–13 Nov.
- **Mid-semester break Sat 14 – Sun 22 Nov.** The user is totally unavailable, and the break has
  no week number. Week 8 starts 23 Nov.
- Deadlines, all at 17:00:
  - **Fri 27 Nov:** Ch 1 + Ch 2 + Progress Report 2.
  - **Thu 24 Dec:** Ch 3 + Progress Report 3.
  - **Wed 6 Jan 2027:** Final Report.
  - The presentation date is still TBC.
- FYP 1 technical tasks are **read-only on the robot**. Robot changes start in FYP 2.

**How to work with these files:**
- **Schedule changes:** edit cells in the existing `FYP1_Gantt.xlsx` with openpyxl (only the
  blue-on-yellow input cells). For styling changes, edit `scripts/make_gantt_xlsx.py` and run it
  with `--restyle`, which keeps every value the user entered. **Never use `--force`**: it wipes
  the user's statuses.
- **After any workbook change:** run `scripts/verify_gantt.ps1 -Png <file>`. It drives Excel
  over COM: recalculates, reports formula errors, caches values and exports a picture. Look at
  the picture before reporting back.
- **Notebooks:** they are generated, so change `scripts/build_notebooks.py` (or the helpers),
  rebuild, then run `scripts/run_nb.py <notebook>`. The supervisor-log and reflection tables in
  `FYP1_Progress.ipynb` are typed in by the user, so ask before rebuilding over them.
- **Python environment:** everything uses the `fyp` conda env. `openpyxl` was installed into
  it this session. Write `.xlsx` files only while the user has them closed in Excel (it locks
  them).
- **User preferences for the workbook:**
  - plain Calibri, no italics, bold only for the title;
  - status values exactly `Done` / `In progress` / `Not started yet`;
  - bars coloured by status (green / amber / grey; the darker part = progress).
- **Citations:** cite `[C#]` = the "Reading order" row in
  `FYP1 Data/Citation Database/AMR_Digital_Twin_Citations.xlsx`, and write out titles. The user
  adds rows over time; `fyp_citations.check()` re-finds moved rows by title. Most rows were read
  at abstract depth only, so check numbers before they go into the thesis.

## Task 1: point the robot at this repo (first robot task; do it before FYP week 9)

Goal: the robot's checkout tracks `origin/dev` of this repo, so changes made on the PC reach
the robot with `git pull` + rebuild, **without changing the version that works today.**

The guiding rule: **the code running on the robot right now is the truth.** If it differs from
`dev`, bring the robot's version into `dev`, don't the other way round, unless the user
decides otherwise. Stop and ask at any step whose result is unexpected.

### 0. Reach the robot

DHCP, so re-resolve every session: `ping -4 -n 1 risabot10.local` from PowerShell (`.local`
does not resolve in Git Bash). It was `192.168.68.67` in August. Then `ssh sunrise@<ip>`.
The robot may also be on the network the app last switched it to, not the AP.

### 1. Inspect only, change nothing

```bash
cd ~/risabotcar_ws/src/RISA-bot        # if missing: find ~/risabotcar_ws -maxdepth 3 -name .git
git remote -v
git branch -vv
git log --oneline -5
git status --short
git stash list
```

Record the HEAD hash, the branch, and whether the tree is dirty. **A dirty tree or a branch
other than the old `refactor-test` is important:** it may be robot-only fixes that make the
current version work.

### 2. Back up before touching anything

```bash
cd ~/risabotcar_ws/src
tar czf ~/RISA-bot-backup-$(date +%F).tgz RISA-bot
cd RISA-bot && git diff > ~/robot-local-changes-$(date +%F).patch   # empty file if clean
sudo cp /etc/risabot/portal/backend/app.py ~/portal-app.py.bak-$(date +%F)
```

### 3. Add this repo as a remote (the working tree is unaffected)

```bash
git remote rename origin upstream-old
git remote add origin https://github.com/shamsulhakimee/FYP_VR_DigitalTwin_RISAbot.git
git fetch origin
```

The repo is public, so fetching over HTTPS needs no credentials. Pushing from the robot would,
but isn't needed: edit on the PC and push from there.

### 4. Compare before switching

```bash
git merge-base --is-ancestor HEAD origin/dev && echo "HEAD is in dev's history"
git log --oneline HEAD..origin/dev          # what dev has that the robot doesn't
git log --oneline origin/dev..HEAD          # what the robot has that dev doesn't (should be empty)
git diff --stat HEAD origin/dev -- src tools
sudo diff /etc/risabot/portal/backend/app.py tools/wifi_provisioning/backend/app.py
```

What to expect: the most recent robot-side commit in `dev` is `296f05a` (2026-08-21,
dashboard redesign: `dashboard.py`, `dashboard_templates.py`, and the portal's `app.py`).
`76d197c` touches no `src/` or `tools/` files. So:

| Result | Action |
| --- | --- |
| HEAD == `origin/dev`, tree clean | Step 5 only changes tracking. No rebuild needed |
| HEAD is an ancestor, tree clean, `src`/`tools` diff empty | Safe to switch; no rebuild needed |
| HEAD is an ancestor, diff touches `src`/`tools` (e.g. HEAD before `296f05a`) | **Ask the user.** Switching changes running code (the dashboard at minimum). Either accept and test, or keep the robot at its commit |
| Robot has commits or local changes not in `dev` | **Stop.** Copy them to the PC (the patch from step 2, or `git format-patch origin/dev..HEAD`), commit them to `dev`, push, then fetch and recompare |

**The portal is a separate copy.** `risabot-wifi-portal.service` runs
`/etc/risabot/portal/backend/app.py`, not the repo file, and no git operation changes it.
If the `diff` above shows differences, **leave the deployed copy alone.** It is the lifeline,
and it works. Instead, record which version is deployed. If the deployed copy is the newer
one, bring it into `dev`.

### 5. Switch (only once step 4 says it's safe)

```bash
test -z "$(git status --porcelain)" \
  && git checkout -B dev origin/dev \
  || echo "DIRTY tree - not switched, see step 4"
# The check matters: git would otherwise silently carry local changes onto dev.
git branch -vv                     # confirm: dev tracking origin/dev
```

Keep the directory name `RISA-bot` and the workspace at `~/risabotcar_ws`.
`risabot-launch.sh` sources `/home/sunrise/risabotcar_ws/install/setup.bash`; the subdirectory
name doesn't matter to it, but moving the workspace would.

Note `--symlink-install`: Python sources are symlinked into `install/`, so a checkout that
changes a node's `.py` file takes effect at the **next stack restart** even without a rebuild.
New or renamed files and entry points still need `colcon build`.

### 6. Rebuild (only if `src/` changed) and verify through the app

```bash
cd ~/risabotcar_ws && colcon build --packages-select <changed packages> && source install/setup.bash
```

Then restart the stack from the app and repeat the user's full check: discover → connect →
camera → start stack → WiFi switch → reconnect. Only call task 1 done when that loop passes.

### Rollback

`git checkout <HEAD hash from step 1>`, rebuild the same packages, and restart. Failing that,
restore `~/RISA-bot-backup-<date>.tgz`. `git remote` changes are harmless and can stay.

## Carried over from 2026-08-06: status unknown since then

None of these has been reported on since the August handoff. Ask before assuming either way.

- **Line follower at 31 Hz has never been track-tested.** It was measured with the robot
  parked only. If the anchor wanders on noisy frames, revisit `max_shift` in
  `_detect_scanlines` first.
- **AI-model upload/rollback was verified against a stub, never on hardware.** Unverified:
  that `dnn.load()` accepts an uploaded model, that the `chown` to `sunrise` works, and the
  class order (below).
- **Provisioning client side:** joining the AP from a laptop should show no "sign in to
  network" banner, and `nslookup github.com` should return a real address. The user's
  2026-11-03 report suggests the AP flow works, but these two specific checks weren't stated.

## Known defects, not fixed

- **Four `ApiClient` timeouts are silently capped at 10 s** (scan 30 s, connect 60 s, launch
  start/stop 90 s, save-defaults 30 s) by the shared `HttpClient`. This is in the connection
  path, so leave it unless the user asks. The fix is to route them through `UploadHttp`.
- **Class order is unvalidated and unvalidatable.** `signage_detector.py` indexes classes by
  position, so a model with a different class order loads fine and acts on the wrong signs.
- `signage_detector` produced a false positive on desk clutter. That's model quality, not
  plumbing.
- `tools/bpu_model/compile_model.bat` doesn't run `patch_onnx_resize.py` before `hb_mapper`.
- `Launch-RisaBotApp.bat` falls back to `dotnet publish -f net10.0-windows10.0.19041.0` when
  `dist/` is missing. That framework no longer exists (NETSDK1005), so it fails on a fresh
  clone. It doesn't matter in practice: the user runs the installed app.
- `RefreshTelemetryAsync` (`DashboardViewModel.cs`) mutates observable properties off the UI
  thread. Harmless on Windows.

## Gotchas

- **`setup_autostart.sh` enables *and starts* `risabot.service`.** Manual `ros2 launch` on
  top of it collides on ports 8080 and 1985: `sudo systemctl stop risabot` first.
- Deploying a new or renamed Python node needs `colcon build --packages-select <pkg>`, not
  just an scp.
- Restarting `risabot.service` can briefly drop the SSH session. It comes back.
- `ros2 param get` intermittently fails with `!rclpy.ok()`. Read `config/params.yaml` instead.
- `obstacle_avoidance` floods the logs with WARNs while parked. Filter before grepping.
- SSH key `claude-code-dev@risabot` may still be in the robot's `authorized_keys`. Revoke with
  `sed -i '/claude-code-dev@risabot/d' ~/.ssh/authorized_keys`.

## Next

**FYP 1 comes first. The writing deadline is Fri 27 Nov, 17:00.** This week (week 6), per
`FYP1_Gantt.xlsx`:
- reading R1, then start R2;
- Ch 1 first draft;
- Ch 2 skeleton;
- T1 robot audit (a desk task);
- supervisor meeting to agree plan v2.1 and the three objectives, and to ask what Progress
  Reports 2 and 3 must contain.

Chapters 1 and 2 should be essentially done by Fri 13 Nov, because the user is away for the
break.

Then, in order:
1. **Task 1 (robot repo) before week 9.** The FYP 1 baseline measurements (weeks 9–10) only
   *read* topics and don't need it. FYP 2 robot changes (Nav2, `twin.launch.py`, `TWIN_NAV`)
   do, so the robot must be pulling from this repo by then.
2. **Weeks 9–13 technical track** (see the workbook):
   - Ubuntu 22.04 + Humble + Gazebo Harmonic on the external SSD (the user has the flash drive
     now);
   - baseline measurements;
   - laptop ↔ robot latency;
   - odometry and map accuracy;
   - measured xacro;
   - first Gazebo open-loop replay.
3. **Open questions to settle with the user / supervisor:**
   - the presentation date;
   - whether week 13 (28 Dec – 3 Jan) is a working week, since results work is scheduled there;
   - the exact contents of Progress Reports 2 and 3.
4. **Earlier robot items** (line-follower track test, model upload on hardware) wait unless the
   user brings them up. Competition behaviour is not the FYP's focus.

Deferred by explicit decision: **Phase 2 of model management**, driving the `hb_mapper`
Docker conversion from the app. Phase 1 (upload an already-compiled `.bin`) is what exists.
