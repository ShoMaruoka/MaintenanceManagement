import { useState, useEffect } from 'react'
import StatusBadge from '../components/StatusBadge'
import { SessionDetailTable } from '../components/SessionDetailTable'
import { getSessions, getSession, getPilotRuns, getPilotRun } from '../api/history'
import type { DeploySession, DbName, SessionStatus, PilotRunSummary, PilotRunDetail } from '../types'

const DB_OPTIONS: (DbName | 'all')[] = ['all', 'kaios', 'gos', 'paf', 'duskin']
const STATUS_OPTIONS: (SessionStatus | 'all')[] = ['all', 'success', 'failed', 'running']
const KIND_OPTIONS = ['all', 'stg', 'pilot'] as const

type KindFilter = (typeof KIND_OPTIONS)[number]

const STATUS_LABELS: Record<SessionStatus | 'all', string> = {
  all: 'すべて',
  success: '成功',
  failed: '失敗',
  running: '実行中',
}

const KIND_LABELS: Record<KindFilter, string> = {
  all: 'すべて',
  stg: 'STG適用',
  pilot: 'Pilot適用',
}

type HistoryRow =
  | { kind: 'stg'; key: string; session: DeploySession }
  | { kind: 'pilot'; key: string; run: PilotRunSummary | PilotRunDetail }

function isPilotDetail(run: PilotRunSummary | PilotRunDetail): run is PilotRunDetail {
  return 'detailsFetched' in run && !!(run as PilotRunDetail).detailsFetched
}

export default function History() {
  const [sessions, setSessions] = useState<DeploySession[]>([])
  const [pilotRuns, setPilotRuns] = useState<(PilotRunSummary | PilotRunDetail)[]>([])
  const [dbFilter, setDbFilter] = useState<DbName | 'all'>('all')
  const [statusFilter, setStatusFilter] = useState<SessionStatus | 'all'>('all')
  const [kindFilter, setKindFilter] = useState<KindFilter>('all')
  const [expandedKey, setExpandedKey] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')
  const [expandError, setExpandError] = useState<string>('')

  useEffect(() => {
    const load = async () => {
      try {
        setLoading(true)
        const [sessionData, pilotData] = await Promise.all([
          getSessions(100),
          getPilotRuns(100),
        ])
        setSessions(sessionData)
        setPilotRuns(pilotData)
      } catch (err) {
        setError((err as Error).message)
      } finally {
        setLoading(false)
      }
    }

    void load()
  }, [])

  const rows: HistoryRow[] = [
    ...sessions.map(session => ({
      kind: 'stg' as const,
      key: `stg-${session.sessionId}`,
      session,
    })),
    ...pilotRuns.map(run => ({
      kind: 'pilot' as const,
      key: `pilot-${run.runId}`,
      run,
    })),
  ].sort((a, b) => {
    const atA = a.kind === 'stg' ? a.session.executedAt : a.run.executedAt
    const atB = b.kind === 'stg' ? b.session.executedAt : b.run.executedAt
    const cmp = atB.localeCompare(atA)
    return cmp !== 0 ? cmp : b.key.localeCompare(a.key)
  })

  const handleExpandStg = async (sessionId: number, key: string) => {
    if (expandedKey === key) {
      setExpandedKey(null)
      setExpandError('')
      return
    }

    const existing = sessions.find(s => s.sessionId === sessionId)
    if (existing?.detailsFetched) {
      setExpandError('')
      setExpandedKey(key)
      return
    }

    try {
      const session = await getSession(sessionId)
      setSessions(prev =>
        prev.map(s => s.sessionId === sessionId
          ? { ...s, details: session.details, logDetail: session.logDetail, detailsFetched: true }
          : s)
      )
      setExpandError('')
      setExpandedKey(key)
    } catch (err) {
      console.error('Failed to load session details:', err)
      setExpandError('セッション詳細の取得に失敗しました')
      setExpandedKey(key)
    }
  }

  const handleExpandPilot = async (runId: string, key: string) => {
    if (expandedKey === key) {
      setExpandedKey(null)
      setExpandError('')
      return
    }

    const existing = pilotRuns.find(r => r.runId === runId)
    if (existing && isPilotDetail(existing)) {
      setExpandError('')
      setExpandedKey(key)
      return
    }

    try {
      const detail = await getPilotRun(runId)
      setPilotRuns(prev => prev.map(r => r.runId === runId ? detail : r))
      setExpandError('')
      setExpandedKey(key)
    } catch (err) {
      console.error('Failed to load pilot run details:', err)
      setExpandError('Pilot適用詳細の取得に失敗しました')
      setExpandedKey(key)
    }
  }

  const filtered = rows.filter(row => {
    if (kindFilter !== 'all' && row.kind !== kindFilter) return false
    if (row.kind === 'stg') {
      if (dbFilter !== 'all' && row.session.dbName !== dbFilter) return false
      if (statusFilter !== 'all' && row.session.status !== statusFilter) return false
      return true
    }
    if (dbFilter !== 'all' && row.run.dbName !== dbFilter) return false
    if (statusFilter === 'running') return false
    if (statusFilter !== 'all' && row.run.result !== statusFilter) return false
    return true
  })

  return (
    <div>
      <div className="history-filters">
        <select
          className="filter-select"
          value={kindFilter}
          onChange={e => setKindFilter(e.target.value as KindFilter)}
        >
          {KIND_OPTIONS.map(k => (
            <option key={k} value={k}>種別: {KIND_LABELS[k]}</option>
          ))}
        </select>
        <select
          className="filter-select"
          value={dbFilter}
          onChange={e => setDbFilter(e.target.value as DbName | 'all')}
        >
          {DB_OPTIONS.map(db => (
            <option key={db} value={db}>{db === 'all' ? 'DB: すべて' : `DB: ${db}`}</option>
          ))}
        </select>
        <select
          className="filter-select"
          value={statusFilter}
          onChange={e => setStatusFilter(e.target.value as SessionStatus | 'all')}
        >
          {STATUS_OPTIONS.map(s => (
            <option key={s} value={s}>結果: {STATUS_LABELS[s]}</option>
          ))}
        </select>
        <span style={{ fontSize: 11, color: '#8a9099', marginLeft: 4, alignSelf: 'center' }}>
          {filtered.length} 件
        </span>
      </div>

      <div className="table-container">
        <div className="table-header-bar">
          <div className="table-header-title">実行履歴<span> — 全期間</span></div>
        </div>
        <div
          className="table-col-header history-table-cols"
          style={{ gridTemplateColumns: '140px 90px 1fr 100px 90px' }}
        >
          <div>日時</div>
          <div>DB</div>
          <div>モジュール</div>
          <div>実行者</div>
          <div style={{ textAlign: 'right' }}>結果</div>
        </div>

        {loading && (
          <div className="empty-state">読み込み中...</div>
        )}
        {error && (
          <div className="empty-state" style={{ color: '#c5283d' }}>エラー: {error}</div>
        )}
        {!loading && !error && filtered.length === 0 && (
          <div className="empty-state">該当する履歴がありません</div>
        )}

        {!loading && !error && filtered.map(row => (
          row.kind === 'stg' ? (
            <StgHistoryRow
              key={row.key}
              session={row.session}
              expanded={expandedKey === row.key}
              expandError={expandedKey === row.key ? expandError : ''}
              onToggle={() => void handleExpandStg(row.session.sessionId, row.key)}
            />
          ) : (
            <PilotHistoryRow
              key={row.key}
              run={row.run}
              expanded={expandedKey === row.key}
              expandError={expandedKey === row.key ? expandError : ''}
              onToggle={() => void handleExpandPilot(row.run.runId, row.key)}
            />
          )
        ))}
      </div>
    </div>
  )
}

function StgHistoryRow({
  session,
  expanded,
  expandError,
  onToggle,
}: {
  session: DeploySession
  expanded: boolean
  expandError: string
  onToggle: () => void
}) {
  return (
    <div>
      <div
        className="table-row"
        style={{ gridTemplateColumns: '140px 90px 1fr 100px 90px', cursor: 'pointer' }}
        onClick={onToggle}
      >
        <div className="table-cell-mono">{session.executedAt}</div>
        <div className="table-cell-db">{session.dbName}</div>
        <div className="table-cell-module">{session.modules}</div>
        <div className="table-cell-user">{session.executedBy}</div>
        <div style={{ textAlign: 'right', display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 6 }}>
          <StatusBadge status={session.status} />
          <ExpandChevron expanded={expanded} />
        </div>
      </div>
      {expanded && (
        <div className="log-session-detail">
          <div className="log-detail-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            セッション詳細
            <span style={{ fontWeight: 400, color: '#9aa0a8' }}>{session.moduleCount} モジュール</span>
          </div>
          {expandError && (
            <div style={{ fontSize: 11, color: '#c5283d', marginTop: 6 }}>{expandError}</div>
          )}
          {!expandError && session.details && session.details.length > 0 ? (
            <SessionDetailTable details={session.details} />
          ) : (
            !expandError && <div style={{ fontSize: 11, color: '#9aa0a8', marginTop: 6 }}>モジュールデータがありません</div>
          )}
          {session.status === 'failed' && (
            <div style={{ marginTop: 8, padding: '8px 10px', background: '#fcebed', border: '1px solid #f3c0c5', borderRadius: 6, fontSize: 11, color: '#c5283d' }}>
              エラーが発生しました。実行ログを確認してください。
            </div>
          )}
          {!expandError && (
            session.logDetail ? (
              <pre className="log-detail-full-log">{session.logDetail}</pre>
            ) : (
              <div style={{ fontSize: 11, color: '#9aa0a8', marginTop: 8 }}>ログがありません</div>
            )
          )}
        </div>
      )}
    </div>
  )
}

function PilotHistoryRow({
  run,
  expanded,
  expandError,
  onToggle,
}: {
  run: PilotRunSummary | PilotRunDetail
  expanded: boolean
  expandError: string
  onToggle: () => void
}) {
  const detail = isPilotDetail(run) ? run : null
  return (
    <div>
      <div
        className="table-row"
        style={{ gridTemplateColumns: '140px 90px 1fr 100px 90px', cursor: 'pointer' }}
        onClick={onToggle}
      >
        <div className="table-cell-mono">{run.executedAt}</div>
        <div className="table-cell-db">{run.dbName}</div>
        <div className="table-cell-module">Pilot適用（{run.dbName}）</div>
        <div className="table-cell-user">{run.executedBy}</div>
        <div style={{ textAlign: 'right', display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 6 }}>
          <StatusBadge status={run.result} />
          <ExpandChevron expanded={expanded} />
        </div>
      </div>
      {expanded && (
        <div className="log-session-detail">
          <div className="log-detail-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            Pilot適用詳細
            <span style={{ fontWeight: 400, color: '#9aa0a8' }}>{run.stepLabel}</span>
          </div>
          {run.summary && (
            <div style={{ fontSize: 11, color: '#6b7280', marginTop: 6 }}>{run.summary}</div>
          )}
          {expandError && (
            <div style={{ fontSize: 11, color: '#c5283d', marginTop: 6 }}>{expandError}</div>
          )}
          {!expandError && detail && detail.targets.length > 0 && (
            <div className="pilot-history-targets">
              {detail.targets.map(t => (
                <div key={`${t.targetName}-${t.mode}`} className="pilot-history-target-row">
                  <span>{t.targetName}</span>
                  <StatusBadge status={t.result === 'failed' ? 'failed' : 'success'} />
                </div>
              ))}
            </div>
          )}
          {run.result === 'failed' && (
            <div style={{ marginTop: 8, padding: '8px 10px', background: '#fcebed', border: '1px solid #f3c0c5', borderRadius: 6, fontSize: 11, color: '#c5283d' }}>
              エラーが発生しました。実行ログを確認してください。
            </div>
          )}
          {!expandError && (
            detail?.logDetail ? (
              <pre className="log-detail-full-log log-detail-full-log-pilot">{detail.logDetail}</pre>
            ) : (
              <div style={{ fontSize: 11, color: '#9aa0a8', marginTop: 8 }}>
                ログがありません（v1.5.2 より前の実行は全文未保存）
              </div>
            )
          )}
        </div>
      )}
    </div>
  )
}

function ExpandChevron({ expanded }: { expanded: boolean }) {
  return (
    <svg
      width="12" height="12" viewBox="0 0 12 12" fill="none"
      style={{ transform: expanded ? 'rotate(180deg)' : 'none', transition: 'transform 0.15s', color: '#9aa0a8' }}
    >
      <path d="M2 4l4 4 4-4" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"/>
    </svg>
  )
}
