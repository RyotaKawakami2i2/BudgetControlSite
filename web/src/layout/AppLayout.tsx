import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { logout } from '../api/client';
import { useMe, useUnreadCount } from '../api/hooks';
import { Icon, type IconName } from '../components/Icon';
import { Avatar, Button, ErrorBox, Loading } from '../components/ui';
import { TaskPanel } from '../features/tasks/TaskPanel';
import { messageText } from '../lib/messages';
import { useCurrentTeam, useTaskPanel, writeCurrentTeamId } from './context';
import { SessionWatcher } from './SessionWatcher';
import styles from './layout.module.css';

const NAV: Array<{ to: string; label: string; icon: IconName; end?: boolean }> = [
  { to: '/', label: 'ホーム', icon: 'home', end: true },
  { to: '/gantt', label: 'ガント', icon: 'gantt' },
  { to: '/my-tasks', label: 'マイタスク', icon: 'check' },
  { to: '/timesheet', label: '週の入力表', icon: 'clock' },
  { to: '/tasks', label: 'タスク一覧', icon: 'list' },
  { to: '/teams', label: 'チーム', icon: 'team' },
  { to: '/notifications', label: '通知', icon: 'bell' },
];

const ADMIN_NAV: Array<{ to: string; label: string; icon: IconName }> = [
  { to: '/admin/users', label: '利用者', icon: 'user' },
  { to: '/admin/teams', label: 'チーム管理', icon: 'team' },
  { to: '/admin/audit-logs', label: '監査ログ', icon: 'shield' },
  { to: '/admin/holidays', label: '祝日', icon: 'today' },
];

const BANNER_KEY = 'tyj.mfaBannerClosed';
const NAV_KEY = 'tyj.navCollapsed';
const MOBILE = '(max-width: 768px)';

function readCollapsed(): boolean {
  try {
    return window.localStorage.getItem(NAV_KEY) === '1';
  } catch {
    return false;
  }
}

/** 共通のレイアウト（基本設計書 4.3）。上部のバー、左のメニュー（折りたためる）、本文、右のパネル。 */
export function AppLayout() {
  const { data: me, error, isLoading } = useMe();
  const { data: unread } = useUnreadCount();
  const { taskId } = useTaskPanel();
  const [navOpen, setNavOpen] = useState(false);
  const [collapsed, setCollapsed] = useState(readCollapsed);

  if (isLoading) return <Loading />;
  if (error || !me) return <ErrorBox error={error} />;

  // 幅の狭い画面ではメニューを重ねて開き、広い画面ではメニューをアイコンだけに折りたたむ
  const toggleNav = () => {
    if (window.matchMedia(MOBILE).matches) {
      setNavOpen((o) => !o);
      return;
    }
    setCollapsed((c) => {
      try {
        window.localStorage.setItem(NAV_KEY, c ? '0' : '1');
      } catch {
        // 保存できなくても切り替える
      }
      return !c;
    });
  };

  const unreadCount = unread?.count ?? 0;
  const navItem = (item: { to: string; label: string; icon: IconName; end?: boolean }, badge?: number) => (
    <li key={item.to}>
      <NavLink
        to={item.to}
        end={item.end}
        title={collapsed ? item.label : undefined}
        onClick={() => setNavOpen(false)}
        className={({ isActive }) => [styles.navLink, isActive && styles.navActive].filter(Boolean).join(' ')}
      >
        <Icon name={item.icon} />
        <span className={styles.navLabel}>{item.label}</span>
        {badge !== undefined && badge > 0 && <span className={styles.navBadge}>{badge > 99 ? '99+' : badge}</span>}
      </NavLink>
    </li>
  );

  return (
    <div className={[styles.shell, collapsed && styles.shellCollapsed].filter(Boolean).join(' ')}>
      <TopBar onToggleNav={toggleNav} navExpanded={navOpen || !collapsed} />
      <MfaBanner show={!me.mfaEnabled} />
      {navOpen && <div className={styles.navBackdrop} aria-hidden="true" onClick={() => setNavOpen(false)} />}
      <nav className={[styles.nav, navOpen && styles.navOpen].filter(Boolean).join(' ')} aria-label="メニュー">
        <p className={styles.navHeading}>業務</p>
        <ul>{NAV.map((item) => navItem(item, item.to === '/notifications' ? unreadCount : undefined))}</ul>
        {me.isAdmin && (
          <>
            <p className={styles.navHeading}>管理</p>
            <ul>{ADMIN_NAV.map((item) => navItem(item))}</ul>
          </>
        )}
      </nav>
      <div className={styles.main}>
        <main className={styles.content} id="main">
          <Outlet />
        </main>
        {taskId && (
          <aside className={styles.panel} aria-label="タスク詳細">
            <TaskPanel taskId={taskId} />
          </aside>
        )}
      </div>
      <SessionWatcher />
    </div>
  );
}

function TopBar({ onToggleNav, navExpanded }: { onToggleNav: () => void; navExpanded: boolean }) {
  const { me, teamId: currentTeamId } = useCurrentTeam();
  const { data: unread } = useUnreadCount();
  const navigate = useNavigate();
  const location = useLocation();
  const [keyword, setKeyword] = useState('');
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const close = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [menuOpen]);

  const changeTeam = (id: string) => {
    writeCurrentTeamId(id);
    const teamPage = /^\/teams\/[^/]+(\/report)?$/.exec(location.pathname);
    if (location.pathname.startsWith('/gantt') || location.pathname.startsWith('/tasks')) {
      navigate(`${location.pathname}?teams=${id}`);
    } else if (teamPage) {
      navigate(`/teams/${id}${teamPage[1] ?? ''}`);
    }
  };

  const search = (e: FormEvent) => {
    e.preventDefault();
    if (!currentTeamId) return;
    const params = new URLSearchParams({ teams: currentTeamId });
    if (keyword.trim()) params.set('q', keyword.trim());
    navigate(`/gantt?${params.toString()}`);
  };

  return (
    <header className={styles.topbar}>
      <Button variant="ghost" iconOnly aria-label={navExpanded ? 'メニューを閉じる' : 'メニューを開く'} aria-expanded={navExpanded} onClick={onToggleNav}>
        <Icon name="menu" />
      </Button>
      <Link to="/" className={styles.brand}>
        <LogoMark />
        <span>タスク予実管理</span>
      </Link>
      {me && me.teams.length > 0 && (
        <label className={styles.teamSwitch}>
          <span className={styles.teamSwitchLabel}>チーム</span>
          <select aria-label="チーム（プロジェクト）の切り替え" value={currentTeamId ?? ''} onChange={(e) => changeTeam(e.target.value)}>
            {me.teams.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
                {t.archived ? '（アーカイブ）' : ''}
              </option>
            ))}
          </select>
        </label>
      )}
      <form className={styles.search} role="search" onSubmit={search}>
        <span className={styles.searchIcon} aria-hidden="true">
          <Icon name="search" size={16} />
        </span>
        <input type="search" placeholder="タスクを検索（タイトル、説明、タグ）して Enter" aria-label="キーワード" value={keyword} maxLength={100} onChange={(e) => setKeyword(e.target.value)} />
        <button type="submit" className="visually-hidden">
          検索
        </button>
      </form>
      <span className={styles.spacer} />
      <Link to="/notifications" className={styles.bell} aria-label={`通知（未読 ${unread?.count ?? 0} 件）`} title="通知">
        <Icon name="bell" />
        {(unread?.count ?? 0) > 0 && <span className={styles.unread}>{unread!.count > 99 ? '99+' : unread!.count}</span>}
      </Link>
      <div className={styles.userMenu} ref={menuRef}>
        <button type="button" className={styles.userButton} aria-expanded={menuOpen} aria-haspopup="menu" onClick={() => setMenuOpen((o) => !o)}>
          <Avatar name={me?.displayName ?? '?'} />
          <span className={styles.userName}>{me?.displayName}</span>
          <Icon name="chevronDown" size={14} />
        </button>
        {menuOpen && (
          <div className={styles.userPanel}>
            <div className={styles.userInfo}>
              <Avatar name={me?.displayName ?? '?'} />
              <div>
                <strong>{me?.displayName}</strong>
                <span className={styles.userEmail}>{me?.email}</span>
              </div>
            </div>
            <Link to="/settings" onClick={() => setMenuOpen(false)}>
              <Icon name="gear" size={16} />
              設定（表示名、最初に開くビュー）
            </Link>
            <a href="/account/manage">
              <Icon name="shield" size={16} />
              アカウントのセキュリティ
            </a>
            <hr />
            <button type="button" onClick={() => void logout(false)}>
              <Icon name="logout" size={16} />
              ログアウト
            </button>
            <button type="button" onClick={() => void logout(true)}>
              <Icon name="logout" size={16} />
              すべての端末からログアウト
            </button>
          </div>
        )}
      </div>
    </header>
  );
}

/** 多要素認証を設定していない利用者に、設定を勧める表示（閉じても、次にログインしたときに再び表示する）。 */
function MfaBanner({ show }: { show: boolean }) {
  const [closed, setClosed] = useState(() => {
    try {
      return window.sessionStorage.getItem(BANNER_KEY) === '1';
    } catch {
      return false;
    }
  });
  if (!show || closed) return null;
  return (
    <div className={styles.banner} role="status">
      <span className={styles.bannerIcon} aria-hidden="true">
        <Icon name="shield" size={18} />
      </span>
      <p>{messageText('MSG-AUT-011')}</p>
      <a className={styles.bannerAction} href="/account/manage/two-factor">
        設定する
      </a>
      <Button
        variant="ghost"
        size="small"
        onClick={() => {
          try {
            window.sessionStorage.setItem(BANNER_KEY, '1');
          } catch {
            // 保存できなくても閉じる
          }
          setClosed(true);
        }}
      >
        閉じる
      </Button>
    </div>
  );
}

/** 画面の左上の印（ガントのバーを図にしたもの）。 */
function LogoMark() {
  return (
    <svg className={styles.logo} width="28" height="28" viewBox="0 0 28 28" aria-hidden="true" focusable="false">
      <rect width="28" height="28" rx="7" fill="currentColor" />
      <rect x="6" y="7" width="10" height="3" rx="1.5" fill="#ffffff" />
      <rect x="10" y="12.5" width="12" height="3" rx="1.5" fill="#ffffff" />
      <rect x="8" y="18" width="8" height="3" rx="1.5" fill="#ffffff" opacity="0.75" />
    </svg>
  );
}
