import { useState, useMemo, useEffect, useCallback } from 'react'
import type { DbName, Module, ModuleType, OpType, SelectedModule } from '../types'
import ConfirmDialog from '../components/ConfirmDialog'
import LogViewer from '../components/LogViewer'
import { getDbList, getModules } from '../api/modules'
import type { DbListItem } from '../api/modules'

const MODULE_TYPES: ModuleType[] = [
  'StoredProcedure',
  'Function',
  'VIEW',
  'Table',
  'UserDefinedTableType',
  'MariaDB',
]

const OP_TYPES: OpType[] = ['更新', '新規', '削除']

type PageState = 'select' | 'confirm' | 'log' | 'done'

export default function DeployStg() {
  const [dbConfigs, setDbConfigs] = useState<DbListItem[]>([])
  const [selectedDb, setSelectedDb] = useState<DbName>('kaios')
  const [activeType, setActiveType] = useState<ModuleType>('StoredProcedure')
  const [selectedModules, setSelectedModules] = useState<Map<string, OpType>>(new Map())
  const [search, setSearch] = useState('')
  const [pageState, setPageState] = useState<PageState>('select')
  const [modulesByDb, setModulesByDb] = useState<Record<DbName, Record<ModuleType, Module[]>>>({
    kaios: {} as Record<ModuleType, Module[]>,
    gos: {} as Record<ModuleType, Module[]>,
    paf: {} as Record<ModuleType, Module[]>,
    duskin: {} as Record<ModuleType, Module[]>,
  })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string>('')

  useEffect(() => {
    getDbList().then(setDbConfigs).catch(() => {})
  }, [])

  useEffect(() => {
    const loadModules = async () => {
      try {
        setLoading(true)
        setError('')
        const modules = await getModules(selectedDb)
        setModulesByDb(prev => ({ ...prev, [selectedDb]: modules }))
      } catch (err) {
        setError((err as Error).message)
      } finally {
        setLoading(false)
      }
    }

    loadModules()
  }, [selectedDb])

  const currentModules = modulesByDb[selectedDb]?.[activeType] ?? []
  const filteredModules = useMemo(
    () => currentModules.filter(m => m.name.toLowerCase().includes(search.toLowerCase())),
    [currentModules, search],
  )

  const totalSelected = selectedModules.size

  function toggleModule(module: Module) {
    const next = new Map(selectedModules)
    if (next.has(module.name)) {
      next.delete(module.name)
    } else {
      next.set(module.name, '更新')
    }
    setSelectedModules(next)
  }

  function setOpType(name: string, op: OpType) {
    const next = new Map(selectedModules)
    next.set(name, op)
    setSelectedModules(next)
  }

  function selectAll() {
    const next = new Map(selectedModules)
    filteredModules.forEach(m => { if (!next.has(m.name)) next.set(m.name, '更新') })
    setSelectedModules(next)
  }

  function clearAll() {
    setSelectedModules(new Map())
  }

  const selectedInCurrentType = filteredModules.filter(m => selectedModules.has(m.name))
  const opsCount = { '新規': 0, '更新': 0, '削除': 0 }
  selectedModules.forEach(op => { opsCount[op] = (opsCount[op] ?? 0) + 1 })

  const confirmModules = useMemo((): SelectedModule[] =>
    Array.from(selectedModules.entries()).map(([name, opType]) => {
      const allModules = Object.values(modulesByDb[selectedDb] ?? {}).flat()
      const found = allModules.find(m => m.name === name)
      return { name, opType, type: found?.type ?? 'StoredProcedure' }
    }),
    [selectedModules, modulesByDb, selectedDb],
  )

  const handleDone = useCallback(() => setPageState('done'), [])

  if (pageState === 'log' || pageState === 'done') {
    return (
      <div style={{ height: 'calc(100vh - 52px - 40px)', display: 'flex', flexDirection: 'column' }}>
        <LogViewer
          dbName={selectedDb}
          modules={confirmModules}
          onDone={handleDone}
        />
        {pageState === 'done' && (
          <div style={{ padding: '12px 0 0' }}>
            <button className="btn-secondary" onClick={() => { setSelectedModules(new Map()); setPageState('select') }}>
              ← 適用画面に戻る
            </button>
          </div>
        )}
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', height: 'calc(100vh - 52px - 40px)', gap: 0, background: '#f4f5f7', margin: '-20px -22px', overflow: 'hidden' }}>
      {/* DB 選択 */}
      <div className="db-selector">
        <div className="db-selector-label">DB 選択</div>
        <div className="db-selector-list">
          {dbConfigs.map(db => (
            <div
              key={db.name}
              className={`db-item${selectedDb === db.name ? ' selected' : ''}`}
              onClick={() => { setSelectedDb(db.name); setSelectedModules(new Map()); setSearch('') }}
            >
              <span className="db-item-radio">
                {selectedDb === db.name && <span className="db-item-radio-inner" />}
              </span>
              <div>
                <div className="db-item-name">{db.name}</div>
                <div className="db-item-devdb">{db.devDb}</div>
              </div>
            </div>
          ))}
        </div>
        <div className="db-target-info">
          <div className="db-target-label">適用先</div>
          <div className="db-target-value">
            {selectedDb}_dev <span style={{ color: '#c4c9d1' }}>→</span> <span>STG</span>
          </div>
        </div>
        <div className="db-selector-note">複数 DB は順次実行されます。並列実行は行いません。</div>
      </div>

      {/* Module Tree */}
      <div className="module-tree-area">
        <div className="module-tree-toolbar">
          <div className="module-tree-title">
            <span className="module-tree-title-text">モジュールツリー</span>
            <span className="module-tree-db-label">{selectedDb}_dev</span>
          </div>
          {totalSelected > 0 && (
            <span className="module-tree-selected-count">{totalSelected} 件選択中</span>
          )}
        </div>

        <div className="module-tree-inner">
          {/* Category tabs */}
          <div className="module-categories">
            <div className="module-cat-label">種別</div>
            {MODULE_TYPES.map((type) => {
              const modules = modulesByDb[selectedDb]?.[type] ?? []
              const selCount = modules.filter(m => selectedModules.has(m.name)).length
              return (
                <div
                  key={type}
                  className={`module-cat-item${activeType === type ? ' active' : ''}`}
                  onClick={() => { setActiveType(type); setSearch('') }}
                >
                  <span className="module-cat-name">{type}</span>
                  {selCount > 0
                    ? <span className="module-cat-count-selected">{selCount}/{modules.length}</span>
                    : <span className="module-cat-count">{modules.length}</span>
                  }
                </div>
              )
            })}
            <div className="module-cat-footer">
              合計 <strong>{totalSelected}</strong> モジュール選択中
            </div>
          </div>

          {/* Module list */}
          <div className="module-list-area">
            <div className="module-list-search-bar">
              <div className="module-search-input-wrap">
                <svg width="12" height="12" viewBox="0 0 14 14" fill="none">
                  <circle cx="6" cy="6" r="4.2" stroke="#9aa0a8" strokeWidth="1.4"/>
                  <path d="M9.2 9.2L12 12" stroke="#9aa0a8" strokeWidth="1.4" strokeLinecap="round"/>
                </svg>
                <input
                  placeholder="名前で絞り込み"
                  value={search}
                  onChange={e => setSearch(e.target.value)}
                />
              </div>
              <span className="select-all-btn" onClick={selectAll}>すべて選択</span>
            </div>

            <div className="module-list">
              {loading && (
                <div className="empty-state">読み込み中...</div>
              )}
              {error && (
                <div className="empty-state" style={{ color: '#c5283d' }}>エラー: {error}</div>
              )}
              {!loading && !error && filteredModules.length === 0 && (
                <div className="empty-state">モジュールがありません</div>
              )}
              {!loading && !error && filteredModules.map(module => {
                const isSelected = selectedModules.has(module.name)
                const opType = selectedModules.get(module.name) ?? '更新'
                return (
                  <div
                    key={module.name}
                    className={`module-item${isSelected ? ' selected' : ''}`}
                    onClick={() => toggleModule(module)}
                  >
                    <span className={`checkbox${isSelected ? ' checked' : ''}`}>
                      {isSelected && (
                        <svg width="9" height="9" viewBox="0 0 10 10">
                          <path d="M1.5 5.2l2.2 2.3L8.5 2.5" stroke="#fff" strokeWidth="1.6" fill="none" strokeLinecap="round" strokeLinejoin="round"/>
                        </svg>
                      )}
                    </span>
                    <div className="module-item-info">
                      <div className="module-item-name">{module.name}</div>
                      <div className="module-item-date">
                        modify_date {module.modifyDate}
                        {(module.type === 'Table' || module.type === 'UserDefinedTableType') && (
                          <span className="module-git-only-badge">Git マージのみ</span>
                        )}
                      </div>
                    </div>
                    {isSelected ? (
                      <div onClick={e => e.stopPropagation()}>
                        <select
                          value={opType}
                          onChange={e => setOpType(module.name, e.target.value as OpType)}
                          className={`op-badge op-badge-${opType === '更新' ? 'update' : opType === '新規' ? 'new' : 'delete'}`}
                          style={{ border: 'none', outline: 'none', appearance: 'none', cursor: 'pointer', paddingRight: 14 }}
                        >
                          {OP_TYPES.map(op => (
                            <option key={op} value={op}>{op}</option>
                          ))}
                        </select>
                      </div>
                    ) : (
                      <span className="module-item-unselected">未選択</span>
                    )}
                  </div>
                )
              })}
            </div>

            <div className="module-list-footer">
              <span className="module-list-footer-left">
                {activeType} — <span style={{ color: '#3858d6', fontWeight: 600 }}>{selectedInCurrentType.length} / {filteredModules.length}</span> 選択
              </span>
              <span className="module-list-footer-right">
                操作区分{' '}
                {opsCount['更新'] > 0 && <span style={{ color: '#1f5fd0', fontWeight: 600 }}>更新 {opsCount['更新']}</span>}
                {opsCount['更新'] > 0 && (opsCount['新規'] > 0 || opsCount['削除'] > 0) && ' ・ '}
                {opsCount['新規'] > 0 && <span style={{ color: '#137a4c', fontWeight: 600 }}>新規 {opsCount['新規']}</span>}
                {opsCount['新規'] > 0 && opsCount['削除'] > 0 && ' ・ '}
                {opsCount['削除'] > 0 && <span style={{ color: '#c5283d', fontWeight: 600 }}>削除 {opsCount['削除']}</span>}
              </span>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="deploy-footer">
          <div className="deploy-footer-info">
            <strong>{totalSelected}</strong> 件のモジュールを選択中
            {totalSelected > 0 && (
              <span style={{ color: '#aab0b4' }}>
                {' '}（{opsCount['更新'] > 0 && `更新 ${opsCount['更新']}`}
                {opsCount['更新'] > 0 && opsCount['新規'] > 0 && ' ・ '}
                {opsCount['新規'] > 0 && `新規 ${opsCount['新規']}`}
                {(opsCount['更新'] > 0 || opsCount['新規'] > 0) && opsCount['削除'] > 0 && ' ・ '}
                {opsCount['削除'] > 0 && `削除 ${opsCount['削除']}`}）
              </span>
            )}
          </div>
          <div className="deploy-footer-actions">
            {totalSelected > 0 && (
              <button className="btn-ghost" onClick={clearAll}>選択をクリア</button>
            )}
            <button
              className="btn-primary"
              disabled={totalSelected === 0}
              onClick={() => setPageState('confirm')}
            >
              実行内容を確認する
              <svg width="13" height="13" viewBox="0 0 14 14" fill="none">
                <path d="M3 7h8M7.5 3.5L11 7l-3.5 3.5" stroke="#fff" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
              </svg>
            </button>
          </div>
        </div>
      </div>

      {/* Confirm Dialog */}
      {pageState === 'confirm' && (
        <ConfirmDialog
          dbName={selectedDb}
          modules={confirmModules}
          onConfirm={() => setPageState('log')}
          onCancel={() => setPageState('select')}
        />
      )}
    </div>
  )
}
