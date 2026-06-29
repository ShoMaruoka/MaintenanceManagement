import type { DeploySessionDetail } from '../types'

export function SessionDetailTable({ details }: { details: DeploySessionDetail[] }) {
  return (
    <table className="log-detail-table">
      <thead>
        <tr>
          <th>種別</th>
          <th>モジュール名</th>
          <th>区分</th>
          <th>結果</th>
        </tr>
      </thead>
      <tbody>
        {details.map((d, i) => (
          <tr key={d.detailId ?? i}>
            <td>
              <span className="log-detail-type-badge">
                {d.moduleType === 'StoredProcedure' ? 'SP'
                  : d.moduleType === 'Function' ? 'Func'
                  : d.moduleType === 'VIEW' ? 'View'
                  : d.moduleType}
              </span>
            </td>
            <td className="log-detail-module-name-cell">{d.moduleName}</td>
            <td>
              <span className={`log-detail-op-badge ${
                d.opType === '新規' ? 'log-detail-op-new'
                : d.opType === '更新' ? 'log-detail-op-update'
                : 'log-detail-op-delete'
              }`}>{d.opType}</span>
            </td>
            <td>
              <span className={
                d.result === 'success' ? 'log-detail-result-success'
                : d.result === 'failed' ? 'log-detail-result-failed'
                : 'log-detail-result-skipped'
              }>
                {d.result === 'success' ? '成功'
                  : d.result === 'failed' ? '失敗'
                  : 'スキップ'}
              </span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
