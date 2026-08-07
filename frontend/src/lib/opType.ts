/** 操作区分（新規／更新／削除／不明）の表示ヘルパー。カード表示と比較ビューで共用する。 */

import type { Module, OpType } from '../types'

export const OP_TYPE_UNKNOWN = '不明'

/**
 * DB/Git 突合結果から操作区分を決定する。
 * 優先順位: 削除候補 → 新規候補 → 更新。
 */
export function resolveOpType(
  module: Pick<Module, 'isDeleteCandidate' | 'isNewCandidate'>,
): OpType {
  if (module.isDeleteCandidate) return '削除'
  if (module.isNewCandidate) return '新規'
  return '更新'
}

/** バッジの配色クラス。既存の op-badge パレット（実行履歴・確認ダイアログ）と同じ色を使う。 */
export function opTypeClass(opType: string): string {
  switch (opType) {
    case '新規': return 'prep-optype-new'
    case '更新': return 'prep-optype-update'
    case '削除': return 'prep-optype-delete'
    default:     return 'prep-optype-unknown'
  }
}

export function isDeleteOp(opType: string): boolean {
  return opType === '削除'
}

/** 比較ビューのセル用の 1 文字ラベル。DB 4 列のテーブルで列幅を膨らませないため。 */
export function opTypeShortLabel(opType: string): string {
  switch (opType) {
    case '新規': return '新'
    case '更新': return '更'
    case '削除': return '削'
    default:     return '?'
  }
}
