# Handoff — 2026-08-06

Supersedes the 2026-08-03 handoff. What is verified, what is not, and what is next.

## Where things stand

**On `refactor-test`, committed but NOT pushed:**

- `700c94a` — line follower vectorisation, 17 Hz -> 31 Hz
- `474fd54` — app camera fixes (signage view, direct MJPEG, Android cleartext)

**Uncommitted on `refactor-test`:** the entire MAUI → WPF rewrite of the companion app, plus
the AI-model upload feature (section 5 below). This is a large working tree; `git status`
before assuming anything about it. Note that another tool (Antigravity) is sometimes driven
against the same working tree, so re-read a file before editing rather than trusting a
remembered version of it.

**Already on `origin` (`eemrull/RISA-bot`) from 07-31:** `e9d2d6f`, `92b1da4`.
Nothing has ever been pushed to the `company` remote — keep it that way.

Robot `risabot10` was at `192.168.68.67` on 08-03. DHCP, so re-resolve each session
(`ping -4 -n 1 risabot10.local` from PowerShell; `.local` does not resolve in Git Bash).

## 1. Camera — DONE, verified on hardware

MJPEG encoding lives in the `ros2go2rtc_bridge` node.
Chain: ROS topic -> bridge `:1985` -> go2rtc `:1984` -> browser/app.

Verified 08-03: all four services listening (`uvicorn`:8000, `dashboard`:8080,
`go2rtc`:1984, `ros2go2rtc_bridge`:1985), raw view delivering **29.3 fps** end to end
against a 30.1 Hz source, go2rtc serving real JPEGs.

## 2. Provisioning portal — server side VERIFIED, client side still open

All four checks from the previous handoff now pass, confirmed live on the robot:

| check | result |
| --- | --- |
| `/api/robot_info` | real JSON |
| `GET /` | 404 |
| `/generate_204` | 404, no redirect |
| `iptables -t nat -L PREROUTING` | empty |

`dnsmasq.conf` on the robot has no `address=/#/` wildcard and forwards to 8.8.8.8 /
1.1.1.1, so the DNS hijack is genuinely gone. The four units are named
`risabot-wifi-portal`, `risabot-hostapd`, `risabot-dnsmasq`, `risabot-ap-interface`
(not the names the old handoff implied) and all are active.

**Still unconfirmed — needs a physical client.** Join `RISABot-10` from a laptop and
check: no Windows "sign in to network" banner, and `nslookup github.com` returns a real
address rather than `192.168.4.1`.

## 3. Line follower performance — DONE, deployed and committed

`_find_all_white_regions` scanned the binary row one pixel at a time in Python. At
8 scanlines x 320 px that cost ~27 ms per frame and alone held the node under the
camera's 30 Hz. Now a vectorised run-length scan over `np.diff`.

Benchmarked on the robot before applying: 19.5x faster, identical output across 1616
cases including all-white, all-black and border-touching runs. Measured after deploy:

| | before | after |
| --- | --- | --- |
| `/lane_error` | 17.05 Hz | **31.2 Hz** |
| `/camera/debug/line_follower` | 18.9 Hz | **29.4 Hz** |
| delivered via go2rtc | — | **29.8 fps** |
| `line_follower_camera` CPU | ~106% | ~83% |

The lane controller runs at twice the rate for less CPU. Filtering is unaffected — the
Kalman path uses measured `dt`, and `auto_driver`'s PID normalises integral, derivative
and slew by `dt` on its own 50 Hz timer.

**Only ever measured with the robot parked. Needs a real track test before competition.**

The anchor tracker in `_detect_scanlines` is still per-frame, so `max_shift` now permits
~465 px/s of lateral travel rather than ~255 px/s. Deliberate, but it is the first knob
to revisit if the anchor wanders on noisy frames.

## 4. App camera path — Windows verified (Android parts now moot, see section 5)

Three defects fixed in `474fd54`:

- **Sign button sent `view=sign`**, not a valid name. Failed twice: the bridge rejects it,
  and `dashboard.py`'s `auto_toggle_debug` matched no branch so it cleared `show_debug`
  on all three perception nodes. That is why the view was blank rather than stale.
  `dashboard.py` returns 200 regardless, so the app reported success throughout.
- **Stream now hits `/api/stream.mjpeg`** instead of go2rtc's `stream.html` player page.
  Wrapped in a minimal document rather than navigated to directly — Android's WebView
  will not reliably render `multipart/x-mixed-replace` as a top-level navigation.
- **`AndroidManifest.xml` had no `usesCleartextTraffic`.** The robot serves plain HTTP
  only, so on Android 9+ *every* API call would have failed, not just the camera.

## 5. App rewritten to WPF, and AI-model upload — verified against a stub, NOT on hardware

The companion app is now WPF (`net10.0-windows`, single target). The MAUI multi-target build
and the Android head are retired, which makes the previous handoff's "build the Android
target" item obsolete rather than pending. Build with `dotnet build RisaBotApp.csproj` and
**no `-f` flag** — the old framework monikers now fail with NETSDK1005.

Added since 08-03: a Data Logger route of its own, a parameters drawer that live-tunes over
`/api/set_param`, and AI-model management on the Settings page backed by three new endpoints
on port 8000 (`GET /api/model/info`, `POST /api/model/upload`, `POST /api/model/rollback`).

**How it was verified without a robot.** `app.py` was imported with `fastapi` stubbed out and
its real handlers driven from a stdlib HTTP server, then the actual WPF UI was driven through
UI Automation against it. So the bytes on the wire, the validation, the atomic swap and the
error bodies are production code; only the ASGI layer was substituted.

Confirmed that way: a 7,865,930-byte model arrives byte-identical (sha256 matches source to
disk), the displaced model is kept as `.bak` at its original mtime, rollback is exactly its
own inverse on disk, and a mistakenly-uploaded zip is rejected with both files left untouched
and the server's actionable message surfaced verbatim in the UI. 52/52 handler assertions pass.

Three real client bugs were found and fixed by doing this: the upload's own success message
was being erased ~1 s later by the verification refresh; `ModelInfoTimeoutMs = 20 s` was dead
because the request went through the 10 s-capped shared `HttpClient`; and "Restart stack"
reported a false timeout at 10 s on an endpoint that can legitimately take 40 s, which would
have led an operator to restart the stack twice.

**Nothing here has touched the robot.** Specifically unverified: that `dnn.load()` accepts an
uploaded model at all, that the `chown` to `sunrise` is correct in practice, and that an
operator-trained model's class order lines up (see the trap below).

## Known defects, not fixed

- `RefreshTelemetryAsync` (`DashboardViewModel.cs:134`) mutates observable properties off
  the UI thread with no `MainThread` marshalling. Harmless on Windows — and now Windows is
  the only target, so this is downgraded from hazard to untidiness.
  `RobotConnectionService.cs:156` shows the correct pattern.
- **Four `ApiClient` timeouts are silently capped at 10 s** by the shared `HttpClient`'s
  `Timeout`, which no per-request `CancelAfter` can raise: scan (30 s), connect (60 s),
  launch start/stop (90 s) and save-defaults (30 s). The fix is to route them through the
  uncapped `UploadHttp` client, as model upload and `restart_ros` already do. Launch
  start/stop is the one most likely to bite — it is the slowest and it is on the happy path.
- `signage_detector` produced a false positive on desk clutter during testing. Model
  quality, not plumbing — worth checking against real signs.
- **Class order is unvalidated and unvalidatable.** `signage_detector.py` indexes classes
  positionally, so a model whose class list is ordered differently loads happily and acts on
  the wrong signs. The upload UI says so in prose because nothing else can catch it.
- The Android head is gone rather than fixed. The `usesCleartextTraffic` and WebView
  reasoning in section 4 is now moot.
- `tools/bpu_model/compile_model.bat` still does not run `patch_onnx_resize.py` before
  `hb_mapper`. Offered as a fix, never confirmed.

## Gotchas

- **`setup_autostart.sh` enables *and starts* `risabot.service`.** Running `ros2 launch`
  by hand on top of it collides on ports 8080 and 1985. `sudo systemctl stop risabot` first.
- **Deploying a Python node needs `colcon build --packages-select risabot_automode`**, not
  just an scp — the installed copy under `install/` is what runs.
- Restarting `risabot.service` can drop the SSH session briefly. It comes back.
- `ros2 param get` intermittently fails with `!rclpy.ok()`. Retry, or read
  `config/params.yaml` directly.
- `obstacle_avoidance` WARN spam drowns the logs while parked. Filter before grepping.
- SSH key `claude-code-dev@risabot` is in the robot's `authorized_keys`. Revoke with
  `sed -i '/claude-code-dev@risabot/d' ~/.ssh/authorized_keys`.

## Next

1. **Track test the line follower** at 31 Hz — the one thing standing between this and
   trusting it in competition. Unchanged and still the highest-value item.
2. **Get the model endpoints onto a robot and prove the loop closes.** Deploy `app.py` to
   `/etc/risabot/portal/backend` (it is not run from the repo — see CLAUDE.md), upload a
   `.bin` from the app, restart the stack, and confirm `signage_detector` actually loads it.
   Everything in section 5 is stub-verified only.
3. **Push `700c94a` and `474fd54`** to `origin`, and decide what to do with the large
   uncommitted WPF rewrite sitting on top of them.
4. **Fix the four capped timeouts** listed under known defects.
5. **Retiring the ROS web dashboard** is now much closer than the 07-31 note implied. The app
   covers LiDAR, parameter tuning, the data logger and IMU calibration. What is left to port
   is record/playback (`/api/record_playback`, `/teach`) and the competition reset
   (`/api/reset_competition`) — for both, `ApiClient` can already call the endpoint and no
   view-model does, so this is UI work rather than plumbing.

Deferred by an explicit decision, not forgotten: **Phase 2 of model management** — driving the
`hb_mapper` Docker conversion from the app, with `risabot_bpu_config.yaml` exposed as a form
and the conversion log streamed live, feeding its output into the Phase 1 uploader. Phase 1
(upload an already-compiled `.bin`) is what exists today.
