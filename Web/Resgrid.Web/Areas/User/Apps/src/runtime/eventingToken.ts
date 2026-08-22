interface EventingTokenResponse {
  accessToken: string;
}

export async function getEventingToken(): Promise<string> {
  const verificationToken = document.querySelector<HTMLMetaElement>(
    'meta[name="request-verification-token"]',
  )?.content;
  const headers = new Headers({ Accept: 'application/json' });
  if (verificationToken) headers.set('RequestVerificationToken', verificationToken);

  const response = await fetch('/api/web-bff/eventing-token', {
    method: 'POST',
    credentials: 'same-origin',
    headers,
  });
  if (!response.ok) return '';

  const value = (await response.json()) as EventingTokenResponse;
  return typeof value.accessToken === 'string' ? value.accessToken : '';
}
