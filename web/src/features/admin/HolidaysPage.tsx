import { useState } from 'react';
import { useHolidayMutations, useHolidays } from '../../api/hooks';
import { Button, ErrorBox, Loading, describeError, useToast } from '../../components/ui';
import { formatDate, todayIso } from '../../lib/dates';

/** 祝日の管理（SC-20。FR-ADM-07）。内閣府の祝日一覧の取り込みは、運用コマンド import-holidays で行う。 */
export function HolidaysPage() {
  const [year, setYear] = useState(() => Number(todayIso().slice(0, 4)));
  const { data, error, isLoading } = useHolidays(year);
  const { add, remove } = useHolidayMutations();
  const toast = useToast();
  const [date, setDate] = useState('');
  const [name, setName] = useState('');

  return (
    <div className="page">
      <div className="page-header">
        <h1>祝日</h1>
        <div className="row">
          <Button size="small" onClick={() => setYear(year - 1)}>
            ◀ {year - 1}年
          </Button>
          <strong>{year}年</strong>
          <Button size="small" onClick={() => setYear(year + 1)}>
            {year + 1}年 ▶
          </Button>
        </div>
      </div>
      <p className="muted">祝日は稼働日の計算（期待進捗、予定工数の按分）とガントの網掛けに使います。内閣府が公表する一覧は、運用コマンド import-holidays で取り込めます。</p>
      <form
        className="row"
        onSubmit={(e) => {
          e.preventDefault();
          add.mutate(
            { date, name: name.trim() },
            { onSuccess: () => (setDate(''), setName(''), toast.show('success', '祝日を登録しました。')), onError: (err) => toast.show('error', describeError(err)) },
          );
        }}
      >
        <input type="date" aria-label="日付" value={date} onChange={(e) => setDate(e.target.value)} required />
        <input type="text" aria-label="名前" placeholder="名前（例: 創立記念日）" value={name} maxLength={50} onChange={(e) => setName(e.target.value)} required />
        <Button type="submit" variant="primary" disabled={!date || !name.trim()}>
          登録する
        </Button>
      </form>
      {isLoading && <Loading />}
      {error && <ErrorBox error={error} />}
      {data && data.length === 0 && <p className="empty">この年の祝日は登録されていません。</p>}
      {data && data.length > 0 && (
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">日付</th>
              <th scope="col">名前</th>
              <th scope="col">
                <span className="visually-hidden">操作</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {data.map((h) => (
              <tr key={h.date}>
                <td>{formatDate(h.date)}</td>
                <td>{h.name}</td>
                <td>
                  <Button size="small" variant="ghost" onClick={() => remove.mutate(h.date, { onError: (e) => toast.show('error', describeError(e)) })}>
                    削除
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
