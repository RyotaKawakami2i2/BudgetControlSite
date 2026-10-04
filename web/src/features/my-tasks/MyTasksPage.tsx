import { useState } from 'react';
import { Link } from 'react-router';
import { useMyTasks } from '../../api/hooks';
import type { IconName } from '../../components/Icon';
import { Icon } from '../../components/Icon';
import { Button, Card, EmptyState, ErrorBox, Loading, PageHeader } from '../../components/ui';
import { useCurrentTeam } from '../../layout/context';
import { formatDate } from '../../lib/dates';
import { TaskFormDialog } from '../tasks/TaskFormDialog';
import { MyTaskList } from './MyTaskList';
import styles from './my-tasks.module.css';

/** マイタスク（SC-08）。自分の担当の未完了タスクを期限で分け、その場で実績を入力できる。 */
export function MyTasksPage() {
  const { data, error, isLoading } = useMyTasks();
  const { teamId, team } = useCurrentTeam();
  const [creating, setCreating] = useState(false);

  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorBox error={error} />;

  const sections: Array<{ id: string; title: string; icon: IconName; tone?: 'danger' | 'warning' | 'primary'; tasks: typeof data.overdue; hint: string }> = [
    { id: 'overdue', title: '遅れ', icon: 'alert', tone: 'danger', tasks: data.overdue, hint: '期限を過ぎた、または開始が遅れているタスクです。予定の見直しが必要ならリーダーに相談してください。' },
    { id: 'today', title: '今日まで', icon: 'today', tone: 'warning', tasks: data.dueToday, hint: '今日が予定終了日のタスクです。' },
    { id: 'week', title: '今週まで', icon: 'calendar', tasks: data.dueThisWeek, hint: '今週中に予定終了日が来るタスクです。' },
    { id: 'later', title: 'それ以降', icon: 'calendar', tasks: data.later, hint: '来週以降に予定終了日が来るタスクです。' },
    { id: 'unscheduled', title: '日程未定', icon: 'info', tasks: data.unscheduled, hint: '予定の日程が決まっていないタスクです。' },
  ];
  const total = sections.reduce((sum, s) => sum + s.tasks.length, 0);

  return (
    <div className="page">
      <PageHeader
        icon="check"
        title="マイタスク"
        description="自分が担当する未完了のタスクを、期限ごとにまとめています。行のボタンから作業実績の記録や完了ができます。"
        meta={`今日 ${formatDate(data.today)}`}
        actions={
          <>
            <Link to="/timesheet" className={styles.summaryItem}>
              <Icon name="clock" size={16} />
              週の入力表で入力する
            </Link>
            {team && !team.archived && (
              <Button variant="primary" onClick={() => setCreating(true)}>
                <Icon name="plus" size={16} />
                自分のタスクを追加
              </Button>
            )}
          </>
        }
      />

      {total > 0 && (
        <nav className={styles.summary} aria-label="期限ごとの件数">
          {sections.map((s) =>
            s.tasks.length > 0 ? (
              <a key={s.id} href={`#section-${s.id}`} className={[styles.summaryItem, s.tone === 'danger' && styles.summaryDanger].filter(Boolean).join(' ')}>
                {s.title}
                <span className={styles.summaryCount}>{s.tasks.length}</span>
              </a>
            ) : (
              <span key={s.id} className={[styles.summaryItem, styles.summaryEmpty].join(' ')}>
                {s.title}
                <span className={styles.summaryCount}>0</span>
              </span>
            ),
          )}
        </nav>
      )}

      {total === 0 ? (
        <EmptyState
          icon="checkCircle"
          title="担当している未完了のタスクはありません"
          description="リーダーからタスクを割り振られると、ここに表示されます。自分でタスクを追加することもできます。"
        />
      ) : (
        <div className="stack-lg">
          {sections
            .filter((s) => s.tasks.length > 0)
            .map((s) => (
              <Card key={s.id} id={`section-${s.id}`} icon={s.icon} tone={s.tone} title={s.title} count={s.tasks.length} description={s.hint}>
                <MyTaskList tasks={s.tasks} today={data.today} />
              </Card>
            ))}
          {sections.some((s) => s.tasks.length === 0) && (
            <p className="muted small">
              該当するタスクがない区分: {sections.filter((s) => s.tasks.length === 0).map((s) => s.title).join('、')}
            </p>
          )}
        </div>
      )}
      {creating && <TaskFormDialog target={{ mode: 'create', teamId }} onClose={() => setCreating(false)} />}
    </div>
  );
}
