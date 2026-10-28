import type { ComponentStatus } from '../api/types'
import { statusLabel } from './statusLabels'
import './StatusPill.css'

/** Colors come from StatusPill.css by `data-status`, so TypeScript holds no colors. */
export function StatusPill({ status }: { status: ComponentStatus }) {
  return (
    <span className="status-pill" data-status={status}>
      <span className="status-pill__dot" aria-hidden="true" />
      {statusLabel(status)}
    </span>
  )
}
