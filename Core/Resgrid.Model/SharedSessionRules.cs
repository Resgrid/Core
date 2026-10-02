using System;
using Resgrid.Config;
using Resgrid.Model.Security;

namespace Resgrid.Model
{
	/// <summary>The attended apps a department can require shared mode for (plan section 10.5).</summary>
	[Flags]
	public enum SharedModeApps
	{
		None = 0,
		Unit = 1,
		Command = 2,
		Dispatch = 4
	}

	/// <summary>Why a session is shared. Kept on the session so its provenance is never re-derived from a label.</summary>
	public enum SharedModeSource
	{
		None = 0,

		/// <summary>The installation asked for shared mode. A request to be stricter, so it needs no proof.</summary>
		InstallationRequested = 1,

		/// <summary>The department's policy requires it for this app, or for any session that does not say which app it is.</summary>
		DepartmentRequired = 2
	}

	public enum SharedSessionLockReason
	{
		/// <summary>The operator (or the app, on an OS lock or backgrounding) asked to lock.</summary>
		Explicit = 1,

		/// <summary>The server's idle deadline passed.</summary>
		Idle = 2
	}

	/// <summary>
	/// Pure decisions for shared vehicle tablets and workstations (passkey plan sections 5.5, 10.5 and 12.5.3). The server
	/// owns them: a client can ask for shared mode or report activity, but it cannot relax the department's policy, move a
	/// deadline past the shift ceiling, or make anything from before a lock usable after it.
	/// </summary>
	public static class SharedSessionRules
	{
		public const int DefaultIdleLockMinutes = 5;
		public const int DefaultShiftHours = 12;

		/// <summary>Session validation's failure code for a shared session that is locked (or whose idle deadline passed).</summary>
		public const string LockedFailureCode = "shared_session_locked";

		/// <summary>Session validation's failure code for a shared session past its shift ceiling.</summary>
		public const string ExpiredFailureCode = "shared_session_expired";

		/// <summary>Sign-in request header asking for a shared session (<c>true</c>). A label that can only tighten.</summary>
		public const string InstallationHeader = "X-Resgrid-Shared-Installation";

		/// <summary>Request header a shared-mode client sends on requests the operator caused (<c>1</c>).</summary>
		public const string ActivityHeader = "X-Resgrid-Operator-Activity";

		public static int MaxIdleLockMinutes => Math.Clamp(PasskeyConfig.SharedMaxIdleLockMinutes, 1, 15);

		public static int MaxShiftHours => Math.Clamp(PasskeyConfig.SharedMaxShiftHours, 1, 24);

		/// <summary>The department's idle lock, within 1 minute and the deployment's cap. No policy row means the default.</summary>
		public static int IdleLockMinutes(DepartmentSecurityPolicy policy) =>
			Math.Clamp(policy?.SharedIdleLockMinutes ?? DefaultIdleLockMinutes, 1, MaxIdleLockMinutes);

		/// <summary>The department's shift ceiling, within 1 hour and the deployment's cap.</summary>
		public static int ShiftHours(DepartmentSecurityPolicy policy) =>
			Math.Clamp(policy?.SharedShiftHours ?? DefaultShiftHours, 1, MaxShiftHours);

		/// <summary>
		/// Whether the department requires shared mode for a session of <paramref name="client"/>. A session that does not
		/// say which app it is (no or an unknown <c>X-Resgrid-Client</c>) is required whenever any app is, so omitting the
		/// header never relaxes the requirement. Web, Responder, BigBoard and MCP sessions are not covered.
		/// </summary>
		public static bool IsRequiredFor(DepartmentSecurityPolicy policy, UserSessionClientApplication client)
		{
			var required = (SharedModeApps)(policy?.SharedModeRequiredApps ?? 0) & (SharedModeApps.Unit | SharedModeApps.Command | SharedModeApps.Dispatch);
			if (required == SharedModeApps.None)
				return false;

			return client switch
			{
				UserSessionClientApplication.Unit => required.HasFlag(SharedModeApps.Unit),
				UserSessionClientApplication.Command => required.HasFlag(SharedModeApps.Command),
				UserSessionClientApplication.Dispatch => required.HasFlag(SharedModeApps.Dispatch),
				UserSessionClientApplication.Api or UserSessionClientApplication.UnknownLegacy => true,
				_ => false
			};
		}

		/// <summary>
		/// Whether a session a sign-in creates is shared, and why (plan section 10.5). The department's requirement always
		/// applies, even with the deployment gate off, because turning the gate off must not waive it; an installation's own
		/// request is honored only while <paramref name="deviceModeEnabled"/> (the shared-device gate) is on.
		/// </summary>
		public static SharedModeSource SourceFor(DepartmentSecurityPolicy policy, UserSessionClientApplication client, bool requested, bool deviceModeEnabled) =>
			IsRequiredFor(policy, client) ? SharedModeSource.DepartmentRequired
			: requested && deviceModeEnabled ? SharedModeSource.InstallationRequested
			: SharedModeSource.None;

		/// <summary>True when a sign-in's <see cref="InstallationHeader"/> asks for shared mode.</summary>
		public static bool IsRequested(string headerValue)
		{
			var value = headerValue?.Trim();
			return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1" ||
				string.Equals(value, "shared", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The session's idle lock now: what sign-in recorded, or the department's current value if that is stricter.</summary>
		public static int EffectiveIdleLockMinutes(UserSession session, DepartmentSecurityPolicy policy) =>
			Math.Min(Math.Clamp(session?.SharedIdleLockMinutes ?? DefaultIdleLockMinutes, 1, MaxIdleLockMinutes), IdleLockMinutes(policy));

		/// <summary>When the session ends whatever the activity: its recorded ceiling, or sooner under a stricter current policy.</summary>
		public static DateTime ShiftEndsOn(UserSession session, DepartmentSecurityPolicy policy)
		{
			var byPolicy = session.CreatedOn.AddHours(ShiftHours(policy));
			return session.ExpiresOn < byPolicy ? session.ExpiresOn : byPolicy;
		}

		/// <summary>When an unlocked shared session locks unless the operator does something first.</summary>
		public static DateTime IdleLocksOn(UserSession session, DepartmentSecurityPolicy policy) =>
			(session.LastOperatorActivityOn ?? session.CreatedOn).AddMinutes(EffectiveIdleLockMinutes(session, policy));

		public static bool IdleLockDue(UserSession session, DepartmentSecurityPolicy policy, DateTime utcNow) =>
			session != null && session.SharedMode && !session.IsLocked && IdleLocksOn(session, policy) <= utcNow;

		/// <summary>
		/// Whether evidence verified at <paramref name="verifiedOnUtc"/> can count for this session. On a shared session,
		/// nothing counts while it is locked, and nothing verified before its last lock counts after it is unlocked. A missing
		/// session (an untracked Web session) is not shared.
		/// </summary>
		public static bool EvidenceCounts(UserSession session, DateTime verifiedOnUtc) =>
			session == null || !session.SharedMode ||
			(!session.IsLocked && (session.LockedOnUtc == null || verifiedOnUtc > session.LockedOnUtc.Value));

		/// <summary>The lock version grants, challenges and approvals bind to: the session's own when shared, otherwise none.</summary>
		public static long? LockVersionOf(UserSession session) => session?.SharedMode == true ? session.LockVersion : null;

		/// <summary>
		/// Whether a redeemed provider step-up unlocks this locked session (plan sections 7.8 and 12.5.3): begun for a shared
		/// unlock of this very session and operator, in its department, under the department's tested mapping, at the current
		/// generation, and after the session's last lock, so a round trip from before a lock or for anything else never unlocks it.
		/// </summary>
		public static bool FederatedUnlockMatches(SsoLoginTransaction transaction, UserSession session, string userId, int departmentId,
			DepartmentSsoConfig testedConfig) =>
			transaction != null && session != null && !string.IsNullOrWhiteSpace(userId) &&
			transaction.DepartmentId == departmentId && FederatedMfaMapping.Satisfies(transaction, testedConfig) &&
			string.Equals(transaction.Operation, SsoLoginTransaction.SharedUnlockOperation, StringComparison.Ordinal) &&
			string.Equals(transaction.SessionId, session.UserSessionId, StringComparison.Ordinal) &&
			string.Equals(transaction.ExpectedUserId, userId, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(transaction.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
			transaction.AuthenticationGeneration == session.AuthenticationGeneration &&
			session.LockedOnUtc != null && transaction.CreatedOnUtc > session.LockedOnUtc.Value;
	}
}
