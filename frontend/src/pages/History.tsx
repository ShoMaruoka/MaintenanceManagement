import { useState, useEffect } from 'react'
import StatusBadge from '../components/StatusBadge'
import { getSessions, getSession } from '../api/history'
import type { DeploySession, DbName, SessionStatus } from '../types'

interface SessionWithDetail extends DeploySession {
  moduleDetail?: { name: string; op: string }[]
}

const DB_OPTIONS: (DbName | 'all')[] = ['all', 'kaios', 'gos', 'paf', 'duskin']
const STATUS_OPTIONS: (SessionStatus | 'all')[] = ['all', 'success', 'failed', 'running']

const STATUS_LABELS: Record<SessionStatus | 'all', string> = {
  all: 'すべて',
  success: '成功',
  failed: '失敗',
  running: '実行中',
}

export default function History() {
  const [sessions, setSessions] = useState<SessionWithDetail[]>([])
  const [dbFilter, setDbFilter] = useState<DbName | 'all'>('all')
  const [statusFilter, setStatusFilter] = useState<SessionStatus | 'all'>('all')
  const [expandedId, setExpandedId] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')

  useEffect(() => {
    const loadSessions = async () => {
      try {
        setLoading(true)
        const data = await getSessions(100)
        setSessions(data)
      } catch (err) {
        setError((err as Error).message)
      } finally {
        setLoading(false)
      }
    }

    loadSessions()
  }, [])

  const handleExpandRow = async (sessionId: number) => {
    if (expandedId === sessionId) {
      setExpandedId(null)
      return
    }

    const existing = sessions.find(s => s.sessionId === sessionId)
    if (existing?.moduleDetail) {
      setExpandedId(sessionId)
      return
    }

    try {
      const session = await getSession(sessionId)
      setSessions(prev =>
        prev.map(s =>
          s.sessionId === sessionId
            ? {
                ...s,
                moduleDetail: session.moduleCount > 0
                  ? [{ name: session.modules, op: '詳細' }]
                  : undefined,
              }
            : s
        )
      )
      setExpandedId(sessionId)
    } catch (err) {
      console.error('Failed to load session details:', err)
    }
  }

  const filtered = sessions.filter(s => {
    if (dbFilter !== 'all' && s.dbName !== dbFilter) return false
    if (statusFilter !== 'all' && s.status !== statusFilter) return false
    return true
  })

  return (
    <div>
      <div className="history-filters">
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

        {!loading && !error && filtered.map(s => (
          <div key={s.sessionId}>
            <div
              className="table-row"
              style={{ gridTemplateColumns: '140px 90px 1fr 100px 90px', cursor: 'pointer' }}
              onClick={() => handleExpandRow(s.sessionId)}
            >
              <div className="table-cell-mono">{s.executedAt}</div>
              <div className="table-cell-db">{s.dbName}</div>
              <div className="table-cell-module">{s.modules}</div>
              <div className="table-cell-user">{s.executedBy}</div>
              <div style={{ textAlign: 'right', display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 6 }}>
                <StatusBadge status={s.status as any} />
                <svg
                  width="12" height="12" viewBox="0 0 12 12" fill="none"
                  style={{ transform: expandedId === s.sessionId ? 'rotate(180deg)' : 'none', transition: 'transform 0.15s', color: '#9aa0a8' }}
                >
                  <path d="M2 4l4 4 4-4" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </div>
            </div>
            {expandedId === s.sessionId && (
              <div className="log-session-detail">
                <div className="log-detail-title">セッション詳細</div>
                <div className="log-detail-modules">
                  <div style={{ fontSize: 11, color: '#6b7280' }}>
                    <strong>{s.moduleCount}</strong> モジュール実行
                  </div>
                  <div style={{ marginTop: 6, fontSize: 12, color: '#3a3f46' }}>
                    {s.modules}
                  </div>
                </div>
                {s.status === 'failed' && (
                  <div style={{ marginTop: 8, padding: '8px 10px', background: '#fcebed', border: '1px solid #f3c0c5', borderRadius: 6, fontSize: 11, color: '#c5283d' }}>
                    エラーが発生しました。実行ログを確認してください。
                  </div>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}
