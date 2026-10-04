import { useState } from 'react';
import { useMe, useUpdateMe, useViewMutations, useViews } from '../../api/hooks';
import type { Me } from '../../api/types';
import { Icon } from '../../components/Icon';
import { Button, Card, Field, Loading, PageHeader, Pill, describeError, fieldErrors, ui, useToast } from '../../components/ui';
import styles from './settings.module.css';

/** 設定（SC-14 の業務の画面の部分）。表示名と、最初に開くビュー。パスワードや多要素認証はアカウントのセキュリティの画面で行う。 */
export function SettingsPage() {
  const { data: me } = useMe();
  if (!me) return <Loading />;
  return <Settings me={me} />;
}

function Settings({ me }: { me: Me }) {
  const update = useUpdateMe();
  const { data: views } = useViews();
  const { setDefault } = useViewMutations();
  const toast = useToast();
  const [name, setName] = useState(me.displayName);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  return (
    <div className="page">
      <PageHeader icon="gear" title="設定" description="画面に表示する名前と、ガントを最初に開いたときの表示を決めます。" />
      <div className={styles.grid}>
        <Card icon="user" title="表示名" description="チームのメンバー一覧や、タスクの担当者として表示される名前です。">
          <form
            className={styles.form}
            onSubmit={(e) => {
              e.preventDefault();
              update.mutate(name, {
                onSuccess: () => {
                  setErrors({});
                  toast.show('success', '保存しました。');
                },
                onError: (err) => {
                  setErrors(fieldErrors(err));
                  toast.show('error', describeError(err));
                },
              });
            }}
          >
            <Field label="表示名" htmlFor="display-name" errors={errors.displayName} hint="画面に表示する名前です（50 字以内）。">
              <input id="display-name" type="text" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} />
            </Field>
            <Button type="submit" variant="primary" disabled={update.isPending || name.trim() === me.displayName}>
              保存する
            </Button>
          </form>
        </Card>

        <Card icon="gantt" title="最初に開くビュー" description="ガントを条件の指定なしで開いたときに、このビューの条件で表示します。ビューはガントの画面で保存できます。">
          <select
            aria-label="最初に開くビュー"
            value={me.defaultViewId ?? ''}
            onChange={(e) => setDefault.mutate(e.target.value || null, { onSuccess: () => toast.show('success', '保存しました。') })}
          >
            <option value="">（使わない）</option>
            {views?.map((v) => (
              <option key={v.id} value={v.id}>
                {v.isShared ? `［共有: ${v.teamName ?? ''}］` : ''}
                {v.name}
              </option>
            ))}
          </select>
        </Card>

        <Card icon="shield" title="アカウントのセキュリティ" description="パスワード、多要素認証（認証アプリ・パスキー）、ログイン中の端末、ログイン履歴は、アカウント設定の画面で確認・変更します。">
          <p className="row">
            多要素認証
            {me.mfaEnabled || me.passkeyCount > 0 ? (
              <Pill tone="success" icon="check">
                設定済み（認証アプリ {me.hasAuthenticator ? 'あり' : 'なし'}、パスキー {me.passkeyCount} 個）
              </Pill>
            ) : (
              <Pill tone="warning" icon="warning">
                未設定。設定をおすすめします
              </Pill>
            )}
          </p>
          <a href="/account/manage" className={ui.button}>
            <Icon name="shield" size={16} />
            アカウント設定を開く
          </a>
        </Card>
      </div>
    </div>
  );
}
