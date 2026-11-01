import type {
  Bootstrap,
  BoardResult,
  DashboardLayout,
  PulseItem,
  SaveResponse,
  TemplateSaveInput,
  TemplateSaveResponse,
  TemplateTargets,
  WidgetDataResult,
} from './types';
import { adpWindow } from './types';

// MVC endpoints on DashboardController (same origin, cookie auth). Writes carry the page's anti-forgery token in the
// RequestVerificationToken header; widget reads carry the ADP grant only when the page is revealed (plan §9A.3).
const BASE = '/User/Dashboard';

export class DashboardHttpError extends Error {
  public readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'DashboardHttpError';
    this.status = status;
  }
}

function verificationToken(): string {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? '';
}

function headers(options: { json?: boolean; antiForgery?: boolean; grant?: boolean }): Headers {
  const result = new Headers({ Accept: 'application/json' });
  if (options.json) {
    result.set('Content-Type', 'application/json');
  }
  if (options.antiForgery) {
    const token = verificationToken();
    if (token) {
      result.set('RequestVerificationToken', token);
    }
  }
  if (options.grant) {
    // The token is written into these headers by the reveal module and never handed back to this code.
    adpWindow().resgridAdpReveal?.applyGrantHeader?.(result);
  }
  return result;
}

async function send<T>(url: string, init: RequestInit, acceptStatuses: readonly number[] = []): Promise<T> {
  const response = await fetch(url, { credentials: 'same-origin', ...init });
  if (!response.ok && !acceptStatuses.includes(response.status)) {
    throw new DashboardHttpError(response.status, `${response.status} ${response.statusText}`);
  }

  const text = await response.text();
  if (!text) {
    return {} as T;
  }

  try {
    return JSON.parse(text) as T;
  } catch {
    // An HTML page (a sign-in redirect, an error page) instead of JSON.
    throw new DashboardHttpError(response.status || 500, 'Unexpected response');
  }
}

function query(params: Record<string, string | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      search.set(key, value);
    }
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

function post<T>(path: string, body: unknown, options: { signal?: AbortSignal; grant?: boolean; acceptStatuses?: number[] } = {}): Promise<T> {
  return send<T>(
    path.startsWith('/') ? path : `${BASE}/${path}`,
    {
      method: 'POST',
      signal: options.signal,
      headers: headers({ json: true, antiForgery: true, grant: options.grant }),
      body: JSON.stringify(body ?? {}),
    },
    options.acceptStatuses,
  );
}

type FieldValue = string | number | boolean;

export interface SubmitOptions {
  /** Appended to the URL's query string (actions that bind from the query, e.g. ?actionType=2). */
  query?: Record<string, FieldValue>;
  /** Sent as application/x-www-form-urlencoded (actions that bind form fields). */
  form?: Record<string, FieldValue>;
  /** Sent as a JSON body instead of `form`. */
  json?: unknown;
  /** Adds X-Requested-With: XMLHttpRequest (actions that answer AJAX callers with JSON). */
  ajax?: boolean;
  method?: 'POST' | 'DELETE';
}

export interface SubmitResult {
  /** 2xx, 3xx, or a redirect the browser did not follow (status 0). */
  ok: boolean;
  status: number;
  /** The parsed body, only when the response says it is JSON. */
  json?: unknown;
}

export interface PreviewRequest {
  Key: string;
  Size: string;
  Settings: Record<string, unknown>;
}

export const dashboardApi = {
  bootstrap(params: { board?: string | null; templateId?: string | null }, signal?: AbortSignal): Promise<Bootstrap> {
    return send<Bootstrap>(`${BASE}/Bootstrap${query({ board: params.board, templateId: params.templateId })}`, {
      signal,
      headers: headers({}),
    });
  },

  /** A stored instance's data. Widget-level errors are HTTP 200 with Error; a gate failure is a 403 with the same body. */
  widgetData(instance: string, board: string | null, options: { signal?: AbortSignal; grant?: boolean }): Promise<WidgetDataResult> {
    return send<WidgetDataResult>(
      `${BASE}/WidgetData${query({ instance, board })}`,
      { signal: options.signal, headers: headers({ grant: options.grant }) },
      [403],
    );
  },

  /** Data for unsaved settings (edit mode, the gallery, the template editor). */
  widgetPreview(body: PreviewRequest, options: { signal?: AbortSignal; grant?: boolean }): Promise<WidgetDataResult> {
    return post<WidgetDataResult>('WidgetPreview', body, { ...options, acceptStatuses: [403] });
  },

  saveLayout(input: { BoardId: string | null; RowVersion: number; Name: string | null; Layout: DashboardLayout }): Promise<SaveResponse> {
    return post<SaveResponse>('SaveLayout', input);
  },

  resetLayout(boardId: string | null): Promise<SaveResponse> {
    return post<SaveResponse>('ResetLayout', { BoardId: boardId });
  },

  addBoard(name: string): Promise<SaveResponse> {
    return post<SaveResponse>('AddBoard', { Name: name });
  },

  deleteBoard(boardId: string): Promise<{ Deleted: boolean }> {
    return post<{ Deleted: boolean }>('DeleteBoard', { BoardId: boardId });
  },

  setClassic(useClassic: boolean): Promise<{ Redirect?: string | null }> {
    return post<{ Redirect?: string | null }>('SetClassic', { UseClassic: useClassic });
  },

  pulse(signal?: AbortSignal): Promise<PulseItem[]> {
    return send<PulseItem[]>(`${BASE}/Pulse`, { signal, headers: headers({}) });
  },

  templateTargets(): Promise<TemplateTargets> {
    return send<TemplateTargets>(`${BASE}/TemplateTargets`, { headers: headers({}) });
  },

  saveTemplate(input: TemplateSaveInput): Promise<TemplateSaveResponse> {
    return post<TemplateSaveResponse>('SaveTemplate', input);
  },

  /**
   * ctx.submit for widgets: an existing MVC action that binds from the query string or form and answers with 204, a
   * redirect to HTML, or JSON. Same origin only, anti-forgery header always, redirects are not followed. Never throws
   * on the response body; a network failure resolves { ok: false, status: 0 }.
   */
  async submit(url: string, options: SubmitOptions = {}): Promise<SubmitResult> {
    let target: URL;
    try {
      target = new URL(url, window.location.origin);
    } catch {
      throw new Error('Dashboard widgets may only submit to same-origin paths.');
    }
    if (!url.startsWith('/') || url.startsWith('//') || target.origin !== window.location.origin) {
      throw new Error('Dashboard widgets may only submit to same-origin paths.');
    }
    for (const [key, value] of Object.entries(options.query ?? {})) {
      target.searchParams.set(key, String(value));
    }

    const requestHeaders = headers({ antiForgery: true });
    requestHeaders.set('Accept', 'application/json, text/html;q=0.9, */*;q=0.8');
    if (options.ajax) {
      requestHeaders.set('X-Requested-With', 'XMLHttpRequest');
    }

    let body: string | undefined;
    if (options.json !== undefined) {
      requestHeaders.set('Content-Type', 'application/json');
      body = JSON.stringify(options.json);
    } else if (options.form) {
      const form = new URLSearchParams();
      for (const [key, value] of Object.entries(options.form)) {
        form.append(key, String(value));
      }
      requestHeaders.set('Content-Type', 'application/x-www-form-urlencoded; charset=UTF-8');
      body = form.toString();
    }

    let response: Response;
    try {
      response = await fetch(`${target.pathname}${target.search}`, {
        method: options.method ?? 'POST',
        credentials: 'same-origin',
        redirect: 'manual',
        headers: requestHeaders,
        body,
      });
    } catch {
      return { ok: false, status: 0 };
    }

    const ok = response.type === 'opaqueredirect' || (response.status >= 200 && response.status < 400);
    const result: SubmitResult = { ok, status: response.status };
    if ((response.headers.get('Content-Type') ?? '').toLowerCase().includes('json')) {
      try {
        const text = await response.text();
        result.json = text ? JSON.parse(text) : undefined;
      } catch {
        // A body that claims JSON but is not: report the status only.
      }
    }
    return result;
  },

  /** ctx.post for widgets: a same-origin JSON POST with the anti-forgery header (and the grant when asked). */
  postJson<T>(url: string, body: unknown, grant: boolean): Promise<T> {
    if (!url.startsWith('/') || url.startsWith('//')) {
      return Promise.reject(new Error('Dashboard widgets may only POST to same-origin paths.'));
    }
    return post<T>(url, body, { grant });
  },
};

export type { BoardResult };
