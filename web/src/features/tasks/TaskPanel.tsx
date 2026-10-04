import { useState } from 'react';
import {
  useCommentMutations,
  useComments,
  useDeleteTask,
  useDependencyMutations,
  useHistory,
  useTask,
  useTeam,
  useTeamTasks,
  useUpdateTask,
  useWorkLogMutations,
  useWorkLogs,
} from '../../api/hooks';
import type { TaskDetail, TaskStatus, WorkLog } from '../../api/types';
import { Icon } from '../../components/Icon';
import {
  Button,
  ConfirmDialog,
  ErrorBox,
  FlagBadges,
  Loading,
  PriorityBadge,
  ProgressBar,
  StatusBadge,
  TagChip,
  Tabs,
  describeError,
  useToast,
} from '../../components/ui';
import { useTaskPanel } from '../../layout/context';
import { AutoLinkText } from '../../lib/autolink';
import { formatDate, formatDateTime } from '../../lib/dates';
import { formatHours, formatSignedHours } from '../../lib/effort';
import { ROLE, STATUS } from '../../lib/labels';
import { messageText } from '../../lib/messages';
import { TaskFormDialog, type TaskFormTarget } from './TaskFormDialog';
import { CompleteDialog, WorkLogDialog } from './WorkDialogs';
import styles from './tasks.module.css';

type Tab = 'overview' | 'worklogs' | 'comments' | 'history';

const HISTORY_FIELD: Record<string, string> = {
  title: 'タイトル',
  description: '説明',
  priority: '優先度',
  plannedStart: '予定開始日',
  plannedEnd: '予定終了日',
  plannedMinutes: '予定工数',
  isMilestone: 'マイルストーン',
  assigneeId: '担当者',
  status: '状態',
  progress: '進捗率',
  actualStart: '実績開始日',
  actualEnd: '実績終了日',
  resultNote: '結果コメント',
  tagIds: 'タグ',
  parentId: '親タスク',
  workLog: '作業実績',
  predecessor: '先行タスク',
};

const HISTORY_KIND: Record<string, string> = {
  created: '作成',
  updated: '変更',
  moved: '移動',
  deleted: '削除',
  restored: '復元',
  worklog_added: '作業実績の記録',
  worklog_updated: '作業実績の修正',
  worklog_deleted: '作業実績の削除',
  dependency_added: '依存関係の追加',
  dependency_removed: '依存関係の削除',
};

/** タスク詳細（SC-06。詳細設計書 7.4）。画面の右側に開き、その場で編集できる。 */
export function TaskPanel({ taskId }: { taskId: string }) {
  const { close } = useTaskPanel();
  const { data, error, isLoading } = useTask(taskId);
  const [tab, setTab] = useState<Tab>('overview');

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <div className={styles.titleRow}>
          <h2>{data?.task.title ?? 'タスク'}</h2>
          <Button variant="ghost" iconOnly aria-label="タスク詳細を閉じる" onClick={close}>
            <Icon name="close" />
          </Button>
        </div>
        {data && (
          <p className={styles.path}>
            {[data.teamName, ...data.path.map((p) => p.title)].join(' > ')}
          </p>
        )}
        <Tabs
          label="タスク詳細"
          active={tab}
          onChange={setTab}
          tabs={[
            { id: 'overview', label: '概要' },
            { id: 'worklogs', label: '作業実績' },
            { id: 'comments', label: 'コメント' },
            { id: 'history', label: '履歴' },
          ]}
        />
      </div>
      <div className={styles.body}>
        {isLoading && <Loading />}
        {error && <ErrorBox error={error} />}
        {data && tab === 'overview' && <Overview detail={data} />}
        {data && tab === 'worklogs' && <WorkLogsTab detail={data} />}
        {data && tab === 'comments' && <CommentsTab detail={data} />}
        {data && tab === 'history' && <HistoryTab taskId={data.task.id} />}
      </div>
    </div>
  );
}

function Overview({ detail }: { detail: TaskDetail }) {
  const t = detail.task;
  const can = detail.can;
  const toast = useToast();
  const update = useUpdateTask();
  const remove = useDeleteTask();
  const { close, open } = useTaskPanel();
  const { data: team } = useTeam(can.assign ? t.teamId : null);
  const [dialog, setDialog] = useState<'worklog' | 'complete' | 'delete' | null>(null);
  const [form, setForm] = useState<TaskFormTarget | null>(null);
  const readOnly = detail.role === 'admin_view' || detail.teamArchived;

  const patch = async (input: Record<string, unknown>, success = messageText('MSG-CMN-001')) => {
    try {
      await update.mutateAsync({ id: t.id, input: { version: t.version, ...input } });
      toast.show('success', success);
    } catch (error) {
      toast.show('error', describeError(error));
    }
  };

  const changeStatus = (status: TaskStatus) => {
    if (status === 'done') setDialog('complete');
    else void patch({ status });
  };

  const variance = t.plannedMinutes !== null ? t.actualMinutes - t.plannedMinutes : null;
  const workTarget = { id: t.id, title: t.title, version: t.version, progress: t.progress, status: t.status };

  return (
    <>
      {readOnly && (
        <p className={styles.readonlyNote}>
          {detail.teamArchived ? messageText('MSG-CMN-ARC') : `${ROLE.admin_view}: このチームのタスクは閲覧だけができます。`}
        </p>
      )}

      <div className={styles.inline}>
        {can.nextStatuses.length > 0 ? (
          <label>
            状態
            <select value={t.status} onChange={(e) => changeStatus(e.target.value as TaskStatus)} disabled={update.isPending}>
              <option value={t.status}>
                {STATUS[t.status].icon} {STATUS[t.status].label}
              </option>
              {can.nextStatuses.map((s) => (
                <option key={s} value={s}>
                  {STATUS[s].icon} {STATUS[s].label}
                </option>
              ))}
            </select>
          </label>
        ) : (
          <StatusBadge status={t.status} />
        )}
        {can.editActual && !t.isSummary && t.status !== 'done' && !t.isMilestone ? (
          <label>
            進捗
            <select value={t.progress} onChange={(e) => void patch({ progress: Number(e.target.value) })} disabled={update.isPending}>
              {Array.from({ length: 21 }, (_, i) => i * 5).map((p) => (
                <option key={p} value={p}>
                  {p}%
                </option>
              ))}
            </select>
          </label>
        ) : (
          <span>
            進捗 {t.progress}% <ProgressBar value={t.progress} />
          </span>
        )}
        <span>
          優先度 <PriorityBadge priority={t.priority} />
        </span>
        <FlagBadges flags={t.flags} />
        {t.descendantFlagged && <span className={styles.warning}>！ 子タスクに遅れあり</span>}
      </div>

      <div className={styles.inline}>
        {can.assign && team ? (
          <label>
            担当
            <select value={t.assigneeId ?? ''} onChange={(e) => void patch({ assigneeId: e.target.value || null })} disabled={update.isPending}>
              <option value="">未割り当て</option>
              {team.members.map((m) => (
                <option key={m.userId} value={m.userId}>
                  {m.displayName}
                </option>
              ))}
            </select>
          </label>
        ) : (
          <span>担当 {detail.assigneeName ?? '未割り当て'}</span>
        )}
        {t.isMilestone && <span>◆ マイルストーン</span>}
        {t.isSummary && <span className="muted">まとめタスク（子 {detail.childCount} 件から計算）</span>}
      </div>

      <table className={styles.planTable}>
        <thead>
          <tr>
            <th scope="col" />
            <th scope="col">開始日</th>
            <th scope="col">終了日</th>
            <th scope="col">工数</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <th scope="row">予定</th>
            <td>{formatDate(t.plannedStart) || '未定'}</td>
            <td>{formatDate(t.plannedEnd) || '未定'}</td>
            <td>{formatHours(t.plannedMinutes, '-')}</td>
          </tr>
          <tr>
            <th scope="row">実績</th>
            <td>{formatDate(t.actualStart) || '-'}</td>
            <td>{formatDate(t.actualEnd) || '-'}</td>
            <td>{formatHours(t.actualMinutes)}</td>
          </tr>
        </tbody>
      </table>
      <p>
        期待進捗 {t.expectedProgress === null ? '-' : `${t.expectedProgress}%`}　工数の差 {variance === null ? '-' : formatSignedHours(variance)}
      </p>

      {detail.tags.length > 0 && (
        <div className={styles.chips}>
          {detail.tags.map((tag) => (
            <TagChip key={tag.id} tag={tag} />
          ))}
        </div>
      )}

      <section className={styles.section}>
        <h3>説明</h3>
        {detail.description ? (
          <p className="prewrap">
            <AutoLinkText text={detail.description} />
          </p>
        ) : (
          <p className="muted">（なし）</p>
        )}
      </section>

      {detail.resultNote && (
        <section className={styles.section}>
          <h3>結果コメント</h3>
          <p className="prewrap">
            <AutoLinkText text={detail.resultNote} />
          </p>
        </section>
      )}

      <Dependencies detail={detail} onOpen={open} />

      <div className={styles.actions}>
        {can.logWork && (
          <Button variant="primary" onClick={() => setDialog('worklog')}>
            <Icon name="clock" />
            実績を記録
          </Button>
        )}
        {can.nextStatuses.includes('done') && <Button onClick={() => setDialog('complete')}>完了にする</Button>}
        {(can.editPlan || can.assign) && <Button onClick={() => setForm({ mode: 'edit', detail })}>編集</Button>}
        {can.addChild && (
          <Button onClick={() => setForm({ mode: 'create', teamId: t.teamId, parentId: t.id })}>
            <Icon name="plus" />
            子タスクを追加
          </Button>
        )}
        {can.delete && (
          <Button variant="danger" onClick={() => setDialog('delete')}>
            削除
          </Button>
        )}
      </div>

      <p className={styles.meta}>
        作成 {detail.createdByName}（{formatDateTime(detail.createdAt)}）　更新 {formatDateTime(detail.updatedAt)}
      </p>

      {dialog === 'worklog' && <WorkLogDialog task={workTarget} onClose={() => setDialog(null)} />}
      {dialog === 'complete' && <CompleteDialog task={workTarget} actualStart={t.actualStart} onClose={() => setDialog(null)} />}
      <ConfirmDialog
        open={dialog === 'delete'}
        title="タスクの削除"
        danger
        busy={remove.isPending}
        confirmLabel="削除する"
        message={
          <>
            <p>「{t.title}」を削除します。</p>
            {detail.childCount > 0 && <p>このタスクには子タスクがあります。子タスクもすべて削除します。</p>}
            <p className="muted">削除から 30 日以内は、リーダーが復元できます。</p>
          </>
        }
        onCancel={() => setDialog(null)}
        onConfirm={async () => {
          try {
            await remove.mutateAsync({ id: t.id, version: t.version });
            toast.show('success', '削除しました。');
            setDialog(null);
            close();
          } catch (error) {
            setDialog(null);
            toast.show('error', describeError(error));
          }
        }}
      />
      {form && <TaskFormDialog target={form} onClose={() => setForm(null)} />}
    </>
  );
}

/** 依存関係（FR-TSK-13）。先行タスクを追加・削除する。日程が逆転していれば警告する。 */
function Dependencies({ detail, onOpen }: { detail: TaskDetail; onOpen: (id: string) => void }) {
  const toast = useToast();
  const { add, remove } = useDependencyMutations(detail.task.id);
  const { data: options } = useTeamTasks(detail.can.editDependencies ? detail.task.teamId : null);
  const [selected, setSelected] = useState('');
  const existing = new Set(detail.predecessors.map((p) => p.id));
  const choices = (options ?? []).filter((o) => o.id !== detail.task.id && !existing.has(o.id));

  if (detail.predecessors.length === 0 && detail.successors.length === 0 && !detail.can.editDependencies) return null;

  return (
    <section className={styles.section}>
      <h3>依存関係</h3>
      {detail.warnings.includes('MSG-TSK-017') && <p className={styles.warning}>！ {messageText('MSG-TSK-017')}</p>}
      <p className="muted">先行タスク（終わってから、このタスクを始める）</p>
      {detail.predecessors.length === 0 && <p className="muted">なし</p>}
      <ul>
        {detail.predecessors.map((p) => (
          <li key={p.id}>
            <Button variant="ghost" size="small" onClick={() => onOpen(p.id)}>
              {p.title}
            </Button>
            <span className="muted">（予定終了 {formatDate(p.plannedEnd) || '未定'}）</span>
            {detail.can.editDependencies && (
              <Button
                variant="ghost"
                size="small"
                aria-label={`${p.title} を先行タスクから外す`}
                onClick={() => remove.mutate(p.id, { onError: (e) => toast.show('error', describeError(e)) })}
              >
                ×
              </Button>
            )}
          </li>
        ))}
      </ul>
      {detail.can.editDependencies && (
        <div className="row">
          <select aria-label="先行タスクを選ぶ" value={selected} onChange={(e) => setSelected(e.target.value)}>
            <option value="">先行タスクを選ぶ</option>
            {choices.map((o) => (
              <option key={o.id} value={o.id}>
                {'　'.repeat(o.depth - 1)}
                {o.title}
              </option>
            ))}
          </select>
          <Button
            size="small"
            disabled={!selected || add.isPending}
            onClick={async () => {
              try {
                const result = await add.mutateAsync(selected);
                setSelected('');
                toast.show(result.warnings.length > 0 ? 'info' : 'success', result.warnings.length > 0 ? messageText('MSG-TSK-017') : '先行タスクを追加しました。');
              } catch (error) {
                toast.show('error', describeError(error));
              }
            }}
          >
            追加
          </Button>
        </div>
      )}
      {detail.successors.length > 0 && (
        <>
          <p className="muted">後続タスク</p>
          <ul>
            {detail.successors.map((s) => (
              <li key={s.id}>
                <Button variant="ghost" size="small" onClick={() => onOpen(s.id)}>
                  {s.title}
                </Button>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

function WorkLogsTab({ detail }: { detail: TaskDetail }) {
  const { data, error, isLoading } = useWorkLogs(detail.task.id);
  const { remove } = useWorkLogMutations();
  const toast = useToast();
  const [editing, setEditing] = useState<WorkLog | null>(null);
  const [adding, setAdding] = useState(false);
  const t = detail.task;
  const target = { id: t.id, title: t.title, version: t.version, progress: t.progress, status: t.status };

  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorBox error={error} />;

  const list = (logs: WorkLog[]) => (
    <table className="data-table">
      <thead>
        <tr>
          <th scope="col">作業日</th>
          <th scope="col">記録した人</th>
          <th scope="col" className="num">
            時間
          </th>
          <th scope="col">メモ</th>
          <th scope="col">
            <span className="visually-hidden">操作</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {logs.map((log) => (
          <tr key={log.id}>
            <td className="nowrap">{formatDate(log.workDate)}</td>
            <td>
              {log.userName}
              {log.taskId !== t.id && <div className={styles.meta}>{log.taskTitle}</div>}
            </td>
            <td className="num">{formatHours(log.minutes)}</td>
            <td className="prewrap">{log.note}</td>
            <td className="nowrap">
              {log.canEdit && (
                <>
                  <Button size="small" variant="ghost" onClick={() => setEditing(log)}>
                    修正
                  </Button>
                  <Button
                    size="small"
                    variant="ghost"
                    onClick={() =>
                      remove.mutate({ id: log.id, taskId: log.taskId }, { onSuccess: () => toast.show('success', '削除しました。'), onError: (e) => toast.show('error', describeError(e)) })
                    }
                  >
                    削除
                  </Button>
                </>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );

  return (
    <>
      {detail.can.logWork && (
        <div>
          <Button variant="primary" onClick={() => setAdding(true)}>
            実績を記録
          </Button>
        </div>
      )}
      <section className={styles.section}>
        <h3>人ごとの合計（計 {formatHours(data.totalMinutes)}）</h3>
        {data.totals.length === 0 && <p className="muted">まだ記録はありません。</p>}
        <ul>
          {data.totals.map((total) => (
            <li key={total.userId}>
              {total.userName}: {formatHours(total.minutes)}
            </li>
          ))}
        </ul>
      </section>
      <section className={styles.section}>
        <h3>自分の記録</h3>
        {data.mine.length === 0 ? <p className="muted">なし</p> : list(data.mine)}
      </section>
      {data.all && (
        <section className={styles.section}>
          <h3>全員の明細</h3>
          {data.all.length === 0 ? <p className="muted">なし</p> : list(data.all)}
        </section>
      )}
      {(adding || editing) && <WorkLogDialog task={target} log={editing ?? undefined} onClose={() => (setAdding(false), setEditing(null))} />}
    </>
  );
}

function CommentsTab({ detail }: { detail: TaskDetail }) {
  const { data, error, isLoading } = useComments(detail.task.id);
  const { create, update, remove } = useCommentMutations(detail.task.id);
  const toast = useToast();
  const [body, setBody] = useState('');
  const [editing, setEditing] = useState<{ id: string; body: string } | null>(null);

  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorBox error={error} />;

  return (
    <>
      {data.length === 0 && <p className="muted">コメントはまだありません。</p>}
      {data.map((c) => (
        <div key={c.id} className={styles.listItem}>
          <p className={styles.meta}>
            {c.authorName}　{formatDateTime(c.createdAt)}
            {c.editedAt && '（編集済み）'}
          </p>
          {c.deleted ? (
            <p className="muted">削除されました</p>
          ) : editing?.id === c.id ? (
            <div className="stack">
              <textarea aria-label="コメントの編集" value={editing.body} maxLength={2000} onChange={(e) => setEditing({ id: c.id, body: e.target.value })} />
              <div className="row">
                <Button
                  size="small"
                  variant="primary"
                  onClick={() =>
                    update.mutate({ id: c.id, body: editing.body }, { onSuccess: () => setEditing(null), onError: (e) => toast.show('error', describeError(e)) })
                  }
                >
                  保存
                </Button>
                <Button size="small" onClick={() => setEditing(null)}>
                  取り消す
                </Button>
              </div>
            </div>
          ) : (
            <>
              <p className="prewrap">
                <AutoLinkText text={c.body} />
              </p>
              {c.canEdit && (
                <div className="row">
                  <Button size="small" variant="ghost" onClick={() => setEditing({ id: c.id, body: c.body ?? '' })}>
                    編集
                  </Button>
                  <Button size="small" variant="ghost" onClick={() => remove.mutate(c.id, { onError: (e) => toast.show('error', describeError(e)) })}>
                    削除
                  </Button>
                </div>
              )}
            </>
          )}
        </div>
      ))}
      {detail.can.comment && (
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault();
            if (!body.trim()) return;
            create.mutate(body, { onSuccess: () => setBody(''), onError: (err) => toast.show('error', describeError(err)) });
          }}
        >
          <label htmlFor="new-comment" className="visually-hidden">
            コメント
          </label>
          <textarea id="new-comment" placeholder="コメントを入力（2,000 字以内。予定の変更の依頼などに使えます）" value={body} maxLength={2000} onChange={(e) => setBody(e.target.value)} />
          <div>
            <Button type="submit" variant="primary" disabled={!body.trim() || create.isPending}>
              投稿する
            </Button>
          </div>
        </form>
      )}
    </>
  );
}

function HistoryTab({ taskId }: { taskId: string }) {
  const { data, error, isLoading } = useHistory(taskId);
  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorBox error={error} />;
  return (
    <table className="data-table">
      <thead>
        <tr>
          <th scope="col">日時</th>
          <th scope="col">変更した人</th>
          <th scope="col">内容</th>
        </tr>
      </thead>
      <tbody>
        {data.items.map((h) => (
          <tr key={h.id}>
            <td className="nowrap">{formatDateTime(h.occurredAt)}</td>
            <td>{h.actorName}</td>
            <td>
              {HISTORY_KIND[h.kind] ?? h.kind}
              {h.field && `: ${HISTORY_FIELD[h.field] ?? h.field}`}
              {(h.oldValue || h.newValue) && (
                <div className="prewrap">
                  {h.oldValue && <span className="muted">{h.oldValue}</span>}
                  {h.oldValue && h.newValue && ' → '}
                  {h.newValue}
                </div>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
