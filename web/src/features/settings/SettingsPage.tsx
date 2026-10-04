import { useState } from 'react';
import { useMe, useUpdateMe, useViewMutations, useViews } from '../../api/hooks';
import type { Me } from '../../api/types';
import { Button, Field, Loading, describeError, fieldErrors, useToast } from '../../components/ui';

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
    <div className="page stack">
      <h1>設定</h1>
      <section className="card stack">
        <h2>表示名</h2>
        <form
          className="row"
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
          <Button type="submit" variant="primary" disabled={update.isPending}>
            保存する
          </Button>
        </form>
      </section>

      <section className="card stack">
        <h2>最初に開くビュー</h2>
        <p className="muted">ガントを条件の指定なしで開いたときに、このビューの条件で表示します。</p>
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
      </section>

      <section className="card stack">
        <h2>アカウントのセキュリティ</h2>
        <p>パスワード、多要素認証（認証アプリ・パスキー）、ログイン中の端末、ログイン履歴は、アカウント設定の画面で確認・変更します。</p>
        <p>
          多要素認証: {me.mfaEnabled ? `✓ 設定済み（認証アプリ ${me.hasAuthenticator ? 'あり' : 'なし'}、パスキー ${me.passkeyCount} 個）` : '！ 未設定'}
        </p>
        <div>
          <a href="/account/manage">アカウント設定を開く</a>
        </div>
      </section>
    </div>
  );
}
