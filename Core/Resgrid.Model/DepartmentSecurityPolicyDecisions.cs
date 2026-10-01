using System;
using System.Globalization;

namespace Resgrid.Model
{
	/// <summary>Pure decisions shared by the owning sign-in/session consumers and administrative previews.</summary>
	public static class DepartmentSecurityPolicyDecisions
	{
		public static bool BlocksPasswordLogin(bool requireSso, bool enabledProvider, bool loginViaSso) => requireSso && enabledProvider && !loginViaSso;
		public static bool RequiresMfaCompletion(bool requireMfa, bool completed) => requireMfa && !completed;

		/// <summary>
		/// Whether a change moves the department's sign-in MFA rules, which advances MfaPolicyVersion (passkey plan section
		/// 10.1): RequireMfa, the sign-in passkey and provider step-up switches, and Responder approval.
		/// </summary>
		public static bool MfaPolicyChanged(DepartmentSecurityPolicy before, DepartmentSecurityPolicy after) =>
			before.RequireMfa != after.RequireMfa ||
			before.AllowPasskeysForLoginMfa != after.AllowPasskeysForLoginMfa ||
			before.AllowFederatedMfaForLoginMfa != after.AllowFederatedMfaForLoginMfa ||
			before.AllowResponderApproval != after.AllowResponderApproval;

		/// <summary>
		/// Whether a change moves which MFA may back a Protected Data Grant, which advances the ADP PolicyEpoch and so revokes
		/// existing grants (passkey plan section 10.1).
		/// </summary>
		public static bool AdpMethodPolicyChanged(DepartmentSecurityPolicy before, DepartmentSecurityPolicy after) =>
			before.AllowPasskeysForAdp != after.AllowPasskeysForAdp ||
			before.AllowFederatedMfaForAdp != after.AllowFederatedMfaForAdp ||
			before.AllowResponderApproval != after.AllowResponderApproval ||
			before.AcceptRecentLoginMfaForAdp != after.AcceptRecentLoginMfaForAdp ||
			before.AcceptRecentUnlockMfaForAdp != after.AcceptRecentUnlockMfaForAdp;

		/// <summary>The MFA rule fields of a policy, for comparing a change against what was stored.</summary>
		public static DepartmentSecurityPolicy SnapshotMfaRules(DepartmentSecurityPolicy p) => new()
		{
			DepartmentId = p.DepartmentId,
			RequireMfa = p.RequireMfa,
			AllowPasskeysForLoginMfa = p.AllowPasskeysForLoginMfa,
			AllowPasskeysForAdp = p.AllowPasskeysForAdp,
			AllowFederatedMfaForLoginMfa = p.AllowFederatedMfaForLoginMfa,
			AllowFederatedMfaForAdp = p.AllowFederatedMfaForAdp,
			AllowResponderApproval = p.AllowResponderApproval,
			AcceptRecentLoginMfaForAdp = p.AcceptRecentLoginMfaForAdp,
			AcceptRecentUnlockMfaForAdp = p.AcceptRecentUnlockMfaForAdp,
			MfaPolicyVersion = p.MfaPolicyVersion,
			SharedIdleLockMinutes = p.SharedIdleLockMinutes,
			SharedShiftHours = p.SharedShiftHours,
			SharedModeRequiredApps = p.SharedModeRequiredApps
		};

		/// <summary>Whether a change moves the shared-device policy (plan section 10.5), which is the managing member's decision.</summary>
		public static bool SharedPolicyChanged(DepartmentSecurityPolicy before, DepartmentSecurityPolicy after) =>
			before.SharedIdleLockMinutes != after.SharedIdleLockMinutes ||
			before.SharedShiftHours != after.SharedShiftHours ||
			before.SharedModeRequiredApps != after.SharedModeRequiredApps;

		/// <summary>
		/// Whether a change requires shared mode for an app that did not need it. Refused while the deployment's shared-mode
		/// gate is off; removing a requirement is always allowed.
		/// </summary>
		public static bool AddsSharedRequirement(DepartmentSecurityPolicy before, DepartmentSecurityPolicy after) =>
			(after.SharedModeRequiredApps & ~before.SharedModeRequiredApps) != 0;

		/// <summary>The shared-device values are within the department's ranges and name only known apps.</summary>
		public static bool SharedPolicyValid(DepartmentSecurityPolicy p) =>
			p.SharedIdleLockMinutes >= 1 && p.SharedIdleLockMinutes <= SharedSessionRules.MaxIdleLockMinutes &&
			p.SharedShiftHours >= 1 && p.SharedShiftHours <= SharedSessionRules.MaxShiftHours &&
			(p.SharedModeRequiredApps & ~(int)(SharedModeApps.Unit | SharedModeApps.Command | SharedModeApps.Dispatch)) == 0;

		/// <summary>Whether any second-factor method switch differs (the managing member's decision, plan section 10.1).</summary>
		public static bool MethodSwitchesChanged(DepartmentSecurityPolicy before, DepartmentSecurityPolicy after) =>
			before.AllowPasskeysForLoginMfa != after.AllowPasskeysForLoginMfa ||
			before.AllowFederatedMfaForLoginMfa != after.AllowFederatedMfaForLoginMfa ||
			AdpMethodPolicyChanged(before, after);

		/// <summary>
		/// Whether a change turns provider step-up on for sign-in or ADP. That needs a tested mapping, and the change cannot
		/// itself be authorized by provider step-up (plan section 7.8).
		/// </summary>
		public static bool EnablesFederatedMfa(DepartmentSecurityPolicy before, DepartmentSecurityPolicy after) =>
			(!before.AllowFederatedMfaForLoginMfa && after.AllowFederatedMfaForLoginMfa) ||
			(!before.AllowFederatedMfaForAdp && after.AllowFederatedMfaForAdp);

		public static int MinimumPasswordLength(int configured) => Math.Max(8, configured);
		public static bool PasswordExpired(int days, DateTime? lastSetOn, DateTime nowUtc) =>
			days > 0 && lastSetOn.HasValue && nowUtc > lastSetOn.Value.AddDays(days);
		public static bool TryGetSessionGate(string configured, out DateTime gateUtc)
		{
			if (DateTimeOffset.TryParse(configured, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
			{
				gateUtc = parsed.UtcDateTime; return true;
			}
			gateUtc = default; return false;
		}
		public static bool IdleExpired(int minutes, DateTime lastActiveOn, DateTime nowUtc) => minutes > 0 && lastActiveOn <= nowUtc.AddMinutes(-minutes);
	}
}
