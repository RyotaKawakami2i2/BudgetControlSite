import { useState } from 'react';
import { useNotificationMutations, useNotifications } from '../../api/hooks';
import type { NotificationKind } from '../../api/types';
import { Icon, type IconName } from '../../components/Icon';
import { Button, EmptyState, ErrorBox, Loading, PageHeader } from '../../components/ui';
import { useTaskPanel } from '../../layout/context';
import { formatDateTime } from '../../lib/dates';
import { NOTIFICATION } from '../../lib/labels';
import styles from './notifications.module.css';

const KIND_ICON: Record<NotificationKind, IconName> = {
  task_assigned: 'user',
  task_unassigned: 'user',
  comment_added: 'info',
  due_tomorrow: 'today',
  overdue: 'alert',
  team_added: 'team',
};

/** 通知（SC-13。FR-NTF-01、02）。タスクの名前は、いま閲覧できる場合だけ表示する。 */
export function NotificationsPage() {
  const { data, error, isLoading } = useNotifications();
  const { read, readAll } = useNotificationMutations();
  const { open } = useTaskPanel();
  const [unreadOnly, setUnreadOnly] = useState(false);
  const items = (data?.items ?? []).filter((n) => !unreadOnly || !n.readAt);
  const unreadCount = (data?.items ?? []).filter((n) => !n.readAt).length;

  return (
    <div className="page">
      <PageHeader
        icon="bell"
        title="通知"
        description="担当になったタスクやコメント、期限が近いタスクなどのお知らせです。タスクの名前を押すと詳細を開きます。"
        actions={
          <>
            <label className="check-label">
              <input type="checkbox" checked={unreadOnly} onChange={(e) => setUnreadOnly(e.target.checked)} />
              未読だけ表示する
            </label>
            <Button onClick={() => readAll.mutate()} disabled={readAll.isPending || unreadCount === 0}>
              <Icon name="check" size={16} />
              すべて既読にする
            </Button>
          </>
        }
      />
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && items.length === 0 && (
        <EmptyState icon="bell" title={unreadOnly ? '未読の通知はありません' : '通知はありません'} description="タスクの担当になったときなどに、ここに届きます。" />
      )}
      {items.length > 0 && (
        <ul className={styles.list}>
          {items.map((n) => (
            <li key={n.id} className={[styles.item, !n.readAt && styles.unread].filter(Boolean).join(' ')}>
              <span className={[styles.icon, n.kind === 'overdue' && styles.iconDanger].filter(Boolean).join(' ')} aria-hidden="true">
                <Icon name={KIND_ICON[n.kind]} size={18} />
              </span>
              <div className={styles.body}>
                <p className={styles.title}>
                  {!n.readAt && <span className="visually-hidden">未読: </span>}
                  {NOTIFICATION[n.kind]}
                  {n.actorName && <span className="muted">（{n.actorName}）</span>}
                </p>
                <p className={styles.target}>
                  {n.teamName && <span className="muted">{n.teamName}</span>}
                  {n.taskId &&
                    (n.taskVisible ? (
                      <Button
                        variant="link"
                        onClick={() => {
                          if (!n.readAt) read.mutate(n.id);
                          open(n.taskId!);
                        }}
                      >
                        {n.taskTitle}
                      </Button>
                    ) : (
                      <span className="muted">（このタスクは閲覧できません）</span>
                    ))}
                </p>
              </div>
              <div className={styles.side}>
                <span className="muted small">{formatDateTime(n.createdAt)}</span>
                {!n.readAt && (
                  <Button size="small" variant="ghost" onClick={() => read.mutate(n.id)}>
                    既読にする
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
