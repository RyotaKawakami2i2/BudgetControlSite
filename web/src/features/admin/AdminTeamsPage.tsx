import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useAdminTeams, useTeamMutations } from '../../api/hooks';
import type { UserSearchResult } from '../../api/types';
import { Icon } from '../../components/Icon';
import { Button, Dialog, EmptyState, ErrorBox, Field, Loading, PageHeader, Pill, describeError, fieldErrors, useToast } from '../../components/ui';
import { formatDateTime } from '../../lib/dates';
import { messageText } from '../../lib/messages';
import { UserPicker } from '../teams/UserPicker';

/** チーム管理（SC-16。FR-ADM-04、05）。全チームの一覧と、リーダーを指定したチームの作成。 */
export function AdminTeamsPage() {
  const { data, error, isLoading } = useAdminTeams();
  const [creating, setCreating] = useState(false);

  return (
    <div className="page">
      <PageHeader
        icon="team"
        title="チーム管理"
        description="すべてのチームの一覧です。管理者は、リーダーを指定してチームを作れます。所属していないチームは閲覧だけができます。"
        actions={
          <Button variant="primary" onClick={() => setCreating(true)}>
            <Icon name="plus" size={16} />
            チーム（プロジェクト）を作る
          </Button>
        }
      />
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && data.length === 0 && <EmptyState icon="team" title="チームはまだありません" description="「チーム（プロジェクト）を作る」から、リーダーを指定して作ってください。" />}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th scope="col">チーム</th>
                <th scope="col">リーダー</th>
                <th scope="col" className="num">
                  メンバー
                </th>
                <th scope="col">作成日時</th>
              </tr>
            </thead>
            <tbody>
              {data.map((t) => (
                <tr key={t.id}>
                  <td>
                    <span className="row">
                      <Link to={`/teams/${t.id}`}>
                        <strong>{t.name}</strong>
                      </Link>
                      {t.archived && (
                        <Pill icon="archive" tone="neutral">
                          アーカイブ
                        </Pill>
                      )}
                    </span>
                    {t.description && <div className="muted small">{t.description}</div>}
                  </td>
                  <td>{t.leaders.length > 0 ? t.leaders.join('、') : <Pill tone="danger">！ リーダーがいません</Pill>}</td>
                  <td className="num">{t.memberCount}人</td>
                  <td className="nowrap muted">{formatDateTime(t.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {creating && <CreateWithLeaderDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

function CreateWithLeaderDialog({ onClose }: { onClose: () => void }) {
  const { create } = useTeamMutations();
  const toast = useToast();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [leader, setLeader] = useState<UserSearchResult | null>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const submit = async () => {
    const local: Record<string, string[]> = {};
    if (!name.trim()) local.name = [messageText('MSG-TEM-001')];
    if (!leader) local.leaderUserId = ['リーダーを選んでください。'];
    setErrors(local);
    if (Object.keys(local).length > 0 || !leader) return;
    try {
      const team = await create.mutateAsync({ name: name.trim(), description: description.trim() || null, leaderUserId: leader.id });
      toast.show('success', `チーム「${team.name}」を作りました。`);
      onClose();
      navigate(`/teams/${team.id}`);
    } catch (e) {
      setErrors(fieldErrors(e));
      toast.show('error', describeError(e));
    }
  };

  return (
    <Dialog
      open
      title="チーム（プロジェクト）を作る"
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>取り消す</Button>
          <Button variant="primary" disabled={create.isPending} onClick={() => void submit()}>
            作る
          </Button>
        </>
      }
    >
      <div className="stack">
        <Field label="チーム名（プロジェクト名）" required htmlFor="admin-team-name" errors={errors.name}>
          <input id="admin-team-name" type="text" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="説明" htmlFor="admin-team-desc" errors={errors.description}>
          <textarea id="admin-team-desc" value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <Field label="リーダー" required errors={errors.leaderUserId}>
          {leader ? (
            <div className="row">
              <span>
                {leader.displayName} <span className="muted">{leader.email}</span>
              </span>
              <Button size="small" variant="ghost" onClick={() => setLeader(null)}>
                選び直す
              </Button>
            </div>
          ) : (
            <UserPicker label="リーダーを検索" onPick={setLeader} />
          )}
        </Field>
      </div>
    </Dialog>
  );
}
