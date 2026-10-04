/**
 * 工数の入力の解釈と表示（FR-ACT-01、詳細設計書 7.5）。
 * 「1.5」（時間の小数）と「1:30」（時:分）の両方を受け付け、分に直す。15 分の倍数にならない場合は丸めずに誤りとする。
 */

export type EffortParse =
  | { ok: true; minutes: number }
  | { ok: false; reason: 'format' | 'step' };

export function parseEffort(input: string): EffortParse {
  const text = input.normalize('NFKC').trim();
  if (text === '') return { ok: false, reason: 'format' };

  let minutes: number;
  const hm = /^(\d{1,4}):([0-5]\d)$/.exec(text);
  if (hm) {
    minutes = Number(hm[1]) * 60 + Number(hm[2]);
  } else if (/^\d{1,4}(\.\d{1,2})?$/.test(text) || /^\.\d{1,2}$/.test(text)) {
    // 小数の誤差を避けるため、時間 × 60 を整数に丸めてから確かめる（1.25 → 75 分）
    const value = Number(text) * 60;
    minutes = Math.round(value);
    if (Math.abs(value - minutes) > 1e-6) return { ok: false, reason: 'step' };
  } else {
    return { ok: false, reason: 'format' };
  }

  if (minutes % 15 !== 0) return { ok: false, reason: 'step' };
  return { ok: true, minutes };
}

/** 「1.5h」の形式（要件定義書 6.2）。 */
export function formatHours(minutes: number | null | undefined, empty = ''): string {
  if (minutes === null || minutes === undefined) return empty;
  const hours = Math.round((minutes / 60) * 100) / 100;
  return `${hours}h`;
}

/** 差を符号付きで「+1.5h」「-0.5h」の形式にする。 */
export function formatSignedHours(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined) return '';
  const sign = minutes > 0 ? '+' : minutes < 0 ? '-' : '';
  return `${sign}${formatHours(Math.abs(minutes))}`;
}

/** 「1:30」の形式（週の入力表のマス）。 */
export function formatHm(minutes: number): string {
  if (minutes <= 0) return '';
  return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`;
}
