import { describe, expect, it } from 'vitest';
import { splitLinks } from './autolink';
import { addDays, countWorkingDays, dueLabel, formatDate, greeting, formatShortDate, mondayOf, presetRange, proratedMinutes, toDay, toIso, todayIso } from './dates';
import { formatHm, formatHours, formatSignedHours, parseEffort } from './effort';
import { fieldMessage, messageText } from './messages';

describe('工数の入力の解釈（FR-ACT-01）', () => {
  it.each([
    ['1.5', 90],
    ['1:30', 90],
    ['0.25', 15],
    ['.75', 45],
    ['8', 480],
    ['24:00', 1440],
    ['１．５', 90],
    [' 2:15 ', 135],
  ])('%s は %d 分', (input, minutes) => {
    expect(parseEffort(input)).toEqual({ ok: true, minutes });
  });

  it.each(['', 'abc', '1h', '1:5', '1:60', '-1', '1.5.5'])('%s は形式の誤り', (input) => {
    expect(parseEffort(input)).toEqual({ ok: false, reason: 'format' });
  });

  it.each(['0.1', '1:10', '0.3'])('%s は 15 分単位でない（丸めない）', (input) => {
    expect(parseEffort(input)).toEqual({ ok: false, reason: 'step' });
  });

  it('時間の表示', () => {
    expect(formatHours(90)).toBe('1.5h');
    expect(formatHours(960)).toBe('16h');
    expect(formatHours(null)).toBe('');
    expect(formatSignedHours(-30)).toBe('-0.5h');
    expect(formatSignedHours(90)).toBe('+1.5h');
    expect(formatHm(90)).toBe('1:30');
    expect(formatHm(0)).toBe('');
  });
});

describe('日付', () => {
  it('日数と ISO の相互変換', () => {
    expect(toDay('1970-01-01')).toBe(0);
    expect(toIso(toDay('2026-10-03'))).toBe('2026-10-03');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2028-03-01', -1)).toBe('2028-02-29');
  });

  it('表示の形式（2026/10/03（土））', () => {
    expect(formatDate('2026-10-03')).toBe('2026/10/03（土）');
    expect(formatShortDate('2026-10-05')).toBe('10/5（月）');
    expect(formatDate(null)).toBe('');
  });

  it('週は月曜始まり', () => {
    expect(mondayOf('2026-10-04')).toBe('2026-09-28');
    expect(mondayOf('2026-10-05')).toBe('2026-10-05');
  });

  it('今日は日本時間', () => {
    expect(todayIso(new Date('2026-10-03T15:30:00Z'))).toBe('2026-10-04');
    expect(todayIso(new Date('2026-10-03T14:59:00Z'))).toBe('2026-10-03');
  });

  it('表示期間の選び方', () => {
    expect(presetRange('default', '2026-10-15')).toEqual({ from: '2026-10-08', to: '2026-11-19' });
    expect(presetRange('this_month', '2026-02-10')).toEqual({ from: '2026-02-01', to: '2026-02-28' });
    expect(presetRange('next_month', '2026-12-10')).toEqual({ from: '2027-01-01', to: '2027-01-31' });
    expect(presetRange('this_quarter', '2026-11-10')).toEqual({ from: '2026-10-01', to: '2026-12-31' });
    expect(presetRange('custom', '2026-11-10', { from: '2026-01-01', to: '2026-01-31' })).toEqual({ from: '2026-01-01', to: '2026-01-31' });
  });

  it('稼働日数と、期間で按分した予定工数（詳細設計書 7.3.7）', () => {
    const holidays = new Set([toDay('2026-10-12')]);
    // 2026-10-05（月）〜 10-16（金）は 10 日のうち祝日 1 日 → 9 稼働日
    expect(countWorkingDays(toDay('2026-10-05'), toDay('2026-10-16'), holidays)).toBe(9);
    // 9 稼働日で 900 分。表示期間 10-05〜10-09 は 5 稼働日 → 500 分
    expect(proratedMinutes('2026-10-05', '2026-10-16', 900, '2026-10-05', '2026-10-09', holidays)).toBe(500);
    expect(proratedMinutes('2026-10-05', '2026-10-16', 900, '2026-11-01', '2026-11-30', holidays)).toBe(0);
    expect(proratedMinutes(null, null, 900, '2026-10-01', '2026-10-31', holidays)).toBe(0);
  });
});

describe('期限までの残り（一覧の表示）', () => {
  it('超過・今日・明日・それ以降を言い分ける', () => {
    expect(dueLabel('2026-10-01', '2026-10-04')).toEqual({ text: '3 日超過', tone: 'danger' });
    expect(dueLabel('2026-10-04', '2026-10-04')).toEqual({ text: '今日まで', tone: 'warning' });
    expect(dueLabel('2026-10-05', '2026-10-04')).toEqual({ text: '明日まで', tone: 'warning' });
    expect(dueLabel('2026-10-10', '2026-10-04')).toEqual({ text: 'あと 6 日', tone: 'neutral' });
    expect(dueLabel(null, '2026-10-04')).toBeNull();
  });

  it('あいさつは日本時間で決める', () => {
    expect(greeting(new Date('2026-10-04T00:00:00Z'))).toBe('おはようございます'); // 9 時
    expect(greeting(new Date('2026-10-04T05:00:00Z'))).toBe('こんにちは'); // 14 時
    expect(greeting(new Date('2026-10-04T12:00:00Z'))).toBe('お疲れさまです'); // 21 時
  });
});

describe('メッセージ', () => {
  it('項目名と上限を埋める', () => {
    expect(fieldMessage('description', 'MSG-CMN-002')).toBe('説明は4000字以内で入力してください。');
    expect(fieldMessage('cells[3]', 'MSG-WL-001')).toContain('15 分単位');
    expect(messageText('MSG-CMN-500', { traceId: 'abc' })).toContain('abc');
  });
});

describe('URL の自動リンク（http と https だけ）', () => {
  it('URL だけをリンクにする', () => {
    expect(splitLinks('資料は https://example.com/a?b=1 を参照。')).toEqual([
      { kind: 'text', value: '資料は ' },
      { kind: 'link', value: 'https://example.com/a?b=1' },
      { kind: 'text', value: ' を参照。' },
    ]);
  });

  it('javascript: などはリンクにしない', () => {
    expect(splitLinks('javascript:alert(1) と ftp://x')).toEqual([{ kind: 'text', value: 'javascript:alert(1) と ftp://x' }]);
  });

  it('文末の句読点は含めない', () => {
    expect(splitLinks('see http://a.example.')[1]).toEqual({ kind: 'link', value: 'http://a.example' });
  });
});
