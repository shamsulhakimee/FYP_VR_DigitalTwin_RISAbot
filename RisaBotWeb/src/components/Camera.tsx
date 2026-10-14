import { useEffect, useState } from 'react'
import { CAMERA_VIEWS, type CameraView } from '../api/types'
import { IconCamera } from './icons'

const VIEW_LABEL: Record<CameraView, string> = {
  raw: 'Raw',
  line_follower: 'Lane',
  obstacle: 'Obstacle',
  signage: 'Signage',
  traffic_light: 'Lights',
}

export function CameraTabs({
  view,
  onSelect,
  extra,
}: {
  view: CameraView | 'lidar'
  onSelect: (view: CameraView | 'lidar') => void
  extra?: { key: 'lidar'; label: string }
}) {
  const tabs: (CameraView | 'lidar')[] = [...CAMERA_VIEWS, ...(extra ? [extra.key] : [])]
  return (
    <div className="no-scrollbar flex gap-1.5 overflow-x-auto">
      {tabs.map((t) => (
        <button
          key={t}
          onClick={() => onSelect(t)}
          className={`shrink-0 rounded-lg border px-3 py-1.5 text-xs font-medium transition duration-150 active:scale-95 ${
            view === t
              ? 'border-accent/50 bg-accent/15 text-accent shadow-[0_0_10px] shadow-accent/15'
              : 'raised border-edge bg-gradient-to-b from-panel-hi to-panel text-slate-400'
          }`}
        >
          {t === 'lidar' ? (extra?.label ?? 'LiDAR') : VIEW_LABEL[t]}
        </button>
      ))}
    </div>
  )
}

/**
 * The stream is a long-lived multipart response, so the <img> is only mounted
 * while it should actually be pulling — unmounting is what closes the socket and
 * lets the bridge stop encoding.
 */
export function Camera({ url, live, view }: { url: string; live: boolean; view: CameraView }) {
  const [failed, setFailed] = useState(false)

  useEffect(() => setFailed(false), [url, live])

  return (
    <div className="raised relative aspect-[4/3] w-full overflow-hidden rounded-2xl border border-edge bg-black">
      {live && !failed ? (
        <img
          key={view}
          src={url}
          alt="robot camera"
          className="h-full w-full object-contain"
          onError={() => setFailed(true)}
        />
      ) : (
        <div className="flex h-full flex-col items-center justify-center gap-2 text-slate-600">
          <IconCamera className="text-[28px]" />
          <span className="px-6 text-center text-xs text-slate-500">
            {failed ? 'Stream unavailable' : 'Camera off — start the system'}
          </span>
        </div>
      )}

      <div className="pointer-events-none absolute left-2.5 top-2.5 flex items-center gap-1.5 rounded-md bg-black/65 px-2 py-1 text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-300 backdrop-blur-sm">
        <span className={`h-1.5 w-1.5 rounded-full ${live && !failed ? 'bg-bad' : 'bg-slate-600'}`} />
        {VIEW_LABEL[view]}
      </div>
    </div>
  )
}
