import { expect, test } from '@playwright/test';
import { USERS, waitForMail } from './support';

/**
 * E2E-07: 5 回続けて失敗するとロックされ、メールが届く。
 * ロックした利用者は 15 分ログインできないため、ほかの場面では使わない利用者で、最後に動かす。
 * E2E-08（30 分でセッションが切れる）と E2E-09（管理の操作の前の再認証）は、時刻を進める必要があるため、
 * 結合テスト（tests/TaskYojitsu.Web.Tests の AuthTests）で、DB の時刻をずらして確かめる。
 */
test('パスワードを 5 回続けて間違えるとロックされ、本人にメールが届く', async ({ page }) => {
  for (let i = 0; i < 5; i++) {
    await page.goto('/account/login');
    await page.fill('#email', USERS.lockTarget);
    await page.fill('#password', `wrong-password-${i}-0123456789`);
    await page.getByRole('button', { name: 'ログイン', exact: true }).click();
    if (page.url().includes('/account/lockout')) break;
    await expect(page.getByText('メールアドレス、パスワード、認証コードのいずれかが正しくありません。')).toBeVisible();
  }

  await expect(page).toHaveURL(/\/account\/lockout/);
  const mail = await waitForMail(USERS.lockTarget, 'ロック');
  expect(mail.text).toContain('ロック');
});
