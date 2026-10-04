import { Link } from 'react-router';
import { EmptyState, ui } from '../components/ui';
import { messageText } from '../lib/messages';

export function NotFoundPage() {
  return (
    <div className="page">
      <EmptyState
        icon="info"
        title="ページが見つかりません"
        description={messageText('MSG-CMN-404')}
        action={
          <Link to="/" className={ui.button}>
            ホームへ戻る
          </Link>
        }
      />
    </div>
  );
}
