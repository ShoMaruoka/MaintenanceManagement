import { useMemo, useState } from 'react'
import type { DbName } from '../types'
import { buildCompareSections, toTsv, type PrepareCompareDbEntry } from '../lib/prepareCompare'

interface PrepareCompareViewProps {
  dbEntries: PrepareCompareDbEntry[]
  checked: Set<string>
  dbOrder: DbName[]
}

export default function PrepareCompareView({ dbEntries, checked, dbOrder }: PrepareCompareViewProps) {
  const [copied, setCopied] = useState(false)

  const sections = useMemo(
    () => buildCompareSections(dbEntries, checked, dbOrder),
    [dbEntries, checked, dbOrder],
  )

  const tsv = useMemo(() => toTsv(sections, dbOrder), [sections, dbOrder])

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(tsv)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      alert('クリップボードへのコピーに失敗しました。ダウンロードをご利用ください。')
    }
  }

  function handleDownload() {
    const now = new Date()
    const pad = (n: number) => String(n).padStart(2, '0')
    const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`
    const blob = new Blob([tsv], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `prepare-compare_${stamp}.txt`
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="prep-compare">
      <div className="prep-compare-toolbar">
        <div className="prep-compare-legend">
          <span className="prep-compare-legend-item">
            <span className="prep-compare-swatch prep-compare-swatch-unique" />
            一部DBのみに存在
          </span>
          <span className="prep-compare-legend-item">
            <span className="prep-compare-mark-pending">○(適用予定)</span>
            保留中だが今回適用予定
          </span>
        </div>
        <div className="prep-compare-actions">
          <button className="btn-secondary" onClick={handleCopy}>
            {copied ? 'コピーしました' : 'コピー'}
          </button>
          <button className="btn-secondary" onClick={handleDownload}>
            ダウンロード
          </button>
        </div>
      </div>

      {sections.map(section => (
        <div key={section.label} className="prep-compare-section">
          <div className="prep-compare-section-title">{section.label}</div>
          <table className="prep-compare-table">
            <thead>
              <tr>
                <th>ファイル名</th>
                {dbOrder.map(db => (
                  <th key={db}>{db}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {section.rows.length === 0 ? (
                <tr>
                  <td className="prep-compare-empty" colSpan={dbOrder.length + 1}>
                    対象ファイルなし
                  </td>
                </tr>
              ) : (
                section.rows.map(row => (
                  <tr
                    key={`${row.dbType}::${row.fileName}`}
                    className={row.isCommon ? '' : 'prep-compare-row-unique'}
                  >
                    <td className="prep-compare-filename">
                      {row.fileName}
                      <span className="prep-file-db-badge">{row.dbType === 'mariadb' ? 'MariaDB' : 'SS'}</span>
                    </td>
                    {dbOrder.map(db => {
                      const cell = row.cells[db]
                      if (!cell?.exists) {
                        return <td key={db} className="prep-compare-cell prep-compare-cell-empty" />
                      }
                      const isPending = section.source === 'hold' && cell.checked
                      return (
                        <td key={db} className="prep-compare-cell">
                          <span className={isPending ? 'prep-compare-mark-pending' : 'prep-compare-mark'}>
                            {isPending ? '○(適用予定)' : '○'}
                          </span>
                        </td>
                      )
                    })}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      ))}
    </div>
  )
}
