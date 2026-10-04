import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useDeletedTasks, useMe, useRestoreTask, useTeam, useTeamMutations } from '../../api/hooks';
import type { Tag, TagColor, TeamDetail } from '../../api/types';
import { Icon } from '../../components/Icon';
import { Avatar, Button, Card, ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, Loading, PageHeader, Pill, TagChip, describeError, fieldErrors, ui, useToast } from '../../components/ui';
import { formatDateTime } from '../../lib/dates';
import { ROLE, TAG_COLORS, TAG_COLOR_LABEL } from '../../lib/labels';
import { UserPicker } from './UserPicker';
import { useSyncCurrentTeam } from '../../layout/context';
import styles from './teams.module.css';

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
      <PageHeader
        icon="team"
        title={team.name}
        description={team.description ?? 'チームの説明はありません。'}
        meta={
          <span className="row">
            あなたの役割 <Pill tone={team.role === 'leader' ? 'primary' : 'neutral'}>{ROLE[team.role]}</Pill>
            {team.archived && (
              <Pill icon="archive" tone="warning">
                アーカイブ済み（読み取り専用）
              </Pill>
            )}
          </span>
        }
        actions={
          <>
            <Link to={`/gantt?teams=${team.id}`} className={ui.button}>
              <Icon name="gantt" size={16} />
              ガントを開く
            </Link>
            {team.can.viewReport && (
              <Link to={`/teams/${team.id}/report`} className={ui.button}>
                <Icon name="report" size={16} />
                担当者別の予実
              </Link>
            )}
            {team.can.update && !team.archived && (
              <Button onClick={() => setEditing(true)}>
                <Icon name="edit" size={16} />
                名前・説明を変える
              </Button>
            )}
          </>
        }
      />

      <div className="stack-lg">
        <Card icon="user" title="メンバー" count={team.members.length} description={team.can.manageMembers ? 'リーダーは、メンバーの追加・役割の変更・チームから外すことができます。' : undefined}>
          <div className="table-wrap">
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
                      <span className={styles.person}>
                        <Avatar name={member.displayName} />
                        <span>
                          {member.displayName}
                          {member.userId === me?.id && <span className="muted">（自分）</span>}
                        </span>
                        {member.disabled && <Pill tone="neutral">無効</Pill>}
                      </span>
                    </td>
                    {team.members.some((x) => x.email) && <td>{member.email}</td>}
                    <td>
                      <Pill tone={member.role === 'leader' ? 'primary' : 'neutral'}>{ROLE[member.role]}</Pill>
                    </td>
                    <td className="nowrap muted">{formatDateTime(member.joinedAt)}</td>
                    {team.can.manageMembers && (
                      <td className="actions">
                        {!team.archived && (
                          <span className="row">
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
                            </Button>
                            <Button size="small" variant="dangerGhost" onClick={() => setRemoving({ userId: member.userId, name: member.displayName })}>
                              チームから外す
                            </Button>
                          </span>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {team.can.manageMembers && !team.archived && (
            <div className={styles.addMember}>
              <h3>メンバーを追加する</h3>
              <p className="muted small">名前かメールアドレスの一部で検索し、「選ぶ」を押すとすぐに追加します。</p>
              <label className={styles.inlineField}>
                追加するときの役割
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
        </Card>

        <Tags team={team} />
        {team.can.restore && <DeletedTasks teamId={team.id} />}

        {(team.can.archive || team.can.unarchive) && (
          <Card icon="warning" tone="warning" title="チームの設定（注意が必要な操作）">
            <div className={styles.dangerRow}>
              <div>
                <strong>{team.archived ? 'アーカイブを解除する' : 'チームをアーカイブする'}</strong>
                <p className="muted small">
                  {team.archived
                    ? '解除すると、タスクの登録や変更がまたできるようになります。'
                    : 'プロジェクトが終わったら、アーカイブして読み取り専用にします。タスクと作業実績は残り、あとで解除できます。'}
                </p>
              </div>
              <Button variant={team.archived ? 'default' : 'dangerGhost'} onClick={() => setArchiving(true)}>
                <Icon name="archive" size={16} />
                {team.archived ? 'アーカイブを解除する' : 'アーカイブする'}
              </Button>
            </div>
          </Card>
        )}
      </div>

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
      description="チームの一覧やガントに表示される名前と説明を変えます。"
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
    <Card icon="tag" title="タグ" count={team.tags.length} description="タスクの分類に使います。ガントとタスク一覧で、タグで絞り込めます。">
      {team.tags.length === 0 && <EmptyState compact icon="tag" title="タグはまだありません。" />}
      <ul className={styles.tags}>
        {team.tags.map((tag) => (
          <li key={tag.id} className={styles.tagItem}>
            <TagChip tag={tag} />
            {manage && (
              <>
                <Button size="small" variant="ghost" iconOnly aria-label={`タグ「${tag.name}」を変更`} title="変更" onClick={() => setEditing(tag)}>
                  <Icon name="edit" size={14} />
                </Button>
                <Button
                  size="small"
                  variant="dangerGhost"
                  iconOnly
                  aria-label={`タグ「${tag.name}」を削除`}
                  title="削除"
                  onClick={() => m.deleteTag.mutate(tag.id, { onSuccess: () => toast.show('success', 'タグを削除しました。'), onError: (e) => toast.show('error', describeError(e)) })}
                >
                  <Icon name="trash" size={14} />
                </Button>
              </>
            )}
          </li>
        ))}
      </ul>
      {manage && (
        <form
          className={styles.tagForm}
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
            <Icon name="plus" size={14} />
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
    </Card>
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
    <Card icon="trash" title="削除したタスク" description="削除から 30 日以内のタスクは、リーダーが元に戻せます。">
      {!open ? (
        <div>
          <Button onClick={() => setOpen(true)}>30 日以内に削除したタスクを表示する</Button>
        </div>
      ) : (
        <>
          {data?.length === 0 && <EmptyState compact icon="check" title="30 日以内に削除したタスクはありません。" />}
          <ul className={styles.deleted}>
            {data?.map((t) => (
              <li key={t.id}>
                <span className={styles.deletedTitle}>
                  {t.title}
                  {t.descendantCount > 0 && <span className="muted">（子タスク {t.descendantCount} 件を含む）</span>}
                </span>
                <span className="muted small">
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
    </Card>
  );
}
