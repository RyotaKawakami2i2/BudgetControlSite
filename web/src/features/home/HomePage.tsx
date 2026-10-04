import { Link } from 'react-router';
import { useDashboard, useMe } from '../../api/hooks';
import { ErrorBox, Loading, ProgressBar } from '../../components/ui';
import { formatDate } from '../../lib/dates';
import { formatHours, formatSignedHours } from '../../lib/effort';
import { MyTaskList } from '../my-tasks/MyTaskList';
import styles from './home.module.css';

/** ホーム（SC-04。FR-DSH-01）。自分の担当タスクの要約と、所属チーム（プロジェクト）ごとの状況。 */
export function HomePage() {
  const { data: me } = useMe();
  const { data, error, isLoading } = useDashboard();
  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorBox error={error} />;

  return (
    <div className="page">
      <div className="page-header">
        <h1>ホーム</h1>
        <span className="muted">
          {me?.displayName} さん　今日: {formatDate(data.today)}
        </span>
      </div>
      <div className={styles.grid}>
        <section className="card">
          <h2>自分のタスク</h2>
          <div className={styles.counts}>
            <Link to="/my-tasks" className={[styles.count, data.overdue.count > 0 && styles.countAlert].filter(Boolean).join(' ')}>
              <span className={styles.countNumber}>{data.overdue.count}</span>
              <span>！ 遅れ</span>
            </Link>
            <Link to="/my-tasks" className={styles.count}>
              <span className={styles.countNumber}>{data.dueToday.count}</span>
              <span>今日まで</span>
            </Link>
            <Link to="/my-tasks" className={styles.count}>
              <span className={styles.countNumber}>{data.dueThisWeek.count}</span>
              <span>今週まで</span>
            </Link>
          </div>
          {data.overdue.items.length > 0 && (
            <>
              <h3>遅れ</h3>
              <MyTaskList tasks={data.overdue.items} compact />
            </>
          )}
          {data.dueToday.items.length > 0 && (
            <>
              <h3>今日まで</h3>
              <MyTaskList tasks={data.dueToday.items} compact />
            </>
          )}
          {data.dueThisWeek.items.length > 0 && (
            <>
              <h3>今週まで</h3>
              <MyTaskList tasks={data.dueThisWeek.items} compact />
            </>
          )}
          {data.overdue.count + data.dueToday.count + data.dueThisWeek.count === 0 && <p className="empty">今週が期限のタスクはありません。</p>}
        </section>

        <section className="card">
          <h2>チーム（プロジェクト）の状況</h2>
          {data.teams.length === 0 && <p className="empty">所属しているチームはありません。</p>}
          {data.teams.map((t) => (
            <div key={t.teamId} className={styles.team}>
              <div className={styles.teamHeader}>
                <Link to={`/gantt?teams=${t.teamId}`}>{t.name}</Link>
                <span className="muted">
                  {t.start ? `${formatDate(t.start)}〜${formatDate(t.end)}` : '日程未定'}
                </span>
              </div>
              <dl className={styles.stats}>
                <div>
                  <dt>完了率</dt>
                  <dd>
                    {t.completionRate}%（{t.doneCount}/{t.taskCount}件） <ProgressBar value={t.completionRate} />
                  </dd>
                </div>
                <div>
                  <dt>遅れ</dt>
                  <dd>{t.delayedCount > 0 ? <Link to={`/gantt?teams=${t.teamId}&flags=overdue,late_start`}>！ {t.delayedCount}件</Link> : '0件'}</dd>
                </div>
                <div>
                  <dt>工数（今日まで）</dt>
                  <dd>
                    予定 {formatHours(t.plannedMinutesToDate)}／実績 {formatHours(t.actualMinutesToDate)}（差 {formatSignedHours(t.actualMinutesToDate - t.plannedMinutesToDate)}）
                  </dd>
                </div>
                <div>
                  <dt>進捗</dt>
                  <dd>{t.progress}%</dd>
                </div>
              </dl>
              {t.role === 'leader' && <Link to={`/teams/${t.teamId}/report`}>担当者別の予実を見る</Link>}
            </div>
          ))}
        </section>
      </div>
    </div>
  );
}
