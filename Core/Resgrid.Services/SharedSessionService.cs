using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="ISharedSessionService"/>
	public class SharedSessionService : ISharedSessionService
	{
		private readonly IUserSessionsRepository _sessions;
		private readonly IUserSessionService _userSessions;
		private readonly IMfaEvidenceService _evidence;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly ISystemAuditsService _audits;
		private readonly IMfaActivityService _activity;
		private readonly TimeProvider _time;

		public SharedSessionService(IUserSessionsRepository sessions, IUserSessionService userSessions, IMfaEvidenceService evidence,
			IDepartmentSsoService departmentSso, ISystemAuditsService audits, IMfaActivityService activity, TimeProvider time)
		{
			_activity = activity;
			_sessions = sessions;
			_userSessions = userSessions;
			_evidence = evidence;
			_departmentSso = departmentSso;
			_audits = audits;
			_time = time;
		}

		public async Task<SharedSessionStatus> GetStatusAsync(UserSession session, CancellationToken cancellationToken = default)
		{
			if (session == null)
				return null;
			if (!session.SharedMode)
				return new SharedSessionStatus { ClientApplication = (UserSessionClientApplication)session.ClientApplication };

			var policy = await PolicyAsync(session, cancellationToken);
			return new SharedSessionStatus
			{
				Shared = true,
				Locked = session.IsLocked,
				LockVersion = session.LockVersion,
				LockReason = session.IsLocked && session.LockReason != null ? (SharedSessionLockReason)session.LockReason.Value : null,
				IdleLockMinutes = SharedSessionRules.EffectiveIdleLockMinutes(session, policy),
				IdleLocksOnUtc = session.IsLocked ? null : SharedSessionRules.IdleLocksOn(session, policy),
				ShiftEndsOnUtc = SharedSessionRules.ShiftEndsOn(session, policy),
				ClientApplication = (UserSessionClientApplication)session.ClientApplication,
				InstallationLabel = session.DeviceName
			};
		}

		public async Task<SharedSessionTransition> LockAsync(UserSession session, SharedSessionRequestInfo request, CancellationToken cancellationToken = default)
		{
			if (session == null || session.State != (int)UserSessionState.Active)
				return SharedSessionTransition.Of(SharedSessionOutcome.SessionEnded);
			if (!session.SharedMode)
				return SharedSessionTransition.Of(SharedSessionOutcome.NotShared);

			// Two tries: a concurrent unlock between reading the row and locking it moves the version once.
			var current = session;
			for (var attempt = 0; attempt < 2; attempt++)
			{
				if (current.IsLocked)
					return SharedSessionTransition.Of(SharedSessionOutcome.Succeeded, current.LockVersion);

				var now = _time.GetUtcNow().UtcDateTime;
				if (await _sessions.TryLockAsync(current.UserSessionId, current.LockVersion, (int)SharedSessionLockReason.Explicit, now, cancellationToken) == 1)
				{
					current.IsLocked = true;
					current.LockVersion++;
					current.LockedOnUtc = now;
					current.LockReason = (int)SharedSessionLockReason.Explicit;
					await AuditAsync(SystemAuditTypes.SharedSessionLocked, current, request, true, "locked", "explicit", cancellationToken);
					// Its realtime connections stop now; the app reconnects after the unlock.
					await _userSessions.CloseConnectionsAsync(current.UserSessionId);
					return SharedSessionTransition.Of(SharedSessionOutcome.Succeeded, current.LockVersion);
				}

				current = await _sessions.GetByIdAsync(session.UserSessionId);
				if (current == null || current.State != (int)UserSessionState.Active ||
					!string.Equals(current.UserId, session.UserId, StringComparison.OrdinalIgnoreCase))
					return SharedSessionTransition.Of(SharedSessionOutcome.SessionEnded);
			}

			return current.IsLocked
				? SharedSessionTransition.Of(SharedSessionOutcome.Succeeded, current.LockVersion)
				: SharedSessionTransition.Of(SharedSessionOutcome.LockChanged, current.LockVersion);
		}

		public async Task<SharedSessionOutcome> CanUnlockAsync(UserSession session, CancellationToken cancellationToken = default)
		{
			if (session == null || session.State != (int)UserSessionState.Active || session.ExpiresOn <= _time.GetUtcNow().UtcDateTime)
				return SharedSessionOutcome.SessionEnded;
			if (!session.SharedMode)
				return SharedSessionOutcome.NotShared;
			if (!session.IsLocked)
				return SharedSessionOutcome.NotLocked;

			// Unlock resumes the first factor this session began with, so a department that has since required SSO gets an SSO
			// sign-in instead (plan section 12.5.3), exactly as a new password sign-in would be refused.
			if (session.DepartmentId.HasValue &&
				session.AuthenticationMethod != (int)UserSessionAuthenticationMethod.OidcSso &&
				session.AuthenticationMethod != (int)UserSessionAuthenticationMethod.SamlSso)
			{
				var policy = await PolicyAsync(session, cancellationToken);
				if (policy?.RequireSso == true &&
					(await _departmentSso.GetSsoConfigsForDepartmentAsync(session.DepartmentId.Value, cancellationToken) ?? Enumerable.Empty<DepartmentSsoConfig>())
					.Any(c => c.IsEnabled))
					return SharedSessionOutcome.SsoReauthenticationRequired;
			}

			return SharedSessionOutcome.Succeeded;
		}

		public async Task<SharedSessionTransition> UnlockAsync(UserSession session, long expectedLockVersion, MfaEvidenceMethod method, string factorReference,
			DateTime verifiedOnUtc, SharedSessionRequestInfo request, CancellationToken cancellationToken = default)
		{
			if (session == null || !session.SharedMode)
				return SharedSessionTransition.Of(session == null ? SharedSessionOutcome.SessionEnded : SharedSessionOutcome.NotShared);

			var now = _time.GetUtcNow().UtcDateTime;
			if (await _sessions.TryUnlockAsync(session.UserId, session.UserSessionId, expectedLockVersion, now, cancellationToken) != 1)
			{
				var current = await _sessions.GetByIdAsync(session.UserSessionId);
				if (current == null || current.State != (int)UserSessionState.Active || current.ExpiresOn <= now)
					return SharedSessionTransition.Of(SharedSessionOutcome.SessionEnded);
				return SharedSessionTransition.Of(current.IsLocked ? SharedSessionOutcome.LockChanged : SharedSessionOutcome.NotLocked, current.LockVersion);
			}

			session.IsLocked = false;
			session.LastOperatorActivityOn = now;

			// Unlock evidence is a real second factor for this operator, recorded after the lock it answers. Protected data may
			// reuse it only where the department's AcceptRecentUnlockMfaForAdp allows; the first-factor time is untouched.
			try
			{
				await _evidence.RecordAsync(session.UserId, MfaEvidence.TrackedSessionKey(session.UserSessionId),
					(UserSessionClientApplication)session.ClientApplication, MfaEvidenceKind.SecondFactor, method, MfaEvidencePurpose.SharedUnlock,
					verifiedOnUtc, session.AuthenticationGeneration, session.DepartmentId, factorReference, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The unlock stands; the operator verifies again for anything that needs recent MFA.
				Logging.LogException(ex, "Shared session unlock evidence could not be recorded.");
			}

			await AuditAsync(SystemAuditTypes.SharedSessionUnlocked, session, request, true, "unlocked", MfaMethodNames.From(method), cancellationToken);
			return SharedSessionTransition.Of(SharedSessionOutcome.Succeeded, session.LockVersion);
		}

		public async Task RecordFailedUnlockAsync(UserSession session, string method, SharedSessionRequestInfo request, CancellationToken cancellationToken = default)
		{
			if (session == null)
				return;

			await AuditAsync(SystemAuditTypes.SharedSessionUnlocked, session, request, false, "unlock failed", method, cancellationToken);
			await _activity.RecordAsync(new MfaActivityEntry
			{
				UserId = session.UserId, Method = MfaMethodNames.Parse(method), Purpose = MfaEvidencePurpose.SharedUnlock, Successful = false,
				ClientApplication = (UserSessionClientApplication)session.ClientApplication, InstallationLabel = session.DeviceName, SharedMode = true,
				DepartmentId = session.DepartmentId, SessionId = session.UserSessionId
			}, cancellationToken);
		}

		public async Task<SharedSessionTransition> EndShiftAsync(UserSession session, bool switchOperator, SharedSessionRequestInfo request,
			CancellationToken cancellationToken = default)
		{
			if (session == null)
				return SharedSessionTransition.Of(SharedSessionOutcome.SessionEnded);
			if (!session.SharedMode)
				return SharedSessionTransition.Of(SharedSessionOutcome.NotShared);

			var result = await _userSessions.RevokeSessionAsync(session.UserId, session.UserId, session.UserSessionId,
				switchOperator ? UserSessionRevocationReason.OperatorSwitched : UserSessionRevocationReason.ShiftEnded, cancellationToken);
			if (result.RevokedSessionCount > 0)
				await AuditAsync(SystemAuditTypes.SharedSessionEnded, session, request, true, "ended", switchOperator ? "switch operator" : "end shift",
					cancellationToken);

			// Ended either way: a session another request already ended is just as finished.
			return SharedSessionTransition.Of(SharedSessionOutcome.Succeeded, session.LockVersion);
		}

		private async Task<DepartmentSecurityPolicy> PolicyAsync(UserSession session, CancellationToken cancellationToken) =>
			session.DepartmentId.HasValue ? await _departmentSso.GetSecurityPolicyForDepartmentAsync(session.DepartmentId.Value, cancellationToken) : null;

		private async Task AuditAsync(SystemAuditTypes type, UserSession session, SharedSessionRequestInfo request, bool successful, string action,
			string detail, CancellationToken cancellationToken)
		{
			try
			{
				await _audits.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)(request?.AuditSystem ?? SystemAuditSystems.Api),
					Type = (int)type,
					DepartmentId = session.DepartmentId,
					UserId = session.UserId,
					Username = request?.UserName,
					TargetUserId = session.UserId,
					SessionId = SharedSessionAudit.SessionSuffix(session.UserSessionId),
					Successful = successful,
					IpAddress = request?.IpAddress,
					ServerName = Environment.MachineName,
					CorrelationId = request?.CorrelationId,
					Data = SharedSessionAudit.Describe(action, session, detail),
					LoggedOn = _time.GetUtcNow().UtcDateTime
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The transition already committed; a missing audit row must not report it as failed.
				Logging.LogException(ex, "Shared session audit failed.");
			}
		}
	}
}
