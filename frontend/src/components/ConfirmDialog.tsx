import { useState } from 'react'
import type { MultiDbModules } from '../types'
import { useUser } from '../context/UserContext'

interface Props {
  allModules: MultiDbModules
  onConfirm: () => void
  onCancel: () => void
}

const OP_LABEL_CLASS: Record<string, string> = {
  '新規': 'op-badge op-badge-new',
  '更新': 'op-badge op-badge-update',
  '削除': 'op-badge op-badge-delete',
}

export default function ConfirmDialog({ allModules, onConfirm, onCancel }: Props) {
  const [checked, setChecked] = useState(false)
  const { currentUser } = useUser()
  const totalCount = allModules.reduce((sum, { modules }) => sum + modules.length, 0)

  return (
    <div className="dialog-overlay" onClick={onCancel}>
      <div className="dialog" onClick={(e) => e.stopPropagation()}>
        <div className="dialog-header">
          <span className="dialog-icon">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
              <path d="M8 2.5l5.8 10A.6.6 0 0113.3 13.4H2.7A.6.6 0 012.2 12.5L8 2.5z" stroke="#d98a2b" strokeWidth="1.4" strokeLinejoin="round"/>
              <path d="M8 6.5v3M8 11.3v.01" stroke="#d98a2b" strokeWidth="1.5" strokeLinecap="round"/>
            </svg>
          </span>
          <div>
            <div className="dialog-title">実行内容の確認</div>
            <div className="dialog-subtitle">この操作は STG 環境に適用されます</div>
          </div>
        </div>

        <div className="dialog-body">
          <div className="dialog-meta">
            <div className="dialog-meta-item">
              <label>対象 DB</label>
              <div style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 13, fontWeight: 600 }}>
                {allModules.map(({ db }) => db).join(', ')}
              </div>
            </div>
            <div className="dialog-meta-item">
              <label>実行者</label>
              <div style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 13, fontWeight: 500 }}>
                {currentUser ?? '-'}
              </div>
            </div>
          </div>

          <div className="dialog-module-count">
            適用モジュール <strong>{totalCount} 件</strong>
          </div>

          {allModules.map(({ db, modules }) => (
            <div key={db}>
              <div className="dialog-module-count" style={{ fontSize: 12, fontWeight: 600 }}>
                {db}（{db}_dev → STG） <strong>{modules.length} 件</strong>
              </div>
              <div className="dialog-module-list">
                {modules.map((m, i) => (
                  <div key={i} className="dialog-module-item">
                    <span className={OP_LABEL_CLASS[m.opType] ?? 'op-badge'} style={{ padding: '1px 7px', fontSize: 10 }}>
                      {m.opType}
                    </span>
                    <span className="dialog-module-name">{m.name}</span>
                    {(m.type === 'Table' || m.type === 'UserDefinedTableType')
                      ? <span className="dialog-module-git-only">Git のみ</span>
                      : <span className="dialog-module-type">{m.type}</span>
                    }
                  </div>
                ))}
              </div>
            </div>
          ))}

          <div className="dialog-warning">
            <span className="dialog-warning-dot">●</span>
            <div className="dialog-warning-text">
              git Live Updates → merge → SQL 変換 → deploy.bat の順で実行されます。
              Table・UserDefinedTableType は Git マージのみ。
            </div>
          </div>

          <label className="dialog-confirm-check" onClick={() => setChecked(!checked)}>
            <span className={`checkbox${checked ? ' checked' : ''}`}>
              {checked && (
                <svg width="9" height="9" viewBox="0 0 10 10">
                  <path d="M1.5 5.2l2.2 2.3L8.5 2.5" stroke="#fff" strokeWidth="1.6" fill="none" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              )}
            </span>
            <span className="dialog-confirm-check-label">適用内容を確認しました</span>
          </label>
        </div>

        <div className="dialog-footer">
          <button className="btn-secondary" onClick={onCancel}>キャンセル</button>
          <button className="btn-primary" onClick={onConfirm} disabled={!checked}>
            適用を実行する
          </button>
        </div>
      </div>
    </div>
  )
}
