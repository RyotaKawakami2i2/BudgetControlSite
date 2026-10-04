import { createHmac } from 'node:crypto';
import { expect, type BrowserContext, type Page } from '@playwright/test';

/** デモデータの利用者（src/TaskYojitsu.Tool/DemoSeeder.cs）。 */
export const USERS = {
  admin: 'admin@example.com',
  /** 販売管理更改と社内ポータル改善のリーダー */
  leader: 'yamada@example.com',
  /** 販売管理更改のメンバー */
  member: 'suzuki@example.com',
  /** 販売管理更改のメンバー */
  member2: 'sato@example.com',
  /** 社内ポータル改善のメンバー（ロックの場面で使う。ほかの場面では使わない） */
  lockTarget: 'tanaka@example.com',
} as const;

export const SALES_TEAM = '販売管理更改';

export function password(): string {
  const value = process.env.E2E_PASSWORD;
  if (!value) throw new Error('環境変数 E2E_PASSWORD に、デモデータの利用者のパスワードを設定してください。');
  return value;
}

const mailpit = process.env.E2E_MAILPIT_URL ?? 'http://mailpit:8025';

/** 毎回違う名前を付ける（同じ DB で何度動かしても重ならないように）。 */
export const unique = () => `${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;

// ログインした Cookie を利用者ごとに覚え、次の場面では使い回す（ログインの回数を減らす）
const sessions = new Map<string, Awaited<ReturnType<BrowserContext['cookies']>>>();

export async function login(page: Page, email: string, pass = password()): Promise<void> {
  const saved = sessions.get(email);
  if (saved) {
    await page.context().addCookies(saved);
    await page.goto('/app');
    if (!page.url().includes('/account/login')) return;
    sessions.delete(email);
  }

  await page.goto('/account/login');
  await page.fill('#email', email);
  await page.fill('#password', pass);
  await page.getByRole('button', { name: 'ログイン', exact: true }).click();
  await page.waitForURL(/\/app(\/|$|\?)/);
  sessions.set(email, await page.context().cookies());
}

/** 画面と同じ方法（CSRF のトークン付き）で API を呼ぶ。準備と結果の確認に使う。 */
export async function api<T = unknown>(page: Page, method: string, path: string, body?: unknown): Promise<{ status: number; json: T }> {
  return page.evaluate(
    async ({ method, path, body }) => {
      const token = document.cookie
        .split('; ')
        .find((c) => c.startsWith('__Host-tyj.xsrf=') || c.startsWith('tyj.xsrf='))
        ?.split('=')[1];
      const res = await fetch(path, {
        method,
        headers: method === 'GET' ? {} : { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': decodeURIComponent(token ?? '') },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      const text = await res.text();
      return { status: res.status, json: (text ? JSON.parse(text) : null) as never };
    },
    { method, path, body },
  );
}

export async function teamId(page: Page, name: string): Promise<string> {
  const me = await api<{ teams: { id: string; name: string }[] }>(page, 'GET', '/api/v1/me');
  const team = me.json.teams.find((t) => t.name === name);
  if (!team) throw new Error(`チーム ${name} がありません。`);
  return team.id;
}

export async function findTask(page: Page, team: string, title: string) {
  const today = new Date();
  const from = new Date(today.getTime() - 120 * 864e5).toISOString().slice(0, 10);
  const to = new Date(today.getTime() + 240 * 864e5).toISOString().slice(0, 10);
  const gantt = await api<{
    tasks: { id: string; title: string; version: number; plannedStart: string | null; plannedEnd: string | null; plannedMinutes: number | null }[];
  }>(
    page,
    'GET',
    `/api/v1/gantt?teamIds=${team}&from=${from}&to=${to}`,
  );
  const task = gantt.json.tasks.find((t) => t.title === title);
  if (!task) throw new Error(`タスク ${title} がありません。`);
  return task;
}

/** Mailpit に届いたメールを待つ（新しい順）。 */
export async function waitForMail(to: string, subjectIncludes: string): Promise<{ subject: string; text: string }> {
  for (let i = 0; i < 30; i++) {
    const res = await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`);
    const data = (await res.json()) as { messages: { ID: string; Subject: string }[] };
    const found = data.messages.find((m) => m.Subject.includes(subjectIncludes));
    if (found) {
      const message = (await (await fetch(`${mailpit}/api/v1/message/${found.ID}`)).json()) as { Subject: string; Text: string };
      return { subject: message.Subject, text: message.Text };
    }

    await new Promise((r) => setTimeout(r, 500));
  }

  throw new Error(`${to} 宛ての「${subjectIncludes}」のメールが届きません。`);
}

/** 認証アプリと同じ方法で、6 桁の認証コードを作る（RFC 6238。30 秒ごと、HMAC-SHA1）。 */
export function totp(base32Key: string, time = Date.now()): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const clean = base32Key.replace(/[\s=]/g, '').toUpperCase();
  let bits = '';
  for (const c of clean) bits += alphabet.indexOf(c).toString(2).padStart(5, '0');
  const bytes = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(time / 30_000)));
  const hmac = createHmac('sha1', bytes).update(counter).digest();
  const offset = hmac[hmac.length - 1]! & 0x0f;
  const code = (hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000;
  return code.toString().padStart(6, '0');
}

/** 画面の上に出るお知らせ（トースト。成功は status、失敗は alert）。 */
export async function expectToast(page: Page, text: string | RegExp): Promise<void> {
  await expect(page.locator('[role="status"], [role="alert"]').filter({ hasText: text }).first()).toBeVisible();
}
