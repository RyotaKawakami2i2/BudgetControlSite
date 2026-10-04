import { formatHm } from '../lib/effort';

/**
 * 工数の入力（「1.5」と「1:30」の両方を受け付ける。詳細設計書 7.5）。入力中の文字はそのまま持ち、確定は呼び出し側で解釈する。
 */
export function EffortInput({
  id,
  value,
  onChange,
  invalid,
  placeholder = '例: 1.5 または 1:30',
  ariaLabel,
  disabled,
  size,
}: {
  id?: string;
  value: string;
  onChange: (text: string) => void;
  invalid?: boolean;
  placeholder?: string;
  ariaLabel?: string;
  disabled?: boolean;
  size?: number;
}) {
  return (
    <input
      id={id}
      type="text"
      inputMode="decimal"
      autoComplete="off"
      value={value}
      placeholder={placeholder}
      aria-label={ariaLabel}
      aria-invalid={invalid || undefined}
      disabled={disabled}
      size={size}
      onChange={(e) => onChange(e.target.value)}
    />
  );
}

/** 分を、入力欄の初期値（時:分）にする。 */
export function effortText(minutes: number | null | undefined): string {
  return minutes ? formatHm(minutes) : '';
}
