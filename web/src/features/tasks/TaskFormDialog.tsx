import { useMemo, useState, type FormEvent } from 'react';
import { useCreateTask, useMe, useMoveTask, useTeam, useTeamTasks, useUpdateTask } from '../../api/hooks';
import type { GanttTask, Priority, TaskDetail, TaskOption, UpdateTaskInput } from '../../api/types';
import { EffortInput, effortText } from '../../components/EffortInput';
import { Button, Dialog, Field, MultiSelect, describeError, fieldErrors, ui, useToast } from '../../components/ui';
import { parseEffort } from '../../lib/effort';
import { PRIORITY, PRIORITY_ORDER } from '../../lib/labels';
import { messageText } from '../../lib/messages';
import styles from './tasks.module.css';

export type TaskFormTarget =
  | { mode: 'create'; teamId: string | null; parentId?: string | null; plannedStart?: string | null }
  | { mode: 'edit'; detail: TaskDetail };

/**
 * タスクの登録・編集（SC-07。詳細設計書 7.5）。メンバーが登録するときは、担当者は自分に固定する。
 * 編集では、変えた項目だけを送る（送った項目だけがサーバーで変わる）。
 */
export function TaskFormDialog({ target, onClose, onSaved }: { target: TaskFormTarget; onClose: () => void; onSaved?: (task: GanttTask) => void }) {
  const { data: me } = useMe();
  const toast = useToast();
  const create = useCreateTask();
  const update = useUpdateTask();
  const move = useMoveTask();
  const editing = target.mode === 'edit' ? target.detail : null;
  const task = editing?.task;

  const writableTeams = (me?.teams ?? []).filter((t) => !t.archived);
  const [teamId, setTeamId] = useState<string>(
    task?.teamId ?? (target.mode === 'create' ? target.teamId : null) ?? writableTeams[0]?.id ?? '',
  );
  const { data: team } = useTeam(teamId || null);
  const { data: options } = useTeamTasks(teamId || null);

  const [title, setTitle] = useState(task?.title ?? '');
  const [description, setDescription] = useState(editing?.description ?? '');
  const [parentId, setParentId] = useState<string>(task?.parentId ?? (target.mode === 'create' ? target.parentId ?? '' : ''));
  const [assigneeId, setAssigneeId] = useState<string>(task ? task.assigneeId ?? '' : '');
  const [plannedStart, setPlannedStart] = useState(task?.plannedStart ?? (target.mode === 'create' ? target.plannedStart ?? '' : ''));
  const [plannedEnd, setPlannedEnd] = useState(task?.plannedEnd ?? (target.mode === 'create' ? target.plannedStart ?? '' : ''));
  const [minutesText, setMinutesText] = useState(effortText(task?.isSummary ? null : task?.plannedMinutes));
  const [priority, setPriority] = useState<Priority>(task?.priority ?? 'medium');
  const [tagIds, setTagIds] = useState<string[]>(task?.tagIds ?? []);
  const [isMilestone, setIsMilestone] = useState(task?.isMilestone ?? false);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const isLeader = team?.role === 'leader';
  const canPlan = !editing || editing.can.editPlan;
  const canAssign = editing ? editing.can.assign : isLeader;
  const isSummary = !!task?.isSummary;

  // 親タスクの選択肢: 同じチームの、マイルストーンでない、深さ 4 未満のタスク。編集では自分と子孫を除く
  const parentOptions = useMemo(() => excludeSubtree(options ?? [], task?.id).filter((o) => !o.isMilestone && o.depth < 4), [options, task?.id]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const local: Record<string, string[]> = {};
    let plannedMinutes: number | null = null;
    if (!isMilestone && minutesText.trim() !== '' && !isSummary) {
      const parsed = parseEffort(minutesText);
      if (!parsed.ok) local.plannedMinutes = [messageText(parsed.reason === 'format' ? 'MSG-WL-007' : 'MSG-TSK-004')];
      else plannedMinutes = parsed.minutes;
    }
    if (!title.trim()) local.title = [messageText('MSG-TSK-001')];
    const start = plannedStart || null;
    const end = isMilestone ? start : plannedEnd || null;
    if (!isSummary && !!start !== !!end) local.plannedEnd = [messageText('MSG-TSK-003')];
    if (start && end && end < start) local.plannedEnd = [messageText('MSG-TSK-002')];
    setErrors(local);
    if (Object.keys(local).length > 0) return;

    try {
      let saved: GanttTask;
      if (!editing) {
        saved = await create.mutateAsync({
          teamId,
          parentId: parentId || null,
          title: title.trim(),
          description: description.trim() || null,
          assigneeId: isLeader ? assigneeId || null : me?.id ?? null,
          plannedStart: start,
          plannedEnd: end,
          plannedMinutes,
          priority,
          tagIds,
          isMilestone,
        });
      } else {
        const t = editing.task;
        const input: UpdateTaskInput = { version: t.version };
        if (canPlan) {
          if (title.trim() !== t.title) input.title = title.trim();
          if ((description.trim() || null) !== (editing.description ?? null)) input.description = description.trim() || null;
          if (priority !== t.priority) input.priority = priority;
          if (tagIds.slice().sort().join() !== t.tagIds.slice().sort().join()) input.tagIds = tagIds;
          if (!isSummary) {
            if (isMilestone !== t.isMilestone) input.isMilestone = isMilestone;
            if (start !== t.plannedStart) input.plannedStart = start;
            if (end !== t.plannedEnd) input.plannedEnd = end;
            if ((isMilestone ? null : plannedMinutes) !== t.plannedMinutes) input.plannedMinutes = isMilestone ? null : plannedMinutes;
          }
        }
        if (canAssign && (assigneeId || null) !== t.assigneeId) input.assigneeId = assigneeId || null;
        saved = Object.keys(input).length > 1 ? await update.mutateAsync({ id: t.id, input }) : t;
        if ((parentId || null) !== t.parentId && editing.can.move) {
          // 親を変えたら、移動先の最後に置く
          const siblings = (options ?? []).filter((o) => o.parentId === (parentId || null) && o.id !== t.id);
          saved = await move.mutateAsync({ id: t.id, version: saved.version, parentId: parentId || null, afterTaskId: siblings[siblings.length - 1]?.id ?? null });
        }
      }
      toast.show('success', messageText('MSG-CMN-001'));
      onSaved?.(saved);
      onClose();
    } catch (error) {
      setErrors(fieldErrors(error));
      toast.show('error', describeError(error));
    }
  };

  const busy = create.isPending || update.isPending || move.isPending;
  return (
    <Dialog
      open
      title={editing ? 'タスクの編集' : 'タスクの登録'}
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>取り消す</Button>
          <Button variant="primary" type="submit" form="task-form" disabled={busy}>
            保存する
          </Button>
        </>
      }
    >
      <form id="task-form" className={styles.form} onSubmit={(e) => void submit(e)} noValidate>
        {!editing && (
          <Field label="チーム" required htmlFor="tf-team">
            <select id="tf-team" value={teamId} onChange={(e) => setTeamId(e.target.value)}>
              {writableTeams.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
          </Field>
        )}
        <Field label="タイトル" required htmlFor="tf-title" errors={errors.title}>
          <input id="tf-title" type="text" value={title} maxLength={200} disabled={!canPlan} onChange={(e) => setTitle(e.target.value)} />
        </Field>
        <Field label="説明" htmlFor="tf-desc" errors={errors.description} hint="プレーンテキスト（URL は自動でリンクになります）。4,000 字以内">
          <textarea id="tf-desc" value={description} maxLength={4000} disabled={!canPlan} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <Field label="親タスク" htmlFor="tf-parent" errors={errors.parentId}>
          <select id="tf-parent" value={parentId} disabled={!!editing && !editing.can.move} onChange={(e) => setParentId(e.target.value)}>
            <option value="">（最上位）</option>
            {parentOptions.map((o) => (
              <option key={o.id} value={o.id}>
                {'　'.repeat(o.depth - 1)}
                {o.title}
              </option>
            ))}
          </select>
        </Field>
        <Field label="担当者" htmlFor="tf-assignee" errors={errors.assigneeId}>
          {canAssign ? (
            <select id="tf-assignee" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)}>
              <option value="">未割り当て</option>
              {(team?.members ?? []).map((m) => (
                <option key={m.userId} value={m.userId}>
                  {m.displayName}
                </option>
              ))}
            </select>
          ) : (
            <input id="tf-assignee" type="text" disabled value={editing ? editing.assigneeName ?? '未割り当て' : me?.displayName ?? ''} />
          )}
        </Field>
        <label className="row">
          <input type="checkbox" checked={isMilestone} disabled={!canPlan || isSummary} onChange={(e) => setIsMilestone(e.target.checked)} />
          マイルストーン（期間のない節目。終了日は開始日と同じになり、工数は入力できません）
        </label>
        {isSummary ? (
          <p className="muted">{messageText('MSG-TSK-008')}</p>
        ) : (
          <div className={ui.grid2}>
            <Field label={isMilestone ? '日付' : '予定開始日'} htmlFor="tf-start" errors={errors.plannedStart}>
              <input id="tf-start" type="date" value={plannedStart} disabled={!canPlan} onChange={(e) => setPlannedStart(e.target.value)} />
            </Field>
            {!isMilestone && (
              <Field label="予定終了日" htmlFor="tf-end" errors={errors.plannedEnd}>
                <input id="tf-end" type="date" value={plannedEnd} min={plannedStart || undefined} disabled={!canPlan} onChange={(e) => setPlannedEnd(e.target.value)} />
              </Field>
            )}
            {!isMilestone && (
              <Field label="予定工数（時間）" htmlFor="tf-minutes" errors={errors.plannedMinutes} hint="「1.5」または「1:30」。15 分単位">
                <EffortInput id="tf-minutes" value={minutesText} onChange={setMinutesText} disabled={!canPlan} invalid={!!errors.plannedMinutes} />
              </Field>
            )}
          </div>
        )}
        <div className={ui.grid2}>
          <Field label="優先度" required htmlFor="tf-priority">
            <select id="tf-priority" value={priority} disabled={!canPlan} onChange={(e) => setPriority(e.target.value as Priority)}>
              {PRIORITY_ORDER.map((p) => (
                <option key={p} value={p}>
                  {PRIORITY[p].label}
                </option>
              ))}
            </select>
          </Field>
          <Field label="タグ" errors={errors.tagIds}>
            <MultiSelect label="タグを選ぶ" options={(team?.tags ?? []).map((t) => ({ value: t.id, label: t.name }))} selected={tagIds} onChange={canPlan ? setTagIds : () => undefined} />
          </Field>
        </div>
      </form>
    </Dialog>
  );
}

function excludeSubtree(options: TaskOption[], rootId: string | undefined): TaskOption[] {
  if (!rootId) return options;
  const excluded = new Set([rootId]);
  for (const o of options) {
    if (o.parentId && excluded.has(o.parentId)) excluded.add(o.id);
  }
  return options.filter((o) => !excluded.has(o.id));
}
