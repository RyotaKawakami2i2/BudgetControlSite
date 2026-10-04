import { useState } from 'react';
import { useAuditLogs } from '../../api/hooks';
import { Icon } from '../../components/Icon';
import { Button, EmptyState, ErrorBox, Loading, PageHeader, Pill } from '../../components/ui';
import styles from './admin.module.css';
import { formatDateTime, todayIso, addDays } from '../../lib/dates';

/** 監査ログ（SC-17。FR-ADM-06）。期間・操作者・操作の種類・対象で検索する。検索したことも記録される。 */
export function AuditLogsPage() {
  const [form, setForm] = useState({ from: addDays(todayIso(), -7), to: todayIso(), action: '', targetType: '', targetId: '' });
  const [search, setSearch] = useState<Record<string, string>>(() => toSearch(form));
  const [cursors, setCursors] = useState<string[]>([]);
  const cursor = cursors[cursors.length - 1] ?? null;
  const { data, error, isLoading } = useAuditLogs(search, cursor);

  return (
    <div className="page">
      <PageHeader
        icon="shield"
        title="監査ログ"
        description="ログイン、データの変更、権限による拒否などの記録です。改ざんを見つけられるよう、記録はハッシュの鎖でつないでいます。検索したことも記録されます。"
      />
      <form
        className={['card', styles.formCard].join(' ')}
        onSubmit={(e) => {
          e.preventDefault();
          setCursors([]);
          setSearch(toSearch(form));
        }}
      >
        <label>
          期間の開始
          <input type="date" value={form.from} onChange={(e) => setForm({ ...form, from: e.target.value })} />
        </label>
        <label>
          期間の終了
          <input type="date" value={form.to} onChange={(e) => setForm({ ...form, to: e.target.value })} />
        </label>
        <label>
          操作の種類
          <input type="text" placeholder="例: auth.login、task.*" value={form.action} onChange={(e) => setForm({ ...form, action: e.target.value })} />
        </label>
        <label>
          対象の種類
          <select value={form.targetType} onChange={(e) => setForm({ ...form, targetType: e.target.value })}>
            <option value="">すべての対象</option>
            {['user', 'team', 'task', 'work_log', 'comment', 'tag', 'view', 'holiday', 'session'].map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </label>
        <label>
          対象の ID
          <input type="text" value={form.targetId} onChange={(e) => setForm({ ...form, targetId: e.target.value })} />
        </label>
        <Button type="submit" variant="primary">
          <Icon name="search" size={16} />
          検索
        </Button>
      </form>
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && data.items.length === 0 && <EmptyState icon="shield" title="条件に合う記録はありません" description="期間を広げるか、操作の種類を変えてください。" />}
      {data && data.items.length > 0 && (
        <>
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col">ID</th>
                  <th scope="col">日時</th>
                  <th scope="col">操作者</th>
                  <th scope="col">操作</th>
                  <th scope="col">結果</th>
                  <th scope="col">対象</th>
                  <th scope="col">送信元</th>
                  <th scope="col">内容</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((a) => (
                  <tr key={a.id}>
                    <td className="num">{a.id}</td>
                    <td className="nowrap">{formatDateTime(a.occurredAt)}</td>
                    <td className="nowrap">{a.actorName ?? (a.actorId ? a.actorId : '（未ログイン）')}</td>
                    <td>{a.action}</td>
                    <td className="nowrap">
                      {a.result === 'success' ? (
                        <Pill tone="success">✓ 成功</Pill>
                      ) : a.result === 'failure' ? (
                        <Pill tone="danger">× 失敗</Pill>
                      ) : (
                        <Pill tone="warning">！ 拒否</Pill>
                      )}
                    </td>
                    <td>
                      {a.targetType} {a.targetId}
                    </td>
                    <td>
                      {a.ip}
                      <div className="muted">{a.requestId}</div>
                    </td>
                    <td>
                      {a.detail ? (
                        <details className={styles.detail}>
                          <summary>内容を見る</summary>
                          <code className="prewrap">{JSON.stringify(a.detail, null, 1)}</code>
                        </details>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="row">
            <Button disabled={cursors.length === 0} onClick={() => setCursors((c) => c.slice(0, -1))}>
              ◀ 新しい記録
            </Button>
            <Button disabled={!data.nextCursor} onClick={() => data.nextCursor && setCursors((c) => [...c, data.nextCursor!])}>
              古い記録 ▶
            </Button>
          </div>
        </>
      )}
    </div>
  );
}

function toSearch(form: { from: string; to: string; action: string; targetType: string; targetId: string }): Record<string, string> {
  // 期間は日本時間の日付で指定し、UTC の日時にして送る
  const search: Record<string, string> = {};
  if (form.from) search.from = new Date(`${form.from}T00:00:00+09:00`).toISOString();
  if (form.to) search.to = new Date(`${addDays(form.to, 1)}T00:00:00+09:00`).toISOString();
  if (form.action.trim()) search.action = form.action.trim();
  if (form.targetType) search.targetType = form.targetType;
  if (form.targetId.trim()) search.targetId = form.targetId.trim();
  return search;
}
