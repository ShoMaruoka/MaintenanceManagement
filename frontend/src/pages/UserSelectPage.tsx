import { useEffect, useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useUser } from '../context/UserContext'
import { getUsers } from '../api/users'
import type { AppUser } from '../types'

export default function UserSelectPage() {
  const { selectUser } = useUser()
  const navigate = useNavigate()
  const [users, setUsers] = useState<AppUser[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getUsers()
      .then(setUsers)
      .catch(() => setError('ユーザー一覧の取得に失敗しました。'))
      .finally(() => setLoading(false))
  }, [])

  const handleSelect = (u: AppUser) => {
    selectUser(u.userName, u.role === 'admin' ? 'admin' : 'user')
    navigate('/')
  }

  return (
    <div className="user-select-page">
      <div className="user-select-container">
        <div className="user-select-logo">
          <div className="sidebar-logo-icon" style={{ width: 40, height: 40, fontSize: 20, borderRadius: 10 }}>M</div>
          <h1 className="user-select-title">Maintenance Manager</h1>
        </div>
        <p className="user-select-subtitle">ご利用になるユーザーを選択してください</p>

        {loading && <p className="user-select-hint">読み込み中...</p>}
        {error && <p className="user-select-error">{error}</p>}

        {!loading && !error && users.length === 0 && (
          <div style={{ textAlign: 'center' }}>
            <p className="user-select-hint">ユーザーが登録されていません。</p>
            <Link to="/admin/users" className="user-select-manage-link">
              ユーザーを追加する →
            </Link>
          </div>
        )}

        <div className="user-select-grid">
          {users.map((u) => (
            <button
              key={u.userName}
              className="user-select-card"
              onClick={() => handleSelect(u)}
            >
              <div className="user-select-avatar">{u.displayName.charAt(0)}</div>
              <div className="user-select-name">{u.displayName}</div>
              <div className="user-select-id">{u.userName}</div>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
