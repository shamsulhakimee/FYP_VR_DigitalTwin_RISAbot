import { useState } from 'react'
import { usePolled } from '../api/usePolled'
import type { CameraView } from '../api/types'
import { useRobot } from '../app/robot'
import { Camera, CameraTabs } from '../components/Camera'
import { ActionPriorityBar, Gamepad, TrafficLight } from '../components/Indicators'
import { Lidar } from '../components/Lidar'
import { LaneErrorBar, StatePanel } from '../components/StatePanel'
import { Card, MeterRow, SensorDot, Stat } from '../components/ui'

export function Dashboard() {
  const { client, telemetry: t, status, online } = useRobot()
  const [view, setView] = useState<CameraView | 'lidar'>('raw')

  const isLidar = view === 'lidar'
  const { data: lidar } = usePolled(() => client.getLidar(), 250, isLidar && online)

  const cameraLive = Boolean(status?.launch_active) && online

  const selectView = (next: CameraView | 'lidar') => {
    setView(next)
    if (next !== 'lidar') void client.setCameraView(next).catch(() => {})
  }

  if (!t) {
    return (
      <div className="rounded-2xl border border-edge bg-panel p-8 text-center text-sm text-slate-400">
        Waiting for telemetry…
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <StatePanel t={t} />

      <Card title="Action priority">
        <ActionPriorityBar state={t.state} />
      </Card>

      <div className="flex flex-col gap-2">
        <CameraTabs view={view} onSelect={selectView} extra={{ key: 'lidar', label: 'LiDAR' }} />
        {isLidar ? (
          <Lidar data={lidar} />
        ) : (
          <Camera url={client.cameraStreamUrl(view)} live={cameraLive} view={view} />
        )}
      </div>

      <Card title="Motion">
        <MeterRow label="Speed" value={t.speed} unit=" m/s" max={1} tone="good" />
        <MeterRow label="Commanded linear" value={t.cmd_lin_x} unit=" m/s" max={1} signed />
        <MeterRow label="Commanded angular" value={t.cmd_ang_z} unit=" rad/s" max={2} signed />
        <div className="mt-3 grid grid-cols-3 gap-2 border-t border-edge pt-3">
          <Stat label="Distance" value={`${t.distance.toFixed(1)} m`} />
          <Stat label="Lap" value={t.lap} />
          <Stat label="Throttle" value={`${t.speed_pct}%`} />
        </div>
      </Card>

      <LaneErrorBar error={t.lane_error} />

      <Card title="Perception">
        <SensorDot label="Boom gate" status={t.boom_gate} trueIsGood trueText="OPEN" falseText="CLOSED" />
        <SensorDot label="Traffic light" status={t.traffic_light === 'green'} trueIsGood trueText="GO" falseText="HOLD" />
        <SensorDot label="Tunnel" status={t.tunnel_detected} trueText="DETECTED" falseText="CLEAR" />
        <SensorDot label="Parking sign" status={t.parking_sign_detected} trueText="DETECTED" falseText="CLEAR" />
        <SensorDot label="LiDAR obstacle" status={t.lidar_obstacle} trueText="BLOCKED" falseText="CLEAR" />
        <SensorDot label="Camera obstacle" status={t.camera_obstacle} trueText="BLOCKED" falseText="CLEAR" />
        <SensorDot label="Fused obstacle" status={t.fused_obstacle} trueText="BLOCKED" falseText="CLEAR" />
        <SensorDot label="Obstruction active" status={t.obstruction_active} trueText="ACTIVE" falseText="CLEAR" />
        <SensorDot label="Parking complete" status={t.parking_complete} trueIsGood trueText="DONE" falseText="PENDING" />
        <div className="mt-3 border-t border-edge pt-3">
          <TrafficLight lamp={t.traffic_light} />
        </div>
      </Card>

      <Card title="Attitude">
        <MeterRow label="Roll" value={t.imu_roll} unit="°" max={45} signed digits={1} />
        <MeterRow label="Pitch" value={t.imu_pitch} unit="°" max={45} signed digits={1} />
        <MeterRow label="Yaw" value={t.imu_yaw} unit="°" max={180} signed digits={1} />
      </Card>

      <Card title="Odometry">
        <div className="grid grid-cols-3 gap-2">
          <Stat label="X" value={`${t.odom_x.toFixed(2)} m`} />
          <Stat label="Y" value={`${t.odom_y.toFixed(2)} m`} />
          <Stat label="Yaw" value={`${t.odom_yaw.toFixed(1)}°`} />
        </div>
      </Card>

      <Card title="Health">
        <SensorDot label="Streams healthy" status={t.health_ok} trueIsGood trueText="OK" falseText="DEGRADED" />
        <SensorDot label="E-stop" status={t.cmd_safety_estop} trueText="ENGAGED" falseText="CLEAR" />
        <div className="mt-2 grid grid-cols-2 gap-2 border-t border-edge pt-3">
          <Stat label="Safety timeouts" value={t.cmd_safety_timeout_count} />
          <Stat label="Stale" value={t.health_stale?.length ? t.health_stale.join(', ') : '—'} />
        </div>
        {t.health_summary && (
          <p className="mt-2 text-[11px] leading-relaxed text-slate-500">{t.health_summary}</p>
        )}
      </Card>

      <Card title={`Manual control · ${t.ctrl_state_name || '—'}`}>
        <Gamepad buttons={t.buttons ?? []} axes={t.axes ?? []} />
      </Card>
    </div>
  )
}
