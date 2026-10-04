import { useState } from 'react';
import { useUserSearch } from '../../api/hooks';
import type { UserSearchResult } from '../../api/types';
import { Button } from '../../components/ui';

/** 有効な利用者の検索（2 文字以上。表示名かメールアドレス）。 */
export function UserPicker({ label, onPick, exclude = [] }: { label: string; onPick: (user: UserSearchResult) => void; exclude?: string[] }) {
  const [q, setQ] = useState('');
  const { data, isFetching } = useUserSearch(q);
  const results = (data ?? []).filter((u) => !exclude.includes(u.id));
  return (
    <div className="stack">
      <input type="search" aria-label={label} placeholder="名前かメールアドレスの一部（2 文字以上）" value={q} maxLength={100} onChange={(e) => setQ(e.target.value)} />
      {q.trim().length >= 2 && !isFetching && results.length === 0 && <p className="muted">見つかりません。</p>}
      {results.length > 0 && (
        <ul className="stack">
          {results.map((u) => (
            <li key={u.id} className="row">
              <span>
                {u.displayName} <span className="muted">{u.email}</span>
              </span>
              <Button size="small" onClick={() => onPick(u)}>
                選ぶ
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
