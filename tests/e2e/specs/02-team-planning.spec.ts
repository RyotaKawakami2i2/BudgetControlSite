import { expect, test } from '@playwright/test';
import { USERS, api, expectToast, login, unique } from './support';

/** E2E-02: リーダーがチームを作り、メンバーを追加し、タスクを計画して割り振る。 */
test('リーダーがチームを作ってメンバーを加え、タスクを割り振るとメンバーに通知が届く', async ({ browser, page }) => {
  const teamName = `E2E チーム ${unique()}`;
  const taskTitle = `E2E 計画のタスク ${unique()}`;

  await login(page, USERS.leader);
  await page.goto('/app/teams');
  await page.getByRole('button', { name: '新しいチーム（プロジェクト）を作る' }).click();
  await page.fill('#team-name', teamName);
  await page.getByRole('dialog').getByRole('button', { name: '作る' }).click();
  await expect(page.getByRole('heading', { name: teamName })).toBeVisible();

  // メンバーを追加する
  await page.getByLabel('追加する利用者を検索').fill('鈴木');
  await page.getByRole('listitem').filter({ hasText: USERS.member }).getByRole('button', { name: '選ぶ' }).click();
  await expectToast(page, 'さんを追加しました。');

  // ガントからタスクを登録し、メンバーに割り振る
  await page.getByRole('link', { name: 'ガントを開く' }).click();
  await page.getByRole('button', { name: 'タスクを追加' }).click();
  await page.fill('#tf-title', taskTitle);
  await page.selectOption('#tf-assignee', { label: '鈴木 次郎' });
  const start = new Date(Date.now() + 7 * 864e5).toISOString().slice(0, 10);
  const end = new Date(Date.now() + 11 * 864e5).toISOString().slice(0, 10);
  await page.fill('#tf-start', start);
  await page.fill('#tf-end', end);
  await page.fill('#tf-minutes', '16');
  await page.getByRole('button', { name: '保存する' }).click();
  await expect(page.getByRole('complementary', { name: 'タスク詳細' }).getByRole('heading', { name: taskTitle })).toBeVisible();

  // メンバーには通知が届き、マイタスクに表示される
  const context = await browser.newContext();
  const member = await context.newPage();
  await login(member, USERS.member);
  const notifications = await api<{ items: { kind: string; taskTitle: string | null }[] }>(member, 'GET', '/api/v1/notifications');
  expect(notifications.json.items.some((n) => n.kind === 'task_assigned' && n.taskTitle === taskTitle)).toBe(true);
  await member.goto('/app/my-tasks');
  await expect(member.getByRole('button', { name: taskTitle })).toBeVisible();
  await context.close();
});
