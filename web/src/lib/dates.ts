/**
 * 日付の計算（詳細設計書 7.3.3）。日付は「1970-01-01 からの日数」という整数で扱い、Date の時差の影響を受けないようにする。
 * 日付ライブラリは使わない（詳細設計書 2.3）。
 */

const MS_PER_DAY = 86_400_000;
const WEEKDAYS = ['日', '月', '火', '水', '木', '金', '土'] as const;

/** "YYYY-MM-DD" を日数にする。 */
export function toDay(iso: string): number {
  const [y, m, d] = iso.split('-').map(Number);
  return Math.floor(Date.UTC(y ?? 1970, (m ?? 1) - 1, d ?? 1) / MS_PER_DAY);
}

/** 日数を "YYYY-MM-DD" にする。 */
export function toIso(day: number): string {
  const date = new Date(day * MS_PER_DAY);
  const y = date.getUTCFullYear();
  const m = String(date.getUTCMonth() + 1).padStart(2, '0');
  const d = String(date.getUTCDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function addDays(iso: string, days: number): string {
  return toIso(toDay(iso) + days);
}

/** 曜日（0 = 日曜日）。 */
export function dayOfWeek(day: number): number {
  return (((day + 4) % 7) + 7) % 7;
}

export function isWeekend(day: number): boolean {
  const w = dayOfWeek(day);
  return w === 0 || w === 6;
}

/** 業務上の今日（日本時間）。 */
export function todayIso(now: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Tokyo',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(now);
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
}

/** 「2026/10/03（土）」の形式（要件定義書 6.2）。 */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '';
  const day = toDay(iso);
  return `${iso.replaceAll('-', '/')}（${WEEKDAYS[dayOfWeek(day)]}）`;
}

/** 「10/3（土）」の短い形式。 */
export function formatShortDate(iso: string | null | undefined): string {
  if (!iso) return '';
  const [, m, d] = iso.split('-');
  return `${Number(m)}/${Number(d)}（${WEEKDAYS[dayOfWeek(toDay(iso))]}）`;
}

/** UTC の日時を、日本時間の「2026/10/03 14:05」にする。 */
export function formatDateTime(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = new Date(utc);
  const parts = new Intl.DateTimeFormat('ja-JP', {
    timeZone: 'Asia/Tokyo',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(date);
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  return `${get('year')}/${get('month')}/${get('day')} ${get('hour')}:${get('minute')}`;
}

/** その週の月曜日（週は月曜始まり）。 */
/**
 * 予定終了日までの残りを短い言葉で表す（一覧で期限を一目で分かるようにする）。
 * 予定終了日がなければ null。完了したタスクには使わない。
 */
export function dueLabel(plannedEnd: string | null, today: string): { text: string; tone: 'danger' | 'warning' | 'neutral' } | null {
  if (!plannedEnd) return null;
  const days = toDay(plannedEnd) - toDay(today);
  if (days < 0) return { text: `${-days} 日超過`, tone: 'danger' };
  if (days === 0) return { text: '今日まで', tone: 'warning' };
  if (days === 1) return { text: '明日まで', tone: 'warning' };
  return { text: `あと ${days} 日`, tone: 'neutral' };
}

/** 時間帯に合わせたあいさつ（ホームの見出しに使う）。 */
export function greeting(now: Date = new Date()): string {
  const hour = Number(new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Tokyo', hour: 'numeric', hourCycle: 'h23' }).format(now));
  if (hour >= 4 && hour < 11) return 'おはようございます';
  if (hour >= 11 && hour < 18) return 'こんにちは';
  return 'お疲れさまです';
}

export function mondayOf(iso: string): string {
  const day = toDay(iso);
  return toIso(day - ((dayOfWeek(day) + 6) % 7));
}

export type RangePreset = 'default' | 'this_month' | 'next_month' | 'this_quarter' | 'custom';

/** 表示期間の選び方から期間を求める（FR-GNT-23。初期表示は今日の 1 週間前から 5 週間後まで）。 */
export function presetRange(preset: RangePreset, today: string, custom?: { from?: string; to?: string }): { from: string; to: string } {
  const [y, m] = today.split('-').map(Number) as [number, number];
  const monthStart = (year: number, month: number) => toIso(Math.floor(Date.UTC(year, month - 1, 1) / MS_PER_DAY));
  const monthEnd = (year: number, month: number) => toIso(Math.floor(Date.UTC(year, month, 0) / MS_PER_DAY));
  switch (preset) {
    case 'this_month':
      return { from: monthStart(y, m), to: monthEnd(y, m) };
    case 'next_month': {
      const ny = m === 12 ? y + 1 : y;
      const nm = m === 12 ? 1 : m + 1;
      return { from: monthStart(ny, nm), to: monthEnd(ny, nm) };
    }
    case 'this_quarter': {
      const q = Math.floor((m - 1) / 3);
      return { from: monthStart(y, q * 3 + 1), to: monthEnd(y, q * 3 + 3) };
    }
    case 'custom':
      if (custom?.from && custom?.to && custom.from <= custom.to) {
        return { from: custom.from, to: custom.to };
      }
      return { from: addDays(today, -7), to: addDays(today, 35) };
    default:
      return { from: addDays(today, -7), to: addDays(today, 35) };
  }
}

/** 稼働日数（両端を含む。土日と祝日を除く）。 */
export function countWorkingDays(from: number, to: number, holidays: ReadonlySet<number>): number {
  let count = 0;
  for (let d = from; d <= to; d++) {
    if (!isWeekend(d) && !holidays.has(d)) count++;
  }
  return count;
}

/**
 * 期間に入る予定工数（分。詳細設計書 7.3.7）。予定工数を予定期間の稼働日数で割り、期間と重なる稼働日数を掛ける。
 * サーバーの EffortRules.ProratedMinutes と同じ計算。
 */
export function proratedMinutes(
  plannedStart: string | null,
  plannedEnd: string | null,
  plannedMinutes: number | null,
  from: string,
  to: string,
  holidays: ReadonlySet<number>,
): number {
  if (!plannedStart || !plannedEnd || !plannedMinutes || plannedMinutes <= 0) return 0;
  const s = toDay(plannedStart);
  const e = toDay(plannedEnd);
  const f = toDay(from);
  const t = toDay(to);
  if (e < f || s > t) return 0;
  const os = Math.max(s, f);
  const oe = Math.min(e, t);
  let whole = countWorkingDays(s, e, holidays);
  let part = countWorkingDays(os, oe, holidays);
  if (whole === 0) {
    whole = e - s + 1;
    part = oe - os + 1;
  }
  return (plannedMinutes * part) / whole;
}
