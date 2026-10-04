import { useState } from 'react';
import type { MyTask } from '../../api/types';
import { Button, FlagBadges, PriorityBadge, ProgressBar, StatusBadge } from '../../components/ui';
import { useTaskPanel } from '../../layout/context';
import { formatDate } from '../../lib/dates';
import { formatHours } from '../../lib/effort';
import { CompleteDialog, WorkLogDialog } from '../tasks/WorkDialogs';

/** 自分の担当タスクの表。各行から作業実績の記録と完了の操作ができる（FR-LST-01、FR-ACT-06）。 */
export function MyTaskList({ tasks, compact }: { tasks: MyTask[]; compact?: boolean }) {
  const { open } = useTaskPanel();
  const [dialog, setDialog] = useState<{ kind: 'worklog' | 'complete'; task: MyTask } | null>(null);
  if (tasks.length === 0) return <p className="empty">該当するタスクはありません。</p>;

  return (
    <>
      <table className="data-table">
        <thead>
          <tr>
            <th scope="col">タスク</th>
            {!compact && <th scope="col">チーム</th>}
            <th scope="col">予定終了日</th>
            <th scope="col">状態</th>
            {!compact && <th scope="col">進捗</th>}
            {!compact && (
              <th scope="col" className="num">
                予定／実績
              </th>
            )}
            <th scope="col">
              <span className="visually-hidden">操作</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {tasks.map((t) => (
            <tr key={t.id}>
              <td>
                <Button variant="link" onClick={() => open(t.id)}>
                  {t.title}
                </Button>
                {t.path.length > 0 && <div className="muted">{t.path.map((p) => p.title).join(' > ')}</div>}
                <div className="row">
                  <FlagBadges flags={t.flags} short />
                  {t.priority === 'high' && <PriorityBadge priority={t.priority} />}
                </div>
              </td>
              {!compact && <td>{t.teamName}</td>}
              <td className="nowrap">{formatDate(t.plannedEnd) || '未定'}</td>
              <td>
                <StatusBadge status={t.status} />
              </td>
              {!compact && (
                <td className="nowrap">
                  {t.progress}% <ProgressBar value={t.progress} />
                </td>
              )}
              {!compact && (
                <td className="num nowrap">
                  {formatHours(t.plannedMinutes, '-')}／{formatHours(t.actualMinutes)}
                </td>
              )}
              <td className="nowrap">
                {t.can.logWork && (
                  <Button size="small" variant="primary" onClick={() => setDialog({ kind: 'worklog', task: t })}>
                    実績を記録
                  </Button>
                )}{' '}
                {t.nextStatuses.includes('done') && (
                  <Button size="small" onClick={() => setDialog({ kind: 'complete', task: t })}>
                    完了
                  </Button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {dialog?.kind === 'worklog' && <WorkLogDialog task={dialog.task} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'complete' && <CompleteDialog task={dialog.task} actualStart={dialog.task.actualStart} onClose={() => setDialog(null)} />}
    </>
  );
}
