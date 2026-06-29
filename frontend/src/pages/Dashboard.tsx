import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import StatusBadge from '../components/StatusBadge'
import { getSessions } from '../api/history'
import type { DeploySession } from '../types'

export default function Dashboard() {
  const [sessions, setSessions] = useState<DeploySession[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')
  const [expandedId, setExpandedId] = useState<number | null>(null)

  useEffect(() => {
    getSessions(10)
      .then(setSessions)
      .catch(err => setError((err as Error).message))
      .finally(() => setLoading(false))
  }, [])

  const runningCount = sessions.filter(s => s.status === 'running').length

  const handleExpandRow = (sessionId: number) => {
    setExpandedId(prev => prev === sessionId ? null : sessionId)
  }

  return (
    <div>
      <div className="stat-cards">
        <div className="stat-card">
          <div className="stat-card-label">本番前準備 最終実行</div>
          <div className="stat-card-value">06/12 03:00</div>
          <div className="stat-card-sub">
            <span className="badge badge-success">成功 · 全4DB</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">直近30日 成功率</div>
          <div className="stat-card-value">
            96.6<span style={{ fontSize: 13, color: '#8a9099' }}>%</span>
          </div>
          <div className="stat-card-sub" style={{ color: '#8a9099', fontFamily: "'JetBrains Mono', monospace" }}>
            28 / 29 セッション成功
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">実行中セッション</div>
          <div className="stat-card-value" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span className="running-pulse" />
            {runningCount}
          </div>
          <div className="stat-card-sub" style={{ color: '#b25e09', fontFamily: "'JetBrains Mono', monospace" }}>
            {runningCount > 0 ? 'kaios — STG 適用中…' : 'なし'}
          </div>
        </div>
      </div>

      <div className="table-container">
        <div className="table-header-bar">
          <div className="table-header-title">
            最近の実行履歴<span> — 直近 10 件</span>
          </div>
          <Link to="/history" className="table-link">すべて表示 →</Link>
        </div>
        <div className="table-col-header" style={{ gridTemplateColumns: '140px 90px 1fr 100px 90px' }}>
          <div>日時</div>
          <div>DB</div>
          <div>モジュール</div>
          <div>実行者</div>
          <div style={{ textAlign: 'right' }}>結果</div>
        </div>
        {loading && (
          <div style={{ padding: '20px', color: '#8a9099' }}>読み込み中...</div>
        )}
        {error && (
          <div style={{ padding: '20px', color: '#c5283d' }}>エラー: {error}</div>
        )}
        {!loading && !error && sessions.length === 0 && (
          <div style={{ padding: '20px', color: '#8a9099' }}>実行履歴がありません</div>
        )}
        {sessions.map((s) => (
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
                <div className="log-detail-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  セッション詳細
                  <span style={{ fontWeight: 400, color: '#9aa0a8' }}>{s.moduleCount} モジュール</span>
                </div>
                {s.details && s.details.length > 0 ? (
                  <table className="log-detail-table">
                    <thead>
                      <tr>
                        <th>種別</th>
                        <th>モジュール名</th>
                        <th>区分</th>
                        <th>結果</th>
                      </tr>
                    </thead>
                    <tbody>
                      {s.details.map((d, i) => (
                        <tr key={d.detailId ?? i}>
                          <td>
                            <span className="log-detail-type-badge">
                              {d.moduleType === 'StoredProcedure' ? 'SP'
                                : d.moduleType === 'Function' ? 'Func'
                                : d.moduleType}
                            </span>
                          </td>
                          <td className="log-detail-module-name-cell">{d.moduleName}</td>
                          <td>
                            <span className={`log-detail-op-badge ${
                              d.opType === '新規' ? 'log-detail-op-new'
                              : d.opType === '更新' ? 'log-detail-op-update'
                              : 'log-detail-op-delete'
                            }`}>{d.opType}</span>
                          </td>
                          <td>
                            <span className={
                              d.result === 'success' ? 'log-detail-result-success'
                              : d.result === 'failed' ? 'log-detail-result-failed'
                              : 'log-detail-result-skipped'
                            }>
                              {d.result === 'success' ? '成功'
                                : d.result === 'failed' ? '失敗'
                                : 'スキップ'}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                ) : (
                  <div style={{ fontSize: 11, color: '#9aa0a8', marginTop: 6 }}>モジュールデータがありません</div>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}
