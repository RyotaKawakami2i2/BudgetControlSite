import { defineConfig, devices } from '@playwright/test';

/**
 * 通しのテスト（詳細設計書 13.2）。デモデータ（taskyojitsu-tool seed-demo）を入れたアプリに対して動かす。
 * 場面ごとにデータを変えるため、1 つずつ順に動かす（ファイル名の番号の順）。
 *   E2E_BASE_URL   アプリの URL（既定 http://localhost:5080）
 *   E2E_PASSWORD   デモデータの利用者のパスワード（seed-demo に指定した値）
 *   E2E_MAILPIT_URL 送ったメールを確かめる Mailpit の URL（既定 http://mailpit:8025）
 */
export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5080',
    locale: 'ja-JP',
    timezoneId: 'Asia/Tokyo',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
  ],
});
