interface Props {
  title: string
  path: string
}

export default function Header({ title, path }: Props) {
  return (
    <header className="header">
      <div className="header-title">
        <span className="header-title-text">{title}</span>
        <span className="header-title-path">{path}</span>
      </div>
      <div className="header-right">
        <div className="env-badge">
          <span className="env-badge-dot" />
          STG 環境
        </div>
        <div className="header-divider" />
        <span className="header-user">TANAKA\yamada</span>
      </div>
    </header>
  )
}
