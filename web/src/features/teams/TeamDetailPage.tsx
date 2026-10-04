import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useDeletedTasks, useMe, useRestoreTask, useTeam, useTeamMutations } from '../../api/hooks';
import type { Tag, TagColor, TeamDetail } from '../../api/types';
import { Button, ConfirmDialog, Dialog, ErrorBox, Field, Loading, TagChip, describeError, fieldErrors, useToast } from '../../components/ui';
import { formatDateTime } from '../../lib/dates';
import { ROLE, TAG_COLORS, TAG_COLOR_LABEL } from '../../lib/labels';
import { UserPicker } from './UserPicker';
import { useSyncCurrentTeam } from '../../layout/context';

/** チームの詳細（SC-12。FR-TEM-01〜07）。メンバーと役割、タグ、名前・説明の変更、アーカイブ、削除したタスクの復元。 */
export function TeamDetailPage() {
  const { teamId = '' } = useParams();
  useSyncCurrentTeam(teamId);
  const { data: team, error, isLoading } = useTeam(teamId);
  if (isLoading) return <Loading />;
  if (error || !team) return <div className="page"><ErrorBox error={error} /></div>;
  return <TeamDetail team={team} />;
}

function TeamDetail({ team }: { team: TeamDetail }) {
  const { data: me } = useMe();
  const m = useTeamMutations();
  const toast = useToast();
  const [editing, setEditing] = useState(false);
  const [archiving, setArchiving] = useState(false);
  const [removing, setRemoving] = useState<{ userId: string; name: string } | null>(null);
  const [addRole, setAddRole] = useState<'member' | 'leader'>('member');
  const run = (promise: Promise<unknown>, success: string) =>
    promise.then(() => toast.show('success', success)).catch((e: unknown) => toast.show('error', describeError(e)));

  return (
    <div className="page">
      <div className="page-header">
        <h1>
          {team.name}
          {team.archived && <span className="muted">（アーカイブ）</span>}
        </h1>
        <Link to={`/gantt?teams=${team.id}`}>ガントを開く</Link>
        {team.can.viewReport && <Link to={`/teams/${team.id}/report`}>担当者別の予実</Link>}
        {team.can.update && !team.archived && <Button onClick={() => setEditing(true)}>名前・説明を変える</Button>}
        {(team.can.archive || team.can.unarchive) && (
          <Button variant={team.archived ? 'default' : 'danger'} onClick={() => setArchiving(true)}>
            {team.archived ? 'アーカイブを解除する' : 'アーカイブする'}
          </Button>
        )}
      </div>
      <p className="muted">あなたの役割: {ROLE[team.role]}</p>
      {team.description && <p className="prewrap">{team.description}</p>}

      <section className="card stack">
        <h2>メンバー（{team.members.length}人）</h2>
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">名前</th>
              {team.members.some((x) => x.email) && <th scope="col">メールアドレス</th>}
              <th scope="col">役割</th>
              <th scope="col">追加した日時</th>
              {team.can.manageMembers && (
                <th scope="col">
                  <span className="visually-hidden">操作</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {team.members.map((member) => (
              <tr key={member.userId}>
                <td>
                  {member.displayName}
                  {member.disabled && <span className="muted">（無効）</span>}
                  {member.userId === me?.id && <span className="muted">（自分）</span>}
                </td>
                {team.members.some((x) => x.email) && <td>{member.email}</td>}
                <td>{ROLE[member.role]}</td>
                <td>{formatDateTime(member.joinedAt)}</td>
                {team.can.manageMembers && (
                  <td className="nowrap">
                    {!team.archived && (
                      <>
                        <Button
                          size="small"
                          onClick={() =>
                            run(
                              m.changeRole.mutateAsync({ id: team.id, userId: member.userId, role: member.role === 'leader' ? 'member' : 'leader' }),
                              '役割を変えました。',
                            )
                          }
                        >
                          {member.role === 'leader' ? 'リーダーから外す' : 'リーダーにする'}
                        </Button>{' '}
                        <Button size="small" variant="ghost" onClick={() => setRemoving({ userId: member.userId, name: member.displayName })}>
                          チームから外す
                        </Button>
                      </>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
        {team.can.manageMembers && !team.archived && (
          <div className="stack">
            <h3>メンバーを追加する</h3>
            <label className="row">
              役割
              <select value={addRole} onChange={(e) => setAddRole(e.target.value as 'member' | 'leader')}>
                <option value="member">メンバー</option>
                <option value="leader">リーダー</option>
              </select>
            </label>
            <UserPicker
              label="追加する利用者を検索"
              exclude={team.members.map((x) => x.userId)}
              onPick={(u) => run(m.addMember.mutateAsync({ id: team.id, userId: u.id, role: addRole }), `${u.displayName} さんを追加しました。`)}
            />
          </div>
        )}
      </section>

      <Tags team={team} />
      {team.can.restore && <DeletedTasks teamId={team.id} />}

      {editing && <EditTeamDialog team={team} onClose={() => setEditing(false)} />}
      <ConfirmDialog
        open={archiving}
        title={team.archived ? 'アーカイブの解除' : 'アーカイブ'}
        danger={!team.archived}
        confirmLabel={team.archived ? '解除する' : 'アーカイブする'}
        message={
          <p>
            {team.archived
              ? `「${team.name}」のアーカイブを解除し、変更できるようにします。`
              : `「${team.name}」をアーカイブします。アーカイブしたチームは読み取り専用になります（タスクと作業実績は残ります）。`}
          </p>
        }
        onCancel={() => setArchiving(false)}
        onConfirm={() => {
          setArchiving(false);
          void run(m.archive.mutateAsync({ id: team.id, archived: !team.archived, version: team.version }), team.archived ? 'アーカイブを解除しました。' : 'アーカイブしました。');
        }}
      />
      <ConfirmDialog
        open={!!removing}
        title="チームから外す"
        danger
        confirmLabel="外す"
        message={
          <p>
            {removing?.name} さんをチームから外します。担当していた未完了のタスクは「未割り当て」に戻ります（作業実績は残ります）。
          </p>
        }
        onCancel={() => setRemoving(null)}
        onConfirm={() => {
          const target = removing;
          setRemoving(null);
          if (target) void run(m.removeMember.mutateAsync({ id: team.id, userId: target.userId }), `${target.name} さんをチームから外しました。`);
        }}
      />
    </div>
  );
}

function EditTeamDialog({ team, onClose }: { team: TeamDetail; onClose: () => void }) {
  const { update } = useTeamMutations();
  const toast = useToast();
  const [name, setName] = useState(team.name);
  const [description, setDescription] = useState(team.description ?? '');
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  return (
    <Dialog
      open
      title="チームの名前・説明"
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>取り消す</Button>
          <Button
            variant="primary"
            disabled={update.isPending}
            onClick={async () => {
              try {
                await update.mutateAsync({ id: team.id, version: team.version, name: name.trim(), description: description.trim() || null });
                toast.show('success', '保存しました。');
                onClose();
              } catch (e) {
                setErrors(fieldErrors(e));
                toast.show('error', describeError(e));
              }
            }}
          >
            保存する
          </Button>
        </>
      }
    >
      <div className="stack">
        <Field label="チーム名" required htmlFor="edit-team-name" errors={errors.name}>
          <input id="edit-team-name" type="text" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="説明" htmlFor="edit-team-desc" errors={errors.description}>
          <textarea id="edit-team-desc" value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
        </Field>
      </div>
    </Dialog>
  );
}

/** タグ（FR-TEM-03）。リーダーが作成・変更・削除する。 */
function Tags({ team }: { team: TeamDetail }) {
  const m = useTeamMutations();
  const toast = useToast();
  const [name, setName] = useState('');
  const [color, setColor] = useState<TagColor>('blue');
  const [editing, setEditing] = useState<Tag | null>(null);
  const manage = team.can.manageTags && !team.archived;

  return (
    <section className="card stack">
      <h2>タグ</h2>
      {team.tags.length === 0 && <p className="muted">タグはありません。</p>}
      <ul className="stack">
        {team.tags.map((tag) => (
          <li key={tag.id} className="row">
            <TagChip tag={tag} />
            {manage && (
              <>
                <Button size="small" variant="ghost" onClick={() => setEditing(tag)}>
                  変更
                </Button>
                <Button
                  size="small"
                  variant="ghost"
                  onClick={() => m.deleteTag.mutate(tag.id, { onSuccess: () => toast.show('success', 'タグを削除しました。'), onError: (e) => toast.show('error', describeError(e)) })}
                >
                  削除
                </Button>
              </>
            )}
          </li>
        ))}
      </ul>
      {manage && (
        <form
          className="row"
          onSubmit={(e) => {
            e.preventDefault();
            m.createTag.mutate(
              { teamId: team.id, name: name.trim(), color },
              { onSuccess: () => (setName(''), toast.show('success', 'タグを作りました。')), onError: (err) => toast.show('error', describeError(err)) },
            );
          }}
        >
          <input type="text" aria-label="新しいタグの名前" placeholder="タグの名前（30 字以内）" value={name} maxLength={30} onChange={(e) => setName(e.target.value)} />
          <ColorSelect value={color} onChange={setColor} />
          <Button type="submit" disabled={!name.trim()}>
            タグを作る
          </Button>
        </form>
      )}
      {editing && (
        <Dialog
          open
          title="タグの変更"
          onClose={() => setEditing(null)}
          footer={
            <>
              <Button onClick={() => setEditing(null)}>取り消す</Button>
              <Button
                variant="primary"
                onClick={() =>
                  m.updateTag.mutate(
                    { id: editing.id, name: editing.name, color: editing.color },
                    { onSuccess: () => (setEditing(null), toast.show('success', '保存しました。')), onError: (e) => toast.show('error', describeError(e)) },
                  )
                }
              >
                保存する
              </Button>
            </>
          }
        >
          <div className="stack">
            <Field label="名前" required htmlFor="tag-name">
              <input id="tag-name" type="text" value={editing.name} maxLength={30} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
            </Field>
            <Field label="色" htmlFor="tag-color">
              <ColorSelect id="tag-color" value={editing.color} onChange={(c) => setEditing({ ...editing, color: c })} />
            </Field>
          </div>
        </Dialog>
      )}
    </section>
  );
}

function ColorSelect({ id, value, onChange }: { id?: string; value: TagColor; onChange: (c: TagColor) => void }) {
  return (
    <select id={id} aria-label="タグの色" value={value} onChange={(e) => onChange(e.target.value as TagColor)}>
      {TAG_COLORS.map((c) => (
        <option key={c} value={c}>
          {TAG_COLOR_LABEL[c]}
        </option>
      ))}
    </select>
  );
}

/** 削除したタスク（30 日以内）の復元（FR-TSK-08）。 */
function DeletedTasks({ teamId }: { teamId: string }) {
  const [open, setOpen] = useState(false);
  const { data } = useDeletedTasks(teamId, open);
  const restore = useRestoreTask();
  const toast = useToast();
  return (
    <section className="card stack">
      <h2>削除したタスク</h2>
      {!open ? (
        <div>
          <Button onClick={() => setOpen(true)}>30 日以内に削除したタスクを表示する</Button>
        </div>
      ) : (
        <>
          {data?.length === 0 && <p className="muted">ありません。</p>}
          <ul className="stack">
            {data?.map((t) => (
              <li key={t.id} className="row">
                <span>
                  {t.title}
                  {t.descendantCount > 0 && <span className="muted">（子タスク {t.descendantCount} 件を含む）</span>}
                </span>
                <span className="muted">
                  {formatDateTime(t.deletedAt)} {t.deletedByName}
                </span>
                <Button
                  size="small"
                  disabled={!t.canRestore}
                  title={t.canRestore ? undefined : '親タスクが削除されているため、先に親タスクを復元してください。'}
                  onClick={() => restore.mutate(t.id, { onSuccess: () => toast.show('success', '復元しました。'), onError: (e) => toast.show('error', describeError(e)) })}
                >
                  復元する
                </Button>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
