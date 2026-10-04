import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useTeamReport } from '../../api/hooks';
import { ErrorBox, Loading } from '../../components/ui';
import { presetRange, todayIso } from '../../lib/dates';
import { formatHours, formatSignedHours } from '../../lib/effort';
import { useSyncCurrentTeam } from '../../layout/context';

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
      <div className="page-header">
        <h1>担当者別の予実{data && `（${data.teamName}）`}</h1>
        <Link to={`/teams/${teamId}`}>チームへ戻る</Link>
      </div>
      <div className="row">
        <label className="row">
          期間
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        〜
        <input type="date" aria-label="期間の終了" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
      </div>
      <p className="muted">予定工数は、タスクの予定工数を期間と重なる稼働日数で按分したものです（このチームの分だけを数えます）。</p>
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && (
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
              <th scope="col" className="num">
                差
              </th>
              <th scope="col" className="num">
                遅れ
              </th>
            </tr>
          </thead>
          <tbody>
            {data.rows.map((r) => (
              <tr key={r.userId ?? 'none'}>
                <td>
                  {r.userId ? <Link to={`/gantt?teams=${teamId}&assignees=${r.userId}&group=assignee`}>{r.displayName}</Link> : r.displayName}
                  {r.userId && !r.currentMember && <span className="muted">（チーム外）</span>}
                </td>
                <td className="num">{r.taskCount}件</td>
                <td className="num">{formatHours(r.plannedMinutes)}</td>
                <td className="num">{formatHours(r.actualMinutes)}</td>
                <td className="num">{formatSignedHours(r.varianceMinutes)}</td>
                <td className="num">{r.delayedCount > 0 ? `！ ${r.delayedCount}件` : '0件'}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <th scope="row">{data.total.displayName}</th>
              <td className="num">{data.total.taskCount}件</td>
              <td className="num">{formatHours(data.total.plannedMinutes)}</td>
              <td className="num">{formatHours(data.total.actualMinutes)}</td>
              <td className="num">{formatSignedHours(data.total.varianceMinutes)}</td>
              <td className="num">{data.total.delayedCount}件</td>
            </tr>
          </tfoot>
        </table>
      )}
    </div>
  );
}
