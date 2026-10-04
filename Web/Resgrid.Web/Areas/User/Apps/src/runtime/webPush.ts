import { apiFetchJson } from './api';

/**
 * Browser push for Core Web.
 *
 * The page mints an FCM web token against /rg-push-sw.js (which shows every push itself; Firebase is not
 * loaded there) and registers it as a Platform 3 device on the user's Novu subscriber, next to their
 * phones and other browsers. Like the phone apps there is no setting for it here: a browser with
 * notification permission is registered, and which pushes reach it is decided by the profile's push
 * preferences on the server, as for any device. The permission is asked for once, the way the phone
 * app asks at sign-in.
 *
 * Two rules keep a signed-out browser from showing someone's calls:
 *  - signing out takes the token off the channel and deletes it at FCM, so even a failed server call
 *    leaves a dead token behind;
 *  - a different user, or a different active department, on this browser rotates the token on the next
 *    page load for the same reason.
 * Firebase is loaded on demand, so pages pay nothing until there is a token to mint or check.
 */

export interface WebPushBrowserConfig {
  apiKey: string;
  authDomain?: string;
  projectId: string;
  messagingSenderId: string;
  appId: string;
  vapidKey: string;
  serviceWorkerUrl: string;
  userId: string;
  departmentId: string;
}

declare global {
  interface Window {
    rgWebPush?: WebPushBrowserConfig;
  }
}

/** Platforms.Web on the server. */
const WEB_PLATFORM = 3;
const REGISTRATION_KEY = 'rg.webPush.registration';
/** When the permission prompt was last dismissed without an answer. */
const ASKED_KEY = 'rg.webPush.asked';
const DEVICE_KEY = 'rg.webPush.device';
const REREGISTER_AFTER_MS = 24 * 60 * 60 * 1000;
const SIGN_OUT_TIMEOUT_MS = 3000;
/**
 * A prompt dismissed without an answer is not shown again for a week: browsers block a site's notification
 * prompt for a while after a few dismissals, so asking on every page would end with no way to ask at all.
 */
const ASK_AGAIN_AFTER_MS = 7 * 24 * 60 * 60 * 1000;

interface StoredRegistration {
  userId: string;
  departmentId: string;
  token: string;
  registeredAt: number;
}

export function getWebPushConfig(): WebPushBrowserConfig | null {
  const config = window.rgWebPush;
  if (!config || !config.apiKey || !config.projectId || !config.messagingSenderId || !config.appId || !config.vapidKey || !config.userId) {
    return null;
  }

  return config;
}

function readJson<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(key);
    return raw ? (JSON.parse(raw) as T) : null;
  } catch {
    return null;
  }
}

function writeJson(key: string, value: unknown): void {
  try {
    if (value === null) {
      localStorage.removeItem(key);
    } else {
      localStorage.setItem(key, JSON.stringify(value));
    }
  } catch {
    // storage unavailable (private browsing): the registration simply isn't remembered
  }
}

function getStoredRegistration(): StoredRegistration | null {
  return readJson<StoredRegistration>(REGISTRATION_KEY);
}

function getDeviceUuid(): string {
  let id = readJson<string>(DEVICE_KEY);
  if (!id) {
    id = typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    writeJson(DEVICE_KEY, id);
  }

  return id;
}

function browserCanPush(): boolean {
  return (
    typeof window !== 'undefined' &&
    window.isSecureContext &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window
  );
}

async function loadMessaging(config: WebPushBrowserConfig) {
  const [{ initializeApp, getApps }, messaging] = await Promise.all([import('firebase/app'), import('firebase/messaging')]);
  if (!(await messaging.isSupported())) {
    return null;
  }

  const appName = 'rg-web-push';
  const app =
    getApps().find((candidate) => candidate.name === appName) ??
    initializeApp(
      {
        apiKey: config.apiKey,
        authDomain: config.authDomain || undefined,
        projectId: config.projectId,
        messagingSenderId: config.messagingSenderId,
        appId: config.appId,
      },
      appName,
    );

  return { api: messaging, instance: messaging.getMessaging(app) };
}

async function registerServiceWorker(config: WebPushBrowserConfig): Promise<ServiceWorkerRegistration> {
  const registration = await navigator.serviceWorker.register(config.serviceWorkerUrl || '/rg-push-sw.js', { scope: '/' });
  // getToken subscribes through this registration, which needs an active worker.
  if (!registration.active) {
    await navigator.serviceWorker.ready;
  }

  return registration;
}

async function mintToken(config: WebPushBrowserConfig): Promise<string | null> {
  const messaging = await loadMessaging(config);
  if (!messaging) {
    return null;
  }

  const serviceWorkerRegistration = await registerServiceWorker(config);
  const token = await messaging.api.getToken(messaging.instance, { vapidKey: config.vapidKey, serviceWorkerRegistration });
  return token || null;
}

async function findServiceWorker(config: WebPushBrowserConfig): Promise<ServiceWorkerRegistration | null> {
  const scriptPath = new URL(config.serviceWorkerUrl || '/rg-push-sw.js', window.location.origin).pathname;
  const registrations = await navigator.serviceWorker.getRegistrations();
  return (
    registrations.find((registration) => {
      const worker = registration.active ?? registration.waiting ?? registration.installing;
      return !!worker && new URL(worker.scriptURL).pathname === scriptPath;
    }) ?? null
  );
}

/** Kills the token at FCM: every channel still holding it stops delivering to this browser. */
async function deleteToken(config: WebPushBrowserConfig): Promise<void> {
  const registration = await findServiceWorker(config).catch(() => null);
  if (!registration) {
    return;
  }

  try {
    const messaging = await loadMessaging(config);
    if (messaging && Notification.permission === 'granted') {
      // deleteToken acts on the worker getToken last named; without one it would register Firebase's
      // default worker, which this site does not serve. With a token already stored this getToken is
      // a local read.
      await messaging.api.getToken(messaging.instance, { vapidKey: config.vapidKey, serviceWorkerRegistration: registration });
      await messaging.api.deleteToken(messaging.instance);
      return;
    }
  } catch (error) {
    console.warn('Web push: the FCM token could not be deleted', error);
  }

  // Without permission Firebase can't run; dropping the push subscription still leaves FCM nowhere to
  // deliver, and it reports the token unregistered from then on.
  try {
    const subscription = await registration.pushManager.getSubscription();
    await subscription?.unsubscribe();
  } catch {
    // nothing more can be done from this page
  }
}

async function registerToken(config: WebPushBrowserConfig, token: string): Promise<void> {
  // An empty Prefix makes the server use the active department's code, the one pushes are sent under.
  await apiFetchJson('api/v4/Devices/RegisterDevice', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ Platform: WEB_PLATFORM, Token: token, DeviceUuid: getDeviceUuid(), Prefix: '' }),
  });

  writeJson(REGISTRATION_KEY, {
    userId: config.userId,
    departmentId: config.departmentId,
    token,
    registeredAt: Date.now(),
  } satisfies StoredRegistration);
}

async function unregisterToken(token: string, init?: RequestInit): Promise<void> {
  await apiFetchJson('api/v4/Devices/UnRegisterWebPush', {
    ...init,
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ Token: token, Prefix: '' }),
  });
}

function isCurrent(config: WebPushBrowserConfig, stored: StoredRegistration | null): stored is StoredRegistration {
  return !!stored && stored.userId === config.userId && stored.departmentId === config.departmentId;
}

/**
 * Runs on every signed-in page. Keeps a permitted browser's registration fresh (FCM rotates tokens), and
 * rotates the token when this browser's last registration belongs to another user or department.
 */
export async function refreshWebPush(): Promise<void> {
  const config = getWebPushConfig();
  if (!config || !browserCanPush()) {
    return;
  }

  const stored = getStoredRegistration();

  if (stored && !isCurrent(config, stored)) {
    // Someone else's token (the previous sign-out missed it), or one from another active department, whose
    // code this page doesn't have. Deleting it at FCM stops it on whichever channel still holds it.
    writeJson(REGISTRATION_KEY, null);
    await deleteToken(config);
  }

  if (Notification.permission !== 'granted') {
    return;
  }

  // A registration under a day old is left alone, and Firebase stays unloaded on this page.
  const current = getStoredRegistration();
  if (isCurrent(config, current) && Date.now() - current.registeredAt < REREGISTER_AFTER_MS) {
    return;
  }

  const token = await mintToken(config);
  if (!token) {
    return;
  }

  if (current && current.token !== token) {
    try {
      await unregisterToken(current.token);
    } catch {
      // a rotated token is already dead at FCM; this only tidies the channel
    }
  }

  await registerToken(config, token);
}

/**
 * Asks for notification permission the way the phone app does after sign-in, once. Browsers only show the
 * prompt from a click, so it waits for the first click on this page that doesn't leave it (a prompt opened
 * by a link closes as the page goes). A granted permission registers this browser straight away.
 */
export function askForPermissionOnce(): void {
  const config = getWebPushConfig();
  if (!config || !browserCanPush() || Notification.permission !== 'default') {
    return;
  }

  const lastAsked = readJson<number>(ASKED_KEY);
  if (lastAsked && Date.now() - lastAsked < ASK_AGAIN_AFTER_MS) {
    return;
  }

  const ask = (event: MouseEvent) => {
    if (leavesPage(event.target)) {
      return;
    }

    document.removeEventListener('click', ask, true);
    // Asked inside the click, while the browser still counts it as the person's.
    void Notification.requestPermission()
      .then((permission) => {
        if (permission === 'granted') {
          writeJson(ASKED_KEY, null);
          return refreshWebPush();
        }

        // Denied is remembered by the browser; a dismissed prompt waits a week before asking again.
        writeJson(ASKED_KEY, permission === 'default' ? Date.now() : null);
        return undefined;
      })
      .catch((error: unknown) => console.warn('Web push: the permission request failed', error));
  };

  document.addEventListener('click', ask, true);
}

/** A click that navigates (a real link, a form submit) would close the prompt as soon as it opened. */
function leavesPage(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) {
    return false;
  }

  const link = target.closest('a[href]');
  if (link) {
    const href = link.getAttribute('href') ?? '';
    if (!href.startsWith('#') && !href.toLowerCase().startsWith('javascript:')) {
      return true;
    }
  }

  // A button submits by default, but only inside a form.
  const submit = target.closest('button, input[type="submit"]');
  if (submit instanceof HTMLButtonElement) {
    return submit.type === 'submit' && !!submit.form;
  }

  return submit instanceof HTMLInputElement && !!submit.form;
}

/** Takes this browser off the user's channel before the sign-out form is posted. */
async function signOutCleanup(config: WebPushBrowserConfig): Promise<void> {
  const stored = getStoredRegistration();
  writeJson(REGISTRATION_KEY, null);

  if (stored?.token && isCurrent(config, stored)) {
    try {
      await unregisterToken(stored.token, { keepalive: true });
    } catch (error) {
      console.warn('Web push: the token could not be unregistered at sign-out', error);
    }
  }

  await deleteToken(config);
}

function withTimeout(work: Promise<void>, milliseconds: number): Promise<void> {
  return Promise.race([work, new Promise<void>((resolve) => window.setTimeout(resolve, milliseconds))]);
}

let signOutHookInstalled = false;

/**
 * Holds every sign-out form (Account/LogOff) until this browser's token is gone, then posts it. The
 * browser's permission stays, so whoever signs in here next is registered without being asked.
 */
export function installSignOutHook(): void {
  const config = getWebPushConfig();
  if (!config || signOutHookInstalled) {
    return;
  }

  signOutHookInstalled = true;
  document.addEventListener(
    'submit',
    (event) => {
      const form = event.target;
      if (!(form instanceof HTMLFormElement) || !/\/Account\/LogOff$/i.test(new URL(form.action, window.location.href).pathname)) {
        return;
      }

      if (!getStoredRegistration() || form.dataset.rgWebPushCleared === 'true') {
        return;
      }

      event.preventDefault();
      form.dataset.rgWebPushCleared = 'true';
      void withTimeout(signOutCleanup(config), SIGN_OUT_TIMEOUT_MS).finally(() => form.submit());
    },
    true,
  );
}
