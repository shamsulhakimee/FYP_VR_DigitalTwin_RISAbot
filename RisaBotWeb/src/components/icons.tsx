/**
 * Stroke icons on a 24x24 grid, sized by the parent's font-size via `1em` so they
 * line up with adjacent text. Drawn here rather than pulled from a library: this is
 * the whole set the app needs, and it keeps the bundle free of one more dependency.
 */
import type { ReactNode } from 'react'

type IconProps = { className?: string; strokeWidth?: number }

function Svg({
  children,
  className = '',
  strokeWidth = 1.75,
}: IconProps & { children: ReactNode }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={`h-[1em] w-[1em] ${className}`}
      aria-hidden="true"
    >
      {children}
    </svg>
  )
}

export function IconActivity(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M3 12h4l3-7 4 14 3-7h4" />
    </Svg>
  )
}

export function IconSliders(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M4 7h8M16.5 7H20M4 12h11M19 12h1M4 17h5M13 17h7" />
      <circle cx="14" cy="7" r="2" />
      <circle cx="17" cy="12" r="2" />
      <circle cx="11" cy="17" r="2" />
    </Svg>
  )
}

export function IconRecord(p: IconProps) {
  return (
    <Svg {...p}>
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="3.5" fill="currentColor" stroke="none" />
    </Svg>
  )
}

export function IconList(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M8 6h12M8 12h12M8 18h7" />
      <path d="M4 6h.01M4 12h.01M4 18h.01" />
    </Svg>
  )
}

export function IconChip(p: IconProps) {
  return (
    <Svg {...p}>
      <rect x="7" y="7" width="10" height="10" rx="2" />
      <path d="M10 3v4M14 3v4M10 17v4M14 17v4M3 10h4M3 14h4M17 10h4M17 14h4" />
    </Svg>
  )
}

export function IconWifi(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M4.5 11.5a11 11 0 0 1 15 0" />
      <path d="M8 15a6 6 0 0 1 8 0" />
      <path d="M12 18.5h.01" />
    </Svg>
  )
}

export function IconRefresh(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M20.5 12a8.5 8.5 0 1 1-2.5-6" />
      <path d="M20.5 3.5V9H15" />
    </Svg>
  )
}

export function IconUpload(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M12 15V4" />
      <path d="m7.5 8.5 4.5-4.5 4.5 4.5" />
      <path d="M4 16v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2" />
    </Svg>
  )
}

export function IconRotateBack(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M3.5 12a8.5 8.5 0 1 0 2.5-6" />
      <path d="M3.5 3.5V9H9" />
    </Svg>
  )
}

export function IconChevronRight(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="m9.5 5.5 6.5 6.5-6.5 6.5" />
    </Svg>
  )
}

export function IconCamera(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M3 8.5A1.5 1.5 0 0 1 4.5 7h2.7l1.3-2h6l1.3 2h2.7A1.5 1.5 0 0 1 20 8.5v9A1.5 1.5 0 0 1 18.5 19h-13A1.5 1.5 0 0 1 4 17.5Z" />
      <circle cx="12" cy="13" r="3.5" />
    </Svg>
  )
}

export function IconPower(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M12 3.5v8" />
      <path d="M7 6.5a7.5 7.5 0 1 0 10 0" />
    </Svg>
  )
}
