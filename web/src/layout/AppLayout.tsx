import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { logout } from '../api/client';
import { useMe, useUnreadCount } from '../api/hooks';
import { Icon, type IconName } from '../components/Icon';
import { Button, ErrorBox, Loading } from '../components/ui';
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

/** 共通のレイアウト（基本設計書 4.3）。上部のバー、左のメニュー、本文、右のパネル。 */
export function AppLayout() {
  const { data: me, error, isLoading } = useMe();
  const { taskId } = useTaskPanel();
  const [navOpen, setNavOpen] = useState(false);

  if (isLoading) return <Loading />;
  if (error || !me) return <ErrorBox error={error} />;

  return (
    <div className={styles.shell}>
      <TopBar onToggleNav={() => setNavOpen((o) => !o)} />
      <MfaBanner show={!me.mfaEnabled} />
      <nav className={[styles.nav, navOpen && styles.navOpen].filter(Boolean).join(' ')} aria-label="メニュー">
        <ul>
          {NAV.map((item) => (
            <li key={item.to}>
              <NavLink to={item.to} end={item.end} onClick={() => setNavOpen(false)} className={({ isActive }) => [styles.navLink, isActive && styles.navActive].filter(Boolean).join(' ')}>
                <Icon name={item.icon} />
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
        {me.isAdmin && (
          <>
            <p className={styles.navHeading}>管理</p>
            <ul>
              {ADMIN_NAV.map((item) => (
                <li key={item.to}>
                  <NavLink to={item.to} onClick={() => setNavOpen(false)} className={({ isActive }) => [styles.navLink, isActive && styles.navActive].filter(Boolean).join(' ')}>
                    <Icon name={item.icon} />
                    {item.label}
                  </NavLink>
                </li>
              ))}
            </ul>
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

function TopBar({ onToggleNav }: { onToggleNav: () => void }) {
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
      <Button variant="ghost" iconOnly aria-label="メニューを開く" onClick={onToggleNav}>
        <Icon name="menu" />
      </Button>
      <Link to="/" className={styles.brand}>
        タスク予実管理
      </Link>
      {me && me.teams.length > 0 && (
        <select className={styles.teamSelect} aria-label="チーム（プロジェクト）の切り替え" value={currentTeamId ?? ''} onChange={(e) => changeTeam(e.target.value)}>
          {me.teams.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
              {t.archived ? '（アーカイブ）' : ''}
            </option>
          ))}
        </select>
      )}
      <form className={styles.search} role="search" onSubmit={search}>
        <input type="search" placeholder="タスクを検索（タイトル、説明、タグ）" aria-label="キーワード" value={keyword} maxLength={100} onChange={(e) => setKeyword(e.target.value)} />
        <Button type="submit" iconOnly aria-label="検索">
          <Icon name="search" />
        </Button>
      </form>
      <span className={styles.spacer} />
      <Link to="/notifications" className={styles.bell} aria-label={`通知（未読 ${unread?.count ?? 0} 件）`}>
        <Icon name="bell" />
        {(unread?.count ?? 0) > 0 && <span className={styles.unread}>{unread!.count > 99 ? '99+' : unread!.count}</span>}
      </Link>
      <div className={styles.userMenu} ref={menuRef}>
        <Button variant="ghost" aria-expanded={menuOpen} onClick={() => setMenuOpen((o) => !o)}>
          <Icon name="user" />
          {me?.displayName}
          <Icon name="chevronDown" size={14} />
        </Button>
        {menuOpen && (
          <div className={styles.userPanel}>
            <span className={styles.userEmail}>{me?.email}</span>
            <Link to="/settings" onClick={() => setMenuOpen(false)}>
              設定（表示名、最初に開くビュー）
            </Link>
            <a href="/account/manage">アカウントのセキュリティ</a>
            <button type="button" onClick={() => void logout(false)}>
              ログアウト
            </button>
            <button type="button" onClick={() => void logout(true)}>
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
      <span aria-hidden="true">！</span>
      <p>{messageText('MSG-AUT-011')}</p>
      <a className="nowrap" href="/account/manage/two-factor">
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
