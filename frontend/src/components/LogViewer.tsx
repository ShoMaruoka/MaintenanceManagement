import { useEffect, useRef, useState } from 'react'
import type { DbName, SelectedModule, LogLine, StepState } from '../types'
import { startDeploy } from '../api/deploy'

interface Props {
  dbName: DbName
  modules: SelectedModule[]
  onDone: () => void
}

const STEPS: { key: StepState['key']; label: string }[] = [
  { key: 'generate',   label: '生成' },
  { key: 'git-update', label: 'git更新' },
  { key: 'merge',      label: 'merge' },
  { key: 'sql-convert',label: 'SQL変換' },
  { key: 'deploy',     label: 'deploy' },
  { key: 'record',     label: '記録' },
]

const VALID_STEPS = new Set(['generate', 'git-update', 'merge', 'sql-convert', 'deploy', 'record'] as const)

export default function LogViewer({ dbName, modules, onDone }: Props) {
  const [lines, setLines] = useState<LogLine[]>([])
  const [stepStates, setStepStates] = useState<Map<StepState['key'], 'pending'|'running'|'done'>>(
    new Map(STEPS.map(s => [s.key, 'pending']))
  )
  const [currentStep, setCurrentStep] = useState<StepState['key']>('generate')
  const [isRunning, setIsRunning] = useState(true)
  const logRef = useRef<HTMLDivElement>(null)
  const abortControllerRef = useRef<AbortController | null>(null)
  // ref で最新の onDone を保持することで deps から除外し、StrictMode 2重実行を防ぐ
  const onDoneRef = useRef(onDone)
  onDoneRef.current = onDone

  // デプロイはマウント時に1回だけ実行する（deps [] = StrictMode の2重呼び出しを防止）
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    const ac = new AbortController()
    abortControllerRef.current = ac

    const handleLog = (line: LogLine, stepKey?: string) => {
      setLines(prev => [...prev, line])
      if (stepKey && VALID_STEPS.has(stepKey as StepState['key'])) {
        const step = stepKey as StepState['key']
        setStepStates(prev => {
          const next = new Map(prev)
          next.set(step, 'done')
          return next
        })
        const nextIdx = STEPS.findIndex(s => s.key === step) + 1
        if (nextIdx < STEPS.length) setCurrentStep(STEPS[nextIdx].key)
      }
    }

    const handleDone = () => {
      setIsRunning(false)
      onDoneRef.current()
    }

    startDeploy(dbName, modules, handleLog, handleDone, undefined, ac.signal)

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
          <span className="header-title-text">STG 適用</span>
          <span className="header-title-path">/ deploy</span>
        </div>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '4px 11px', borderRadius: 6, background: isRunning ? '#fbf1dd' : '#e6f4ec', color: isRunning ? '#b25e09' : '#137a4c', fontSize: 12, fontWeight: 600 }}>
          <span style={{ width: 7, height: 7, borderRadius: '50%', background: isRunning ? '#d98a2b' : '#22a06b', display: 'inline-block' }} />
          {isRunning ? '実行中' : '完了'}
        </div>
      </div>

      {/* Stepper */}
      <div className="log-stepper">
        {STEPS.map((step, i) => {
          const state = stepStates.get(step.key) ?? 'pending'
          const isActive = currentStep === step.key && isRunning
          const isDone = state === 'done'
          const isNext = i > 0 && stepStates.get(STEPS[i - 1].key) === 'done'
          const lineState = isDone ? 'done' : isNext ? 'running' : 'pending'

          return (
            <div key={step.key} style={{ display: 'flex', alignItems: 'center', gap: 0, flex: i === STEPS.length - 1 ? 'none' : 1 }}>
              <div className="step-item">
                <span className={`step-circle${isDone ? ' done' : isActive ? ' running' : ''}`}>
                  {isDone
                    ? <svg width="9" height="9" viewBox="0 0 10 10"><path d="M1.5 5.2l2.2 2.3L8.5 2.5" stroke="#fff" strokeWidth="1.6" fill="none" strokeLinecap="round" strokeLinejoin="round"/></svg>
                    : isActive
                      ? <span className="step-circle-inner" />
                      : null
                  }
                </span>
                <span className={`step-label${isDone ? ' done' : isActive ? ' running' : ''}`}>{step.label}</span>
              </div>
              {i < STEPS.length - 1 && (
                <div className={`step-line${lineState === 'done' ? ' done' : lineState === 'running' ? ' running' : ''}`} />
              )}
            </div>
          )
        })}
      </div>

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
          {isRunning ? 'SSE 接続中 — 自動スクロール' : '実行完了'}
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
