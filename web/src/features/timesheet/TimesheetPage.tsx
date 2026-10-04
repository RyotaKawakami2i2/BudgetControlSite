import { useMemo, useState } from 'react';
import { ApiError } from '../../api/client';
import { useSaveTimesheet, useTimesheet } from '../../api/hooks';
import type { Timesheet } from '../../api/types';
import { EffortInput } from '../../components/EffortInput';
import { Icon } from '../../components/Icon';
import { Button, ErrorBox, Loading, describeError, useToast } from '../../components/ui';
import { useTaskPanel } from '../../layout/context';
import { addDays, formatShortDate, mondayOf, todayIso } from '../../lib/dates';
import { formatHm, parseEffort } from '../../lib/effort';
import { STATUS } from '../../lib/labels';
import { fieldMessage, messageText } from '../../lib/messages';
import styles from './timesheet.module.css';

/** 週の実績入力表（SC-09。FR-ACT-07、詳細設計書 7.6）。担当タスク × 曜日のマスに作業時間を入れ、一括で保存する。 */
export function TimesheetPage() {
  const [weekStart, setWeekStart] = useState(() => mondayOf(todayIso()));
  const { data, error, isLoading } = useTimesheet(weekStart);

  return (
    <div className="page">
      <div className="page-header">
        <h1>週の入力表</h1>
        <div className="row">
          <Button size="small" onClick={() => setWeekStart(addDays(weekStart, -7))}>
            ◀ 前の週
          </Button>
          <Button size="small" onClick={() => setWeekStart(mondayOf(todayIso()))}>
            今週
          </Button>
          <Button size="small" onClick={() => setWeekStart(addDays(weekStart, 7))}>
            次の週 ▶
          </Button>
        </div>
      </div>
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && <Grid key={data.weekStart + JSON.stringify(data.rows.map((r) => r.cells))} data={data} />}
    </div>
  );
}

function Grid({ data }: { data: Timesheet }) {
  const toast = useToast();
  const save = useSaveTimesheet();
  const { open } = useTaskPanel();
  const today = todayIso();
  const initial = useMemo(() => {
    const map: Record<string, string> = {};
    for (const row of data.rows) for (const cell of row.cells) map[`${row.taskId}|${cell.date}`] = formatHm(cell.minutes);
    return map;
  }, [data]);
  const [values, setValues] = useState(initial);
  const [extraRows, setExtraRows] = useState<Timesheet['addable']>([]);
  const [errors, setErrors] = useState<Record<string, string>>({});

  const rows = [
    ...data.rows,
    ...extraRows.map((a) => ({
      taskId: a.taskId,
      title: a.title,
      teamId: '',
      teamName: a.teamName,
      status: a.status,
      editable: true,
      cells: data.days.map((d) => ({ date: d, minutes: 0, locked: false })),
    })),
  ];

  const minutesOf = (key: string): number | null => {
    const text = values[key] ?? '';
    if (text.trim() === '') return 0;
    const parsed = parseEffort(text);
    return parsed.ok ? parsed.minutes : null;
  };

  const dayTotals = data.days.map((d) => rows.reduce((sum, r) => sum + (minutesOf(`${r.taskId}|${d}`) ?? 0), 0));
  const overDays = data.days.filter((_, i) => (dayTotals[i] ?? 0) > 1440);

  const submit = async () => {
    const cells: Array<{ taskId: string; date: string; minutes: number }> = [];
    const localErrors: Record<string, string> = {};
    for (const row of rows) {
      for (const cell of row.cells) {
        const key = `${row.taskId}|${cell.date}`;
        const minutes = minutesOf(key);
        if (minutes === null) {
          localErrors[key] = messageText('MSG-WL-007');
          continue;
        }
        if (minutes > 1440) localErrors[key] = messageText('MSG-WL-001');
        if (minutes > 0 && cell.date > today) localErrors[key] = messageText('MSG-WL-003');
        if (minutes !== cell.minutes && !cell.locked && row.editable) cells.push({ taskId: row.taskId, date: cell.date, minutes });
      }
    }
    setErrors(localErrors);
    if (Object.keys(localErrors).length > 0) return;
    if (cells.length === 0) {
      toast.show('info', '変更はありません。');
      return;
    }
    try {
      await save.mutateAsync({ weekStart: data.weekStart, cells });
      setExtraRows([]);
      toast.show('success', messageText('MSG-CMN-001'));
    } catch (error) {
      // 1 つでも誤りがあれば何も保存しない。誤りのあるマスに印を付ける
      if (error instanceof ApiError) {
        const next: Record<string, string> = {};
        for (const [field, ids] of Object.entries(error.errors)) {
          const match = /^cells\[(\d+)\]$/.exec(field);
          const cell = match ? cells[Number(match[1])] : undefined;
          if (cell) next[`${cell.taskId}|${cell.date}`] = ids.map((id) => fieldMessage(field, id, { 日付: formatShortDate(cell.date) })).join(' ');
        }
        setErrors(next);
      }
      toast.show('error', describeError(error));
    }
  };

  const addable = data.addable.filter((a) => !rows.some((r) => r.taskId === a.taskId));

  return (
    <>
      <p className="muted">作業時間を「1.5」または「1:30」で入力して、まとめて保存します。タスク詳細から記録した日（鍵のマーク）は、ここでは変えられません。</p>
      <div className={styles.scroll}>
        <table className={['data-table', styles.table].join(' ')}>
          <thead>
            <tr>
              <th scope="col">タスク</th>
              {data.days.map((d) => (
                <th key={d} scope="col" className={[styles.day, d === today && styles.today].filter(Boolean).join(' ')}>
                  {formatShortDate(d)}
                </th>
              ))}
              <th scope="col" className="num">
                合計
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 && (
              <tr>
                <td colSpan={data.days.length + 2} className="empty">
                  担当しているタスクがありません。
                </td>
              </tr>
            )}
            {rows.map((row) => {
              const rowTotal = row.cells.reduce((sum, c) => sum + (minutesOf(`${row.taskId}|${c.date}`) ?? 0), 0);
              return (
                <tr key={row.taskId}>
                  <th scope="row" className={styles.taskCell}>
                    <Button variant="link" onClick={() => open(row.taskId)}>
                      {row.title}
                    </Button>
                    <div className="muted">
                      {row.teamName}　{STATUS[row.status].icon} {STATUS[row.status].label}
                    </div>
                  </th>
                  {row.cells.map((cell) => {
                    const key = `${row.taskId}|${cell.date}`;
                    return (
                      <td key={key} className={styles.cell}>
                        {cell.locked || !row.editable ? (
                          <span className={styles.locked} title={cell.locked ? messageText('MSG-WL-006') : '記録できないタスクです'}>
                            {cell.locked && <Icon name="lock" size={12} label="変更できません" />}
                            {formatHm(cell.minutes) || '-'}
                          </span>
                        ) : (
                          <EffortInput
                            value={values[key] ?? ''}
                            placeholder=""
                            size={5}
                            ariaLabel={`${row.title} ${formatShortDate(cell.date)} の作業時間`}
                            invalid={!!errors[key]}
                            onChange={(text) => setValues((v) => ({ ...v, [key]: text }))}
                          />
                        )}
                        {errors[key] && (
                          <span className={styles.error} role="alert">
                            {errors[key]}
                          </span>
                        )}
                      </td>
                    );
                  })}
                  <td className="num">{formatHm(rowTotal) || '0:00'}</td>
                </tr>
              );
            })}
          </tbody>
          <tfoot>
            <tr>
              <th scope="row">1 日の合計</th>
              {dayTotals.map((total, i) => (
                <td key={data.days[i]} className={['num', total > 1440 && styles.over].filter(Boolean).join(' ')}>
                  {formatHm(total) || '0:00'}
                </td>
              ))}
              <td className="num">{formatHm(dayTotals.reduce((a, b) => a + b, 0)) || '0:00'}</td>
            </tr>
          </tfoot>
        </table>
      </div>
      {overDays.length > 0 && (
        <p className={styles.error} role="alert">
          ！ {overDays.map((d) => messageText('MSG-WL-002', { 日付: formatShortDate(d) })).join(' ')}
        </p>
      )}
      <div className="row">
        {addable.length > 0 && (
          <select
            aria-label="ほかの担当タスクを行に加える"
            value=""
            onChange={(e) => {
              const item = addable.find((a) => a.taskId === e.target.value);
              if (item) setExtraRows((list) => [...list, item]);
            }}
          >
            <option value="">ほかの担当タスクを行に加える</option>
            {addable.map((a) => (
              <option key={a.taskId} value={a.taskId}>
                {a.teamName} / {a.title}（{STATUS[a.status].label}）
              </option>
            ))}
          </select>
        )}
        <Button variant="primary" onClick={() => void submit()} disabled={save.isPending}>
          まとめて保存する
        </Button>
      </div>
    </>
  );
}
