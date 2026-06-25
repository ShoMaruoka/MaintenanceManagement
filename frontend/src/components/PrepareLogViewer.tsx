import { useEffect, useRef, useState } from 'react'
import type { DbName, LogLine } from '../types'
import { fetchStream } from '../api/client'

interface Props {
  dbName: DbName
  onDone: () => void
}

interface ApiLogEntry {
  timestamp: string
  level: 'INFO' | 'STEP' | 'OK' | 'RUN' | 'WARN' | 'ERROR' | 'DETAIL'
  message: string
}

interface ApiPrepareDone {
  type: 'done'
  applied: number
  held: number
}

type ApiPrepareStreamEvent = (ApiLogEntry & { type?: never }) | ApiPrepareDone

export default function PrepareLogViewer({ dbName, onDone }: Props) {
  const [lines, setLines] = useState<LogLine[]>([])
  const [isRunning, setIsRunning] = useState(true)
  const [applied, setApplied] = useState(0)
  const [held, setHeld] = useState(0)
  const logRef = useRef<HTMLDivElement>(null)
  const abortControllerRef = useRef<AbortController | null>(null)
  const onDoneRef = useRef(onDone)
  onDoneRef.current = onDone

  useEffect(() => {
    const ac = new AbortController()
    abortControllerRef.current = ac

    fetchStream<ApiPrepareStreamEvent>(
      '/prepare/stream',
      (event) => {
        if ('type' in event && event.type === 'done') {
          setApplied((event as ApiPrepareDone).applied)
          setHeld((event as ApiPrepareDone).held)
          setIsRunning(false)
          onDoneRef.current()
        } else if ('timestamp' in event) {
          setLines(prev => [...prev, {
            timestamp: event.timestamp,
            level: event.level,
            message: event.message,
          }])
        }
      },
      {
        method: 'POST',
        body: JSON.stringify({ dbName }),
        signal: ac.signal,
      },
      undefined,
    ).catch(() => {})

    return () => {
      ac.abort()
    }
  }, [])

  useEffect(() => {
    if (logRef.current) {
      logRef.current.scrollTop = logRef.current.scrollHeight
    }
  }, [lines])

  function levelClass(level: LogLine['level']): string {
    switch (level) {
      case 'INFO':   return 'log-level-info'
      case 'STEP':   return 'log-level-step'
      case 'OK':     return 'log-level-ok'
      case 'RUN':    return 'log-level-run'
      case 'WARN':   return 'log-level-warn'
      case 'ERROR':  return 'log-level-error'
      case 'DETAIL': return 'log-level-detail'
    }
  }

  function msgClass(level: LogLine['level']): string {
    if (level === 'DETAIL') return 'log-msg-detail'
    if (level === 'OK')     return 'log-msg-ok'
    return 'log-msg-default'
  }

  function copyLog() {
    const text = lines.map(l => `${l.timestamp} [${l.level}] ${l.message}`).join('\n')
    navigator.clipboard.writeText(text).catch(() => {})
  }

  function abort() {
    abortControllerRef.current?.abort()
    setIsRunning(false)
  }

  return (
    <div style={{ background: '#fff', border: '1px solid #e4e6ea', borderRadius: 8, overflow: 'hidden', display: 'flex', flexDirection: 'column', height: '100%' }}>
      {/* Header */}
      <div className="header" style={{ borderBottom: '1px solid #e4e6ea', padding: '0 22px' }}>
        <div className="header-title">
          <span className="header-title-text">本番前準備</span>
          <span className="header-title-path">/ prepare</span>
        </div>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '4px 11px', borderRadius: 6, background: isRunning ? '#fbf1dd' : '#e6f4ec', color: isRunning ? '#b25e09' : '#137a4c', fontSize: 12, fontWeight: 600 }}>
          <span style={{ width: 7, height: 7, borderRadius: '50%', background: isRunning ? '#d98a2b' : '#22a06b', display: 'inline-block' }} />
          {isRunning ? '実行中' : '完了'}
        </div>
      </div>

      {/* Status */}
      {!isRunning && (
        <div style={{ padding: '12px 22px', background: '#e6f4ec', borderBottom: '1px solid #e4e6ea' }}>
          <div style={{ fontSize: 13, color: '#137a4c', fontWeight: 500 }}>
            適用: <strong>{applied}</strong> 件  /  保留: <strong>{held}</strong> 件
          </div>
        </div>
      )}

      {/* Log output */}
      <div className="log-output" ref={logRef}>
        {lines.map((line, i) => (
          <div key={i} className="log-line">
            <span className="log-ts">{line.timestamp}</span>
            <span className={levelClass(line.level)}>{line.level.padEnd(6)}</span>
            <span className={msgClass(line.level)}>{line.message}</span>
            {i === lines.length - 1 && isRunning && <span className="log-cursor" />}
          </div>
        ))}
      </div>

      {/* Footer */}
      <div className="log-footer">
        <div className="log-footer-status">
          <span className="log-footer-status-dot" style={{ background: isRunning ? '#5ec48c' : '#3858d6' }} />
          {isRunning ? 'SSE 接続中 — 自動スクロール' : '準備完了'}
        </div>
        <div className="log-footer-actions">
          <button className="btn-log-copy" onClick={copyLog}>ログをコピー</button>
          {isRunning && (
            <button className="btn-log-abort" onClick={abort}>
              中断
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
