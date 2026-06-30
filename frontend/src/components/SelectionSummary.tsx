import { useState } from 'react'
import type { DbName, ModuleType, OpType } from '../types'

interface Props {
  selectedModulesByDb: Map<DbName, Map<string, OpType>>
  moduleTypeOf: (db: DbName, name: string) => ModuleType
  onRemove: (db: DbName, name: string) => void
}

export default function SelectionSummary({ selectedModulesByDb, moduleTypeOf, onRemove }: Props) {
  const [expandedDbs, setExpandedDbs] = useState<Set<DbName>>(new Set())

  const activeDbs = Array.from(selectedModulesByDb.entries()).filter(([, m]) => m.size > 0)
  if (activeDbs.length === 0) return null

  const totalAll = activeDbs.reduce((sum, [, m]) => sum + m.size, 0)

  function toggleDb(db: DbName) {
    setExpandedDbs(prev => {
      const next = new Set(prev)
      next.has(db) ? next.delete(db) : next.add(db)
      return next
    })
  }

  const opColor: Record<OpType, string> = { '更新': '#1f5fd0', '新規': '#137a4c', '削除': '#c5283d' }

  return (
    <div style={{ marginTop: 14, borderTop: '1px solid #ebedf0', paddingTop: 12 }}>
      <div style={{ fontSize: 10, fontWeight: 600, color: '#6b7280', marginBottom: 8, letterSpacing: '.02em' }}>
        選択中のモジュール（全DB）
        <span style={{ marginLeft: 6, background: '#3858d6', color: '#fff', borderRadius: 9, padding: '1px 6px', fontSize: 10 }}>
          {totalAll}
        </span>
      </div>
      {activeDbs.map(([db, moduleMap]) => {
        const isExpanded = expandedDbs.has(db)
        const entries = Array.from(moduleMap.entries())
        return (
          <div key={db} style={{ marginBottom: 6 }}>
            <div
              onClick={() => toggleDb(db)}
              style={{
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                padding: '5px 8px', borderRadius: 6, background: '#f4f5f7',
                cursor: 'pointer', fontSize: 11,
              }}
            >
              <span style={{ fontWeight: 600, color: '#26314f' }}>{db}</span>
              <span style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <span style={{ color: '#3858d6', fontWeight: 600, fontSize: 11 }}>{moduleMap.size}件</span>
                <svg
                  width="10" height="10" viewBox="0 0 10 10" fill="none"
                  style={{ transform: isExpanded ? 'rotate(180deg)' : undefined, transition: 'transform .15s' }}
                >
                  <path d="M2 4l3 3 3-3" stroke="#6b7280" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </span>
            </div>
            {isExpanded && (
              <div style={{ marginTop: 3, paddingLeft: 4 }}>
                {entries.map(([name, op]) => (
                  <div
                    key={name}
                    style={{
                      display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                      padding: '3px 4px', fontSize: 10, borderBottom: '1px solid #f0f1f3',
                    }}
                  >
                    <div style={{ minWidth: 0, flex: 1 }}>
                      <div style={{ color: '#3a3f46', fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {name}
                      </div>
                      <div style={{ color: '#9aa0a8', fontSize: 9 }}>
                        {moduleTypeOf(db, name)}
                      </div>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 5, flexShrink: 0, marginLeft: 6 }}>
                      <span style={{ color: opColor[op], fontWeight: 600, fontSize: 10 }}>{op}</span>
                      <button
                        onClick={e => { e.stopPropagation(); onRemove(db, name) }}
                        style={{
                          border: 'none', background: 'none', cursor: 'pointer',
                          color: '#aab0b4', padding: '1px 2px', lineHeight: 1, fontSize: 12,
                        }}
                        title="選択解除"
                      >×</button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
