import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useLocation } from 'react-router-dom'
import { UserProvider, useUser } from './context/UserContext'
import Sidebar from './components/Sidebar'
import Header from './components/Header'
import Dashboard from './pages/Dashboard'
import DeployStg from './pages/DeployStg'
import ImagePrepare from './pages/ImagePrepare'
import PrepareForPrd from './pages/PrepareForPrd'
import History from './pages/History'
import UserSelectPage from './pages/UserSelectPage'
import UserManagePage from './pages/UserManagePage'

const PAGE_TITLES: Record<string, { title: string; path: string }> = {
  '/':             { title: 'ダッシュボード', path: '/ dashboard' },
  '/deploy':       { title: 'STG 適用',      path: '/ deploy' },
  '/images':       { title: '画像情報準備',   path: '/ images' },
  '/prepare':      { title: '本番前準備',     path: '/ prepare' },
  '/history':      { title: '実行履歴',       path: '/ history' },
  '/admin/users':  { title: 'ユーザー管理',   path: '/ admin / users' },
}

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { currentUser } = useUser()
  return currentUser ? <>{children}</> : <Navigate to="/select-user" replace />
}

function AdminRoute({ children }: { children: React.ReactNode }) {
  const { currentUser, currentRole } = useUser()
  // 未選択状態（初回起動・ユーザー未登録）の場合はそのまま通す
  if (!currentUser) return <>{children}</>
  if (currentRole !== 'admin') return <Navigate to="/" replace />
  return <>{children}</>
}

function AppInner() {
  const location = useLocation()
  const meta = PAGE_TITLES[location.pathname] ?? PAGE_TITLES['/']

  return (
    <div className="layout">
      <Sidebar />
      <div className="main-area">
        <Header title={meta.title} path={meta.path} />
        <div className="page-content">
          <Routes>
            <Route path="/"             element={<ProtectedRoute><Dashboard /></ProtectedRoute>} />
            <Route path="/deploy"       element={<ProtectedRoute><DeployStg /></ProtectedRoute>} />
            <Route path="/images"       element={<ProtectedRoute><ImagePrepare /></ProtectedRoute>} />
            <Route path="/prepare"      element={<ProtectedRoute><PrepareForPrd /></ProtectedRoute>} />
            <Route path="/history"      element={<ProtectedRoute><History /></ProtectedRoute>} />
            <Route path="/admin/users"  element={<AdminRoute><UserManagePage /></AdminRoute>} />
            <Route path="*"             element={<Navigate to="/" replace />} />
          </Routes>
        </div>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <UserProvider>
        <Routes>
          <Route path="/select-user" element={<UserSelectPage />} />
          <Route path="*" element={<AppInner />} />
        </Routes>
      </UserProvider>
    </BrowserRouter>
  )
}
