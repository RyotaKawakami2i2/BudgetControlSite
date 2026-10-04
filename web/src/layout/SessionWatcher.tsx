import { useEffect, useState } from 'react';
import { api, goToLogin } from '../api/client';
import type { SessionStatus } from '../api/types';
import { Button, Dialog } from '../components/ui';
import { messageText } from '../lib/messages';

/**
 * セッションの時間切れの予告（詳細設計書 6.6）。残り時間を確かめ（延長しない要求）、残り 5 分で予告を出す。
 * 「続ける」で延長する。時間切れになったら、ログインの画面へ移る。
 */
export function SessionWatcher() {
  const [status, setStatus] = useState<SessionStatus | null>(null);
  const [checkedAt, setCheckedAt] = useState(() => Date.now());
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    let cancelled = false;
    const check = async () => {
      try {
        const s = await api.get<SessionStatus>('/session/status');
        if (!cancelled) {
          setStatus(s);
          setCheckedAt(Date.now());
        }
      } catch {
        // 401 のときは、呼び出しの共通処理がログインの画面へ移る
      }
    };
    void check();
    const poll = window.setInterval(check, 60_000);
    const tick = window.setInterval(() => setNow(Date.now()), 1_000);
    return () => {
      cancelled = true;
      window.clearInterval(poll);
      window.clearInterval(tick);
    };
  }, []);

  const remaining = status ? status.remainingSeconds - Math.floor((now - checkedAt) / 1000) : null;

  useEffect(() => {
    if (remaining !== null && remaining <= 0) goToLogin();
  }, [remaining]);

  const warn = status !== null && remaining !== null && remaining > 0 && remaining <= status.warningSeconds;
  const keepAlive = async () => {
    const s = await api.post<SessionStatus>('/session/keepalive');
    setStatus(s);
    setCheckedAt(Date.now());
  };

  return (
    <Dialog
      open={warn}
      title="まもなくログアウトします"
      onClose={() => void keepAlive()}
      footer={
        <Button variant="primary" onClick={() => void keepAlive()}>
          続ける
        </Button>
      }
    >
      <p>{messageText('MSG-SES-001')}</p>
      {remaining !== null && (
        <p className="muted">
          残り {Math.floor(remaining / 60)} 分 {remaining % 60} 秒
        </p>
      )}
    </Dialog>
  );
}
