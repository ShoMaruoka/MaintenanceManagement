import type { DbName } from '../types'

export interface PrepareCompareFile {
  fileName: string
  source: 'deployed' | 'hold'
  dbType: 'sqlserver' | 'mariadb'
}

export interface PrepareCompareDbEntry {
  dbName: DbName
  files: PrepareCompareFile[]
}

export interface CompareCell {
  exists: boolean
  checked: boolean
}

export interface CompareRow {
  fileName: string
  dbType: 'sqlserver' | 'mariadb'
  cells: Partial<Record<DbName, CompareCell>>
  isCommon: boolean
}

export interface CompareSection {
  label: '今回適用する' | '保留中'
  source: 'deployed' | 'hold'
  rows: CompareRow[]
}

function rowKey(fileName: string, dbType: string) {
  return `${dbType}::${fileName}`
}

function fileKey(dbName: DbName, file: PrepareCompareFile) {
  return `${dbName}::${file.dbType}::${file.source}::${file.fileName}`
}

function buildSection(
  label: '今回適用する' | '保留中',
  source: 'deployed' | 'hold',
  dbEntries: PrepareCompareDbEntry[],
  dbOrder: DbName[],
  checked: Set<string>,
): CompareSection {
  const rows = new Map<string, CompareRow>()

  dbEntries.forEach(db => {
    db.files
      .filter(f => f.source === source)
      .forEach(f => {
        const key = rowKey(f.fileName, f.dbType)
        let row = rows.get(key)
        if (!row) {
          row = { fileName: f.fileName, dbType: f.dbType, cells: {}, isCommon: false }
          rows.set(key, row)
        }
        row.cells[db.dbName] = {
          exists: true,
          checked: checked.has(fileKey(db.dbName, f)),
        }
      })
  })

  const sortedRows = Array.from(rows.values())
    .map(row => ({
      ...row,
      isCommon: dbOrder.every(db => row.cells[db]?.exists === true),
    }))
    .sort((a, b) => a.fileName.localeCompare(b.fileName) || a.dbType.localeCompare(b.dbType))

  return { label, source, rows: sortedRows }
}

export function buildCompareSections(
  dbEntries: PrepareCompareDbEntry[],
  checked: Set<string>,
  dbOrder: DbName[],
): CompareSection[] {
  return [
    buildSection('今回適用する', 'deployed', dbEntries, dbOrder, checked),
    buildSection('保留中', 'hold', dbEntries, dbOrder, checked),
  ]
}

export function toTsv(sections: CompareSection[], dbOrder: DbName[]): string {
  const lines: string[] = []

  sections.forEach(section => {
    lines.push(`# ${section.label}`)
    lines.push(['ファイル名', ...dbOrder].join('\t'))

    if (section.rows.length === 0) {
      lines.push('（対象ファイルなし）')
    } else {
      section.rows.forEach(row => {
        const cells = dbOrder.map(db => {
          const cell = row.cells[db]
          if (!cell?.exists) return ''
          if (section.source === 'hold') return cell.checked ? '○(適用予定)' : '○'
          return '○'
        })
        lines.push([row.fileName, ...cells].join('\t'))
      })
    }

    lines.push('')
  })

  return lines.join('\n').trimEnd()
}
