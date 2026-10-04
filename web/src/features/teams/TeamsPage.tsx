import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useMe, useTeamMutations, useTeams } from '../../api/hooks';
import { Button, Dialog, ErrorBox, Field, Loading, describeError, fieldErrors, useToast } from '../../components/ui';
import { ROLE } from '../../lib/labels';
import { messageText } from '../../lib/messages';

/** チーム（SC-12。FR-TEM-04、05）。所属チームの一覧。リーダーは新しいチーム（プロジェクト）を作れる。 */
export function TeamsPage() {
  const { data: me } = useMe();
  const [includeArchived, setIncludeArchived] = useState(false);
  const { data, error, isLoading } = useTeams(includeArchived);
  const [creating, setCreating] = useState(false);
  const leaderAnywhere = me?.teams.some((t) => t.role === 'leader') ?? false;

  return (
    <div className="page">
      <div className="page-header">
        <h1>チーム</h1>
        <label className="row">
          <input type="checkbox" checked={includeArchived} onChange={(e) => setIncludeArchived(e.target.checked)} />
          アーカイブしたチームも表示する
        </label>
        {leaderAnywhere && (
          <Button variant="primary" onClick={() => setCreating(true)}>
            新しいチーム（プロジェクト）を作る
          </Button>
        )}
        {!leaderAnywhere && me?.isAdmin && <Link to="/admin/teams">チーム管理でチームを作る</Link>}
      </div>
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && data.length === 0 && <p className="empty">所属しているチームはありません。</p>}
      {data && data.length > 0 && (
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">チーム（プロジェクト）</th>
              <th scope="col">自分の役割</th>
              <th scope="col" className="num">
                メンバー
              </th>
              <th scope="col">説明</th>
            </tr>
          </thead>
          <tbody>
            {data.map((t) => (
              <tr key={t.id}>
                <td>
                  <Link to={`/teams/${t.id}`}>{t.name}</Link>
                  {t.archived && <span className="muted">（アーカイブ）</span>}
                </td>
                <td>{ROLE[t.role]}</td>
                <td className="num">{t.memberCount}人</td>
                <td className="prewrap">{t.description}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {creating && <CreateTeamDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

/** チームの作成。リーダーが作る場合は、作った人がリーダーになる（leaderUserId は空）。 */
export function CreateTeamDialog({ onClose }: { onClose: () => void }) {
  const { create } = useTeamMutations();
  const toast = useToast();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const submit = async () => {
    if (!name.trim()) {
      setErrors({ name: [messageText('MSG-TEM-001')] });
      return;
    }
    try {
      const team = await create.mutateAsync({ name: name.trim(), description: description.trim() || null, leaderUserId: null });
      toast.show('success', `チーム「${team.name}」を作りました。`);
      onClose();
      navigate(`/teams/${team.id}`);
    } catch (error) {
      setErrors(fieldErrors(error));
      toast.show('error', describeError(error));
    }
  };

  return (
    <Dialog
      open
      title="新しいチーム（プロジェクト）"
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
        <Field label="チーム名（プロジェクト名）" required htmlFor="team-name" errors={errors.name}>
          <input id="team-name" type="text" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="説明" htmlFor="team-desc" errors={errors.description} hint="500 字以内">
          <textarea id="team-desc" value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <p className="muted">作った人が、このチームのリーダーになります。</p>
      </div>
    </Dialog>
  );
}
