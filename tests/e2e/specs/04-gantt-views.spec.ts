import { expect, test } from '@playwright/test';
import { SALES_TEAM, USERS, expectToast, login, teamId, unique } from './support';

/** E2E-04: ガントの絞り込み、まとめ方の切り替え、URL での共有、ビューの保存。 */
test('絞り込みとまとめ方は URL に残り、同じ URL を開くと同じ表示になる。ビューとして保存できる', async ({ browser, page }) => {
  await login(page, USERS.leader);
  const sales = await teamId(page, SALES_TEAM);
  await page.goto(`/app/gantt?teams=${sales}`);
  await expect(page.getByRole('treegrid')).toBeVisible();

  await page.getByRole('button', { name: '遅れ', exact: true }).click();
  await page.selectOption('#gantt-group', 'assignee');
  await expect(page).toHaveURL(/group=assignee/);
  const shared = page.url();

  // 同じ URL をほかの人（同じチームのメンバー）が開く
  const context = await browser.newContext();
  const other = await context.newPage();
  await login(other, USERS.member);
  await other.goto(shared);
  await expect(other.getByRole('button', { name: '遅れ', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(other.locator('#gantt-group')).toHaveValue('assignee');
  await context.close();

  // ビューとして保存し、選択肢に出ることを確かめる
  const viewName = `E2E ビュー ${unique()}`;
  await page.getByRole('button', { name: '保存', exact: true }).click();
  await page.fill('#view-name', viewName);
  await page.getByRole('dialog').getByRole('button', { name: '保存する' }).click();
  await expectToast(page, `ビュー「${viewName}」を保存しました。`);
  await expect(page.locator('#gantt-view option', { hasText: viewName })).toHaveCount(1);
});
