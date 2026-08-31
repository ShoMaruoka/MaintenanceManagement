import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import StatusBadge from '../components/StatusBadge'
import { SessionDetailTable } from '../components/SessionDetailTable'
import { formatDateTime, formatPrepareSummary, getDashboardStats, getSessions } from '../api/history'
import type { DashboardStats, DeploySession } from '../types'

const MONO = "'JetBrains Mono', monospace"

export default function Dashboard() {
  const [sessions, setSessions] = useState<DeploySession[]>([])
  const [stats, setStats] = useState<DashboardStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')
  const [expandedId, setExpandedId] = useState<number | null>(null)

  useEffect(() => {
    getSessions(10)
      .then(setSessions)
      .catch(err => setError((err as Error).message))
      .finally(() => setLoading(false))

    // サマリーカードは履歴テーブルとは独立に描画するため、失敗しても表全体は壊さない。
    getDashboardStats(30)
      .then(setStats)
      .catch(() => setStats(null))
  }, [])

  const lastPrepare = stats?.lastPrepare ?? null
  const successRate = stats && stats.totalSessions > 0
    ? (stats.successSessions / stats.totalSessions) * 100
    : null
  const runningCount = stats?.runningCount ?? 0

  const handleExpandRow = (sessionId: number) => {
    setExpandedId(prev => prev === sessionId ? null : sessionId)
  }

  return (
    <div>
      <div className="stat-cards">
        <div className="stat-card">
          <div className="stat-card-label">本番前準備 最終実行</div>
          <div className="stat-card-value">
            {lastPrepare ? formatDateTime(lastPrepare.executedAt) : '—'}
          </div>
          <div className="stat-card-sub">
            {lastPrepare ? (
              <span className={lastPrepare.result === 'success' ? 'badge badge-success' : 'badge badge-failed'}>
                {lastPrepare.result === 'success' ? '成功' : '失敗'} · {formatPrepareSummary(lastPrepare)}
              </span>
            ) : (
              <span style={{ color: '#8a9099' }}>実行履歴なし</span>
            )}
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">直近{stats?.days ?? 30}日 成功率</div>
          <div className="stat-card-value">
            {successRate === null ? '—' : (
              <>
                {successRate.toFixed(1)}<span style={{ fontSize: 13, color: '#8a9099' }}>%</span>
              </>
            )}
          </div>
          <div className="stat-card-sub" style={{ color: '#8a9099', fontFamily: MONO }}>
            {stats && stats.totalSessions > 0
              ? `${stats.successSessions} / ${stats.totalSessions} セッション成功`
              : '対象セッションなし'}
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">実行中セッション</div>
          <div className="stat-card-value" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            {runningCount > 0 && <span className="running-pulse" />}
            {runningCount}
          </div>
          <div className="stat-card-sub" style={{ color: runningCount > 0 ? '#b25e09' : '#8a9099', fontFamily: MONO }}>
            {runningCount > 0
              ? `${stats?.runningDbName ?? '-'} — 適用中…${runningCount > 1 ? ` (他${runningCount - 1}件)` : ''}`
              : 'なし'}
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Pilot 最終適用（kaios）</div>
          <div className="stat-card-value">
            {stats?.lastPilotKaios ? formatDateTime(stats.lastPilotKaios.executedAt) : '—'}
          </div>
          <div className="stat-card-sub" style={{ color: '#8a9099' }}>
            {stats?.lastPilotKaios
              ? `実行者: ${stats.lastPilotKaios.executedBy}`
              : '実行履歴なし'}
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Pilot 最終適用（gos）</div>
          <div className="stat-card-value">
            {stats?.lastPilotGos ? formatDateTime(stats.lastPilotGos.executedAt) : '—'}
          </div>
          <div className="stat-card-sub" style={{ color: '#8a9099' }}>
            {stats?.lastPilotGos
              ? `実行者: ${stats.lastPilotGos.executedBy}`
              : '実行履歴なし'}
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
                  <SessionDetailTable details={s.details} />
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
