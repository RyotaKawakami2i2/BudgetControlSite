/**
 * API の呼び出し（詳細設計書 7.2）。fetch を包んだこの関数だけを使う。
 * - credentials は same-origin（セッションの Cookie）。認証用のトークンはブラウザの保存領域に置かない
 * - 状態を変える要求には、Cookie の CSRF のトークンを X-XSRF-TOKEN ヘッダーに入れ、Content-Type: application/json を付ける
 * - 応答のコードに応じて、ログインや再認証の画面へ移る（詳細設計書 5.2）
 */

export interface ProblemDetails {
  status: number;
  code: string;
  title?: string;
  errors?: Record<string, string[]>;
  latest?: unknown;
  traceId?: string;
}

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly errors: Record<string, string[]>;
  readonly latest: unknown;
  readonly traceId: string | undefined;

  constructor(problem: ProblemDetails) {
    super(problem.title ?? problem.code);
    this.status = problem.status;
    this.code = problem.code;
    this.errors = problem.errors ?? {};
    this.latest = problem.latest;
    this.traceId = problem.traceId;
  }

  /** 項目ごとのメッセージ ID（項目名の空のものは、画面全体へのメッセージ）。 */
  messageIds(field: string): string[] {
    return this.errors[field] ?? [];
  }
}

const XSRF_COOKIES = ['__Host-tyj.xsrf', 'tyj.xsrf'];

function readXsrfToken(): string | null {
  const cookies = document.cookie.split(';').map((c) => c.trim());
  for (const name of XSRF_COOKIES) {
    const found = cookies.find((c) => c.startsWith(`${name}=`));
    if (found) return decodeURIComponent(found.slice(name.length + 1));
  }
  return null;
}

function currentPath(): string {
  return window.location.pathname + window.location.search;
}

export function goToLogin(): void {
  window.location.assign(`/account/login?returnUrl=${encodeURIComponent(currentPath())}`);
}

export function goToReauth(): void {
  window.location.assign(`/account/reauthenticate?returnUrl=${encodeURIComponent(currentPath())}`);
}

export async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (method !== 'GET') {
    headers['Content-Type'] = 'application/json';
    const token = readXsrfToken();
    if (token) headers['X-XSRF-TOKEN'] = token;
  }

  const response = await fetch(`/api/v1${path}`, {
    method,
    headers,
    credentials: 'same-origin',
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.status === 204) {
    return undefined as T;
  }

  const isJson = (response.headers.get('content-type') ?? '').includes('json');
  const payload: unknown = isJson ? await response.json() : null;
  if (response.ok) {
    return payload as T;
  }

  const problem: ProblemDetails = {
    status: response.status,
    code: 'server.error',
    ...(typeof payload === 'object' && payload !== null ? (payload as Partial<ProblemDetails>) : {}),
  };

  if (response.status === 401) {
    goToLogin();
  } else if (problem.code === 'auth.reauth_required') {
    goToReauth();
  }

  throw new ApiError(problem);
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
  delete: <T = void>(path: string) => request<T>('DELETE', path),
};

/** ログアウト（サーバー側のセッションを破棄する）。 */
export async function logout(all = false): Promise<void> {
  const token = readXsrfToken();
  await fetch(all ? '/account/logout-all' : '/account/logout', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { Accept: 'application/json', ...(token ? { 'X-XSRF-TOKEN': token } : {}) },
  });
  window.location.assign('/account/login?loggedOut=1');
}

/** クエリ文字列を作る（空の値は省く）。 */
export function query(params: Record<string, string | number | boolean | null | undefined | string[]>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue;
    search.set(key, Array.isArray(value) ? value.join(',') : String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}
