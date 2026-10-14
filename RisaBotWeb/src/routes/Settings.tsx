import { useState } from 'react'
import { useRobot } from '../app/robot'
import { ModelCard } from '../components/ModelCard'
import { Secret } from '../components/Secret'
import { Badge, Btn, Card, PageHeader } from '../components/ui'

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 border-b border-edge/50 py-2 last:border-0">
      <span className="shrink-0 text-xs text-slate-500">{label}</span>
      <span className="truncate text-xs font-medium text-slate-200">{value || '—'}</span>
    </div>
  )
}

/** Same row, but the value is an address — hidden until tapped. */
function SecretRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 border-b border-edge/50 py-2 last:border-0">
      <span className="shrink-0 text-xs text-slate-500">{label}</span>
      <Secret value={value} className="readout text-xs font-medium text-slate-200" />
    </div>
  )
}

export function Settings() {
  const { paired, status, forget, host } = useRobot()
  const [confirming, setConfirming] = useState(false)

  return (
    <div className="flex flex-col gap-3">
      <PageHeader title="Settings" subtitle="Identity and pairing for the connected robot." />

      <Card title="Robot">
        <Row label="Name" value={paired?.name ?? ''} />
        <Row label="Model" value={paired?.model ?? ''} />
        <Row label="Serial" value={paired?.serial ?? ''} />
        <Row label="Firmware" value={paired?.firmware ?? ''} />
        <Row label="Hostname" value={paired?.mdns ?? ''} />
        <SecretRow label="Address" value={host} />
      </Card>

      <Card
        title="Network"
        right={
          <Badge tone={status?.internet_online ? 'good' : 'warn'}>
            {status?.internet_online ? 'online' : 'no internet'}
          </Badge>
        }
      >
        <Row label="SSID" value={status?.connected_ssid ?? ''} />
        <Row label="Interface" value={status?.wlan0_state ?? ''} />
        <SecretRow label="wlan0 IP" value={status?.wlan0_ip ?? ''} />
        <SecretRow label="Dashboard" value={status?.dashboard_url ?? ''} />
      </Card>

      <ModelCard />

      <Card title="Pairing">
        <Row
          label="Paired at"
          value={paired ? new Date(paired.pairedAt).toLocaleString() : ''}
        />
        <p className="mt-3 text-xs leading-relaxed text-slate-500">
          Forgetting clears the saved pairing and returns to discovery. The robot itself is not
          changed.
        </p>
        {confirming ? (
          <div className="mt-3 grid grid-cols-2 gap-2">
            <Btn onClick={() => setConfirming(false)}>Cancel</Btn>
            <Btn tone="bad" onClick={forget}>
              Confirm
            </Btn>
          </div>
        ) : (
          <Btn tone="bad" className="mt-3 w-full" onClick={() => setConfirming(true)}>
            Forget robot
          </Btn>
        )}
      </Card>
    </div>
  )
}
