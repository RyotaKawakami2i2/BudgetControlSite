import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useTeamReport } from '../../api/hooks';
import { Icon } from '../../components/Icon';
import { Avatar, ErrorBox, Loading, PageHeader, Pill, ProgressBar, ui } from '../../components/ui';
import { presetRange, todayIso } from '../../lib/dates';
import { formatHours, formatSignedHours } from '../../lib/effort';
import { useSyncCurrentTeam } from '../../layout/context';
import styles from './teams.module.css';

/** チームの予実（SC-11。FR-DSH-02）。期間を指定して、担当者別の予定工数・実績工数・差・遅れの件数を表で見る。 */
export function TeamReportPage() {
  const { teamId = '' } = useParams();
  useSyncCurrentTeam(teamId);
  const initial = presetRange('this_month', todayIso());
  const [from, setFrom] = useState(initial.from);
  const [to, setTo] = useState(initial.to);
  const { data, error, isLoading } = useTeamReport(teamId, from, to);

  return (
    <div className="page">
      <PageHeader
        icon="report"
        title={`担当者別の予実${data ? `（${data.teamName}）` : ''}`}
        description="期間の中で、だれにどれだけの予定工数があり、実際にどれだけ作業したかを比べます。担当者の名前を押すと、その人のタスクをガントで開きます。"
        actions={
          <Link to={`/teams/${teamId}`} className={ui.button}>
            <Icon name="chevronLeft" size={16} />
            チームへ戻る
          </Link>
        }
      />
      <div className={['card', styles.filterBar].join(' ')}>
        <label className={styles.inlineField} htmlFor="report-from">
          期間
        </label>
        <input id="report-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        〜
        <input type="date" aria-label="期間の終了" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
        <p className="muted small">予定工数は、タスクの予定工数を期間と重なる稼働日数で按分したものです（このチームの分だけを数えます）。</p>
      </div>
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th scope="col">担当者</th>
                <th scope="col" className="num">
                  タスク
                </th>
                <th scope="col" className="num">
                  予定工数
                </th>
                <th scope="col" className="num">
                  実績工数
                </th>
                <th scope="col">実績の割合（予定に対して）</th>
                <th scope="col" className="num">
                  差
                </th>
                <th scope="col" className="num">
                  遅れ
                </th>
              </tr>
            </thead>
            <tbody>
              {data.rows.map((r) => {
                const ratio = r.plannedMinutes > 0 ? Math.round((r.actualMinutes / r.plannedMinutes) * 100) : 0;
                return (
                  <tr key={r.userId ?? 'none'}>
                    <td>
                      <span className={styles.person}>
                        {r.userId ? <Avatar name={r.displayName} size="small" /> : <span className={styles.noAvatar} aria-hidden="true" />}
                        {r.userId ? <Link to={`/gantt?teams=${teamId}&assignees=${r.userId}&group=assignee`}>{r.displayName}</Link> : <span className="muted">{r.displayName}</span>}
                        {r.userId && !r.currentMember && <Pill tone="neutral">チーム外</Pill>}
                      </span>
                    </td>
                    <td className="num">{r.taskCount}件</td>
                    <td className="num">{formatHours(r.plannedMinutes)}</td>
                    <td className="num">{formatHours(r.actualMinutes)}</td>
                    <td>
                      {r.plannedMinutes > 0 ? (
                        <span className={styles.ratio}>
                          <ProgressBar value={Math.min(ratio, 100)} tone={ratio > 100 ? 'danger' : 'primary'} label="予定に対する実績の割合" />
                          <span className={ratio > 100 ? 'danger-text' : 'muted'}>{ratio}%</span>
                        </span>
                      ) : (
                        <span className="muted">-</span>
                      )}
                    </td>
                    <td className={['num', r.varianceMinutes > 0 && 'danger-text'].filter(Boolean).join(' ')}>{formatSignedHours(r.varianceMinutes)}</td>
                    <td className="num">{r.delayedCount > 0 ? <Pill tone="danger">！ {r.delayedCount}件</Pill> : <span className="muted">0件</span>}</td>
                  </tr>
                );
              })}
            </tbody>
            <tfoot>
              <tr className={styles.totalRow}>
                <th scope="row">{data.total.displayName}</th>
                <td className="num">{data.total.taskCount}件</td>
                <td className="num">{formatHours(data.total.plannedMinutes)}</td>
                <td className="num">{formatHours(data.total.actualMinutes)}</td>
                <td />
                <td className="num">{formatSignedHours(data.total.varianceMinutes)}</td>
                <td className="num">{data.total.delayedCount}件</td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}
    </div>
  );
}
