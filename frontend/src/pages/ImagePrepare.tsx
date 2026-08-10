import { useCallback, useEffect, useRef, useState } from 'react'
import type { DbName } from '../types'
import { getDbList, type DbListItem } from '../api/modules'
import {
  createImageFolder,
  deleteImageEntries,
  getImageTree,
  ImagePrepareConflictError,
  ImagePreparePartialDeleteError,
  uploadImages,
  validateSubPath,
  type ApiImageCategoryNode,
  type ApiImageTreeEntry,
} from '../api/imagePrepare'

const FALLBACK_DBS: DbListItem[] = [
  { name: 'kaios', devDb: 'kaios_dev', stgDb: '', prdDb: 'kaios' },
  { name: 'gos', devDb: 'gos_dev', stgDb: '', prdDb: 'gos' },
  { name: 'paf', devDb: 'paf_dev', stgDb: '', prdDb: 'paf' },
  { name: 'duskin', devDb: 'duskin_dev', stgDb: '', prdDb: 'duskin' },
]

const CATEGORIES = ['Images', 'news', 'pdf'] as const

type TreeNodeProps = {
  entry: ApiImageTreeEntry
  depth: number
  selected: Set<string>
  onToggle: (path: string, checked: boolean) => void
  disabled: boolean
}

function TreeNode({ entry, depth, selected, onToggle, disabled }: TreeNodeProps) {
  const [open, setOpen] = useState(depth < 1)
  const canSelect = !entry.isDirectory || entry.children.length === 0
  const checked = selected.has(entry.relativePath)

  if (!entry.isDirectory) {
    return (
      <div className="imgprep-tree-row" style={{ paddingLeft: 12 + depth * 16 }}>
        <input
          type="checkbox"
          className="imgprep-tree-check"
          checked={checked}
          disabled={disabled}
          onChange={e => onToggle(entry.relativePath, e.target.checked)}
          aria-label={`${entry.name} を選択`}
        />
        <span className="imgprep-tree-icon file" aria-hidden />
        <span className="imgprep-tree-name">{entry.name}</span>
      </div>
    )
  }

  return (
    <div>
      <div className="imgprep-tree-row imgprep-tree-folder-row" style={{ paddingLeft: 12 + depth * 16 }}>
        {canSelect ? (
          <input
            type="checkbox"
            className="imgprep-tree-check"
            checked={checked}
            disabled={disabled}
            onChange={e => onToggle(entry.relativePath, e.target.checked)}
            aria-label={`${entry.name} を選択`}
          />
        ) : (
          <span className="imgprep-tree-check-spacer" aria-hidden />
        )}
        <button
          type="button"
          className="imgprep-tree-folder-btn"
          onClick={() => setOpen(v => !v)}
        >
          <span className={`imgprep-tree-caret${open ? ' open' : ''}`} aria-hidden />
          <span className="imgprep-tree-icon folder" aria-hidden />
          <span className="imgprep-tree-name">{entry.name}</span>
        </button>
      </div>
      {open && entry.children.map(child => (
        <TreeNode
          key={child.relativePath}
          entry={child}
          depth={depth + 1}
          selected={selected}
          onToggle={onToggle}
          disabled={disabled}
        />
      ))}
    </div>
  )
}

function CategoryBlock({
  category,
  selected,
  onToggle,
  disabled,
}: {
  category: ApiImageCategoryNode
  selected: Set<string>
  onToggle: (path: string, checked: boolean) => void
  disabled: boolean
}) {
  const [open, setOpen] = useState(true)
  const fileCount = countFiles(category.entries)

  return (
    <div className="imgprep-category">
      <button type="button" className="imgprep-category-header" onClick={() => setOpen(v => !v)}>
        <span className={`imgprep-tree-caret${open ? ' open' : ''}`} aria-hidden />
        <span className="imgprep-category-name">{category.name}</span>
        <span className="imgprep-category-count">{fileCount} ファイル</span>
      </button>
      {open && (
        <div className="imgprep-category-body">
          {category.entries.length === 0 ? (
            <div className="imgprep-empty">（空）</div>
          ) : (
            category.entries.map(entry => (
              <TreeNode
                key={entry.relativePath}
                entry={entry}
                depth={0}
                selected={selected}
                onToggle={onToggle}
                disabled={disabled}
              />
            ))
          )}
        </div>
      )}
    </div>
  )
}

function countFiles(entries: ApiImageTreeEntry[]): number {
  let n = 0
  for (const e of entries) {
    if (e.isDirectory) n += countFiles(e.children)
    else n += 1
  }
  return n
}

/** ツリー上に存在する relativePath を収集する（選択状態の間引き用）。 */
function collectTreePaths(categories: ApiImageCategoryNode[]): Set<string> {
  const paths = new Set<string>()
  function walk(entries: ApiImageTreeEntry[]) {
    for (const e of entries) {
      paths.add(e.relativePath)
      if (e.isDirectory) walk(e.children)
    }
  }
  for (const c of categories) walk(c.entries)
  return paths
}

function pruneSelection(prev: Set<string>, valid: Set<string>): Set<string> {
  const next = new Set<string>()
  for (const p of prev) {
    if (valid.has(p)) next.add(p)
  }
  return next
}

function buildConfirmMessage(paths: string[]): string {
  const previewLimit = 8
  const preview = paths.slice(0, previewLimit).join('\n')
  const more = paths.length > previewLimit ? `\n…他 ${paths.length - previewLimit} 件` : ''
  return `選択した ${paths.length} 件を削除します。よろしいですか？\n\n${preview}${more}`
}

export default function ImagePrepare() {
  const [dbConfigs, setDbConfigs] = useState<DbListItem[]>(FALLBACK_DBS)
  const [selectedDb, setSelectedDb] = useState<DbName>('kaios')
  const [categories, setCategories] = useState<ApiImageCategoryNode[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const [category, setCategory] = useState<string>('Images')
  const [subPath, setSubPath] = useState('')
  const [selectedFiles, setSelectedFiles] = useState<File[]>([])
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [formError, setFormError] = useState('')
  const [selectedPaths, setSelectedPaths] = useState<Set<string>>(() => new Set())
  const fileInputRef = useRef<HTMLInputElement>(null)

  const reloadTree = useCallback(async (db: DbName) => {
    setLoading(true)
    setError('')
    try {
      const tree = await getImageTree(db)
      setCategories(tree.categories)
      const valid = collectTreePaths(tree.categories)
      setSelectedPaths(prev => pruneSelection(prev, valid))
    } catch (err) {
      setCategories([])
      setError((err as Error).message)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    getDbList()
      .then(list => { if (list.length > 0) setDbConfigs(list) })
      .catch(() => {})
  }, [])

  useEffect(() => {
    setSelectedPaths(new Set())
    void reloadTree(selectedDb)
  }, [selectedDb, reloadTree])

  function onTogglePath(path: string, checked: boolean) {
    setSelectedPaths(prev => {
      const next = new Set(prev)
      if (checked) next.add(path)
      else next.delete(path)
      return next
    })
  }

  function onFilesPicked(list: FileList | null) {
    setSelectedFiles(list ? Array.from(list) : [])
    setFormError('')
    setMessage('')
  }

  async function handleCreateFolder() {
    setFormError('')
    setMessage('')
    const pathErr = validateSubPath(subPath)
    if (pathErr) {
      setFormError(pathErr)
      return
    }
    if (!subPath.trim()) {
      setFormError('フォルダ作成にはサブフォルダパスが必要です（例: flash/img）')
      return
    }

    setBusy(true)
    try {
      const result = await createImageFolder(selectedDb, category, subPath.trim())
      const note = result.dryRun ? '（DryRun: 実作成なし）' : ''
      setMessage(
        result.created
          ? `フォルダを作成しました: ${result.relativePath} ${note}`
          : `フォルダは既に存在します: ${result.relativePath} ${note}`,
      )
      await reloadTree(selectedDb)
    } catch (err) {
      setFormError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function doUpload(overwrite: boolean) {
    const result = await uploadImages(
      selectedDb,
      category,
      subPath.trim(),
      selectedFiles,
      overwrite,
    )
    const note = result.dryRun ? '（DryRun: 実書き込みなし）' : ''
    setMessage(`${result.saved.length} 件アップロードしました ${note}`)
    setSelectedFiles([])
    setSelectedPaths(new Set())
    if (fileInputRef.current) fileInputRef.current.value = ''
    await reloadTree(selectedDb)
  }

  async function handleUpload() {
    setFormError('')
    setMessage('')
    const pathErr = validateSubPath(subPath)
    if (pathErr) {
      setFormError(pathErr)
      return
    }
    if (selectedFiles.length === 0) {
      setFormError('アップロードするファイルを選択してください')
      return
    }

    setBusy(true)
    try {
      await doUpload(false)
    } catch (err) {
      if (err instanceof ImagePrepareConflictError) {
        const names = err.conflicts.join('\n')
        const ok = window.confirm(
          `同名のファイルが既に存在します。上書きしますか？\n\n${names}`,
        )
        if (ok) {
          try {
            await doUpload(true)
          } catch (retryErr) {
            setFormError((retryErr as Error).message)
          }
        } else {
          setFormError('上書きをキャンセルしました')
        }
      } else {
        setFormError((err as Error).message)
      }
    } finally {
      setBusy(false)
    }
  }

  async function handleDeleteSelected() {
    const paths = Array.from(selectedPaths)
    if (paths.length === 0) return

    setFormError('')
    setMessage('')

    if (!window.confirm(buildConfirmMessage(paths))) return

    setBusy(true)
    try {
      const result = await deleteImageEntries(selectedDb, paths)
      const note = result.dryRun ? '（DryRun: 実削除なし）' : ''
      setMessage(`${result.deleted.length} 件削除しました ${note}`)
      setSelectedPaths(new Set())
      await reloadTree(selectedDb)
    } catch (err) {
      if (err instanceof ImagePreparePartialDeleteError) {
        const deletedNote = err.deleted.length > 0
          ? `（削除済み: ${err.deleted.join(', ')}）`
          : ''
        setFormError(`${err.message} ${deletedNote}`)
      } else {
        setFormError((err as Error).message)
      }
      setSelectedPaths(new Set())
      try {
        await reloadTree(selectedDb)
      } catch {
        // reloadTree 内で error 表示済み
      }
    } finally {
      setBusy(false)
    }
  }

  const destPreview = subPath.trim()
    ? `${category}/${subPath.trim().replace(/\\/g, '/')}`
    : category

  const selectedCount = selectedPaths.size

  return (
    <div className="imgprep-layout">
      <div className="db-selector">
        <div className="db-selector-label">DB 選択</div>
        <div className="db-selector-list">
          {dbConfigs.map(db => (
            <div
              key={db.name}
              className={`db-item${selectedDb === db.name ? ' selected' : ''}`}
              onClick={() => setSelectedDb(db.name)}
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
          <div className="db-target-label">保管先</div>
          <div className="db-target-value">Deploy_DEV2STG\Files</div>
        </div>
        <div className="db-selector-note">
          Images / news / pdf へアップロードするファイルを管理します。
        </div>
      </div>

      <div className="imgprep-main">
        <div className="imgprep-toolbar">
          <div className="imgprep-title">
            <span className="imgprep-title-text">Files ツリー</span>
            <span className="imgprep-db-label">{selectedDb}</span>
          </div>
          <div className="imgprep-toolbar-actions">
            <span className="imgprep-selection-count">{selectedCount} 件選択</span>
            <button
              type="button"
              className="btn-danger"
              onClick={() => void handleDeleteSelected()}
              disabled={busy || selectedCount === 0}
            >
              選択削除
            </button>
          </div>
        </div>

        <div className="imgprep-upload-panel">
          <div className="imgprep-upload-row">
            <label className="imgprep-field">
              <span className="imgprep-field-label">カテゴリ</span>
              <select
                value={category}
                onChange={e => setCategory(e.target.value)}
                disabled={busy}
              >
                {CATEGORIES.map(c => (
                  <option key={c} value={c}>{c}</option>
                ))}
              </select>
            </label>

            <label className="imgprep-field imgprep-field-grow">
              <span className="imgprep-field-label">サブフォルダ（最大2階層）</span>
              <input
                type="text"
                value={subPath}
                onChange={e => setSubPath(e.target.value)}
                placeholder="例: flash/img（空可）"
                disabled={busy}
              />
            </label>
          </div>

          <div className="imgprep-upload-row">
            <label className="imgprep-field imgprep-field-grow">
              <span className="imgprep-field-label">ファイル</span>
              <input
                ref={fileInputRef}
                type="file"
                multiple
                onChange={e => onFilesPicked(e.target.files)}
                disabled={busy}
              />
            </label>
          </div>

          <div className="imgprep-upload-meta">
            保存先: <code>{destPreview}</code>
            {selectedFiles.length > 0 && (
              <span> / {selectedFiles.length} 件選択中</span>
            )}
          </div>

          <div className="imgprep-upload-actions">
            <button
              type="button"
              className="btn-secondary"
              onClick={() => void handleCreateFolder()}
              disabled={busy}
            >
              フォルダ作成
            </button>
            <button
              type="button"
              className="btn-primary"
              onClick={() => void handleUpload()}
              disabled={busy}
            >
              アップロード
            </button>
          </div>

          {formError && <div className="imgprep-form-error">{formError}</div>}
          {message && <div className="imgprep-form-ok">{message}</div>}
        </div>

        {loading && <div className="imgprep-status">読み込み中…</div>}
        {error && <div className="imgprep-error">{error}</div>}

        {!loading && !error && (
          <div className="imgprep-tree">
            {categories.map(cat => (
              <CategoryBlock
                key={cat.name}
                category={cat}
                selected={selectedPaths}
                onToggle={onTogglePath}
                disabled={busy}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
