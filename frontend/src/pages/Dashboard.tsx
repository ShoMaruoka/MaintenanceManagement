import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import StatusBadge from '../components/StatusBadge'
import { getSessions } from '../api/history'
import type { DeploySession } from '../types'

export default function Dashboard() {
  const [sessions, setSessions] = useState<DeploySession[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')

  useEffect(() => {
    getSessions(10)
      .then(setSessions)
      .catch(err => setError((err as Error).message))
      .finally(() => setLoading(false))
  }, [])

  const runningCount = sessions.filter(s => s.status === 'running').length

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
          <div
            key={s.sessionId}
            className="table-row"
            style={{ gridTemplateColumns: '140px 90px 1fr 100px 90px' }}
          >
            <div className="table-cell-mono">{s.executedAt}</div>
            <div className="table-cell-db">{s.dbName}</div>
            <div className="table-cell-module">{s.modules}</div>
            <div className="table-cell-user">{s.executedBy}</div>
            <div style={{ textAlign: 'right' }}>
              <StatusBadge status={s.status as any} />
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
