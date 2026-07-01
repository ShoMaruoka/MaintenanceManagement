import { useEffect, useRef, useState } from 'react'
import type { LogLine, StepState, MultiDbModules } from '../types'
import { startDeploy } from '../api/deploy'
import { useUser } from '../context/UserContext'

interface Props {
  allModules: MultiDbModules
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

const INITIAL_STEP_STATES = () =>
  new Map(STEPS.map(s => [s.key, 'pending' as const]))

export default function LogViewer({ allModules, onDone }: Props) {
  const { currentUser } = useUser()
  const [lines, setLines] = useState<LogLine[]>([])
  const [stepStates, setStepStates] = useState<Map<StepState['key'], 'pending'|'running'|'done'>>(INITIAL_STEP_STATES())
  const [currentStep, setCurrentStep] = useState<StepState['key']>('generate')
  const [currentDbLabel, setCurrentDbLabel] = useState<string>('')
  const [isRunning, setIsRunning] = useState(true)
  const logRef = useRef<HTMLDivElement>(null)
  const abortControllerRef = useRef<AbortController | null>(null)
  const onDoneRef = useRef(onDone)
  const startedRef = useRef(false)
  onDoneRef.current = onDone

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    // StrictMode は useEffect を2回実行するが ref でガードして1回だけ実行する
    // cleanup で abort しないのは、中断で InsertDeploySession が "running" のまま残るのを防ぐため
    if (startedRef.current) return
    startedRef.current = true

    const ac = new AbortController()
    abortControllerRef.current = ac

    const addLine = (line: LogLine) => setLines(prev => [...prev, line])

    const runAll = async () => {
      const total = allModules.length

      for (let i = 0; i < allModules.length; i++) {
        if (ac.signal.aborted) break

        const { db, modules } = allModules[i]
        setCurrentDbLabel(`${db}（${i + 1}/${total}）`)

        // DBヘッダーをログに追加
        addLine({
          timestamp: new Date().toLocaleTimeString('ja-JP', { hour12: false }),
          level: 'STEP',
          message: `=== ${db} 適用開始（${i + 1}/${total}） ===`,
        })

        // ステップをリセット
        setStepStates(INITIAL_STEP_STATES())
        setCurrentStep('generate')

        await new Promise<void>(resolve => {
          startDeploy(
            db,
            modules,
            currentUser ?? 'unknown',
            (line: LogLine, stepKey?: string) => {
              addLine(line)
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
            },
            (_sessionId: number) => resolve(),  // onDone: 完了（成功・失敗問わず）
            () => resolve(),                   // onError: エラーでも次のDBへ継続
            ac.signal,
          )
        })

        if (ac.signal.aborted) break

        addLine({
          timestamp: new Date().toLocaleTimeString('ja-JP', { hour12: false }),
          level: 'OK',
          message: `=== ${db} 適用完了（${i + 1}/${total}） ===`,
        })
      }

      setIsRunning(false)
      onDoneRef.current()
    }

    runAll()

    // cleanup では abort しない（StrictMode の2回目マウントで中断が起きるのを防ぐ）
    // ユーザーによる中断は abort() ボタン経由で abortControllerRef を使う
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
          {currentDbLabel && (
            <span className="header-title-path">/ {currentDbLabel}</span>
          )}
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
