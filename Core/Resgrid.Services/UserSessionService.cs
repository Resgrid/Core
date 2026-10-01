using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UserSessionService : IUserSessionService
	{
		private readonly IUserSessionsRepository _sessionsRepository;
		private readonly IIdentityUserRepository _identityUserRepository;
		private readonly IIdentityRepository _identityRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSsoService _departmentSsoService;
		private readonly IClientSessionMetadataParser _metadataParser;
		private readonly IIpLocationProvider _ipLocationProvider;
		private readonly IPasskeyFeatureGates _gates;
		private readonly ISystemAuditsService _audits;
		private readonly ISessionEventPublisher _sessionEvents;
		private readonly TimeProvider _time;

		public UserSessionService(IUserSessionsRepository sessionsRepository,
			IIdentityUserRepository identityUserRepository, IIdentityRepository identityRepository,
			IDepartmentsService departmentsService, IDepartmentSsoService departmentSsoService,
			IClientSessionMetadataParser metadataParser,
			IIpLocationProvider ipLocationProvider, IPasskeyFeatureGates gates, ISystemAuditsService audits, ISessionEventPublisher sessionEvents,
			TimeProvider time)
		{
			_sessionEvents = sessionEvents;
			_sessionsRepository = sessionsRepository;
			_identityUserRepository = identityUserRepository;
			_identityRepository = identityRepository;
			_departmentsService = departmentsService;
			_departmentSsoService = departmentSsoService;
			_metadataParser = metadataParser;
			_ipLocationProvider = ipLocationProvider;
			_gates = gates;
			_audits = audits;
			_time = time;
		}

		public async Task<UserSession> CreateSessionAsync(SessionIssueContext context, CancellationToken cancellationToken = default)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (string.IsNullOrWhiteSpace(context.UserId))
				throw new ArgumentException("A user is required to create a session.", nameof(context));

			var now = _time.GetUtcNow().UtcDateTime;
			var concurrentSessionLimit = 0;
			var concurrencyGateOn = DateTime.MinValue;
			DepartmentSecurityPolicy policy = null;
			if (context.DepartmentId.HasValue)
			{
				var member = await _departmentsService.GetDepartmentMemberAsync(context.UserId,
					context.DepartmentId.Value, bypassCache: true);
				if (member == null || member.IsDeleted || member.IsDisabled == true)
					throw new SessionCreationDeniedException("membership_inactive");

				// Read for every new session: whether the department requires shared mode is not behind the session-policy
				// gate, and a failed read refuses the sign-in rather than issuing a personal session by default.
				policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(
					context.DepartmentId.Value, cancellationToken);
				if (TryGetDepartmentPolicyGate(out var policyGate) && now >= policyGate && policy?.MaxConcurrentSessions > 0)
				{
					// Deliberately not checked here: counting now and inserting later lets two concurrent
					// logins both see room and both insert. The limit is enforced by the insert itself below.
					concurrentSessionLimit = policy.MaxConcurrentSessions;
					concurrencyGateOn = policyGate;
				}
			}

			// Shared mode (plan section 10.5), by the department's requirement or the installation's request while the gate is
			// on. Either way the session takes the department's idle lock and shift ceiling.
			var sharedSource = SharedSessionRules.SourceFor(policy, context.ClientApplication, context.SharedModeRequested, _gates.SharedDeviceModeEnabled);
			var shared = sharedSource != SharedModeSource.None;
			var expiresOn = context.ExpiresOn > now ? context.ExpiresOn : now.AddHours(24);
			if (shared && expiresOn > now.AddHours(SharedSessionRules.ShiftHours(policy)))
				expiresOn = now.AddHours(SharedSessionRules.ShiftHours(policy));

			var metadata = _metadataParser.Parse(context.UserAgent, context.DeviceName, context.DeviceType,
				context.OperatingSystem, context.Browser, context.ApplicationVersion);
			var location = await ResolveLocationAsync(context.IpAddress, context.Country, context.Region,
				context.City, cancellationToken);
			var session = new UserSession
			{
				UserSessionId = Guid.NewGuid().ToString("N"),
				UserId = context.UserId,
				DepartmentId = context.DepartmentId,
				AuthenticationGeneration = context.AuthenticationGeneration,
				State = (int)UserSessionState.Active,
				StateVersion = 0,
				ClientApplication = (int)context.ClientApplication,
				ClientInstanceIdHash = Limit(context.ClientInstanceIdHash, 128),
				DeviceName = Limit(metadata.DeviceName, 256),
				DeviceType = Limit(metadata.DeviceType, 128),
				OperatingSystem = Limit(metadata.OperatingSystem, 128),
				Browser = Limit(metadata.Browser, 128),
				ApplicationVersion = Limit(metadata.ApplicationVersion, 64),
				AuthenticationMethod = (int)context.AuthenticationMethod,
				DepartmentSsoConfigId = Limit(context.DepartmentSsoConfigId, 128),
				OpenIddictAuthorizationId = Limit(context.OpenIddictAuthorizationId, 128),
				WebCookieTicketKey = Limit(context.WebCookieTicketKey, 512),
				CreatedOn = now,
				LastActiveOn = now,
				ExpiresOn = expiresOn,
				FirstIpAddress = CanonicalIp(context.IpAddress),
				LastIpAddress = CanonicalIp(context.IpAddress),
				LastCountry = Limit(location?.Country, 128),
				LastRegion = Limit(location?.Region, 128),
				LastCity = Limit(location?.City, 128),
				UserAgent = Limit(context.UserAgent, Math.Max(128, SessionSecurityConfig.UserAgentMaximumLength)),
				IsLegacyAdopted = context.IsLegacyAdopted,
				LoginMfaMethod = context.LoginMfaMethod == null ? null : (int)context.LoginMfaMethod.Value,
				LoginMfaFactorReference = Limit(context.LoginMfaFactorReference, 256),
				SharedMode = shared,
				SharedModeSource = (int)sharedSource,
				SharedIdleLockMinutes = shared ? SharedSessionRules.IdleLockMinutes(policy) : null,
				LastOperatorActivityOn = shared ? now : null
			};

			if (concurrentSessionLimit > 0)
			{
				// Single atomic operation: the department's managed active sessions are counted and the row is
				// inserted under one lock, so the limit cannot be exceeded by simultaneous logins.
				var inserted = await _sessionsRepository.TryInsertWithinDepartmentSessionLimitAsync(session,
					context.DepartmentId.Value, concurrencyGateOn, concurrentSessionLimit, now, cancellationToken);
				if (!inserted)
					throw new SessionCreationDeniedException("maximum_sessions");

				return session;
			}

			return await _sessionsRepository.InsertAsync(session, cancellationToken, true);
		}

		public bool ShouldRecordActivity(UserSession session, DateTime occurredOn)
		{
			if (session == null)
				return false;

			// Exactly the predicate the UPDATE carries, so a caller that skips here would only have
			// issued a statement that matched no rows.
			return session.LastActiveOn <= ActivityWriteBefore(
				occurredOn == default ? DateTime.UtcNow : occurredOn);
		}

		private static DateTime ActivityWriteBefore(DateTime occurredOn) =>
			occurredOn.AddMinutes(-Math.Max(1, SessionSecurityConfig.LastActivityWriteIntervalMinutes));

		public async Task<SessionValidationResult> ValidateAsync(SessionPrincipalContext context, CancellationToken cancellationToken = default)
		{
			if (context == null || string.IsNullOrWhiteSpace(context.UserId))
				return SessionValidationResult.Invalid("missing_user");

			var user = await _identityUserRepository.GetByIdAsync(context.UserId);
			if (user == null)
				return SessionValidationResult.Invalid("user_not_found");

			if (user.CredentialsValidAfterUtc.HasValue &&
				(!context.CredentialIssuedOn.HasValue || context.CredentialIssuedOn.Value <= user.CredentialsValidAfterUtc.Value))
				return SessionValidationResult.Invalid("credential_cutoff");

			if (string.IsNullOrWhiteSpace(context.SessionId))
			{
				if (!SessionSecurityConfig.LegacyAdoptionEnabled)
					return SessionValidationResult.Invalid("session_required");

				if (DateTime.TryParse(SessionSecurityConfig.RequireSessionClaimForCredentialsIssuedAfterUtc, out var requiredAfter) &&
					context.CredentialIssuedOn.HasValue && context.CredentialIssuedOn.Value >= requiredAfter.ToUniversalTime())
					return SessionValidationResult.Invalid("session_required");

				return SessionValidationResult.Valid(canAdoptLegacy: true);
			}

			var now = _time.GetUtcNow().UtcDateTime;
			var session = await _sessionsRepository.GetByIdAsync(context.SessionId);
			if (session == null)
				return SessionValidationResult.Invalid("session_not_found");
			if (!string.Equals(session.UserId, context.UserId, StringComparison.Ordinal))
				return SessionValidationResult.Invalid("session_user_mismatch");
			if (session.State != (int)UserSessionState.Active)
				return SessionValidationResult.Invalid("session_revoked");
			if (session.ExpiresOn <= now)
				return SessionValidationResult.Invalid(session.SharedMode ? SharedSessionRules.ExpiredFailureCode : "session_expired");
			if (session.AuthenticationGeneration != user.AuthenticationGeneration ||
				(context.AuthenticationGeneration.HasValue && context.AuthenticationGeneration.Value != user.AuthenticationGeneration))
				return SessionValidationResult.Invalid("authentication_generation_mismatch");
			if (session.DepartmentId.HasValue && context.DepartmentId.HasValue && session.DepartmentId != context.DepartmentId)
				return SessionValidationResult.Invalid("session_department_mismatch");
			DepartmentSecurityPolicy policy = null;
			if (session.DepartmentId.HasValue)
			{
				var member = await _departmentsService.GetDepartmentMemberAsync(context.UserId,
					session.DepartmentId.Value, bypassCache: true);
				if (member == null || member.IsDeleted || member.IsDisabled == true)
					return SessionValidationResult.Invalid("membership_inactive");

				if (TryGetDepartmentPolicyGate(out var policyGate) && session.CreatedOn >= policyGate)
				{
					policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(
						session.DepartmentId.Value, cancellationToken);
					if (policy?.SessionTimeoutMinutes > 0 &&
						DepartmentSecurityPolicyDecisions.IdleExpired(policy.SessionTimeoutMinutes, session.LastActiveOn, now))
						return SessionValidationResult.Invalid("session_idle_timeout");
				}
				else if (session.SharedMode)
				{
					policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(
						session.DepartmentId.Value, cancellationToken);
				}
			}

			return session.SharedMode ? await ValidateSharedAsync(session, policy, now) : SessionValidationResult.Valid(session);
		}

		/// <summary>
		/// The shared-session checks every validator applies (passkey plan section 12.5.3), so HTTP, the token endpoint,
		/// SignalR and the broker all agree. The shift ceiling ends the session. A passed idle deadline locks it here, durably
		/// and once, whether or not the client showed its lock screen. A locked session is invalid for everything except the
		/// locked-session endpoints, which read it from the result.
		/// </summary>
		private async Task<SessionValidationResult> ValidateSharedAsync(UserSession session, DepartmentSecurityPolicy policy, DateTime now)
		{
			if (SharedSessionRules.ShiftEndsOn(session, policy) <= now)
				return SessionValidationResult.Invalid(SharedSessionRules.ExpiredFailureCode);

			if (SharedSessionRules.IdleLockDue(session, policy, now))
			{
				if (await _sessionsRepository.TryLockAsync(session.UserSessionId, session.LockVersion, (int)SharedSessionLockReason.Idle, now,
						CancellationToken.None) == 1)
				{
					session.IsLocked = true;
					session.LockVersion++;
					session.LockedOnUtc = now;
					session.LockReason = (int)SharedSessionLockReason.Idle;
					await AuditIdleLockAsync(session);
					await CloseConnectionsAsync(session.UserSessionId);
				}
				else
				{
					// Another request locked (or ended) it first. Whatever it did, this request is not let through on the stale row.
					var current = await _sessionsRepository.GetByIdAsync(session.UserSessionId);
					if (current == null || current.State != (int)UserSessionState.Active)
						return SessionValidationResult.Invalid("session_revoked");
					session = current;
					if (!session.IsLocked && SharedSessionRules.IdleLockDue(session, policy, now))
						return SessionValidationResult.Locked(session);
				}
			}

			return session.IsLocked ? SessionValidationResult.Locked(session) : SessionValidationResult.Valid(session);
		}

		private async Task AuditIdleLockAsync(UserSession session)
		{
			try
			{
				await _audits.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Api,
					Type = (int)SystemAuditTypes.SharedSessionLocked,
					DepartmentId = session.DepartmentId,
					UserId = session.UserId,
					TargetUserId = session.UserId,
					SessionId = SharedSessionAudit.SessionSuffix(session.UserSessionId),
					Successful = true,
					ServerName = Environment.MachineName,
					Data = SharedSessionAudit.Describe("locked", session, "idle"),
					LoggedOn = _time.GetUtcNow().UtcDateTime
				}, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// The lock stands without its audit row; failing the request would not undo it.
				Resgrid.Framework.Logging.LogException(ex, "Shared session idle-lock audit failed.");
			}
		}

		public async Task<IReadOnlySet<string>> GetUnusableSessionIdsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
		{
			var unusable = new HashSet<string>(sessionIds ?? Array.Empty<string>(), StringComparer.Ordinal);
			if (unusable.Count == 0)
				return unusable;

			var now = _time.GetUtcNow().UtcDateTime;
			foreach (var session in await _sessionsRepository.GetStatesAsync(unusable.ToList(), cancellationToken))
			{
				var idleLocksOn = (session.LastOperatorActivityOn ?? session.CreatedOn)
					.AddMinutes(Math.Clamp(session.SharedIdleLockMinutes ?? SharedSessionRules.DefaultIdleLockMinutes, 1, SharedSessionRules.MaxIdleLockMinutes));
				var usable = session.State == (int)UserSessionState.Active && session.ExpiresOn > now &&
					!(session.SharedMode && (session.IsLocked || idleLocksOn <= now));
				if (usable)
					unusable.Remove(session.UserSessionId);
			}

			return unusable;
		}

		public async Task RecordOperatorActivityAsync(UserSession session, CancellationToken cancellationToken = default)
		{
			if (session == null || !session.SharedMode || session.IsLocked)
				return;

			var now = _time.GetUtcNow().UtcDateTime;
			var writeBefore = now.AddSeconds(-Math.Max(1, PasskeyConfig.SharedActivityWriteIntervalSeconds));
			if ((session.LastOperatorActivityOn ?? session.CreatedOn) > writeBefore)
				return;

			DepartmentSecurityPolicy policy = null;
			if (session.DepartmentId.HasValue)
				policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(session.DepartmentId.Value, cancellationToken);
			var idleCutoff = now.AddMinutes(-SharedSessionRules.EffectiveIdleLockMinutes(session, policy));
			// The caller's copy follows the row, so a status read in the same request shows the moved deadline.
			if (await _sessionsRepository.RecordOperatorActivityAsync(session.UserSessionId, now, writeBefore, idleCutoff, cancellationToken) > 0)
				session.LastOperatorActivityOn = now;
		}

		public async Task<UserSession> AdoptLegacyAsync(LegacySessionContext context, CancellationToken cancellationToken = default)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));

			if (!string.IsNullOrWhiteSpace(context.StableCredentialIdentifier))
			{
				var existing = await _sessionsRepository.GetByAuthorizationIdAsync(context.StableCredentialIdentifier);
				if (existing != null)
					return existing;
				context.OpenIddictAuthorizationId = context.StableCredentialIdentifier;
			}

			context.IsLegacyAdopted = true;
			if (context.ClientApplication == default)
				context.ClientApplication = UserSessionClientApplication.UnknownLegacy;
			context.AuthenticationMethod = UserSessionAuthenticationMethod.LegacyUnknown;
			return await CreateSessionAsync(context, cancellationToken);
		}

		public async Task TouchAsync(string sessionId, RequestActivity activity, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sessionId) || activity == null)
				return;

			var occurredOn = activity.OccurredOn == default ? DateTime.UtcNow : activity.OccurredOn;
			var writeBefore = ActivityWriteBefore(occurredOn);
			var location = await ResolveLocationAsync(activity.IpAddress, activity.Country, activity.Region,
				activity.City, cancellationToken);
			await _sessionsRepository.TouchAsync(sessionId, occurredOn, writeBefore,
				CanonicalIp(activity.IpAddress), Limit(location?.Country, 128), Limit(location?.Region, 128),
				Limit(location?.City, 128), Limit(activity.UserAgent, Math.Max(128, SessionSecurityConfig.UserAgentMaximumLength)),
				cancellationToken);
		}

		public async Task<bool> MoveSessionToDepartmentAsync(string userId, string sessionId, int departmentId,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId) || departmentId <= 0)
				return false;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, bypassCache: true);
			if (member == null || member.IsDeleted || member.IsDisabled == true)
				return false;

			return await _sessionsRepository.UpdateDepartmentAsync(userId, sessionId, departmentId,
				cancellationToken) == 1;
		}

		public async Task<IReadOnlyList<UserSessionSummary>> GetActiveForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			var sessions = await _sessionsRepository.GetActiveByUserAsync(userId, DateTime.UtcNow);
			return sessions.Select(session => new UserSessionSummary
			{
				UserSessionId = session.UserSessionId,
				DepartmentId = session.DepartmentId,
				State = (UserSessionState)session.State,
				ClientApplication = (UserSessionClientApplication)session.ClientApplication,
				DeviceName = session.DeviceName,
				DeviceType = session.DeviceType,
				OperatingSystem = session.OperatingSystem,
				Browser = session.Browser,
				ApplicationVersion = session.ApplicationVersion,
				AuthenticationMethod = (UserSessionAuthenticationMethod)session.AuthenticationMethod,
				CreatedOn = session.CreatedOn,
				LastActiveOn = session.LastActiveOn,
				ExpiresOn = session.ExpiresOn,
				LastIpAddress = session.LastIpAddress,
				LastCountry = session.LastCountry,
				LastRegion = session.LastRegion,
				LastCity = session.LastCity,
				UserAgent = session.UserAgent,
				IsLegacyAdopted = session.IsLegacyAdopted,
				SharedMode = session.SharedMode,
				IsLocked = session.IsLocked
			}).ToList();
		}

		public async Task<RevocationResult> RevokeSessionAsync(string actorUserId, string targetUserId, string sessionId,
			UserSessionRevocationReason reason, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var count = await _sessionsRepository.RevokeAsync(targetUserId, sessionId, actorUserId, (int)reason, now, cancellationToken);
			if (count > 0)
				await CloseConnectionsAsync(sessionId);
			return new RevocationResult { RevokedSessionCount = count, RevokedOn = now };
		}

		/// <summary>
		/// Asks every SignalR host to close this session's open connections now (slice 16). Best effort: each host's sweep
		/// closes them within its interval anyway, and bulk revocations rely on the sweep alone.
		/// </summary>
		public async Task CloseConnectionsAsync(string sessionId)
		{
			try
			{
				await _sessionEvents.PublishAsync(sessionId, new SessionEventMessage { Name = SessionEvents.SessionClosed }, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "A session close event could not be sent; the connection sweep closes it instead.");
			}
		}

		public async Task<RevocationResult> RevokeOtherSessionsAsync(string userId, string currentSessionId,
			UserSessionRevocationReason reason, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var count = await _sessionsRepository.RevokeOthersAsync(userId, currentSessionId, (int)reason, now, cancellationToken);
			return new RevocationResult { RevokedSessionCount = count, RevokedOn = now };
		}

		public async Task<RevocationResult> RevokeAllAsync(string actorUserId, string targetUserId,
			UserSessionRevocationReason reason, DateTime validAfterUtc, CancellationToken cancellationToken = default)
		{
			var user = await _identityUserRepository.GetByIdAsync(targetUserId);
			if (user == null)
				return new RevocationResult { RevokedOn = validAfterUtc };

			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = validAfterUtc;
			user.AuthenticationStateChangedOn = validAfterUtc;
			user.SecurityStamp = Guid.NewGuid().ToString();
			await _identityUserRepository.UpdateAsync(user, cancellationToken);

			return await RevokePersistedCredentialsAsync(actorUserId, targetUserId, reason, validAfterUtc, cancellationToken);
		}

		public Task<RevocationResult> RevokeAllAfterCredentialChangeAsync(string actorUserId, string targetUserId,
			UserSessionRevocationReason reason, DateTime validAfterUtc, CancellationToken cancellationToken = default)
		{
			return RevokePersistedCredentialsAsync(actorUserId, targetUserId, reason, validAfterUtc, cancellationToken);
		}

		private async Task<RevocationResult> RevokePersistedCredentialsAsync(string actorUserId, string targetUserId,
			UserSessionRevocationReason reason, DateTime validAfterUtc, CancellationToken cancellationToken)
		{
			var count = await _sessionsRepository.RevokeAllAsync(targetUserId, actorUserId, (int)reason, validAfterUtc, cancellationToken);
			await _identityRepository.CleanUpOIDCTokensByUserAsync(targetUserId);
			return new RevocationResult { RevokedSessionCount = count, RevokedOn = validAfterUtc };
		}

		public async Task<RevocationResult> RevokeDepartmentSessionsAsync(string targetUserId, int departmentId,
			UserSessionRevocationReason reason, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var count = await _sessionsRepository.RevokeDepartmentAsync(targetUserId, departmentId, (int)reason, now, cancellationToken);
			return new RevocationResult { RevokedSessionCount = count, RevokedOn = now };
		}

		private static string Limit(string value, int maximumLength)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var sanitized = value.Replace("\r", " ").Replace("\n", " ").Trim();
			return sanitized.Length <= maximumLength ? sanitized : sanitized.Substring(0, maximumLength);
		}

		private async Task<IpLocationResult> ResolveLocationAsync(string ipAddress, string country,
			string region, string city, CancellationToken cancellationToken)
		{
			if (!string.IsNullOrWhiteSpace(country) || !string.IsNullOrWhiteSpace(region) ||
				!string.IsNullOrWhiteSpace(city))
				return new IpLocationResult {Country = country, Region = region, City = city};
			try
			{
				return await _ipLocationProvider.GetApproximateLocationAsync(CanonicalIp(ipAddress), cancellationToken);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Optional session IP location lookup failed.");
				return null;
			}
		}

		private static string CanonicalIp(string value) =>
			IPAddress.TryParse(value, out var address) ? address.ToString() : null;

		private static bool TryGetDepartmentPolicyGate(out DateTime gateUtc) =>
			DepartmentSecurityPolicyDecisions.TryGetSessionGate(SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc, out gateUtc);
	}
}
