import { expect, test, type Page } from '@playwright/test';
import { SALES_TEAM, USERS, api, expectToast, findTask, login, teamId } from './support';

/** E2E-05: ドラッグで日程を変え、2 人が同時に変えたときに競合を知らせる。 */
const TITLE = 'ガント実装';
const DAY_WIDTH = 32; // 目盛りが「日」のときの 1 日の幅

/** バーをつかんで動かし始める。返した関数でボタンを離す。 */
async function startDrag(page: Page, days: number): Promise<() => Promise<void>> {
  const bar = page.locator(`g[aria-label^="${TITLE}"]`).first();
  await bar.scrollIntoViewIfNeeded();
  const box = await bar.boundingBox();
  if (!box) throw new Error('バーが表示されていません。');
  // バーが画面の右にはみ出していることがあるため、左の端（大きさを変えるつまみ）より少し内側をつかむ
  const y = box.y + box.height / 2;
  const x = box.x + Math.min(box.width / 2, 24);
  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x + (DAY_WIDTH * days) / 2, y, { steps: 4 });
  await page.mouse.move(x + DAY_WIDTH * days, y, { steps: 4 });
  return () => page.mouse.up();
}

test('バーをドラッグすると日程が変わり、ほかの人が先に変えていたら知らせる', async ({ page }) => {
  await login(page, USERS.leader);
  const sales = await teamId(page, SALES_TEAM);
  const original = await findTask(page, sales, TITLE);

  try {
    await page.goto(`/app/gantt?teams=${sales}`);
    await expect(page.getByRole('treegrid')).toBeVisible();

    const drop = await startDrag(page, 2);
    await drop();
    await expectToast(page, '日程を変更しました');
    await expect.poll(async () => (await findTask(page, sales, TITLE)).plannedStart).not.toBe(original.plannedStart);

    // ドラッグしている間に、ほかの人（別の画面）が同じタスクを先に変える
    const dropLater = await startDrag(page, 1);
    const latest = await findTask(page, sales, TITLE);
    const changed = await api(page, 'PATCH', `/api/v1/tasks/${latest.id}`, { version: latest.version, plannedMinutes: latest.plannedMinutes === 3540 ? 3480 : 3540 });
    expect(changed.status).toBe(200);
    await dropLater();

    // 画面は古い版のまま保存しようとするため、競合を知らせて最新の内容を読み込み直す
    await expectToast(page, 'ほかの人が先に更新しました');
  } finally {
    // 何度動かしても同じ結果になるよう、日程と工数を元に戻す
    const current = await findTask(page, sales, TITLE);
    await api(page, 'PATCH', `/api/v1/tasks/${current.id}`, {
      version: current.version,
      plannedStart: original.plannedStart,
      plannedEnd: original.plannedEnd,
      plannedMinutes: 3600,
    });
  }
});
