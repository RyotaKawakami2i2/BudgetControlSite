import { useState } from 'react';
import { Link } from 'react-router';
import { useMyTasks } from '../../api/hooks';
import { Button, ErrorBox, Loading } from '../../components/ui';
import { useCurrentTeam } from '../../layout/context';
import { formatDate } from '../../lib/dates';
import { TaskFormDialog } from '../tasks/TaskFormDialog';
import { MyTaskList } from './MyTaskList';

/** マイタスク（SC-08）。自分の担当の未完了タスクを期限で分け、その場で実績を入力できる。 */
export function MyTasksPage() {
  const { data, error, isLoading } = useMyTasks();
  const { teamId, team } = useCurrentTeam();
  const [creating, setCreating] = useState(false);

  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorBox error={error} />;

  const sections = [
    { id: 'overdue', title: '！ 遅れ', tasks: data.overdue },
    { id: 'today', title: '今日まで', tasks: data.dueToday },
    { id: 'week', title: '今週まで', tasks: data.dueThisWeek },
    { id: 'later', title: 'それ以降', tasks: data.later },
    { id: 'unscheduled', title: '日程未定', tasks: data.unscheduled },
  ];

  return (
    <div className="page">
      <div className="page-header">
        <h1>マイタスク</h1>
        <span className="muted">今日: {formatDate(data.today)}</span>
        <Link to="/timesheet">週の入力表で入力する</Link>
        {team && !team.archived && (
          <Button variant="primary" onClick={() => setCreating(true)}>
            自分のタスクを追加
          </Button>
        )}
      </div>
      <div className="stack">
        {sections.map((s) => (
          <section key={s.id}>
            <h2>
              {s.title}（{s.tasks.length}件）
            </h2>
            <MyTaskList tasks={s.tasks} />
          </section>
        ))}
      </div>
      {creating && <TaskFormDialog target={{ mode: 'create', teamId }} onClose={() => setCreating(false)} />}
    </div>
  );
}
