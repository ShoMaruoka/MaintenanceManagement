import { useEffect, useState } from 'react'
import { getUsers, addUser, deleteUser } from '../api/users'
import type { AppUser } from '../types'
import type { UserRole } from '../context/UserContext'

export default function UserManagePage() {
  const [users, setUsers] = useState<AppUser[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [newUserName, setNewUserName] = useState('')
  const [newDisplayName, setNewDisplayName] = useState('')
  const [newRole, setNewRole] = useState<UserRole>('user')
  const [adding, setAdding] = useState(false)
  const [addError, setAddError] = useState<string | null>(null)

  const load = () => {
    setLoading(true)
    getUsers()
      .then(setUsers)
      .catch(() => setError('ユーザー一覧の取得に失敗しました。'))
      .finally(() => setLoading(false))
  }

  useEffect(() => { load() }, [])

  const handleAdd = async () => {
    setAddError(null)
    if (!newUserName.trim() || !newDisplayName.trim()) {
      setAddError('ユーザーIDと表示名は必須です。')
      return
    }
    setAdding(true)
    try {
      await addUser(newUserName.trim(), newDisplayName.trim(), newRole)
      setNewUserName('')
      setNewDisplayName('')
      setNewRole('user')
      load()
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'エラーが発生しました。'
      setAddError(msg)
    } finally {
      setAdding(false)
    }
  }

  const handleDelete = async (userName: string) => {
    await deleteUser(userName)
    setUsers(prev => prev.filter(u => u.userName !== userName))
  }

  return (
    <div>
      <div className="user-manage-header">
        <h2 style={{ fontSize: 15, fontWeight: 700 }}>ユーザー管理</h2>
      </div>

      {loading && <p className="user-manage-empty">読み込み中...</p>}
      {error && <p style={{ color: '#c5283d', fontSize: 13 }}>{error}</p>}

      {!loading && !error && (
        <div className="user-manage-list">
          {users.length === 0 ? (
            <p className="user-manage-empty">ユーザーが登録されていません。</p>
          ) : (
            users.map(u => (
              <div key={u.userName} className="user-manage-row">
                <div className="user-manage-avatar">{u.displayName.charAt(0)}</div>
                <div className="user-manage-info">
                  <div className="user-manage-display">{u.displayName}</div>
                  <div className="user-manage-username">{u.userName}</div>
                </div>
                <span className={`role-badge role-badge-${u.role}`}>
                  {u.role === 'admin' ? '管理者' : '一般'}
                </span>
                <button
                  className="user-manage-delete"
                  onClick={() => handleDelete(u.userName)}
                >
                  削除
                </button>
              </div>
            ))
          )}
        </div>
      )}

      <div className="user-manage-add-form">
        <div className="user-manage-field">
          <label className="user-manage-label">ユーザーID</label>
          <input
            className="user-manage-input"
            placeholder="例: yamada"
            value={newUserName}
            onChange={e => setNewUserName(e.target.value)}
          />
        </div>
        <div className="user-manage-field">
          <label className="user-manage-label">表示名</label>
          <input
            className="user-manage-input"
            placeholder="例: 山田 太郎"
            value={newDisplayName}
            onChange={e => setNewDisplayName(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && handleAdd()}
          />
        </div>
        <div className="user-manage-field" style={{ maxWidth: 110 }}>
          <label className="user-manage-label">権限</label>
          <select
            className="user-manage-input"
            value={newRole}
            onChange={e => setNewRole(e.target.value as UserRole)}
          >
            <option value="user">一般</option>
            <option value="admin">管理者</option>
          </select>
        </div>
        <button
          className="btn-primary"
          onClick={handleAdd}
          disabled={adding}
          style={{ whiteSpace: 'nowrap' }}
        >
          {adding ? '追加中...' : '+ 追加'}
        </button>
      </div>
      {addError && <p style={{ color: '#c5283d', fontSize: 12, marginTop: 6 }}>{addError}</p>}
    </div>
  )
}
