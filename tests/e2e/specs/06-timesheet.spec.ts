import { expect, test } from '@playwright/test';
import { USERS, expectToast, login } from './support';

/** E2E-06: 週の入力表でまとめて入力する。 */
test('週の入力表で複数の日に作業時間を入れ、まとめて保存できる', async ({ page }) => {
  await login(page, USERS.member2);
  await page.goto('/app/timesheet');
  await page.getByRole('button', { name: '◀ 前の週' }).click();

  // 入力できるマス（タスク詳細から記録していない日）を 2 つ選ぶ
  const inputs = page.locator('table input[aria-label$="の作業時間"]');
  await expect(inputs.first()).toBeVisible();
  const first = inputs.nth(0);
  const second = inputs.nth(1);
  // 何度動かしても値が変わるよう、今の値と違う値を入れる
  const next = (current: string) => (current === '1:30' ? '2:15' : '1:30');
  const firstValue = next(await first.inputValue());
  const secondValue = next(await second.inputValue());
  const firstLabel = (await first.getAttribute('aria-label'))!;
  const secondLabel = (await second.getAttribute('aria-label'))!;
  await first.fill(firstValue);
  await second.fill(secondValue);
  await page.getByRole('button', { name: 'まとめて保存する' }).click();
  await expectToast(page, '保存しました');

  await page.reload();
  await page.getByRole('button', { name: '◀ 前の週' }).click();
  await expect(page.getByLabel(firstLabel, { exact: true })).toHaveValue(firstValue);
  await expect(page.getByLabel(secondLabel, { exact: true })).toHaveValue(secondValue);
});
