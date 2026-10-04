import { expect, test } from '@playwright/test';
import { SALES_TEAM, USERS, api, expectToast, findTask, login, teamId, unique } from './support';

/** E2E-03: メンバーが自分のタスクを追加し、作業実績・進捗・完了を入力する。 */
test('メンバーが自分のタスクを追加して作業実績を記録し、完了にする', async ({ page }) => {
  const title = `E2E 自分のタスク ${unique()}`;
  await login(page, USERS.member);
  await page.goto('/app/my-tasks');
  await page.selectOption('select[aria-label^="チーム"]', { label: SALES_TEAM });

  await page.getByRole('button', { name: '自分のタスクを追加' }).click();
  await page.fill('#tf-title', title);
  const today = new Date().toLocaleDateString('sv-SE', { timeZone: 'Asia/Tokyo' });
  await page.fill('#tf-start', today);
  await page.fill('#tf-end', today);
  await page.fill('#tf-minutes', '3');
  await page.getByRole('button', { name: '保存する' }).click();
  await expectToast(page, '保存しました。');

  // 作業実績を記録すると進行中になる
  const row = page.getByRole('row').filter({ hasText: title });
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: '実績を記録' }).click();
  await page.fill('#wl-minutes', '1:30');
  await page.fill('#wl-note', '通しのテストの作業');
  await page.selectOption('#wl-progress', '50');
  await page.getByRole('dialog').getByRole('button', { name: '記録する' }).click();
  await expectToast(page, '保存しました。');
  await expect(row.getByText('進行中')).toBeVisible();

  // 完了にする（実績終了日は今日）
  await row.getByRole('button', { name: '完了' }).click();
  await page.fill('#cp-end', today);
  await page.fill('#cp-note', '予定どおり終わった');
  await page.getByRole('dialog').getByRole('button', { name: '完了にする' }).click();
  await expectToast(page, '保存しました。');

  const task = await findTask(page, await teamId(page, SALES_TEAM), title);
  const detail = await api<{ task: { status: string; progress: number; actualMinutes: number; actualStart: string; actualEnd: string } }>(
    page,
    'GET',
    `/api/v1/tasks/${task.id}`,
  );
  expect(detail.json.task).toMatchObject({ status: 'done', progress: 100, actualMinutes: 90, actualStart: today, actualEnd: today });
});
