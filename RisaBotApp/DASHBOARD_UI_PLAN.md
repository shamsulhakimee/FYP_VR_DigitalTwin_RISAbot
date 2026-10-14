# Dashboard UI — port plan

Working document for the WPF companion app. Not repo documentation; do not push to `main`.

Source of truth for the target look:
`src/risabot_automode/risabot_automode/dashboard_templates.py` (`DASHBOARD_HTML`, line 6)
and the data it consumes from `dashboard.py` (`self.data`, line 331).

---

## 1. Where the two ends stand

The web dashboard is sixteen card regions laid out on a three-column grid
(`260px 1fr 260px`, 14px gap, capped at 1600px) plus two pop-out drawers. The WPF
`DashboardView` is six tiles in two columns (`* MinWidth=480 | 360`). The camera feed and
the view switcher are the only parts that already correspond one-to-one.

The gap is not only visual. `RosDashboardData.cs` maps 20 of the roughly 40 keys the
dashboard node publishes, and `ApiClient.cs` calls 4 of its 17 endpoints. Half the cards
below cannot be built at all until those two files are widened, so the work has to run
model → client → controls → layout, in that order.

### Card inventory

| Web card | Column | WPF today | Data it needs |
| --- | --- | --- | --- |
| State Machine | left | absent | `state`, `lap`, `auto_mode`, `state_time`, `state_dist`, `stop_reason` |
| Traffic Light | left | one badge inside Perception | `traffic_light` |
| IMU | left | tile (values only, no meters) | `imu_roll/pitch/yaw` + `/api/calibrate_imu` |
| Manual Control | left | absent | `speed_pct`, `ctrl_state_index`, `ctrl_state_name` |
| Record & Playback | left | absent | six `rp_*` keys + `/api/record_playback` |
| Lane Following | left | error strip under the camera | `lane_error`, `cmd_lin_x`, `cmd_ang_z` |
| Camera Feed | middle | present (`MjpegView` + 5 tabs) | `/api/set_cam_view` |
| LiDAR Top View | middle | absent | `/lidar_data` |
| Sensors (10 dot rows) | right | 4 badges inside Perception | `lidar/camera/fused_obstacle`, `boom_gate`, `tunnel_detected`, `obstruction_active`, `parking_*`, `health_ok`, `health_stale` |
| Odometry | right | tile | already mapped |
| Controller (13 buttons + 2 sticks) | right | absent | `buttons`, `axes` |
| Action Priority flow bar | full width | absent | derived from `state` |
| Subsystem Data Logger | full width | absent | five `/api/logger/*` endpoints |
| Event Log | full width | absent | client-side, derived from state transitions |
| Parameters drawer | drawer | **ported** (stage 4b) | `/api/get_param`, `/api/set_param`, `/api/save_defaults` |
| Controls drawer | drawer | absent | static reference text |

The app also has two cards the web dashboard does not: **Link** (ROS nodes / Wi-Fi SSID /
Internet) and the launch-control state machine in `DashboardViewModel`. Both stay — they
answer questions the browser never has to ask, because reaching the page at all proves the
robot is up.

---

## 2. Theme

The web dashboard is Catppuccin **Latte** — a light theme (`--bg:#eff1f5`,
`--text:#4c4f69`, `--accent:#1e66f5`) with three blurred orbs drifting on a 20s loop, an
animated gradient title, and cards that lift and scale on hover. The app is dark blue/ice
throughout Splash, Discovery and Network Setup.

**Recommendation: keep the dark palette; port the layout, the card inventory and the
information density, not the colours.** Repainting one screen light would make the app read
as two applications stitched together, and the animated orbs and hover-lift are browser
idioms that cost real frame time in WPF for no information gain. What is worth carrying over
verbatim is the *structure*: the 16px-radius surface cards, the uppercase 2.5px-tracked
muted card headings, the colour-per-state badge vocabulary, and the meter bars.

The one colour set that must survive the translation is semantic, because operators read it
at a glance: green `#40a02b` success, red `#d20f39` danger, amber `#df8e1d` warning, blue
`#1e66f5` accent. Those need dark-theme equivalents added to `Colors.xaml` — same hues,
lifted in luminance so they hold against the dark surface.

If the intent turns out to be a literal light-mode reproduction instead, say so before stage
4 starts; stages 1–3 are theme-neutral and are not wasted either way.

---

## 3. Stages

### Stage 1 — widen `RosDashboardData.cs`

Add the 20 missing keys. Nothing renders differently yet; this just stops every later stage
from being blocked on the model.

```
state_time, state_dist, stop_reason,
lidar_obstacle, camera_obstacle, fused_obstacle,
obstruction_active, parking_complete,
buttons (int[]), axes (double[]),
speed_pct, ctrl_state_index, ctrl_state_name,
health_stale, cmd_safety_estop, cmd_safety_timeout_count,
loop_stats,
rp_state, rp_buffer_size, rp_playback_index,
rp_recording_name, rp_active_parking, rp_saved_recordings
```

Keep the nullable pattern already used for `BoomGate`/`TunnelDetected` — the node omits keys
when the corresponding subsystem has not reported, and a nullable is what distinguishes
"false" from "no reading yet" in the sensor dots.

A second model file is needed for `/lidar_data`. The points are objects, not pairs —
`{points: [{x, y}, …], tunnel: bool}` (`dashboard.py:718`) — plus the optional `left_dist`,
`right_dist`, `dist_error`, `angular_z` and `centerline` that the handler adds together, and
only while tunnel debug is on. `centerline` is a list of the same `{x, y}` objects, capped at
ten points by `tunnel_wall_follower.py:325`.

### Stage 2 — widen `ApiClient.cs`

Add, in rough order of how much UI depends on them:

- `GET /lidar_data` — gates the LiDAR card, polled on its own 250 ms timer (the web page uses
  250 ms; the existing telemetry poll is 500 ms and should not be sped up to suit it)
- `POST /api/calibrate_imu` — one button on a card that already exists
- `POST /api/record_playback` — record / stop / play / save
- `GET /api/recording_data`
- `GET /api/get_param`, `POST /api/set_param`, `POST /api/save_defaults` — the parameters drawer
- `/api/logger/start|stop|status|list|download`

All of them derive their base URL the same way the existing four do, so this is mechanical.

### Stage 3 — new controls

These are the parts with no WPF equivalent, and they are the real work. Each goes in
`Controls/` as a standalone `UserControl` or `Control` so it can be dropped into the layout
independently.

**`LidarView`** — the largest single item. The web version (`drawLidar`,
`dashboard_templates.py:2440`) draws to a 320×320 canvas: range rings at 0.2/0.4/0.6 m with
labels, two tinted tunnel wedges (blue −90°…−30°, red +30°…+90°), a dashed forward line,
points coloured by distance (red under 0.3 m, amber under 0.5 m, green beyond), a cyan
dashed centerline polyline with dots, a blue triangle for the robot, FRONT/L/R labels, and a
debug overlay reading `L: … R: … lat: … ω: …` with a direction arrow while tunnel is active.

In WPF this should be a `FrameworkElement` with an `OnRender` override drawing to
`DrawingContext`, not a tree of `Shape` elements — a few hundred points arriving four times a
second would otherwise churn the visual tree. Static furniture (rings, wedges, labels) can be
cached in a `DrawingGroup` and only the points redrawn.

**`TrafficLightControl`** — three 32px lamps in a pill, radial gradient plus a glow when
active, label beneath. Straightforward `Border` + `Ellipse` with a `DropShadowEffect`, driven
by a string-to-state converter on `traffic_light`.

**`MeterBar`** — the recurring "label, value, filled track" primitive. Used by IMU
(roll/pitch/yaw), lane error (signed, centre-anchored — the app already has this shape in the
lane strip), and manual control speed. One control with a `Signed` flag covers all three;
`SignedMultiply` already exists as the converter for the centre-anchored case.

**`GamepadView`** — 13-button 3-column grid (LB ▲ RB / ◀ STA ▶ / X ▼ Y / A LT B / RT) plus
two 60px joystick circles with a dot positioned from `axes`. Pure layout plus two
`TranslateTransform`s; the only subtlety is that button and axis indices are controller-
specific and should be named constants, not literals scattered through the XAML.

**`StateBadge`** — the colour-per-state pill. Seventeen states from `ChallengeState`
(`auto_driver.py:63`) with a colour each in the web CSS (`.state-LANE_FOLLOW` and friends).
Best done as a converter backed by a static dictionary rather than seventeen `DataTrigger`s.

**`ActionPriorityBar`** — six nodes (Lane Follow, Tunnel, Obstruction, Boom Gate, Traffic
Light, Manual Control) with the active one highlighted, derived from `state`. A horizontal
`ItemsControl` with a chevron separator.

**`SensorDotRow`** — trivial, but there are ten of them, so it earns a control: a dot whose
brush comes from a nullable bool, plus a label.

### Stage 4 — layout

Restructure `DashboardView.xaml` to the three-column shape (`260 | * | 260`), with the
full-width regions below.

**Built, differing from the suggestion above.** The plan proposed reflowing the right column
under the left below ~1100px via a width trigger. That was dropped. Moving an element between
`Grid` cells by trigger works — `Grid.Column` is attached and settable from a `Setter` — but
the 260px `ColumnDefinition` it vacates cannot be collapsed with it, because
`ColumnDefinition` has no `Style` property; closing the gap needs either a new
`double`→`GridLength` converter bound to `ActualWidth`, or a second copy of the panel.
Neither is worth it for a window whose minimum width the app controls. Instead
`MainWindow.MinWidth` was raised from 1080 to 1280, which is what `244` of sidebar plus
`28+28` of page margin plus `260 + 14 + 380 + 14 + 260` of body actually needs. Below that
the window will not resize; above it only the middle column grows. Nothing is ever hidden or
horizontally scrolled. The reasoning is repeated in the `DashboardView.xaml` header so it is
not rediscovered from the outside.

**Superseded — the sidebar is now an overlay drawer.** The fixed 244px sidebar column was
replaced by a 280px drawer that slides in over the content, plus a 48px top bar carrying the
hamburger, inline nav tabs and the launch status. Because the body no longer shares its row
with a sidebar, `MinWidth` came back down to **1060** — the same `928` of body plus `56` of
margin, with headroom. The three-column body itself is unchanged, and still does not reflow.

Two constraints the drawer introduced, both easy to undo by accident:

- **The START/STOP button belongs in the top bar, not only in the drawer.** The drawer is
  modal — its backdrop swallows clicks — so a launch control reachable only from inside it is
  two clicks from every screen, which is the phone layout this window exists to improve on.
  The drawer footer keeps a second copy for when it is already open.
- **`Escape` closes the drawer**, and is consumed only while it is open. A modal overlay with
  no keyboard exit is a trap.

**Amended after first use on hardware.** The middle column originally stacked the camera feed
above the LiDAR plot, as the web page does. On a 1080-tall screen that filled the window: the
plot had to be trimmed from the web canvas's 320px to 280 to fit at all, and the two of them
together set a 900px height floor. They are now exclusive tabs, selected by a sixth button on
the same switcher row as the five camera views. The plot stretches instead of being pinned,
and `MainWindow` drops to `Height=860` / `MinHeight=700`.

That tab is local state (`IsLidarView`), deliberately not a sixth value of `ActiveCamView`:
`/api/set_cam_view` accepts exactly the five camera names, answers `200` to anything else and
silently blanks every debug overlay, so `lidar` must never reach it. Both are read through one
computed `ActiveDisplayView` so the six buttons check a single property. Two things also stop
while hidden — the MJPEG pull (`IsCameraStreaming`, since collapsing the control does not by
itself end the multipart response) and the 250 ms `/lidar_data` poll.

**Deferred out of this pass:** Record & Playback, the Subsystem Data Logger, the Parameters
drawer and the Event Log — the four §4 flagged as heaviest and least likely to be reached for
mid-competition. Deferring both drawers also means the right-hand overlay panel is not needed
in this pass; when they land, the collapsed-until-invoked pattern from Network Setup is still
the one to reuse rather than reinvent.

### Stage 4b — the parameters drawer (landed)

`/api/get_param`, `/api/set_param` and `/api/save_defaults` were already wired in stage 2 and
had no caller. They do now: a 420px drawer sliding in from the right, toggled by `⚙ Params` in
the top bar. Seventeen collapsible groups, ~200 parameters, ported from `PARAM_GROUPS` and
`PARAM_TIPS` (`dashboard_templates.py:1929–2171`).

Four decisions that are load-bearing and easy to undo by accident:

- **It opens from the right, overlays, and has no backdrop.** The web version opens from the
  left and *pushes* the layout (`.layout.drawer-open`). The left edge here already belongs to
  the navigation drawer, and pushing would drive the dashboard body under its 984px floor and
  clip cards. The missing scrim is the important part: the whole point of live tuning is
  watching the camera overlay or the state pill react to a gain change, so what is behind the
  drawer must stay visible *and* clickable. It sits in body row 1 only, leaving the top bar's
  toggle and START/STOP reachable. `Escape` is deliberately not wired to it — non-modal, so
  nothing is trapped, and the drawer is full of text boxes mid-edit.
- **Groups are objects, not node lookups.** The web `toggleNodeParams(el, nodeName)` resolves
  its list with `PARAM_GROUPS.find(g => g.node === nodeName)`, and `auto_driver` owns three
  groups — so expanding Hill Climb there fetches the *Auto Driver* list and the hill parameters
  never populate. `ParameterGroupViewModel` loads its own `ParamGroup.Params`.
- **`obstacle_avoidance_camera` appears once.** The web version lists it twice with six
  parameters in common; since its DOM ids are `pv_{node}_{param}`, the duplicate ids leave the
  second copy's inputs dead. `ParameterCatalog` merges them into the union of nine.
- **`UpdateSourceTrigger=PropertyChanged` on the value box is required, not stylistic.** The
  Enter-to-Set `KeyBinding` fires on KeyDown while the box still has focus, so with the default
  lost-focus trigger it would send the *previous* value while displaying the new one.

Values load on first expand, sequentially. There is no batch endpoint — `get_param` answers one
name at a time, each a round trip plus a ROS parameter service call — and the dashboard's HTTP
server is threaded, so 26 parallel requests would pile 26 concurrent service calls onto the
same single-threaded executor that runs the 50 Hz control loop. A slower drawer beats a
stuttering control loop.

`SaveParameterDefaultsAsync` was changed from `ApiResult<bool>` to `ApiResult<SaveDefaultsResponse>`:
the handler reports how many parameters it rewrote, and `updated == 0` is a successful no-op, not
a save. It also got its own 30 s timeout — it walks the node graph, rewrites params.yaml and
flushes to the SD card, and a spurious timeout would report failure over a file it had just
overwritten. The confirmation keeps the web version's blunt wording, including the rebuild
caveat, because the file it edits is in the robot's git working tree.

`MinWidth` went **1060 → 1120**. The dashboard body is no longer what sets the floor; the top
bar is, and `⚙ Params` is the ~110px that pushed it over. Anything else added to that bar means
raising it again.

Still deferred: Record & Playback, the Subsystem Data Logger, the Event Log.

Order within the rebuild: middle column first (camera already works, LiDAR joins it), then
left, then right, then the full-width strip. That keeps the screen usable at every commit.

### Stage 5 — verification

Nothing in this repo builds or runs in-session, so verification is:

1. `dotnet build RisaBotApp.csproj` from inside `RisaBotApp\` — no `-f` flag; that was a
   MAUI leftover and produces `NETSDK1005`.
2. Point the app at a live robot and compare each card against the browser at `:8080`
   side by side.
3. Confirm the 250 ms LiDAR poll does not starve the 500 ms telemetry poll — both go through
   the same `HttpClient`, and `UiThread.RunAsync` marshals every applied write.

---

## 4. Things to decide before stage 4

- **Theme**, as above — dark with ported structure, or a literal light reproduction.
- **Scope.** Record & Playback, the Data Logger and the Parameters drawer are the three
  heaviest cards and the three least likely to be used from a phone mid-competition. They
  could be deferred to a later pass without leaving a hole in the main screen.
- **Event Log.** The web version accumulates entries in the page and loses them on refresh.
  The app could keep it in the view-model with the same lifetime, or persist it — the latter
  is more useful but is new behaviour, not a port.

---

## 5. Files touched

```
RisaBotApp/Models/RosDashboardData.cs        widen 20 → ~40 keys
RisaBotApp/Models/LidarData.cs               new
RisaBotApp/Services/ApiClient.cs             + 13 endpoints
RisaBotApp/ViewModels/DashboardViewModel.cs  + LiDAR poll, + state/sensor/controller props
RisaBotApp/Views/DashboardView.xaml          restructure to 3 columns
RisaBotApp/Controls/LidarView.cs             new
RisaBotApp/Controls/TrafficLightControl.xaml new
RisaBotApp/Controls/MeterBar.xaml            new
RisaBotApp/Controls/GamepadView.xaml         new
RisaBotApp/Controls/ActionPriorityBar.xaml   new
RisaBotApp/Themes/Colors.xaml                + semantic success/danger/warning
RisaBotApp/Themes/Controls.xaml              + StateBadge, SensorDot, card heading styles

Stage 4b:
RisaBotApp/Models/ParameterCatalog.cs             new  17 groups, ~200 specs + tips
RisaBotApp/Models/DashboardApiModels.cs           + SaveDefaultsResponse
RisaBotApp/Services/ApiClient.cs                  SaveParameterDefaults now returns the body
RisaBotApp/ViewModels/ParameterRowViewModel.cs    new
RisaBotApp/ViewModels/ParameterGroupViewModel.cs  new  lazy load per group
RisaBotApp/ViewModels/ParametersViewModel.cs      new  + guarded Save as defaults
RisaBotApp/Views/ParametersDrawer.xaml            new
RisaBotApp/MainWindow.xaml                        + ⚙ toggle, right drawer, MinWidth 1120
RisaBotApp/ViewModels/MainViewModel.cs            + IsParametersOpen
```
