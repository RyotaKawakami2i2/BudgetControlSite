import { useCallback, useEffect, useSyncExternalStore } from 'react';
import { useSearchParams } from 'react-router';
import { useMe } from '../api/hooks';
import type { Me, MyTeam } from '../api/types';

const TEAM_KEY = 'tyj.currentTeam';

/** 上部のバーで選んでいるチーム（利用者ごとの表示の好みとして、ブラウザに覚える）。 */
export function readCurrentTeamId(me: Me | undefined): string | null {
  if (!me) return null;
  let stored: string | null = null;
  try {
    stored = window.localStorage.getItem(TEAM_KEY);
  } catch {
    stored = null;
  }
  const active = me.teams.filter((t) => !t.archived);
  if (stored && me.teams.some((t) => t.id === stored)) return stored;
  return active[0]?.id ?? me.teams[0]?.id ?? null;
}

// 選んでいるチームが変わったことを、表示中の部品に知らせる
const listeners = new Set<() => void>();
let version = 0;

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function writeCurrentTeamId(id: string): void {
  try {
    if (window.localStorage.getItem(TEAM_KEY) === id) return;
    window.localStorage.setItem(TEAM_KEY, id);
  } catch {
    // 保存できなくても動作は変わらない
  }
  version += 1;
  listeners.forEach((l) => l());
}

export function useCurrentTeam(): { me: Me | undefined; team: MyTeam | undefined; teamId: string | null } {
  const { data: me } = useMe();
  useSyncExternalStore(subscribe, () => version);
  const teamId = readCurrentTeamId(me);
  return { me, teamId, team: me?.teams.find((t) => t.id === teamId) };
}

/** チームの画面を開いたら、上部のバーのチームもそのチームに合わせる（所属しているチームだけ）。 */
export function useSyncCurrentTeam(teamId: string | null | undefined): void {
  const { data: me } = useMe();
  useEffect(() => {
    if (teamId && me?.teams.some((t) => t.id === teamId)) writeCurrentTeamId(teamId);
  }, [me, teamId]);
}

/** タスク詳細のパネル（URL の task で開く。どの画面からでも開ける）。 */
export function useTaskPanel() {
  const [params, setParams] = useSearchParams();
  const taskId = params.get('task');
  const open = useCallback(
    (id: string) =>
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.set('task', id);
          return next;
        },
        { replace: false },
      ),
    [setParams],
  );
  const close = useCallback(
    () =>
      setParams((prev) => {
        const next = new URLSearchParams(prev);
        next.delete('task');
        return next;
      }),
    [setParams],
  );
  return { taskId, open, close };
}
