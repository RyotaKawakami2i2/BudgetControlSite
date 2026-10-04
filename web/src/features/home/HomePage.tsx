import { Link } from 'react-router';
import { useDashboard, useMe } from '../../api/hooks';
import type { Dashboard } from '../../api/types';
import { Icon, type IconName } from '../../components/Icon';
import { Card, EmptyState, ErrorBox, Loading, PageHeader, Pill, ProgressBar } from '../../components/ui';
import { formatDate, greeting } from '../../lib/dates';
import { formatHours, formatSignedHours } from '../../lib/effort';
import { ROLE } from '../../lib/labels';
import { MyTaskList } from '../my-tasks/MyTaskList';
import styles from './home.module.css';

/** ホーム（SC-04。FR-DSH-01）。自分の担当タスクの要約と、所属チーム（プロジェクト）ごとの状況。 */
export function HomePage() {
  const { data: me } = useMe();
  const { data, error, isLoading } = useDashboard();
  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorBox error={error} />;

  const dueSoon = data.overdue.count + data.dueToday.count + data.dueThisWeek.count;

  return (
    <div className="page">
      <PageHeader
        title={`${greeting()}、${me?.displayName ?? ''} さん`}
        description="今日やることと、所属しているチームの状況をまとめています。"
        meta={`今日 ${formatDate(data.today)}`}
        actions={
          <>
            <Link to="/timesheet" className={styles.action}>
              <Icon name="clock" size={16} />
              週の入力表で記録する
            </Link>
            <Link to="/gantt" className={styles.action}>
              <Icon name="gantt" size={16} />
              ガントを開く
            </Link>
          </>
        }
      />

      <div className={styles.stats}>
        <StatTile to="/my-tasks" icon="alert" label="遅れ" caption="期限を過ぎた・開始が遅れているタスク" count={data.overdue.count} alert />
        <StatTile to="/my-tasks" icon="today" label="今日まで" caption="今日が予定終了日のタスク" count={data.dueToday.count} />
        <StatTile to="/my-tasks" icon="calendar" label="今週まで" caption="今週中に予定終了日が来るタスク" count={data.dueThisWeek.count} />
      </div>

      <div className={styles.grid}>
        <Card
          icon="check"
          title="期限が近い自分のタスク"
          count={dueSoon}
          actions={
            <Link to="/my-tasks" className={styles.more}>
              マイタスクをすべて見る
              <Icon name="arrowRight" size={14} />
            </Link>
          }
        >
          {dueSoon === 0 ? (
            <EmptyState
              icon="checkCircle"
              title="今週が期限のタスクはありません"
              description="担当になったタスクは、マイタスクで期限ごとに確かめられます。"
            />
          ) : (
            <div className="stack">
              <TaskGroup title="遅れ" tone="danger" items={data.overdue.items} today={data.today} />
              <TaskGroup title="今日まで" tone="warning" items={data.dueToday.items} today={data.today} />
              <TaskGroup title="今週まで" items={data.dueThisWeek.items} today={data.today} />
            </div>
          )}
        </Card>

        <Card icon="team" title="チーム（プロジェクト）の状況" count={data.teams.length}>
          {data.teams.length === 0 ? (
            <EmptyState icon="team" title="所属しているチームはありません" description="チームのリーダーか管理者に、チームへの追加を依頼してください。" />
          ) : (
            <div className="stack">
              {data.teams.map((t) => (
                <TeamStatus key={t.teamId} team={t} />
              ))}
            </div>
          )}
        </Card>
      </div>
    </div>
  );
}

function StatTile({ to, icon, label, caption, count, alert }: { to: string; icon: IconName; label: string; caption: string; count: number; alert?: boolean }) {
  const active = alert && count > 0;
  return (
    <Link to={to} className={[styles.stat, active && styles.statAlert].filter(Boolean).join(' ')}>
      <span className={styles.statIcon} aria-hidden="true">
        <Icon name={icon} size={20} />
      </span>
      <span className={styles.statBody}>
        <span className={styles.statLabel}>{label}</span>
        <span className={styles.statNumber}>
          {count}
          <small>件</small>
        </span>
        <span className={styles.statCaption}>{caption}</span>
      </span>
    </Link>
  );
}

function TaskGroup({ title, items, tone, today }: { title: string; items: Dashboard['overdue']['items']; tone?: 'danger' | 'warning'; today: string }) {
  if (items.length === 0) return null;
  return (
    <div>
      <h3 className={[styles.groupTitle, tone === 'danger' && styles.groupDanger, tone === 'warning' && styles.groupWarning].filter(Boolean).join(' ')}>
        {tone === 'danger' && <span aria-hidden="true">！</span>}
        {title}
        <span className={styles.groupCount}>{items.length}件</span>
      </h3>
      <MyTaskList tasks={items} today={today} compact />
    </div>
  );
}

function TeamStatus({ team: t }: { team: Dashboard['teams'][number] }) {
  const variance = t.actualMinutesToDate - t.plannedMinutesToDate;
  const usage = t.plannedMinutesToDate > 0 ? Math.round((t.actualMinutesToDate / t.plannedMinutesToDate) * 100) : 0;
  return (
    <article className={styles.team}>
      <header className={styles.teamHeader}>
        <Link to={`/gantt?teams=${t.teamId}`} className={styles.teamName}>
          {t.name}
        </Link>
        <Pill tone={t.role === 'leader' ? 'primary' : 'neutral'}>{ROLE[t.role]}</Pill>
        <span className={styles.teamPeriod}>
          <Icon name="calendar" size={14} />
          {t.start ? `${formatDate(t.start)} 〜 ${formatDate(t.end)}` : '日程未定'}
        </span>
      </header>

      <div className={styles.metrics}>
        <div className={styles.metric}>
          <span className={styles.metricLabel}>完了率</span>
          <span className={styles.metricValue}>
            {t.completionRate}%<small>（{t.doneCount}/{t.taskCount} 件）</small>
          </span>
          <ProgressBar value={t.completionRate} wide tone="success" label="完了率" />
        </div>
        <div className={styles.metric}>
          <span className={styles.metricLabel} title="予定工数で重み付けした、タスク全体の進捗率">全体の進捗</span>
          <span className={styles.metricValue}>{t.progress}%</span>
          <ProgressBar value={t.progress} wide />
        </div>
        <div className={styles.metric}>
          <span className={styles.metricLabel}>遅れ</span>
          <span className={styles.metricValue}>
            {t.delayedCount > 0 ? (
              <Link to={`/gantt?teams=${t.teamId}&flags=overdue,late_start`} className={styles.delayLink}>
                ！ {t.delayedCount} 件
              </Link>
            ) : (
              <Pill tone="success" icon="check">
                なし
              </Pill>
            )}
          </span>
        </div>
        <div className={styles.metric}>
          <span className={styles.metricLabel}>工数（今日まで）</span>
          <span className={styles.metricValue}>
            {formatHours(t.actualMinutesToDate)}
            <small>／予定 {formatHours(t.plannedMinutesToDate)}</small>
          </span>
          <span className={variance > 0 ? 'danger-text small' : 'muted small'}>
            差 {formatSignedHours(variance)}
            {t.plannedMinutesToDate > 0 && `（予定の ${usage}%）`}
          </span>
        </div>
      </div>

      <footer className={styles.teamLinks}>
        <Link to={`/gantt?teams=${t.teamId}`}>ガントを開く</Link>
        {t.role === 'leader' && <Link to={`/teams/${t.teamId}/report`}>担当者別の予実を見る</Link>}
        <Link to={`/teams/${t.teamId}`}>チームの設定</Link>
      </footer>
    </article>
  );
}
