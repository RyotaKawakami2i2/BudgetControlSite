import { useState, type FormEvent } from 'react';
import { useUpdateTask, useWorkLogMutations } from '../../api/hooks';
import type { WorkLog } from '../../api/types';
import { EffortInput, effortText } from '../../components/EffortInput';
import { Icon } from '../../components/Icon';
import { Button, Dialog, Field, describeError, fieldErrors, ui, useToast } from '../../components/ui';
import { todayIso } from '../../lib/dates';
import { parseEffort } from '../../lib/effort';
import { messageText } from '../../lib/messages';
import styles from './tasks.module.css';

const PROGRESS_STEPS = Array.from({ length: 21 }, (_, i) => i * 5);

/** 記録する対象のタスク。 */
export interface WorkTarget {
  id: string;
  title: string;
  version: number;
  progress: number;
  status: string;
}

/**
 * 作業実績の記録・修正（詳細設計書 7.5）。作業日（初期値は今日）、作業時間（15 分単位）、メモ、進捗率（変えた場合だけ送る）。
 */
export function WorkLogDialog({ task, log, onClose }: { task: WorkTarget; log?: WorkLog; onClose: () => void }) {
  const toast = useToast();
  const { create, update } = useWorkLogMutations();
  const [workDate, setWorkDate] = useState(log?.workDate ?? todayIso());
  const [minutesText, setMinutesText] = useState(effortText(log?.minutes));
  const [note, setNote] = useState(log?.note ?? '');
  const [progress, setProgress] = useState<string>('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const parsed = parseEffort(minutesText);
    if (!parsed.ok || parsed.minutes < 15 || parsed.minutes > 1440) {
      setErrors({ minutes: [messageText(!parsed.ok && parsed.reason === 'format' ? 'MSG-WL-007' : 'MSG-WL-001')] });
      return;
    }
    if (workDate > todayIso()) {
      setErrors({ workDate: [messageText('MSG-WL-003')] });
      return;
    }
    setErrors({});
    try {
      if (log) {
        await update.mutateAsync({ id: log.id, taskId: task.id, version: log.version, workDate, minutes: parsed.minutes, note: note.trim() || null });
      } else {
        await create.mutateAsync({
          taskId: task.id,
          workDate,
          minutes: parsed.minutes,
          note: note.trim() || null,
          progress: progress === '' ? null : Number(progress),
          taskVersion: progress === '' ? undefined : task.version,
        });
      }
      toast.show('success', messageText('MSG-CMN-001'));
      onClose();
    } catch (error) {
      const fields = fieldErrors(error);
      if (fields.minutes?.some((m) => m.includes('{日付}'))) {
        fields.minutes = [messageText('MSG-WL-002', { 日付: workDate.replaceAll('-', '/') })];
      }
      setErrors(fields);
      toast.show('error', describeError(error));
    }
  };

  return (
    <Dialog
      open
      title={log ? '作業実績の修正' : '作業実績の記録'}
      description={log ? '記録した日と時間を直します。' : '作業した日と時間を記録します。最初の記録で、状態は「進行中」になります。'}
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>取り消す</Button>
          <Button variant="primary" type="submit" form="worklog-form" disabled={create.isPending || update.isPending}>
            記録する
          </Button>
        </>
      }
    >
      <form id="worklog-form" className={styles.form} onSubmit={(e) => void submit(e)} noValidate>
        <p className={styles.taskBadge}>
          <Icon name="check" size={16} />
          {task.title}
        </p>
        <div className={ui.grid2}>
          <Field label="作業日" required htmlFor="wl-date" errors={errors.workDate}>
            <input id="wl-date" type="date" value={workDate} max={todayIso()} onChange={(e) => setWorkDate(e.target.value)} />
          </Field>
          <Field label="作業時間" required htmlFor="wl-minutes" errors={errors.minutes} hint="「1.5」または「1:30」。15 分単位">
            <EffortInput id="wl-minutes" value={minutesText} onChange={setMinutesText} invalid={!!errors.minutes} />
          </Field>
        </div>
        <Field label="メモ" htmlFor="wl-note" errors={errors.note}>
          <input id="wl-note" type="text" value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />
        </Field>
        {!log && task.status !== 'done' && (
          <Field label="進捗率" htmlFor="wl-progress" hint={`いまの進捗率: ${task.progress}%`}>
            <select id="wl-progress" value={progress} onChange={(e) => setProgress(e.target.value)}>
              <option value="">変えない</option>
              {PROGRESS_STEPS.map((p) => (
                <option key={p} value={p}>
                  {p}%
                </option>
              ))}
            </select>
          </Field>
        )}
      </form>
    </Dialog>
  );
}

/** 完了（詳細設計書 7.5）。実績終了日（初期値は今日）と結果コメント。進捗率は 100% になる。 */
export function CompleteDialog({ task, actualStart, onClose }: { task: WorkTarget; actualStart: string | null; onClose: () => void }) {
  const toast = useToast();
  const update = useUpdateTask();
  const [actualEnd, setActualEnd] = useState(todayIso());
  const [resultNote, setResultNote] = useState('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!actualEnd) {
      setErrors({ actualEnd: [messageText('MSG-TSK-010')] });
      return;
    }
    if (actualEnd > todayIso() || (actualStart && actualEnd < actualStart)) {
      setErrors({ actualEnd: [messageText('MSG-TSK-009')] });
      return;
    }
    try {
      await update.mutateAsync({
        id: task.id,
        input: { version: task.version, status: 'done', actualEnd, resultNote: resultNote.trim() || null },
      });
      toast.show('success', '完了にしました。');
      onClose();
    } catch (error) {
      setErrors(fieldErrors(error));
      toast.show('error', describeError(error));
    }
  };

  return (
    <Dialog
      open
      title="完了にする"
      description="実績終了日を入れて完了にします。進捗率は 100% になります。"
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>取り消す</Button>
          <Button variant="primary" type="submit" form="complete-form" disabled={update.isPending}>
            完了にする
          </Button>
        </>
      }
    >
      <form id="complete-form" className={styles.form} onSubmit={(e) => void submit(e)} noValidate>
        <p className={styles.taskBadge}>
          <Icon name="check" size={16} />
          {task.title}
        </p>
        <Field label="実績終了日" required htmlFor="cp-end" errors={errors.actualEnd}>
          <input id="cp-end" type="date" value={actualEnd} max={todayIso()} min={actualStart ?? undefined} onChange={(e) => setActualEnd(e.target.value)} />
        </Field>
        <Field label="結果コメント" htmlFor="cp-note" errors={errors.resultNote} hint="4,000 字以内">
          <textarea id="cp-note" value={resultNote} maxLength={4000} onChange={(e) => setResultNote(e.target.value)} />
        </Field>
      </form>
    </Dialog>
  );
}
