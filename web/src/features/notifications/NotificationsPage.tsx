import { useNotificationMutations, useNotifications } from '../../api/hooks';
import { Button, ErrorBox, Loading } from '../../components/ui';
import { useTaskPanel } from '../../layout/context';
import { formatDateTime } from '../../lib/dates';
import { NOTIFICATION } from '../../lib/labels';

/** 通知（SC-13。FR-NTF-01、02）。タスクの名前は、いま閲覧できる場合だけ表示する。 */
export function NotificationsPage() {
  const { data, error, isLoading } = useNotifications();
  const { read, readAll } = useNotificationMutations();
  const { open } = useTaskPanel();

  return (
    <div className="page">
      <div className="page-header">
        <h1>通知</h1>
        <Button onClick={() => readAll.mutate()} disabled={readAll.isPending}>
          すべて既読にする
        </Button>
      </div>
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && data.items.length === 0 && <p className="empty">通知はありません。</p>}
      {data && data.items.length > 0 && (
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">日時</th>
              <th scope="col">内容</th>
              <th scope="col">対象</th>
              <th scope="col">
                <span className="visually-hidden">操作</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((n) => (
              <tr key={n.id}>
                <td className="nowrap">
                  {!n.readAt && <strong aria-label="未読">● </strong>}
                  {formatDateTime(n.createdAt)}
                </td>
                <td>
                  {NOTIFICATION[n.kind]}
                  {n.actorName && <span className="muted">（{n.actorName}）</span>}
                </td>
                <td>
                  {n.teamName && <span className="muted">{n.teamName} / </span>}
                  {n.taskId &&
                    (n.taskVisible ? (
                      <Button
                        variant="ghost"
                        size="small"
                        onClick={() => {
                          if (!n.readAt) read.mutate(n.id);
                          open(n.taskId!);
                        }}
                      >
                        {n.taskTitle}
                      </Button>
                    ) : (
                      <span className="muted">（閲覧できません）</span>
                    ))}
                </td>
                <td>
                  {!n.readAt && (
                    <Button size="small" variant="ghost" onClick={() => read.mutate(n.id)}>
                      既読にする
                    </Button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
