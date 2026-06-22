import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import Sidebar from './components/Sidebar'
import Header from './components/Header'
import Dashboard from './pages/Dashboard'
import DeployStg from './pages/DeployStg'
import PrepareForPrd from './pages/PrepareForPrd'
import History from './pages/History'
import { useLocation } from 'react-router-dom'

const PAGE_TITLES: Record<string, { title: string; path: string }> = {
  '/':        { title: 'ダッシュボード', path: '/ dashboard' },
  '/deploy':  { title: 'STG 適用',      path: '/ deploy' },
  '/prepare': { title: '本番前準備',     path: '/ prepare' },
  '/history': { title: '実行履歴',       path: '/ history' },
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
            <Route path="/"        element={<Dashboard />} />
            <Route path="/deploy"  element={<DeployStg />} />
            <Route path="/prepare" element={<PrepareForPrd />} />
            <Route path="/history" element={<History />} />
            <Route path="*"        element={<Navigate to="/" replace />} />
          </Routes>
        </div>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <AppInner />
    </BrowserRouter>
  )
}
