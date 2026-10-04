/**
 * 共通の部品（基本設計書 4.4）。画面の部品集のライブラリは使わず、ここにそろえる。
 */
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type ReactNode,
} from 'react';
import { ApiError } from '../api/client';
import type { DelayFlag, Priority, Tag, TaskStatus } from '../api/types';
import { FLAG, PRIORITY, STATUS, tagColorVar } from '../lib/labels';
import { fieldMessage, messageText } from '../lib/messages';
import { Icon, type IconName } from './Icon';
import styles from './ui.module.css';

export { styles as ui };

// ---------------------------------------------------------------- ボタン

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  /**
   * link は表の中のタスク名など、文字のリンクのように見せるボタン。
   * dangerGhost は削除などの取り消せない操作を、目立たせすぎずに置くときに使う（押すと確認のダイアログを出す）。
   */
  variant?: 'default' | 'primary' | 'danger' | 'dangerGhost' | 'ghost' | 'link';
  size?: 'normal' | 'small';
  pressed?: boolean;
  iconOnly?: boolean;
};

export function Button({ variant = 'default', size = 'normal', pressed, iconOnly, className, type, ...rest }: ButtonProps) {
  const classes = [
    variant === 'link' ? styles.link : styles.button,
    variant === 'primary' && styles.primary,
    variant === 'danger' && styles.danger,
    variant === 'dangerGhost' && styles.dangerGhost,
    variant === 'ghost' && styles.ghost,
    size === 'small' && styles.small,
    pressed && styles.pressed,
    iconOnly && styles.iconOnly,
    className,
  ]
    .filter(Boolean)
    .join(' ');
  return <button type={type ?? 'button'} className={classes} aria-pressed={pressed} {...rest} />;
}

// ---------------------------------------------------------------- ダイアログ（HTML 標準の dialog 要素）

export function Dialog({
  open,
  title,
  description,
  onClose,
  children,
  footer,
}: {
  open: boolean;
  title: string;
  /** 見出しの下に出す短い説明（このダイアログで何をするか） */
  description?: ReactNode;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);

  return (
    <dialog
      ref={ref}
      className={styles.dialog}
      aria-labelledby={titleId}
      onCancel={(e) => {
        e.preventDefault();
        onClose();
      }}
    >
      {open && (
        <>
          <div className={styles.dialogHeader}>
            <div className={styles.dialogTitle}>
              <h2 id={titleId}>{title}</h2>
              {description && <p className={styles.dialogDescription}>{description}</p>}
            </div>
            <Button variant="ghost" iconOnly aria-label="閉じる" onClick={onClose}>
              <Icon name="close" />
            </Button>
          </div>
          <div className={styles.dialogBody}>{children}</div>
          {footer && <div className={styles.dialogFooter}>{footer}</div>}
        </>
      )}
    </dialog>
  );
}

/** 削除などの取り消せない操作の確認（確認のダイアログは、取り消せない操作に限る。要件定義書 6.2）。 */
export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel,
  danger,
  busy,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  title: string;
  message: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <Dialog
      open={open}
      title={title}
      onClose={onCancel}
      footer={
        <>
          <Button onClick={onCancel}>取り消す</Button>
          <Button variant={danger ? 'danger' : 'primary'} onClick={onConfirm} disabled={busy}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      {message}
    </Dialog>
  );
}

// ---------------------------------------------------------------- お知らせ（保存の結果などを画面上部に短く表示する）

type ToastKind = 'success' | 'error' | 'info';
interface ToastItem {
  id: number;
  kind: ToastKind;
  text: string;
}

const ToastContext = createContext<{ show: (kind: ToastKind, text: string) => void }>({ show: () => undefined });

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const next = useRef(1);
  const dismiss = useCallback((id: number) => setItems((list) => list.filter((t) => t.id !== id)), []);
  const show = useCallback(
    (kind: ToastKind, text: string) => {
      const id = next.current++;
      setItems((list) => [...list.slice(-3), { id, kind, text }]);
      // エラーは閉じるまで表示する（基本設計書 4.3）
      if (kind !== 'error') window.setTimeout(() => dismiss(id), 4000);
    },
    [dismiss],
  );
  const value = useMemo(() => ({ show }), [show]);
  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className={styles.toasts} aria-live="polite">
        {items.map((t) => (
          <div
            key={t.id}
            role={t.kind === 'error' ? 'alert' : 'status'}
            className={[styles.toast, t.kind === 'success' ? styles.toastSuccess : t.kind === 'error' ? styles.toastError : styles.toastInfo].join(' ')}
          >
            <span className={styles.toastIcon}>
              <Icon name={t.kind === 'success' ? 'checkCircle' : t.kind === 'error' ? 'alert' : 'info'} size={20} />
            </span>
            <p>{t.text}</p>
            <Button variant="ghost" size="small" aria-label="閉じる" onClick={() => dismiss(t.id)}>
              <Icon name="close" size={14} />
            </Button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  return useContext(ToastContext);
}

/** API のエラーを、利用者に見せる文にする（詳細設計書 5.2 の「画面の動き」）。 */
export function describeError(error: unknown): string {
  if (!(error instanceof ApiError)) return messageText('MSG-CMN-500', { traceId: '-' });
  switch (error.code) {
    case 'validation.failed': {
      const general = error.messageIds('');
      return general.length > 0 ? general.map((id) => messageText(id)).join(' ') : '入力内容を確認してください。';
    }
    case 'rule.violation':
      return Object.values(error.errors).flat().map((id) => messageText(id)).join(' ') || messageText('MSG-CMN-409');
    case 'auth.forbidden':
      return messageText('MSG-CMN-403');
    case 'auth.mfa_required':
      return '管理の操作には、多要素認証でのログインが必要です。';
    case 'team.archived':
      return messageText('MSG-CMN-ARC');
    case 'security.csrf':
      return messageText('MSG-CMN-CSRF');
    case 'resource.not_found':
      return messageText('MSG-CMN-404');
    case 'concurrency.conflict':
      return messageText('MSG-CMN-409');
    case 'rate.limited':
      return messageText('MSG-CMN-429');
    case 'auth.unauthenticated':
    case 'auth.session_expired':
      return messageText('MSG-CMN-401');
    case 'auth.reauth_required':
      return '続けるには、もう一度本人確認をしてください。';
    default:
      return messageText('MSG-CMN-500', { traceId: error.traceId ?? '-' });
  }
}

/** 項目の誤りを、項目ごとの文の一覧にする（入力の誤りは、その項目の近くに日本語で示す。NF-USE-02）。 */
export function fieldErrors(error: unknown): Record<string, string[]> {
  if (!(error instanceof ApiError) || error.code !== 'validation.failed') return {};
  const result: Record<string, string[]> = {};
  for (const [field, ids] of Object.entries(error.errors)) {
    if (field === '') continue;
    result[field] = ids.map((id) => fieldMessage(field, id));
  }
  return result;
}

// ---------------------------------------------------------------- 入力欄

export function Field({
  label,
  required,
  hint,
  errors,
  children,
  htmlFor,
}: {
  label: string;
  required?: boolean;
  hint?: string;
  errors?: string[];
  children: ReactNode;
  htmlFor?: string;
}) {
  return (
    <div className={styles.field}>
      <label className={styles.fieldLabel} htmlFor={htmlFor}>
        {label}
        {required && <span className={styles.required}>必須</span>}
      </label>
      {children}
      {hint && <span className={styles.fieldHint}>{hint}</span>}
      {errors?.map((e) => (
        <span key={e} className={styles.fieldError} role="alert">
          {e}
        </span>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------- 印

const STATUS_CLASS: Record<TaskStatus, string | undefined> = {
  not_started: styles.statusNotStarted,
  in_progress: styles.statusInProgress,
  on_hold: styles.statusOnHold,
  done: styles.statusDone,
  cancelled: styles.statusCancelled,
};

/** 状態の印。色だけで示さず、アイコンと文字を添える（基本設計書 4.4）。 */
export function StatusBadge({ status }: { status: TaskStatus }) {
  const s = STATUS[status];
  return (
    <span className={[styles.badge, STATUS_CLASS[status]].join(' ')}>
      <span aria-hidden="true">{s.icon}</span>
      {s.label}
    </span>
  );
}

const PRIORITY_CLASS: Record<Priority, string | undefined> = {
  high: styles.priorityHigh,
  medium: styles.priorityMedium,
  low: styles.priorityLow,
};

/** 優先度の印。withLabel を付けると「優先度 高」と表示する（列の見出しがない場所で使う）。 */
export function PriorityBadge({ priority, withLabel }: { priority: Priority; withLabel?: boolean }) {
  const p = PRIORITY[priority];
  return (
    <span className={[styles.badge, PRIORITY_CLASS[priority]].join(' ')} title={`優先度 ${p.label}`}>
      {withLabel ? `優先度 ${p.label}` : p.label}
    </span>
  );
}

export function FlagBadges({ flags, short }: { flags: DelayFlag[]; short?: boolean }) {
  if (flags.length === 0) return null;
  return (
    <>
      {flags.map((f) => (
        <span key={f} className={styles.flag} title={FLAG[f].label}>
          <span aria-hidden="true">！</span>
          {short ? FLAG[f].short : FLAG[f].label}
        </span>
      ))}
    </>
  );
}

/** 汎用の小さな印（役割、利用者の状態など）。 */
export function Pill({ tone = 'neutral', icon, children, title }: { tone?: 'neutral' | 'primary' | 'success' | 'warning' | 'danger'; icon?: IconName; children: ReactNode; title?: string }) {
  return (
    <span className={[styles.pill, styles[`pill_${tone}`]].join(' ')} title={title}>
      {icon && <Icon name={icon} size={14} />}
      {children}
    </span>
  );
}

export function TagChip({ tag }: { tag: Tag }) {
  const c = tagColorVar(tag.color);
  return (
    <span className={styles.tag} style={{ background: c.bg, color: c.fg }}>
      {tag.name}
    </span>
  );
}

export function ProgressBar({ value, wide, tone = 'primary', label = '進捗' }: { value: number; wide?: boolean; tone?: 'primary' | 'success' | 'danger'; label?: string }) {
  return (
    <span className={[styles.progressBar, wide && styles.progressWide].filter(Boolean).join(' ')} role="img" aria-label={`${label} ${value}%`}>
      <span
        className={[styles.progressFill, tone === 'success' && styles.progressSuccess, tone === 'danger' && styles.progressDanger].filter(Boolean).join(' ')}
        style={{ width: `${Math.min(100, Math.max(0, value))}%` }}
      />
    </span>
  );
}

export function Loading() {
  return (
    <p className={styles.spinner} role="status">
      <span className={styles.spinnerIcon} aria-hidden="true" />
      読み込んでいます…
    </p>
  );
}

export function ErrorBox({ error }: { error: unknown }) {
  return (
    <div className={styles.errorBox} role="alert">
      <Icon name="alert" size={18} />
      <span>{describeError(error)}</span>
    </div>
  );
}

// ---------------------------------------------------------------- 画面の見出し、カード、空の表示、利用者の印

/** 画面の見出し。何をする画面かを 1 行で説明し、主な操作を右に置く。 */
export function PageHeader({ title, description, icon, actions, meta }: { title: ReactNode; description?: ReactNode; icon?: IconName; actions?: ReactNode; meta?: ReactNode }) {
  return (
    <header className={styles.pageHeader}>
      {icon && (
        <span className={styles.pageIcon} aria-hidden="true">
          <Icon name={icon} size={22} />
        </span>
      )}
      <div className={styles.pageTitle}>
        <h1>{title}</h1>
        {description && <p className={styles.pageDescription}>{description}</p>}
      </div>
      {meta && <div className={styles.pageMeta}>{meta}</div>}
      {actions && <div className={styles.pageActions}>{actions}</div>}
    </header>
  );
}

/** 内容のまとまり。見出し、件数、説明、右上の操作を持てる。 */
export function Card({
  title,
  count,
  description,
  actions,
  icon,
  tone,
  children,
  className,
  id,
}: {
  title?: ReactNode;
  count?: number;
  description?: ReactNode;
  actions?: ReactNode;
  icon?: IconName;
  tone?: 'danger' | 'warning' | 'primary';
  children?: ReactNode;
  className?: string;
  id?: string;
}) {
  return (
    <section className={['card', styles.cardTone, tone && styles[`card_${tone}`], className].filter(Boolean).join(' ')} id={id}>
      {(title || actions) && (
        <div className={styles.cardHeader}>
          {icon && (
            <span className={styles.cardIcon} aria-hidden="true">
              <Icon name={icon} size={18} />
            </span>
          )}
          {title && (
            <h2 className={styles.cardTitle}>
              {title}
              {count !== undefined && <span className={styles.cardCount}>{count}</span>}
            </h2>
          )}
          {actions && <div className={styles.cardActions}>{actions}</div>}
        </div>
      )}
      {description && <p className={styles.cardDescription}>{description}</p>}
      {children}
    </section>
  );
}

/** データがないときの表示。理由と、次にできることを示す。 */
export function EmptyState({ icon = 'inbox', title, description, action, compact }: { icon?: IconName; title: string; description?: ReactNode; action?: ReactNode; compact?: boolean }) {
  return (
    <div className={[styles.emptyState, compact && styles.emptyCompact].filter(Boolean).join(' ')}>
      <span className={styles.emptyIcon} aria-hidden="true">
        <Icon name={icon} size={compact ? 18 : 24} />
      </span>
      <div>
        <p className={styles.emptyTitle}>{title}</p>
        {description && <p className={styles.emptyDescription}>{description}</p>}
        {action && <div className={styles.emptyAction}>{action}</div>}
      </div>
    </div>
  );
}

const AVATAR_COLORS = ['blue', 'green', 'purple', 'orange', 'teal', 'red', 'yellow', 'gray'] as const;

/** 利用者の頭文字の丸印（名前から色を決める。写真は扱わない）。 */
export function Avatar({ name, size = 'normal' }: { name: string; size?: 'small' | 'normal' }) {
  let hash = 0;
  for (const ch of name) hash = (hash * 31 + ch.codePointAt(0)!) >>> 0;
  const color = tagColorVar(AVATAR_COLORS[hash % AVATAR_COLORS.length]!);
  return (
    <span className={[styles.avatar, size === 'small' && styles.avatarSmall].filter(Boolean).join(' ')} style={{ background: color.bg, color: color.fg }} aria-hidden="true">
      {[...name.trim()][0] ?? '?'}
    </span>
  );
}

// ---------------------------------------------------------------- 複数選択

export function MultiSelect<T extends string>({
  label,
  options,
  selected,
  onChange,
}: {
  label: string;
  options: Array<{ value: T; label: string }>;
  selected: T[];
  onChange: (values: T[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const panelId = useId();
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);

  const toggle = (value: T) => {
    onChange(selected.includes(value) ? selected.filter((v) => v !== value) : [...selected, value]);
  };

  return (
    <div className={styles.multi} ref={ref}>
      <Button size="small" aria-expanded={open} aria-controls={panelId} onClick={() => setOpen((o) => !o)} pressed={selected.length > 0}>
        {label}
        {selected.length > 0 && <span className={styles.count}>{selected.length}</span>}
        <Icon name="chevronDown" size={14} />
      </Button>
      {open && (
        <div className={styles.multiPanel} id={panelId} role="group" aria-label={label}>
          {options.length === 0 && <p className="muted">選択肢がありません</p>}
          {options.map((o) => (
            <label key={o.value} className={styles.multiOption}>
              <input type="checkbox" checked={selected.includes(o.value)} onChange={() => toggle(o.value)} />
              {o.label}
            </label>
          ))}
          {selected.length > 0 && (
            <Button variant="ghost" size="small" onClick={() => onChange([])}>
              選択を解除
            </Button>
          )}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------- ポップオーバー（ボタンで開く小さな設定の欄）

export function Popover({ label, icon, badge, children }: { label: string; icon?: IconName; badge?: number; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const panelId = useId();
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);

  return (
    <div className={styles.multi} ref={ref}>
      <Button size="small" aria-expanded={open} aria-controls={panelId} onClick={() => setOpen((o) => !o)} pressed={(badge ?? 0) > 0}>
        {icon && <Icon name={icon} size={14} />}
        {label}
        {(badge ?? 0) > 0 && <span className={styles.count}>{badge}</span>}
        <Icon name="chevronDown" size={14} />
      </Button>
      {open && (
        <div className={[styles.multiPanel, styles.popoverPanel].join(' ')} id={panelId} role="group" aria-label={label}>
          {children}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------- タブ

export function Tabs<T extends string>({
  tabs,
  active,
  onChange,
  label,
}: {
  tabs: Array<{ id: T; label: string }>;
  active: T;
  onChange: (id: T) => void;
  label: string;
}) {
  return (
    <div className={styles.tabs} role="tablist" aria-label={label}>
      {tabs.map((t) => (
        <button
          key={t.id}
          type="button"
          role="tab"
          aria-selected={t.id === active}
          className={[styles.tab, t.id === active && styles.tabActive].filter(Boolean).join(' ')}
          onClick={() => onChange(t.id)}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}
