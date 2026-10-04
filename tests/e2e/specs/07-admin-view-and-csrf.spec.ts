import { expect, test } from '@playwright/test';
import { SALES_TEAM, USERS, login, teamId } from './support';

/** E2E-11: 管理者が、所属していないチームのガントとタスクを閲覧でき、変更はできない（監査ログに記録される）。 */
test('管理者は所属していないチームを閲覧だけでき、閲覧したことが監査ログに残る', async ({ browser, page }) => {
  // チームの ID はリーダーの画面から調べる
  const context = await browser.newContext();
  const leader = await context.newPage();
  await login(leader, USERS.leader);
  const sales = await teamId(leader, SALES_TEAM);
  await context.close();

  await login(page, USERS.admin);
  await page.goto(`/app/gantt?teams=${sales}`);
  await expect(page.getByText('閲覧のみ（管理者）')).toBeVisible();
  await expect(page.getByRole('button', { name: 'タスクを追加' })).toHaveCount(0);

  await page.getByRole('treegrid').getByRole('button', { name: 'API 設計' }).click();
  const panel = page.getByRole('complementary', { name: 'タスク詳細' });
  await expect(panel.getByRole('heading', { name: 'API 設計' })).toBeVisible();
  await expect(panel.getByRole('button', { name: '実績を記録' })).toHaveCount(0);
  await expect(panel.getByRole('button', { name: '編集' })).toHaveCount(0);

  await page.goto('/app/admin/audit-logs');
  const search = page.locator('form').filter({ has: page.getByLabel('操作の種類') });
  await search.getByLabel('操作の種類').fill('admin.team_viewed');
  await search.getByRole('button', { name: '検索' }).click();
  await expect(page.getByRole('cell', { name: 'admin.team_viewed' }).first()).toBeVisible();
});

/** E2E-10: CSRF のトークンがない要求や、ほかのオリジンからの要求が拒否される。 */
test('CSRF のトークンがない要求は拒否され、画面からの要求は受け付けられる', async ({ page }) => {
  await login(page, USERS.member);
  await page.goto('/app');

  const statuses = await page.evaluate(async () => {
    const token = document.cookie
      .split('; ')
      .find((c) => c.startsWith('__Host-tyj.xsrf=') || c.startsWith('tyj.xsrf='))
      ?.split('=')[1];
    const send = (headers: Record<string, string>) =>
      fetch('/api/v1/notifications/read-all', { method: 'POST', headers: { 'Content-Type': 'application/json', ...headers } }).then((r) => r.status);
    return {
      withoutToken: await send({}),
      wrongToken: await send({ 'X-XSRF-TOKEN': 'forged' }),
      withToken: await send({ 'X-XSRF-TOKEN': decodeURIComponent(token ?? '') }),
    };
  });

  expect(statuses).toEqual({ withoutToken: 403, wrongToken: 403, withToken: 200 });
});
