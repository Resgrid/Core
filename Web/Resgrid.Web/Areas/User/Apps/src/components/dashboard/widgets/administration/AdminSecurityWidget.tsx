import { HeroNumber, ItemList, KpiGrid, Note } from '../../primitives';
import type { ListItem } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Administration/AdminSecurityWidgetData.cs */
export interface AdminSecurityWidgetData {
  Members: number;
  MfaEnabled: number;
  PasskeyUsers: number;
  StrongAuthentication: number;
  StrongAuthenticationPercent: number;
  ActiveSessions: number;
  UsersWithSessions: number;
  SsoProviders: number;
  SsoEnabled: boolean;
  ScimEnabled: boolean;
  RequireMfa: boolean;
  RequireSso: boolean;
  MaxConcurrentSessions?: number | null;
  SessionTimeoutMinutes?: number | null;
}

const SECURITY_URL = '/User/Security';
const SSO_URL = '/User/Security/Sso';
const POLICY_URL = '/User/Security/SecurityPolicy';

/** admin.security (plan §7.11): MFA / passkey adoption, single sign-on and SCIM state, and active sessions — counts only. */
export default function AdminSecurityWidget({ data, size, t, ctx }: WidgetProps<AdminSecurityWidgetData>) {
  const k = 'W.admin.security.';
  const onOff = (value: boolean) => <span className="rg-mu">{value ? t(`${k}On`) : t(`${k}Off`)}</span>;
  const switches: ListItem[] = [
    { key: 'sso', title: t(`${k}Sso`), right: onOff(data.SsoEnabled), tone: data.SsoEnabled ? 'good' : undefined, icon: data.SsoEnabled ? undefined : 'fa-sign-in', href: SSO_URL },
    { key: 'scim', title: t(`${k}Scim`), right: onOff(data.ScimEnabled), tone: data.ScimEnabled ? 'good' : undefined, icon: data.ScimEnabled ? undefined : 'fa-users', href: SSO_URL },
    { key: 'mfa', title: t(`${k}RequireMfa`), right: onOff(data.RequireMfa), tone: data.RequireMfa ? 'good' : undefined, icon: data.RequireMfa ? undefined : 'fa-key', href: POLICY_URL },
  ];

  return (
    <>
      <HeroNumber
        size="md"
        value={ctx.format.percent(data.StrongAuthenticationPercent)}
        label={t(`${k}StrongAuthenticationOf`, data.StrongAuthentication, data.Members)}
        icon={data.StrongAuthenticationPercent < 50 ? 'fa-exclamation-triangle' : undefined}
        iconColor={data.StrongAuthenticationPercent < 50 ? 'var(--rg-warning-text)' : undefined}
      />
      <KpiGrid
        columns={3}
        items={[
          { value: ctx.format.number(data.MfaEnabled, 0), label: t(`${k}Mfa`), href: SECURITY_URL },
          { value: ctx.format.number(data.PasskeyUsers, 0), label: t(`${k}Passkeys`) },
          { value: ctx.format.number(data.ActiveSessions, 0), label: t(`${k}Sessions`), title: t(`${k}SessionUsers`, data.UsersWithSessions) },
        ]}
      />
      {size === 'm' ? (
        <Note>
          <a href={SSO_URL}>{t(`${k}Sso`)}</a>: {data.SsoEnabled ? t(`${k}On`) : t(`${k}Off`)}
          {' · '}
          {t(`${k}Scim`)}: {data.ScimEnabled ? t(`${k}On`) : t(`${k}Off`)}
          {' · '}
          <a href={POLICY_URL}>{t(`${k}RequireMfa`)}</a>: {data.RequireMfa ? t(`${k}On`) : t(`${k}Off`)}
        </Note>
      ) : size !== 's' ? (
        <ItemList items={switches} />
      ) : null}
    </>
  );
}
