import { expect, test, type Browser, type Page } from '@playwright/test';
import { USERS, login, totp, unique, waitForMail } from './support';

/** E2E-01: 招待から最初のログインまで。 */

async function invite(browser: Browser, label: string): Promise<{ email: string; link: string }> {
  const email = `e2e-${label}-${unique()}@example.com`;
  const context = await browser.newContext();
  const admin = await context.newPage();
  await login(admin, USERS.admin);
  await admin.goto('/app/admin/users');
  await admin.getByRole('button', { name: '招待する' }).first().click();
  await admin.fill('#invite-email', email);
  await admin.fill('#invite-name', `通しのテスト ${label}`);
  await admin.getByRole('dialog').getByRole('button', { name: '招待する' }).click();
  await expect(admin.getByText(`${email} に招待のメールを送りました`)).toBeVisible();
  await context.close();

  const mail = await waitForMail(email, '招待');
  const link = /https?:\/\/\S+\/account\/accept-invitation\?token=[A-Za-z0-9_-]+/.exec(mail.text)?.[0];
  if (!link) throw new Error('招待のメールにリンクがありません。');
  // メールのリンクのホストは App:BaseUrl。テストではテスト先の URL に読み替える
  return { email, link: new URL(link).pathname + new URL(link).search };
}

async function acceptInvitation(page: Page, link: string, newPassword: string): Promise<void> {
  await page.goto(link);
  // トークンは Cookie に移され、URL からは消える（詳細設計書 6.8）
  await expect(page).toHaveURL(/\/account\/accept-invitation$/);
  await page.fill('#password', newPassword);
  await page.fill('#confirm', newPassword);
  await page.getByRole('button', { name: '設定して始める' }).click();
  await expect(page).toHaveURL(/\/account\/setup-mfa/);
}

test('パスワードを設定し、多要素認証の設定を後回しにして使い始める', async ({ browser, page }) => {
  const { link } = await invite(browser, 'later');

  await acceptInvitation(page, link, `e2e-later-${unique()}-river-cloud`);
  await page.getByRole('link', { name: '後で設定する' }).click();

  await expect(page).toHaveURL(/\/app/);
  await expect(page.getByText('多要素認証が設定されていません')).toBeVisible();

  // 使い終わった招待のリンクは、もう使えない
  await page.context().clearCookies();
  await page.goto(link);
  await expect(page.getByRole('button', { name: '設定して始める' })).toHaveCount(0);
});

test('パスワードを設定し、続けて認証アプリを登録するとリカバリーコードが表示される', async ({ browser, page }) => {
  const { email, link } = await invite(browser, 'totp');
  const newPassword = `e2e-totp-${unique()}-river-cloud`;

  await acceptInvitation(page, link, newPassword);
  await page.getByRole('link', { name: '認証アプリを設定する' }).click();
  const key = (await page.locator('kbd').first().innerText()).replace(/\s/g, '');
  await page.fill('#code', totp(key));
  await page.getByRole('button', { name: '確定する' }).click();

  const codes = page.locator('ul.recovery-codes li');
  await expect(codes.first()).toBeVisible();
  expect(await codes.count()).toBeGreaterThanOrEqual(8);

  // 次のログインからは認証コードを求められる
  await page.context().clearCookies();
  await page.goto('/account/login');
  await page.fill('#email', email);
  await page.fill('#password', newPassword);
  await page.getByRole('button', { name: 'ログイン', exact: true }).click();
  await expect(page).toHaveURL(/\/account\/login-2fa/);
});
