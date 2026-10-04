import { Link } from 'react-router';
import { messageText } from '../lib/messages';

export function NotFoundPage() {
  return (
    <div className="page">
      <h1>ページが見つかりません</h1>
      <p>{messageText('MSG-CMN-404')}</p>
      <p>
        <Link to="/">ホームへ戻る</Link>
      </p>
    </div>
  );
}
