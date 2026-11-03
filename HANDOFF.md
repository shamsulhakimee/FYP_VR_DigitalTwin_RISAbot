# Handoff — 2026-11-03

Supersedes the 2026-08-06 handoff. Covers what is known to work, what the next session should
do first, and what is still open.

## Where things stand

**This repo is new.** It is `shamsulhakimee/FYP_VR_DigitalTwin_RISAbot` (public, only remote
`origin`), and it began as a copy of the old `eemrull/RISA-bot`. Development happens on `dev`.
The old repo's branches (`main`/`test`/`refactor-test`) and `company` remotes no longer apply.
`main` and `dev` both point at `76d197c`.

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

**Uncommitted on the PC (`dev`):** the doc corrections made this session (CLAUDE.md,
ARCHITECTURE.md, README.md, RISA_Bot_System_Context.md, this file), plus the user's own
`.gitignore` addition (`FYP1 Data/`).

## Task 1 (do this first): point the robot at this repo

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

## Next, after task 1

1. Commit (and push, when the user says so) this session's doc changes on `dev`.
2. Ask the user which item to take on: the line-follower track test, proving model upload on
   hardware, or new development.

Deferred by explicit decision: **Phase 2 of model management**, driving the `hb_mapper`
Docker conversion from the app. Phase 1 (upload an already-compiled `.bin`) is what exists.
