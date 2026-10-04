import { useState } from 'react';
import { useMe, useViewMutations, useViews } from '../../api/hooks';
import { Button, ConfirmDialog, Dialog, Field, describeError, fieldErrors, useToast } from '../../components/ui';
import { fromViewConditions, toViewConditions, type GanttConditions } from './model/conditions';

/**
 * ビュー（FR-GNT-26、27）。条件に名前を付けて保存し、次から選ぶだけで開ける。リーダーはチームの共有ビューを作れる。
 */
export function ViewMenu({ conditions, onApply }: { conditions: GanttConditions; onApply: (c: GanttConditions) => void }) {
  const { data: me } = useMe();
  const { data: views } = useViews();
  const { create, update, remove, setDefault } = useViewMutations();
  const toast = useToast();
  const [selectedId, setSelectedId] = useState('');
  const [saving, setSaving] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [name, setName] = useState('');
  const [shared, setShared] = useState(false);
  const [sharedTeamId, setSharedTeamId] = useState('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const selected = views?.find((v) => v.id === selectedId);
  const leaderTeams = (me?.teams ?? []).filter((t) => t.role === 'leader' && !t.archived);

  const apply = (id: string) => {
    setSelectedId(id);
    const view = views?.find((v) => v.id === id);
    if (view) onApply(fromViewConditions(view.conditions));
  };

  const save = async () => {
    try {
      const view = await create.mutateAsync({
        name,
        conditions: toViewConditions(conditions),
        isShared: shared,
        teamId: shared ? sharedTeamId || leaderTeams[0]?.id || null : null,
      });
      setSelectedId(view.id);
      setSaving(false);
      toast.show('success', `ビュー「${view.name}」を保存しました。`);
    } catch (error) {
      setErrors(fieldErrors(error));
      toast.show('error', describeError(error));
    }
  };

  return (
    <>
      <label className="muted" htmlFor="gantt-view">
        ビュー
      </label>
      <select id="gantt-view" value={selectedId} onChange={(e) => apply(e.target.value)}>
        <option value="">（選ぶ）</option>
        {views?.map((v) => (
          <option key={v.id} value={v.id}>
            {v.isShared ? `［共有: ${v.teamName ?? ''}］` : ''}
            {v.name}
            {v.isDefault ? '（最初に開く）' : ''}
          </option>
        ))}
      </select>
      <Button
        size="small"
        onClick={() => {
          setName('');
          setShared(false);
          setErrors({});
          setSaving(true);
        }}
      >
        保存
      </Button>
      {selected?.canEdit && (
        <>
          <Button
            size="small"
            onClick={() =>
              update.mutate(
                { id: selected.id, version: selected.version, conditions: toViewConditions(conditions) },
                { onSuccess: () => toast.show('success', '今の条件で上書きしました。'), onError: (e) => toast.show('error', describeError(e)) },
              )
            }
          >
            上書き
          </Button>
          <Button size="small" variant="ghost" onClick={() => setDeleting(true)}>
            削除
          </Button>
        </>
      )}
      {selected && (
        <Button
          size="small"
          variant="ghost"
          onClick={() =>
            setDefault.mutate(selected.isDefault ? null : selected.id, {
              onSuccess: () => toast.show('success', selected.isDefault ? '最初に開くビューを解除しました。' : '最初に開くビューにしました。'),
            })
          }
        >
          {selected.isDefault ? '最初に開くのをやめる' : '最初に開く'}
        </Button>
      )}

      <Dialog
        open={saving}
        title="ビューの保存"
        onClose={() => setSaving(false)}
        footer={
          <>
            <Button onClick={() => setSaving(false)}>取り消す</Button>
            <Button variant="primary" disabled={!name.trim() || create.isPending} onClick={() => void save()}>
              保存する
            </Button>
          </>
        }
      >
        <div className="stack">
          <Field label="名前" required htmlFor="view-name" errors={errors.name}>
            <input id="view-name" type="text" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} />
          </Field>
          <p className="muted">いまの絞り込み・まとめ方・表示期間・目盛り・列を保存します。</p>
          {leaderTeams.length > 0 && (
            <>
              <label className="row">
                <input type="checkbox" checked={shared} onChange={(e) => setShared(e.target.checked)} />
                チームの全員が使える共有ビューにする
              </label>
              {shared && (
                <Field label="共有するチーム" htmlFor="view-team">
                  <select id="view-team" value={sharedTeamId || leaderTeams[0]?.id} onChange={(e) => setSharedTeamId(e.target.value)}>
                    {leaderTeams.map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.name}
                      </option>
                    ))}
                  </select>
                </Field>
              )}
            </>
          )}
        </div>
      </Dialog>
      <ConfirmDialog
        open={deleting}
        title="ビューの削除"
        danger
        confirmLabel="削除する"
        message={<p>ビュー「{selected?.name}」を削除します。</p>}
        onCancel={() => setDeleting(false)}
        onConfirm={() =>
          selected &&
          remove.mutate(selected.id, {
            onSuccess: () => {
              setDeleting(false);
              setSelectedId('');
              toast.show('success', '削除しました。');
            },
            onError: (e) => toast.show('error', describeError(e)),
          })
        }
      />
    </>
  );
}
