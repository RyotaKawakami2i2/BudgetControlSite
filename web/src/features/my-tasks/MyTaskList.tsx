import { useState } from 'react';
import type { MyTask } from '../../api/types';
import { Icon } from '../../components/Icon';
import { Button, FlagBadges, PriorityBadge, ProgressBar, StatusBadge } from '../../components/ui';
import { useTaskPanel } from '../../layout/context';
import { dueLabel, formatDate } from '../../lib/dates';
import { formatHours } from '../../lib/effort';
import { CompleteDialog, WorkLogDialog } from '../tasks/WorkDialogs';
import styles from './my-tasks.module.css';

/** 自分の担当タスクの表。各行から作業実績の記録と完了の操作ができる（FR-LST-01、FR-ACT-06）。 */
export function MyTaskList({ tasks, today, compact }: { tasks: MyTask[]; today: string; compact?: boolean }) {
  const { open } = useTaskPanel();
  const [dialog, setDialog] = useState<{ kind: 'worklog' | 'complete'; task: MyTask } | null>(null);
  if (tasks.length === 0) return null;

  return (
    <>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">タスク</th>
              {!compact && <th scope="col">チーム</th>}
              <th scope="col">期限</th>
              <th scope="col">状態</th>
              {!compact && <th scope="col">進捗</th>}
              {!compact && (
                <th scope="col" className="num">
                  実績／予定
                </th>
              )}
              <th scope="col">
                <span className="visually-hidden">操作</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {tasks.map((t) => {
              const due = dueLabel(t.plannedEnd, today);
              return (
                <tr key={t.id}>
                  <td className={styles.taskCell}>
                    <Button variant="link" onClick={() => open(t.id)}>
                      {t.title}
                    </Button>
                    {t.path.length > 0 && <div className={styles.path}>{t.path.map((p) => p.title).join(' › ')}</div>}
                    {(t.flags.length > 0 || t.priority === 'high') && (
                      <div className={styles.marks}>
                        <FlagBadges flags={t.flags} short />
                        {t.priority === 'high' && <PriorityBadge priority={t.priority} withLabel />}
                      </div>
                    )}
                  </td>
                  {!compact && <td className="nowrap">{t.teamName}</td>}
                  <td className="nowrap">
                    {t.plannedEnd ? (
                      <>
                        <div>{formatDate(t.plannedEnd)}</div>
                        {due && <span className={[styles.due, styles[`due_${due.tone}`]].join(' ')}>{due.text}</span>}
                      </>
                    ) : (
                      <span className="muted">未定</span>
                    )}
                  </td>
                  <td>
                    <StatusBadge status={t.status} />
                  </td>
                  {!compact && (
                    <td className="nowrap">
                      <span className={styles.progress}>
                        <ProgressBar value={t.progress} />
                        {t.progress}%
                      </span>
                    </td>
                  )}
                  {!compact && (
                    <td className="num nowrap">
                      {formatHours(t.actualMinutes)}
                      <span className="muted">／{formatHours(t.plannedMinutes, '-')}</span>
                    </td>
                  )}
                  <td className="actions">
                    <span className={styles.rowActions}>
                      {t.can.logWork && (
                        <Button
                          size="small"
                          variant="primary"
                          iconOnly={compact}
                          aria-label={compact ? `${t.title} の実績を記録` : undefined}
                          title={compact ? '実績を記録' : undefined}
                          onClick={() => setDialog({ kind: 'worklog', task: t })}
                        >
                          <Icon name="clock" size={14} />
                          {!compact && '実績を記録'}
                        </Button>
                      )}
                      {t.nextStatuses.includes('done') && (
                        <Button
                          size="small"
                          iconOnly={compact}
                          aria-label={compact ? `${t.title} を完了にする` : undefined}
                          title={compact ? '完了にする' : undefined}
                          onClick={() => setDialog({ kind: 'complete', task: t })}
                        >
                          <Icon name="check" size={14} />
                          {!compact && '完了'}
                        </Button>
                      )}
                    </span>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {dialog?.kind === 'worklog' && <WorkLogDialog task={dialog.task} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'complete' && <CompleteDialog task={dialog.task} actualStart={dialog.task.actualStart} onClose={() => setDialog(null)} />}
    </>
  );
}
