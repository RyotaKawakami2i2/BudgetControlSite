import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useMe, useTeamMutations, useTeams } from '../../api/hooks';
import { Icon } from '../../components/Icon';
import { Button, Dialog, EmptyState, ErrorBox, Field, Loading, PageHeader, Pill, describeError, fieldErrors, ui, useToast } from '../../components/ui';
import { ROLE } from '../../lib/labels';
import { messageText } from '../../lib/messages';
import styles from './teams.module.css';

/** チーム（SC-12。FR-TEM-04、05）。所属チームの一覧。リーダーは新しいチーム（プロジェクト）を作れる。 */
export function TeamsPage() {
  const { data: me } = useMe();
  const [includeArchived, setIncludeArchived] = useState(false);
  const { data, error, isLoading } = useTeams(includeArchived);
  const [creating, setCreating] = useState(false);
  const leaderAnywhere = me?.teams.some((t) => t.role === 'leader') ?? false;

  return (
    <div className="page">
      <PageHeader
        icon="team"
        title="チーム"
        description="所属しているチーム（プロジェクト）です。チームを開くと、メンバー・役割・タグを確かめられます。"
        actions={
          <>
            <label className="check-label">
              <input type="checkbox" checked={includeArchived} onChange={(e) => setIncludeArchived(e.target.checked)} />
              アーカイブしたチームも表示する
            </label>
            {leaderAnywhere && (
              <Button variant="primary" onClick={() => setCreating(true)}>
                <Icon name="plus" size={16} />
                新しいチーム（プロジェクト）を作る
              </Button>
            )}
            {!leaderAnywhere && me?.isAdmin && (
              <Link to="/admin/teams" className={ui.button}>
                チーム管理でチームを作る
              </Link>
            )}
          </>
        }
      />
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && data.length === 0 && (
        <EmptyState icon="team" title="所属しているチームはありません" description="チームのリーダーか管理者に、チームへの追加を依頼してください。" />
      )}
      {data && data.length > 0 && (
        <div className={styles.cards}>
          {data.map((t) => (
            <article key={t.id} className={[styles.teamCard, t.archived && styles.archived].filter(Boolean).join(' ')}>
              <header className={styles.teamCardHeader}>
                <Link to={`/teams/${t.id}`} className={styles.teamCardName}>
                  {t.name}
                </Link>
                <Pill tone={t.role === 'leader' ? 'primary' : 'neutral'}>{ROLE[t.role]}</Pill>
                {t.archived && (
                  <Pill icon="archive" tone="neutral">
                    アーカイブ
                  </Pill>
                )}
              </header>
              <p className={styles.teamCardDescription}>{t.description || <span className="muted">（説明はありません）</span>}</p>
              <footer className={styles.teamCardFooter}>
                <span className="muted">
                  <Icon name="user" size={14} /> メンバー {t.memberCount} 人
                </span>
                <span className="spacer" />
                <Link to={`/gantt?teams=${t.id}`}>ガント</Link>
                <Link to={`/teams/${t.id}`}>開く</Link>
              </footer>
            </article>
          ))}
        </div>
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
      description="作った人が、このチームのリーダーになります。メンバーは作ったあとに追加します。"
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
      </div>
    </Dialog>
  );
}
