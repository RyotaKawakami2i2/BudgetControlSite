/** アイコン（SVG を自前で持つ。外部の部品やフォントは使わない）。 */
const PATHS = {
  home: 'M3 10.5 12 3l9 7.5V21h-6v-6H9v6H3z',
  gantt: 'M3 5h10v3H3zm4 5.5h12v3H7zM5 16h9v3H5z',
  list: 'M4 6h2v2H4zm4 0h12v2H8zM4 11h2v2H4zm4 0h12v2H8zm-4 5h2v2H4zm4 0h12v2H8z',
  check: 'M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z',
  clock: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 10.4 3.5 2.1-.8 1.3L11 13V7h2z',
  team: 'M8 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zm8 0a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM2 20c0-3.3 2.7-6 6-6s6 2.7 6 6zm13 0c0-1.7-.6-3.3-1.6-4.5.8-.3 1.7-.5 2.6-.5 3 0 5 2 5 5z',
  bell: 'M12 22a2.5 2.5 0 0 0 2.5-2.5h-5A2.5 2.5 0 0 0 12 22zm7-6V11a7 7 0 0 0-5.5-6.8V3a1.5 1.5 0 0 0-3 0v1.2A7 7 0 0 0 5 11v5l-2 2v1h18v-1z',
  gear: 'M19.4 13a7.5 7.5 0 0 0 0-2l2.1-1.6-2-3.5-2.5 1a7.4 7.4 0 0 0-1.7-1L14.9 3h-4l-.4 2.6a7.4 7.4 0 0 0-1.7 1l-2.5-1-2 3.5L6.4 11a7.5 7.5 0 0 0 0 2l-2.1 1.6 2 3.5 2.5-1a7.4 7.4 0 0 0 1.7 1l.4 2.6h4l.4-2.6a7.4 7.4 0 0 0 1.7-1l2.5 1 2-3.5zM12.9 15.5a3.5 3.5 0 1 1 0-7 3.5 3.5 0 0 1 0 7z',
  shield: 'M12 2 4 5v6c0 5 3.4 9.7 8 11 4.6-1.3 8-6 8-11V5z',
  search: 'M10 2a8 8 0 1 0 4.9 14.3l5.4 5.4 1.4-1.4-5.4-5.4A8 8 0 0 0 10 2zm0 2a6 6 0 1 1 0 12 6 6 0 0 1 0-12z',
  plus: 'M11 4h2v7h7v2h-7v7h-2v-7H4v-2h7z',
  close: 'm6.4 5 5.6 5.6L17.6 5 19 6.4 13.4 12l5.6 5.6-1.4 1.4-5.6-5.6L6.4 19 5 17.6l5.6-5.6L5 6.4z',
  chevronRight: 'm9 6 6 6-6 6-1.4-1.4 4.6-4.6-4.6-4.6z',
  chevronDown: 'm6 9 6 6 6-6-1.4-1.4-4.6 4.6-4.6-4.6z',
  menu: 'M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z',
  today: 'M7 2h2v2h6V2h2v2h3v18H4V4h3zm-1 7v11h12V9zm5 2h2v6h-2z',
  report: 'M4 20V10h3v10zm6 0V4h3v16zm6 0v-7h3v7z',
  user: 'M12 12a5 5 0 1 0 0-10 5 5 0 0 0 0 10zm0 2c-4.4 0-8 2.2-8 5v2h16v-2c0-2.8-3.6-5-8-5z',
  warning: 'M12 2 1 21h22zm1 15h-2v-2h2zm0-4h-2V9h2z',
  lock: 'M12 2a5 5 0 0 0-5 5v3H5v12h14V10h-2V7a5 5 0 0 0-5-5zm-3 8V7a3 3 0 1 1 6 0v3zm2 4h2v4h-2z',
  edit: 'M4 17.2V20h2.8l8.3-8.3-2.8-2.8zM19.7 7.1a1 1 0 0 0 0-1.4l-1.4-1.4a1 1 0 0 0-1.4 0l-1.1 1.1 2.8 2.8z',
  trash: 'M6 7h12l-1 14H7zm3-4h6l1 2h4v2H4V5h4z',
  calendar: 'M7 2h2v2h6V2h2v2h3v18H4V4h3zm-1 7v11h12V9z',
  filter: 'M3 5h18l-7 8v6l-4 2v-8z',
  info: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-1 8h2v7h-2zm0-4h2v2h-2z',
  alert: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-1 5h2v7h-2zm0 9h2v2h-2z',
  checkCircle: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-1.5 14.2-4.2-4.2 1.4-1.4 2.8 2.8 5.8-5.8 1.4 1.4z',
  arrowRight: 'M4 11h12.2l-5.6-5.6L12 4l8 8-8 8-1.4-1.4 5.6-5.6H4z',
  chevronLeft: 'm15 6-6 6 6 6 1.4-1.4-4.6-4.6 4.6-4.6z',
  logout: 'M10 3H4v18h6v-2H6V5h4zm5.6 4.4-1.4 1.4 2.2 2.2H9v2h7.4l-2.2 2.2 1.4 1.4 4.6-4.6z',
  inbox: 'M3 4h18v16H3zm2 2v7h4l1 2h4l1-2h4V6z',
  sliders: 'M4 6h9v2H4zm13 0h3v2h-3zm-2-2h2v6h-2zM4 16h3v2H4zm7 0h9v2h-9zm-2-2h2v6H9z',
  eye: 'M12 5C6 5 2 12 2 12s4 7 10 7 10-7 10-7-4-7-10-7zm0 11a4 4 0 1 1 0-8 4 4 0 0 1 0 8zm0-2a2 2 0 1 0 0-4 2 2 0 0 0 0 4z',
  flag: 'M5 3h2v18H5zm3 1h11l-3 4 3 4H8z',
  archive: 'M3 4h18v4H3zm1 6h16v10H4zm5 2v2h6v-2z',
  tag: 'M3 3h8l10 10-8 8L3 11zm4 2.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z',
  sidebar: 'M3 4h18v16H3zm2 2v12h4V6zm6 0v12h8V6z',
} as const;

export type IconName = keyof typeof PATHS;

// 内側に穴のある形（外枠と内側の図形を重ねて描くもの）
const EVENODD: ReadonlySet<IconName> = new Set(['calendar', 'info', 'alert', 'checkCircle', 'inbox', 'eye', 'archive', 'tag', 'sidebar']);

export function Icon({ name, size = 18, label }: { name: IconName; size?: number; label?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="currentColor"
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      focusable="false"
    >
      <path d={PATHS[name]} fillRule={EVENODD.has(name) ? 'evenodd' : undefined} />
    </svg>
  );
}
