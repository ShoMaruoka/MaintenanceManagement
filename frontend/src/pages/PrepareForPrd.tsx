import { useState, useMemo, useEffect, useCallback } from 'react'
import type { DbName, LogLine } from '../types'
import {
  getPrepareFiles,
  startPrepare,
  type ApiPrepareImageSelection,
  type ApiPrepareSelection,
} from '../api/prepare'
import { useUser } from '../context/UserContext'
import PrepareCompareView from '../components/PrepareCompareView'

type PageState = 'select' | 'confirm' | 'running' | 'done'
type ViewMode = 'cards' | 'compare'

const DB_ORDER: DbName[] = ['kaios', 'gos', 'paf', 'duskin']

interface PrepareFile {
  fileName: string
  source: 'deployed' | 'hold'
  dbType: 'sqlserver' | 'mariadb'
}

interface DbEntry {
  dbName: DbName
  files: PrepareFile[]
  imageFiles: string[]
}

function fileKey(dbName: DbName, file: PrepareFile) {
  return `${dbName}::${file.dbType}::${file.source}::${file.fileName}`
}

function imageKey(dbName: DbName, relativePath: string) {
  return `${dbName}::image::${relativePath}`
}

export default function PrepareForPrd() {
  const { currentUser } = useUser()
  const [pageState, setPageState] = useState<PageState>('select')
  const [viewMode, setViewMode] = useState<ViewMode>('cards')
  const [logLines, setLogLines] = useState<LogLine[]>([])
  const [dbEntries, setDbEntries] = useState<DbEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')
  const [checked, setChecked] = useState<Set<string>>(new Set())

  const loadFiles = useCallback(async () => {
    try {
      setLoading(true)
      setError('')
      const entries = await getPrepareFiles()
      const dbMap: Record<DbName, DbEntry> = {
        kaios: { dbName: 'kaios', files: [], imageFiles: [] },
        gos: { dbName: 'gos', files: [], imageFiles: [] },
        paf: { dbName: 'paf', files: [], imageFiles: [] },
        duskin: { dbName: 'duskin', files: [], imageFiles: [] },
      }

      entries.forEach(entry => {
        if (dbMap[entry.dbName]) {
          dbMap[entry.dbName].files = entry.files.map(f => ({
            fileName: f.fileName,
            source: f.source,
            dbType: f.dbType,
          }))
          dbMap[entry.dbName].imageFiles = entry.imageFiles ?? []
        }
      })

      const orderedEntries = Object.values(dbMap)
      setDbEntries(orderedEntries)

      const initial = new Set<string>()
      orderedEntries.forEach(db => {
        db.files.forEach(f => {
          if (f.source === 'deployed') {
            initial.add(fileKey(db.dbName, f))
          }
        })
        db.imageFiles.forEach(path => {
          initial.add(imageKey(db.dbName, path))
        })
      })
      setChecked(initial)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadFiles()
  }, [loadFiles])

  function toggle(key: string) {
    setChecked(prev => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  function toggleAll(dbName: DbName, source: 'deployed' | 'hold', toCheck: boolean) {
    const db = dbEntries.find(d => d.dbName === dbName)
    if (!db) return
    setChecked(prev => {
      const next = new Set(prev)
      db.files.filter(f => f.source === source).forEach(f => {
        const k = fileKey(dbName, f)
        if (toCheck) next.add(k)
        else next.delete(k)
      })
      return next
    })
  }

  function toggleAllImages(dbName: DbName, toCheck: boolean) {
    const db = dbEntries.find(d => d.dbName === dbName)
    if (!db) return
    setChecked(prev => {
      const next = new Set(prev)
      db.imageFiles.forEach(path => {
        const k = imageKey(dbName, path)
        if (toCheck) next.add(k)
        else next.delete(k)
      })
      return next
    })
  }

  const { totalSqlChecked, totalImageChecked, totalChecked } = useMemo(() => {
    let sql = 0
    let images = 0
    dbEntries.forEach(db => {
      db.files.forEach(f => {
        if (checked.has(fileKey(db.dbName, f))) sql++
      })
      db.imageFiles.forEach(path => {
        if (checked.has(imageKey(db.dbName, path))) images++
      })
    })
    return {
      totalSqlChecked: sql,
      totalImageChecked: images,
      totalChecked: sql + images,
    }
  }, [checked, dbEntries])

  async function runPreparation() {
    setPageState('running')
    setLogLines([])

    const selections: ApiPrepareSelection[] = []
    const imageSelections: ApiPrepareImageSelection[] = []
    dbEntries.forEach(db => {
      db.files.forEach(f => {
        const k = fileKey(db.dbName, f)
        selections.push({
          dbName: db.dbName,
          fileName: f.fileName,
          source: f.source,
          dbType: f.dbType,
          apply: checked.has(k),
        })
      })
      db.imageFiles.forEach(path => {
        imageSelections.push({
          dbName: db.dbName,
          relativePath: path,
          apply: checked.has(imageKey(db.dbName, path)),
        })
      })
    })

    const handleLog = (line: LogLine) => {
      setLogLines(prev => [...prev, line])
    }

    const handleDone = () => {
      setPageState('done')
    }

    await startPrepare(
      selections,
      imageSelections,
      currentUser ?? 'unknown',
      handleLog,
      handleDone,
    )
  }

  async function backToSelect() {
    setLogLines([])
    setPageState('select')
    await loadFiles()
  }

  if (loading) {
    return (
      <div style={{ padding: '20px' }}>
        <div style={{ color: '#8a9099' }}>読み込み中...</div>
      </div>
    )
  }

  if (error) {
    return (
      <div style={{ padding: '20px' }}>
        <div style={{ color: '#c5283d' }}>エラー: {error}</div>
      </div>
    )
  }

  if (pageState === 'running' || pageState === 'done') {
    return (
      <div>
        <div style={{ background: '#fff', border: '1px solid #e4e6ea', borderRadius: 8, overflow: 'hidden', marginBottom: 14 }}>
          <div style={{ padding: '11px 16px', borderBottom: '1px solid #eef0f2', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <span style={{ fontSize: 12.5, fontWeight: 600 }}>本番前準備 実行ログ</span>
            <span style={{
              display: 'inline-flex', alignItems: 'center', gap: 6,
              padding: '3px 10px', borderRadius: 5, fontSize: 11, fontWeight: 600,
              background: pageState === 'running' ? '#fbf1dd' : '#e6f4ec',
              color: pageState === 'running' ? '#b25e09' : '#137a4c',
            }}>
              <span style={{ width: 6, height: 6, borderRadius: '50%', background: pageState === 'running' ? '#d98a2b' : '#22a06b', display: 'inline-block' }} />
              {pageState === 'running' ? '実行中' : '完了'}
            </span>
          </div>
          <div style={{ background: '#16181d', padding: '14px 18px', minHeight: 240, maxHeight: 400, overflowY: 'auto', fontFamily: "'JetBrains Mono', monospace", fontSize: 11.5, lineHeight: 1.85 }}>
            {logLines.map((line, i) => (
              <div key={i} style={{
                color: line.level === 'OK'   ? '#5ec48c'
                  : line.level === 'STEP'    ? '#7fb4e8'
                  : line.level === 'INFO'    ? '#6f87c9'
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
        {pageState === 'done' && (
          <button className="btn-secondary" onClick={() => void backToSelect()}>
            ← 本番前準備画面に戻る
          </button>
        )}
      </div>
    )
  }

  return (
    <div>
      <div style={{ marginBottom: 16, fontSize: 12, color: '#6b7280', lineHeight: 1.6 }}>
        各 DB の{' '}
        <span style={{ fontFamily: "'JetBrains Mono', monospace", color: '#3a3f46' }}>deployed/</span>{' '}
        フォルダから本番適用フォルダへコピーします。
        未選択のファイルは{' '}
        <span style={{ fontFamily: "'JetBrains Mono', monospace", color: '#3a3f46' }}>deployed_hold/</span>{' '}
        へ移動して次回まで保留されます。
        画像ファイル（Files）も同様に選択し、本番フォルダへ移動できます。
      </div>

      <button
        className="btn-secondary prep-compare-toggle-btn"
        onClick={() => setViewMode(prev => (prev === 'cards' ? 'compare' : 'cards'))}
      >
        {viewMode === 'compare' ? '← 一覧に戻る' : '比較する'}
      </button>

      {viewMode === 'compare' ? (
        <PrepareCompareView dbEntries={dbEntries} checked={checked} dbOrder={DB_ORDER} />
      ) : (
      <div className="prep-grid">
        {dbEntries.map(db => {
          const deployedFiles = db.files.filter(f => f.source === 'deployed')
          const holdFiles     = db.files.filter(f => f.source === 'hold')
          const deployedCheckedCount = deployedFiles.filter(f => checked.has(fileKey(db.dbName, f))).length
          const holdCheckedCount     = holdFiles.filter(f => checked.has(fileKey(db.dbName, f))).length
          const imageCheckedCount    = db.imageFiles.filter(p => checked.has(imageKey(db.dbName, p))).length
          const allDeployedChecked   = deployedFiles.length > 0 && deployedCheckedCount === deployedFiles.length
          const allHoldChecked       = holdFiles.length > 0 && holdCheckedCount === holdFiles.length
          const allImagesChecked     = db.imageFiles.length > 0 && imageCheckedCount === db.imageFiles.length
          const totalItems = db.files.length + db.imageFiles.length
          const totalCheckedInDb = deployedCheckedCount + holdCheckedCount + imageCheckedCount
          const hasAny = totalItems > 0

          return (
            <div key={db.dbName} className="prep-db-card">
              <div className="prep-db-card-header">
                <span className="prep-db-name">{db.dbName}</span>
                <span className="prep-db-count">
                  {totalCheckedInDb}/{totalItems} 件
                </span>
              </div>

              {!hasAny ? (
                <div className="prep-db-files">
                  <div className="prep-file-item-empty">対象ファイルなし</div>
                </div>
              ) : (
                <div className="prep-db-files">
                  {/* 今回適用する セクション */}
                  <div className="prep-section-header">
                    <span className="prep-section-label prep-section-label-apply">今回適用する（SQL）</span>
                    {deployedFiles.length > 1 && (
                      <button
                        className="prep-toggle-all-btn"
                        onClick={() => toggleAll(db.dbName, 'deployed', !allDeployedChecked)}
                      >
                        {allDeployedChecked ? '全解除' : '全選択'}
                      </button>
                    )}
                  </div>
                  {deployedFiles.length === 0 ? (
                    <div className="prep-file-item-empty">対象ファイルなし</div>
                  ) : (
                    deployedFiles.map(f => {
                      const k = fileKey(db.dbName, f)
                      const isChecked = checked.has(k)
                      return (
                        <div
                          key={k}
                          className={`prep-file-item prep-file-item-selectable${isChecked ? ' selected' : ''}`}
                          onClick={() => toggle(k)}
                        >
                          <span className={`checkbox${isChecked ? ' checked' : ''}`} style={{ width: 13, height: 13, minWidth: 13, borderRadius: 3 }}>
                            {isChecked && (
                              <svg width="8" height="8" viewBox="0 0 10 10">
                                <path d="M1.5 5.2l2.2 2.3L8.5 2.5" stroke="#fff" strokeWidth="1.6" fill="none" strokeLinecap="round" strokeLinejoin="round"/>
                              </svg>
                            )}
                          </span>
                          <span className="prep-file-name">{f.fileName}</span>
                          <span className="prep-file-db-badge">{f.dbType === 'mariadb' ? 'MariaDB' : 'SS'}</span>
                        </div>
                      )
                    })
                  )}

                  {/* 保留中 セクション */}
                  {holdFiles.length > 0 && (
                    <>
                      <div className="prep-section-header" style={{ marginTop: 10 }}>
                        <span className="prep-section-label prep-section-label-hold">保留中（SQL）</span>
                        {holdFiles.length > 1 && (
                          <button
                            className="prep-toggle-all-btn"
                            onClick={() => toggleAll(db.dbName, 'hold', !allHoldChecked)}
                          >
                            {allHoldChecked ? '全解除' : '全選択'}
                          </button>
                        )}
                      </div>
                      {holdFiles.map(f => {
                        const k = fileKey(db.dbName, f)
                        const isChecked = checked.has(k)
                        return (
                          <div
                            key={k}
                            className={`prep-file-item prep-file-item-selectable prep-file-item-hold${isChecked ? ' selected' : ''}`}
                            onClick={() => toggle(k)}
                          >
                            <span className={`checkbox${isChecked ? ' checked' : ''}`} style={{ width: 13, height: 13, minWidth: 13, borderRadius: 3 }}>
                              {isChecked && (
                                <svg width="8" height="8" viewBox="0 0 10 10">
                                  <path d="M1.5 5.2l2.2 2.3L8.5 2.5" stroke="#fff" strokeWidth="1.6" fill="none" strokeLinecap="round" strokeLinejoin="round"/>
                                </svg>
                              )}
                            </span>
                            <span className="prep-file-name">{f.fileName}</span>
                            <span className="prep-file-db-badge">{f.dbType === 'mariadb' ? 'MariaDB' : 'SS'}</span>
                          </div>
                        )
                      })}
                    </>
                  )}

                  {/* 画像ファイル セクション */}
                  <div className="prep-section-header" style={{ marginTop: 10 }}>
                    <span className="prep-section-label prep-section-label-image">画像・静的ファイル</span>
                    {db.imageFiles.length > 1 && (
                      <button
                        className="prep-toggle-all-btn"
                        onClick={() => toggleAllImages(db.dbName, !allImagesChecked)}
                      >
                        {allImagesChecked ? '全解除' : '全選択'}
                      </button>
                    )}
                  </div>
                  {db.imageFiles.length === 0 ? (
                    <div className="prep-file-item-empty">対象ファイルなし</div>
                  ) : (
                    db.imageFiles.map(path => {
                      const k = imageKey(db.dbName, path)
                      const isChecked = checked.has(k)
                      return (
                        <div
                          key={k}
                          className={`prep-file-item prep-file-item-selectable prep-file-item-image${isChecked ? ' selected' : ''}`}
                          onClick={() => toggle(k)}
                        >
                          <span className={`checkbox${isChecked ? ' checked' : ''}`} style={{ width: 13, height: 13, minWidth: 13, borderRadius: 3 }}>
                            {isChecked && (
                              <svg width="8" height="8" viewBox="0 0 10 10">
                                <path d="M1.5 5.2l2.2 2.3L8.5 2.5" stroke="#fff" strokeWidth="1.6" fill="none" strokeLinecap="round" strokeLinejoin="round"/>
                              </svg>
                            )}
                          </span>
                          <span className="prep-file-name" title={path}>{path}</span>
                          <span className="prep-file-db-badge">Files</span>
                        </div>
                      )
                    })
                  )}
                </div>
              )}
            </div>
          )
        })}
      </div>
      )}

      <div className="prep-action-area">
        <div className="prep-action-desc">
          <strong>計 {totalChecked} ファイル</strong>
          {' '}（SQL {totalSqlChecked} / 画像 {totalImageChecked}）を本番適用フォルダへコピー・移動します。
          {(() => {
            let holdCount = 0
            dbEntries.forEach(db => {
              db.files.forEach(f => {
                if (f.source === 'deployed' && !checked.has(fileKey(db.dbName, f))) holdCount++
              })
            })
            return holdCount > 0
              ? <span style={{ color: '#8a5c14', marginLeft: 6 }}>（SQL {holdCount} 件は保留フォルダへ移動）</span>
              : null
          })()}
        </div>
        {pageState === 'confirm' ? (
          <div style={{ display: 'flex', gap: 10 }}>
            <button className="btn-secondary" onClick={() => setPageState('select')}>キャンセル</button>
            <button className="btn-primary" onClick={runPreparation}>実行する</button>
          </div>
        ) : (
          <button
            className="btn-primary"
            disabled={totalChecked === 0}
            onClick={() => setPageState('confirm')}
          >
            本番前準備を実行する
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
            選択した SQL {totalSqlChecked} 件・画像 {totalImageChecked} 件（合計 {totalChecked}）を本番フォルダへコピー／移動します。
            未選択の deployed/ SQL は deployed_hold/ へ移動されます。実行してよろしいですか？
          </span>
        </div>
      )}
    </div>
  )
}
