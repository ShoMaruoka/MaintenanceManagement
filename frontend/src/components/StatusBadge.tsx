import type { SessionStatus } from '../types'

interface Props {
  status: SessionStatus
}

const LABELS: Record<SessionStatus, string> = {
  running: '実行中',
  success: '成功',
  failed:  '失敗',
}

export default function StatusBadge({ status }: Props) {
  return (
    <span className={`badge badge-${status}`}>
      {status === 'running' && (
        <span className={`badge-dot badge-dot-${status}`} />
      )}
      {LABELS[status]}
    </span>
  )
}
