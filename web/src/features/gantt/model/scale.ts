/**
 * 時間軸の目盛り（詳細設計書 7.3.3）。x 座標は「(日付 − 表示開始日) の日数 × 1 日の幅」。
 */
import { dayOfWeek, toDay, toIso } from '../../../lib/dates';
import type { Zoom } from '../../../lib/labels';

export const DAY_WIDTH: Record<Zoom, number> = { day: 32, week: 14, month: 4, quarter: 1.5 };

export const ROW = {
  normal: { height: 40, planY: 8, planH: 12, actualY: 24, actualH: 6, diamond: 12 },
  dense: { height: 28, planY: 4, planH: 10, actualY: 18, actualH: 4, diamond: 10 },
} as const;

export interface Tick {
  x: number;
  width: number;
  label: string;
}

export function xOf(iso: string, from: string, dayWidth: number): number {
  return (toDay(iso) - toDay(from)) * dayWidth;
}

/** バーの幅（両端を含む日数 × 1 日の幅。2px より細くしない）。 */
export function widthOf(start: string, end: string, dayWidth: number): number {
  return Math.max(2, (toDay(end) - toDay(start) + 1) * dayWidth);
}

/** 見出しの上の段（月または年）と下の段（日・週・月・四半期）。 */
export function ticks(from: string, to: string, zoom: Zoom): { upper: Tick[]; lower: Tick[] } {
  const dw = DAY_WIDTH[zoom];
  const start = toDay(from);
  const end = toDay(to);
  const upper: Tick[] = [];
  const lower: Tick[] = [];

  const segment = (list: Tick[], key: (iso: string) => string, label: (iso: string) => string, align?: (day: number) => boolean) => {
    let currentKey: string | null = null;
    for (let d = start; d <= end; d++) {
      const iso = toIso(d);
      const k = key(iso);
      if (k !== currentKey || (align && align(d) && d !== start)) {
        list.push({ x: (d - start) * dw, width: 0, label: label(iso) });
        currentKey = k;
      }
    }
    for (let i = 0; i < list.length; i++) {
      const nextX = i + 1 < list.length ? list[i + 1]!.x : (end - start + 1) * dw;
      list[i]!.width = nextX - list[i]!.x;
    }
  };

  const month = (iso: string) => iso.slice(0, 7);
  const monthLabel = (iso: string) => `${iso.slice(0, 4)}/${Number(iso.slice(5, 7))}`;
  const quarterKey = (iso: string) => `${iso.slice(0, 4)}Q${Math.floor((Number(iso.slice(5, 7)) - 1) / 3)}`;

  switch (zoom) {
    case 'day':
      segment(upper, month, monthLabel);
      segment(lower, (iso) => iso, (iso) => String(Number(iso.slice(8, 10))));
      break;
    case 'week':
      segment(upper, month, monthLabel);
      segment(lower, (iso) => toIso(toDay(iso) - ((dayOfWeek(toDay(iso)) + 6) % 7)), (iso) => `${Number(iso.slice(5, 7))}/${Number(iso.slice(8, 10))}`);
      break;
    case 'month':
      segment(upper, (iso) => iso.slice(0, 4), (iso) => `${iso.slice(0, 4)}年`);
      segment(lower, month, (iso) => `${Number(iso.slice(5, 7))}月`);
      break;
    case 'quarter':
      segment(upper, (iso) => iso.slice(0, 4), (iso) => `${iso.slice(0, 4)}年`);
      segment(lower, quarterKey, (iso) => `${Math.floor((Number(iso.slice(5, 7)) - 1) / 3) + 1}Q`);
      break;
  }

  return { upper, lower };
}
