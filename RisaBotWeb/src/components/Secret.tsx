import { useEffect, useState } from 'react'
import { maskAddresses } from './mask'

/** Long enough to read a value off the screen, short enough to not linger in a demo. */
const REVEAL_MS = 8000

/**
 * An address that is hidden until deliberately asked for.
 *
 * This is presentation, not protection — see the note in Settings. It exists so
 * a screenshot, a projector at the competition, or someone reading over a
 * shoulder does not casually pick up the robot's address. An operator debugging
 * still needs the real value, so it is one tap away rather than gone.
 */
export function Secret({ value, className = '' }: { value: string; className?: string }) {
  const [shown, setShown] = useState(false)

  // Re-hide on its own. A reveal that stays revealed is just a slower leak, and
  // nobody remembers to press it again.
  useEffect(() => {
    if (!shown) return
    const t = setTimeout(() => setShown(false), REVEAL_MS)
    return () => clearTimeout(t)
  }, [shown])

  // A changed value is a different robot; do not carry the reveal across.
  useEffect(() => setShown(false), [value])

  if (!value) return <span className={className}>—</span>

  // Nothing in this value is hidden — a URL on the hotspot gateway, say. Render
  // it as text: a reveal control that reveals what is already visible reads as
  // a bug, and invites tapping something that does nothing.
  const masked = maskAddresses(value)
  if (masked === value) return <span className={`truncate ${className}`}>{value}</span>

  return (
    <button
      type="button"
      onClick={() => setShown((s) => !s)}
      className={`truncate text-left transition active:opacity-60 ${className}`}
      title={shown ? 'Tap to hide' : 'Tap to reveal'}
    >
      {shown ? value : masked}
    </button>
  )
}
