import { useState, useEffect, useCallback, useRef } from 'react'
import type { LogLine } from '../types'
import {
  getWebSourceInfo,
  startWebSourceDeploy,
  type ApiWebSourceInfo,
  type ApiWebSourceDeployDone,
  type WebSourceDbName,
  type WebSourceDeployStep,
} from '../api/webSourcePrepare'
import { useUser } from '../context/UserContext'

type PageState = 'select' | 'confirm' | 'running' | 'done'

const DB_OPTIONS: { value: WebSourceDbName; label: string }[] = [
  { value: 'kaios', label: 'kaios' },
  { value: 'gos', label: 'gos' },
]

const STEP_OPTIONS: { value: WebSourceDeployStep; label: string; desc: string }[] = [
  { value: 'both', label: '両方', desc: 'Webソースコピー（pilot1→pilot2）＋SQL適用' },
  { value: 'web', label: 'Webソースコピーのみ', desc: 'SQL適用は行いません' },
  { value: 'sql', label: 'SQL適用のみ', desc: 'Webソースコピーは行わず、SQL適用のみ実行します' },
]

export default function WebSourcePrepare() {
  const { currentUser } = useUser()
  const [dbName, setDbName] = useState<WebSourceDbName>('kaios')
  const [step, setStep] = useState<WebSourceDeployStep>('both')
  const [info, setInfo] = useState<ApiWebSourceInfo | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')
  const [pageState, setPageState] = useState<PageState>('select')
  const [logLines, setLogLines] = useState<LogLine[]>([])
  const [currentTarget, setCurrentTarget] = useState<string>('')
  const [result, setResult] = useState<ApiWebSourceDeployDone | null>(null)
  const logRef = useRef<HTMLDivElement>(null)

  // info取得のレース対策: 最後に発行したリクエストの世代のみ反映する
  const infoRequestSeq = useRef(0)

  useEffect(() => {
    if (logRef.current) {
      logRef.current.scrollTop = logRef.current.scrollHeight
    }
  }, [logLines])

  const loadInfo = useCallback(async (target: WebSourceDbName) => {
    const seq = ++infoRequestSeq.current
    try {
      setLoading(true)
      setError('')
      const data = await getWebSourceInfo(target)
      if (seq !== infoRequestSeq.current) return
      setInfo(data)
    } catch (err) {
      if (seq !== infoRequestSeq.current) return
      setError((err as Error).message)
      setInfo(null)
    } finally {
      if (seq === infoRequestSeq.current) setLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadInfo(dbName)
  }, [dbName, loadInfo])

  function handleLog(line: LogLine) {
    setLogLines(prev => [...prev, line])
    // "▶ {targetName} 適用開始" 形式のログから現在処理中ターゲットを推定
    const match = info?.pilotTargets.find(t => line.message.includes(`${t.name} 適用開始`))
    if (match) setCurrentTarget(match.name)
  }

  function handleDone(doneResult: ApiWebSourceDeployDone) {
    setResult(doneResult)
    setPageState('done')
  }

  function handleError(err: Error) {
    setError(err.message)
    setPageState('done')
  }

  async function runDeploy() {
    setPageState('running')
    setLogLines([])
    setCurrentTarget('')
    setResult(null)
    setError('')

    let completed = false
    const markDone = (fn: () => void) => {
      completed = true
      fn()
    }

    try {
      await startWebSourceDeploy(
        dbName,
        currentUser ?? 'unknown',
        step,
        handleLog,
        (doneResult) => markDone(() => handleDone(doneResult)),
        (err) => markDone(() => handleError(err)),
      )
      // ストリームが done/error を送らずに終了した場合（Backend側の想定外切断等）に備え、
      // 実行中のまま残留しないよう失敗扱いへ遷移させる
      if (!completed) {
        handleError(new Error('サーバーからの完了通知を受信できませんでした。pilot サーバーの状態を直接確認してください。'))
      }
    } catch (err) {
      if (!completed) {
        handleError(err instanceof Error ? err : new Error(String(err)))
      }
    }
  }

  function backToSelect() {
    setLogLines([])
    setResult(null)
    setPageState('select')
    void loadInfo(dbName)
  }

  if (pageState === 'running' || pageState === 'done') {
    return (
      <div>
        <div style={{ background: '#fff', border: '1px solid #e4e6ea', borderRadius: 8, overflow: 'hidden', marginBottom: 14 }}>
          <div style={{ padding: '11px 16px', borderBottom: '1px solid #eef0f2', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <span style={{ fontSize: 12.5, fontWeight: 600 }}>
              Pilot環境適用 実行ログ（{dbName}）
              {pageState === 'running' && currentTarget && (
                <span style={{ marginLeft: 8, color: '#6b7280', fontWeight: 400 }}>
                  処理中: {currentTarget}
                </span>
              )}
            </span>
            <span style={{
              display: 'inline-flex', alignItems: 'center', gap: 6,
              padding: '3px 10px', borderRadius: 5, fontSize: 11, fontWeight: 600,
              background: pageState === 'running' ? '#fbf1dd' : (result?.success ? '#e6f4ec' : '#fbe6e6'),
              color: pageState === 'running' ? '#b25e09' : (result?.success ? '#137a4c' : '#c5283d'),
            }}>
              <span style={{
                width: 6, height: 6, borderRadius: '50%',
                background: pageState === 'running' ? '#d98a2b' : (result?.success ? '#22a06b' : '#c5283d'),
                display: 'inline-block',
              }} />
              {pageState === 'running' ? '実行中' : (result?.success ? '完了' : '失敗')}
            </span>
          </div>
          <div ref={logRef} style={{ background: '#16181d', padding: '14px 18px', minHeight: 240, maxHeight: 400, overflowY: 'auto', fontFamily: "'JetBrains Mono', monospace", fontSize: 11.5, lineHeight: 1.85 }}>
            {logLines.map((line, i) => (
              <div key={i} style={{
                color: line.level === 'OK'   ? '#5ec48c'
                  : line.level === 'STEP'    ? '#7fb4e8'
                  : line.level === 'INFO'    ? '#6f87c9'
                  : line.level === 'ERROR'   ? '#e57373'
                  : line.level === 'WARN'    ? '#e0a44b'
                  : '#cdd2da'
              }}>
                {line.timestamp} [{line.level}] {line.message}
              </div>
            ))}
            {pageState === 'running' && (
              <span style={{ display: 'inline-block', width: 7, height: 13, background: '#e0a44b', marginLeft: 4, verticalAlign: 'middle', animation: 'blink 1s step-end infinite' }} />
            )}
          </div>
        </div>

        {pageState === 'done' && result && (
          <div style={{ background: '#fff', border: '1px solid #e4e6ea', borderRadius: 8, padding: '12px 16px', marginBottom: 14 }}>
            <div style={{ fontSize: 12.5, fontWeight: 600, marginBottom: 8 }}>ターゲット別結果</div>
            {result.targets.map(t => (
              <div key={t.targetName} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, padding: '4px 0' }}>
                <span style={{ color: t.success ? '#22a06b' : '#c5283d' }}>{t.success ? '✓' : '✗'}</span>
                <span style={{ fontWeight: 600 }}>{t.targetName}</span>
                {!t.success && t.errorMessage && (
                  <span style={{ color: '#c5283d' }}>{t.errorMessage}</span>
                )}
              </div>
            ))}
            {result.sqlDeploy && (
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, padding: '4px 0', borderTop: '1px solid #eef0f2', marginTop: 6, paddingTop: 8 }}>
                <span style={{ color: result.sqlDeploy.success ? '#22a06b' : '#c5283d' }}>{result.sqlDeploy.success ? '✓' : '✗'}</span>
                <span style={{ fontWeight: 600 }}>SQL適用</span>
                {!result.sqlDeploy.success && result.sqlDeploy.errorMessage && (
                  <span style={{ color: '#c5283d' }}>{result.sqlDeploy.errorMessage}</span>
                )}
              </div>
            )}
          </div>
        )}

        {pageState === 'done' && error && (
          <div style={{ marginBottom: 14, padding: '10px 14px', background: '#fbe6e6', border: '1px solid #f0cccc', borderRadius: 7, fontSize: 12, color: '#c5283d' }}>
            エラー: {error}
          </div>
        )}

        {pageState === 'done' && (
          <button className="btn-secondary" onClick={backToSelect}>
            ← Pilot環境適用画面に戻る
          </button>
        )}
      </div>
    )
  }

  return (
    <div>
      <div style={{ marginBottom: 16, fontSize: 12, color: '#6b7280', lineHeight: 1.6 }}>
        STG サーバーの Web ソース（公開フォルダ）を pilot1 → pilot2 の順に自動でコピーします（削除同期なしの全量コピーのみ）。
        コピー完了後に各 pilot 側 web.config の接続文字列を書き換えます。pilot1 が失敗した場合、pilot2 は実行されません。
        すべて成功した場合、続けて SQL ファイルの pilot 環境への適用（コピー＋deploy.bat実行）を行います。
      </div>

      <div style={{ background: '#fff', border: '1px solid #e4e6ea', borderRadius: 8, padding: '16px 18px', marginBottom: 14 }}>
        <div style={{ marginBottom: 14 }}>
          <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 6 }}>対象システム</div>
          <div style={{ display: 'flex', gap: 8 }}>
            {DB_OPTIONS.map(opt => (
              <button
                key={opt.value}
                className={dbName === opt.value ? 'btn-primary' : 'btn-secondary'}
                onClick={() => setDbName(opt.value)}
              >
                {opt.label}
              </button>
            ))}
          </div>
        </div>

        {loading ? (
          <div style={{ color: '#8a9099', fontSize: 12 }}>読み込み中...</div>
        ) : error ? (
          <div style={{ color: '#c5283d', fontSize: 12 }}>エラー: {error}</div>
        ) : info ? (
          <div style={{ marginBottom: 14 }}>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 6 }}>コピー元・コピー先</div>
            <div style={{ fontSize: 11.5, fontFamily: "'JetBrains Mono', monospace", background: '#f6f7f9', borderRadius: 6, padding: '10px 12px', lineHeight: 1.9 }}>
              <div>STG: {info.webSourcePath}</div>
              {info.pilotTargets.map(t => (
                <div key={t.name}>{t.name}: {t.destWebSourcePath}</div>
              ))}
            </div>
          </div>
        ) : null}

        <div>
          <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 6 }}>実行内容</div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {STEP_OPTIONS.map(opt => (
              <button
                key={opt.value}
                className={step === opt.value ? 'btn-primary' : 'btn-secondary'}
                onClick={() => setStep(opt.value)}
                title={opt.desc}
              >
                {opt.label}
              </button>
            ))}
          </div>
          <div style={{ fontSize: 11, color: '#8a9099', marginTop: 6 }}>
            {STEP_OPTIONS.find(o => o.value === step)?.desc}
          </div>
        </div>
      </div>

      <div className="prep-action-area">
        <div className="prep-action-desc">
          {step === 'both' && <><strong>{dbName}</strong> の Web ソースを pilot1 → pilot2 へコピーし、続けて SQL を適用します。</>}
          {step === 'web' && <><strong>{dbName}</strong> の Web ソースを pilot1 → pilot2 へコピーします（SQL適用は行いません）。</>}
          {step === 'sql' && <><strong>{dbName}</strong> の SQL を pilot 環境へ適用します（Webソースコピーは行いません）。</>}
        </div>
        {pageState === 'confirm' ? (
          <div style={{ display: 'flex', gap: 10 }}>
            <button className="btn-secondary" onClick={() => setPageState('select')}>キャンセル</button>
            <button className="btn-primary" onClick={runDeploy}>実行する</button>
          </div>
        ) : (
          <button
            className="btn-primary"
            disabled={loading || !info}
            onClick={() => setPageState('confirm')}
          >
            Pilot環境適用を実行する
            <svg width="13" height="13" viewBox="0 0 14 14" fill="none">
              <path d="M3 7h8M7.5 3.5L11 7l-3.5 3.5" stroke="#fff" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
          </button>
        )}
      </div>

      {pageState === 'confirm' && (
        <div style={{ marginTop: 12, padding: '10px 14px', background: '#fbf1dd', border: '1px solid #ece2cf', borderRadius: 7, fontSize: 12, color: '#7a6433', display: 'flex', gap: 8 }}>
          <span style={{ color: '#b25e09' }}>●</span>
          <span>
            {dbName} に対して「{STEP_OPTIONS.find(o => o.value === step)?.label}」を実行します。実行してよろしいですか？
          </span>
        </div>
      )}
    </div>
  )
}
