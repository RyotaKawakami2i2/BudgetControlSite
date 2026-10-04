import { useState } from 'react';
import { useAdminUserMutations, useAdminUsers, useMe } from '../../api/hooks';
import type { AdminUser } from '../../api/types';
import { Icon } from '../../components/Icon';
import { Avatar, Button, ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, Loading, PageHeader, Pill, describeError, fieldErrors, useToast } from '../../components/ui';
import styles from './admin.module.css';
import { formatDateTime } from '../../lib/dates';
import { USER_STATUS } from '../../lib/labels';

type Action = { kind: 'disable' | 'enable' | 'revoke' | 'grant' | 'ungrant'; user: AdminUser } | { kind: 'reset-mfa'; user: AdminUser };

/**
 * 利用者管理（SC-15。FR-ADM-01〜03、FR-AUT-08）。招待、無効化、管理者権限、多要素認証のリセット。
 * 多要素認証の設定状況も確認できる（NF-AUT-11）。操作の前に再認証を求める（サーバーが判定する）。
 */
export function AdminUsersPage() {
  const { data: me } = useMe();
  const [q, setQ] = useState('');
  const [status, setStatus] = useState('');
  const { data, error, isLoading } = useAdminUsers(q, status);
  const m = useAdminUserMutations();
  const toast = useToast();
  const [inviting, setInviting] = useState(false);
  const [action, setAction] = useState<Action | null>(null);
  const [verification, setVerification] = useState('');

  const run = async (promise: Promise<unknown>, success: string) => {
    try {
      await promise;
      toast.show('success', success);
    } catch (e) {
      toast.show('error', describeError(e));
    } finally {
      setAction(null);
      setVerification('');
    }
  };

  const confirm = () => {
    if (!action) return;
    const u = action.user;
    switch (action.kind) {
      case 'disable':
        return run(m.disable.mutateAsync(u.id), `${u.displayName} さんを無効にしました。`);
      case 'enable':
        return run(m.enable.mutateAsync(u.id), `${u.displayName} さんを再び有効にしました。`);
      case 'revoke':
        return run(m.revoke.mutateAsync(u.id), '招待を取り消しました。');
      case 'grant':
        return run(m.setAdmin.mutateAsync({ id: u.id, isAdmin: true }), '管理者にしました。');
      case 'ungrant':
        return run(m.setAdmin.mutateAsync({ id: u.id, isAdmin: false }), '管理者権限を外しました。');
      case 'reset-mfa':
        return run(m.resetMfa.mutateAsync({ id: u.id, verification }), '多要素認証をリセットしました。');
    }
  };

  const ACTION_TEXT: Record<Action['kind'], { title: string; label: string; message: string; danger: boolean }> = {
    disable: { title: '無効化', label: '無効にする', message: 'ログイン中のセッションはすぐに失効し、所属チームから外れます（担当していた未完了のタスクは未割り当てに戻ります）。データは残ります。', danger: true },
    enable: { title: '再有効化', label: '有効にする', message: '再びログインできるようにします。チームの所属は戻りません。', danger: false },
    revoke: { title: '招待の取り消し', label: '取り消す', message: '招待のリンクを無効にし、利用者を無効にします。', danger: true },
    grant: { title: '管理者権限の付与', label: '管理者にする', message: '管理者は、利用者とチームの管理、監査ログの閲覧、すべてのチームのタスクの閲覧ができます。本人には、ログインし直してもらいます。', danger: false },
    ungrant: { title: '管理者権限の解除', label: '解除する', message: '管理者権限を外します。本人には、ログインし直してもらいます。最後の管理者は外せません。', danger: true },
    'reset-mfa': { title: '多要素認証のリセット', label: 'リセットする', message: '認証アプリの登録とリカバリーコードを無効にし、ログイン中のセッションを失効させます。本人確認の方法を記録してください。', danger: true },
  };

  return (
    <div className="page">
      <PageHeader
        icon="user"
        title="利用者管理"
        description="利用者の招待、無効化、管理者権限、多要素認証のリセットを行います。操作の前に、もう一度本人確認を求めることがあります。"
        actions={
          <Button variant="primary" onClick={() => setInviting(true)}>
            <Icon name="plus" size={16} />
            招待する
          </Button>
        }
      />
      <div className={styles.filters}>
        <span className={styles.search}>
          <Icon name="search" size={16} />
          <input type="search" aria-label="名前かメールアドレスで検索" placeholder="名前かメールアドレスで検索" value={q} onChange={(e) => setQ(e.target.value)} />
        </span>
        <select aria-label="状態" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">すべての状態</option>
          <option value="invited">招待中</option>
          <option value="active">有効</option>
          <option value="disabled">無効</option>
        </select>
      </div>
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && data.length === 0 && <EmptyState icon="user" title="条件に合う利用者はいません" />}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th scope="col">名前</th>
                <th scope="col">メールアドレス</th>
                <th scope="col">状態</th>
                <th scope="col">管理者</th>
                <th scope="col">多要素認証</th>
                <th scope="col" className="num">
                  チーム
                </th>
                <th scope="col">最終ログイン</th>
                <th scope="col">
                  <span className="visually-hidden">操作</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {data.map((u) => (
                <tr key={u.id}>
                  <td>
                    <span className={styles.person}>
                      <Avatar name={u.displayName} />
                      <span>
                        {u.displayName}
                        {u.id === me?.id && <span className="muted">（自分）</span>}
                      </span>
                    </span>
                  </td>
                  <td>{u.email}</td>
                  <td>
                    <span className="row">
                      <Pill tone={u.status === 'active' ? 'success' : u.status === 'invited' ? 'primary' : 'neutral'}>{USER_STATUS[u.status]}</Pill>
                      {u.locked && (
                        <Pill tone="danger" icon="lock">
                          ロック中
                        </Pill>
                      )}
                    </span>
                    {u.status === 'invited' && u.invitationExpiresAt && <div className="muted small">期限 {formatDateTime(u.invitationExpiresAt)}</div>}
                  </td>
                  <td>{u.isAdmin && <Pill tone="primary" icon="shield">管理者</Pill>}</td>
                  <td>
                    {u.mfaEnabled || u.passkeyCount > 0 ? (
                      <Pill tone="success" icon="check">
                        {[u.mfaEnabled && '認証アプリ', u.passkeyCount > 0 && `パスキー ${u.passkeyCount}`].filter(Boolean).join('、')}
                      </Pill>
                    ) : (
                      <Pill tone="warning">！ 未設定</Pill>
                    )}
                  </td>
                  <td className="num">{u.teamCount}</td>
                  <td className="nowrap muted">{formatDateTime(u.lastLoginAt) || '-'}</td>
                  <td className="actions">
                    <span className={styles.actions}>
                      {u.status === 'invited' && (
                        <>
                          <Button size="small" onClick={() => void run(m.resend.mutateAsync(u.id), '招待を再送しました。')}>
                            招待を再送
                          </Button>
                          <Button size="small" variant="dangerGhost" onClick={() => setAction({ kind: 'revoke', user: u })}>
                            招待を取り消す
                          </Button>
                        </>
                      )}
                      {u.status === 'active' && (
                        <Button size="small" variant="dangerGhost" onClick={() => setAction({ kind: 'disable', user: u })}>
                          無効にする
                        </Button>
                      )}
                      {u.status === 'disabled' && (
                        <Button size="small" onClick={() => setAction({ kind: 'enable', user: u })}>
                          有効にする
                        </Button>
                      )}
                      {u.status !== 'disabled' && (
                        <Button size="small" variant="ghost" onClick={() => setAction({ kind: u.isAdmin ? 'ungrant' : 'grant', user: u })}>
                          {u.isAdmin ? '管理者を外す' : '管理者にする'}
                        </Button>
                      )}
                      {u.mfaEnabled && (
                        <Button size="small" variant="ghost" onClick={() => setAction({ kind: 'reset-mfa', user: u })}>
                          多要素認証をリセット
                        </Button>
                      )}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {inviting && <InviteDialog onClose={() => setInviting(false)} />}
      {action && (
        <ConfirmDialog
          open
          title={ACTION_TEXT[action.kind].title}
          danger={ACTION_TEXT[action.kind].danger}
          confirmLabel={ACTION_TEXT[action.kind].label}
          busy={action.kind === 'reset-mfa' && !verification.trim()}
          message={
            <div className="stack">
              <p>
                {action.user.displayName}（{action.user.email}）: {ACTION_TEXT[action.kind].message}
              </p>
              {action.kind === 'reset-mfa' && (
                <Field label="本人確認の方法" required htmlFor="verification" hint="例: 対面で社員証を確認した（200 字以内）">
                  <input id="verification" type="text" value={verification} maxLength={200} onChange={(e) => setVerification(e.target.value)} />
                </Field>
              )}
            </div>
          }
          onCancel={() => setAction(null)}
          onConfirm={() => void confirm()}
        />
      )}
    </div>
  );
}

function InviteDialog({ onClose }: { onClose: () => void }) {
  const { invite } = useAdminUserMutations();
  const toast = useToast();
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  return (
    <Dialog
      open
      title="利用者の招待"
      description="招待のメールを送ります。本人はメールのリンクからパスワードを設定して使い始めます（リンクは 72 時間有効）。"
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>取り消す</Button>
          <Button
            variant="primary"
            disabled={invite.isPending}
            onClick={async () => {
              try {
                const user = await invite.mutateAsync({ email: email.trim(), displayName: displayName.trim(), isAdmin });
                toast.show('success', `${user.email} に招待のメールを送りました（72 時間有効）。`);
                onClose();
              } catch (e) {
                setErrors(fieldErrors(e));
                toast.show('error', describeError(e));
              }
            }}
          >
            招待する
          </Button>
        </>
      }
    >
      <div className="stack">
        <Field label="メールアドレス" required htmlFor="invite-email" errors={errors.email}>
          <input id="invite-email" type="email" value={email} maxLength={254} onChange={(e) => setEmail(e.target.value)} />
        </Field>
        <Field label="表示名" required htmlFor="invite-name" errors={errors.displayName}>
          <input id="invite-name" type="text" value={displayName} maxLength={50} onChange={(e) => setDisplayName(e.target.value)} />
        </Field>
        <label className="check-label">
          <input type="checkbox" checked={isAdmin} onChange={(e) => setIsAdmin(e.target.checked)} />
          管理者として招待する（利用者とチームの管理、監査ログの閲覧ができます）
        </label>
      </div>
    </Dialog>
  );
}
