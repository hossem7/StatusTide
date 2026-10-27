import type { ComponentStatus } from '../types/status'
import { statusDisplay } from '../types/status'
import './StatusBadge.css'

interface StatusBadgeProps {
  status: ComponentStatus
}

function StatusBadge({ status }: StatusBadgeProps) {
  const { label, color } = statusDisplay[status]

  return (
    <span className="status-badge">
      <span
        className="status-badge__dot"
        style={{ backgroundColor: color }}
        aria-hidden="true"
      />
      <span className="status-badge__label">{label}</span>
    </span>
  )
}

export default StatusBadge
