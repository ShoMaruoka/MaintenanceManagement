import { NavLink, useNavigate } from 'react-router-dom'
import { useUser } from '../context/UserContext'

const NAV_ITEMS = [
  {
    to: '/',
    label: 'ダッシュボード',
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <rect x="1.5" y="1.5" width="5" height="5" rx="1" stroke="currentColor" strokeWidth="1.4"/>
        <rect x="9.5" y="1.5" width="5" height="5" rx="1" stroke="currentColor" strokeWidth="1.4"/>
        <rect x="1.5" y="9.5" width="5" height="5" rx="1" stroke="currentColor" strokeWidth="1.4"/>
        <rect x="9.5" y="9.5" width="5" height="5" rx="1" stroke="currentColor" strokeWidth="1.4"/>
      </svg>
    ),
  },
  {
    to: '/deploy',
    label: 'STG 適用',
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <path d="M8 9.5v-7" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round"/>
        <path d="M5 5.5L8 2.5l3 3" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"/>
        <path d="M2.5 10.5v2a1 1 0 001 1h9a1 1 0 001-1v-2" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round"/>
      </svg>
    ),
  },
  {
    to: '/images',
    label: '画像情報準備',
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <rect x="2" y="3" width="12" height="10" rx="1.3" stroke="currentColor" strokeWidth="1.4"/>
        <circle cx="5.5" cy="6.5" r="1.2" stroke="currentColor" strokeWidth="1.2"/>
        <path d="M2.5 11.5l3.2-3.2a1 1 0 011.4 0L10 11.2l1.3-1.3a1 1 0 011.4 0l1.3 1.3" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"/>
      </svg>
    ),
  },
  {
    to: '/prepare',
    label: '本番前準備',
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <rect x="2" y="4.5" width="8" height="9" rx="1.3" stroke="currentColor" strokeWidth="1.4"/>
        <path d="M6 4.5v-2a1 1 0 011-1h6a1 1 0 011 1v8a1 1 0 01-1 1h-2" stroke="currentColor" strokeWidth="1.4"/>
      </svg>
    ),
  },
  {
    to: '/history',
    label: '実行履歴',
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <circle cx="8" cy="8" r="6" stroke="currentColor" strokeWidth="1.4"/>
        <path d="M8 4.7V8l2.4 1.5" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"/>
      </svg>
    ),
  },
]

const ADMIN_ITEMS = [
  {
    to: '/admin/users',
    label: 'ユーザー管理',
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <circle cx="8" cy="5" r="3" stroke="currentColor" strokeWidth="1.4"/>
        <path d="M2 14c0-3.314 2.686-5 6-5s6 1.686 6 5" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round"/>
      </svg>
    ),
  },
]

export default function Sidebar() {
  const { currentUser, currentRole, clearUser } = useUser()
  const navigate = useNavigate()

  const handleSwitch = () => {
    clearUser()
    navigate('/select-user')
  }

  const avatarChar = currentUser ? currentUser.charAt(0).toUpperCase() : '?'

  return (
    <aside className="sidebar">
      <div className="sidebar-logo">
        <div className="sidebar-logo-icon">M</div>
        <div className="sidebar-logo-text">
          Maintenance<span> Mgr</span>
        </div>
      </div>

      <nav className="sidebar-nav">
        <div className="sidebar-nav-label">メニュー</div>
        {NAV_ITEMS.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            className={({ isActive }) =>
              `sidebar-nav-item${isActive ? ' active' : ''}`
            }
          >
            {item.icon}
            {item.label}
          </NavLink>
        ))}

        {currentRole === 'admin' && (
          <>
            <div className="sidebar-nav-label" style={{ marginTop: 12 }}>管理</div>
            {ADMIN_ITEMS.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) =>
                  `sidebar-nav-item${isActive ? ' active' : ''}`
                }
              >
                {item.icon}
                {item.label}
              </NavLink>
            ))}
          </>
        )}
      </nav>

      <div className="sidebar-info-card">
        <div className="sidebar-info-label">本番前準備 最終実行</div>
        <div className="sidebar-info-value">2026-06-12 03:00</div>
        <div className="sidebar-info-status">
          <span className="sidebar-info-dot" />
          <span className="sidebar-info-status-text">成功 ・ BATCH</span>
        </div>
      </div>

      <div className="sidebar-user">
        <div className="sidebar-user-avatar">{avatarChar}</div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div className="sidebar-user-name">{currentUser ?? '未選択'}</div>
          <div className="sidebar-user-domain">社内ユーザー</div>
        </div>
      </div>
      <button className="sidebar-switch-btn" onClick={handleSwitch}>
        <svg width="13" height="13" viewBox="0 0 16 16" fill="none">
          <path d="M3 8h10M9 4l4 4-4 4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
        </svg>
        ユーザー切り替え
      </button>
    </aside>
  )
}
