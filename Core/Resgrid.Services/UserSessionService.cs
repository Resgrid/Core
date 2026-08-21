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

		public UserSessionService(IUserSessionsRepository sessionsRepository,
			IIdentityUserRepository identityUserRepository, IIdentityRepository identityRepository,
			IDepartmentsService departmentsService, IDepartmentSsoService departmentSsoService,
			IClientSessionMetadataParser metadataParser,
			IIpLocationProvider ipLocationProvider)
		{
			_sessionsRepository = sessionsRepository;
			_identityUserRepository = identityUserRepository;
			_identityRepository = identityRepository;
			_departmentsService = departmentsService;
			_departmentSsoService = departmentSsoService;
			_metadataParser = metadataParser;
			_ipLocationProvider = ipLocationProvider;
		}

		public async Task<UserSession> CreateSessionAsync(SessionIssueContext context, CancellationToken cancellationToken = default)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (string.IsNullOrWhiteSpace(context.UserId))
				throw new ArgumentException("A user is required to create a session.", nameof(context));

			var now = DateTime.UtcNow;
			if (context.DepartmentId.HasValue)
			{
				var member = await _departmentsService.GetDepartmentMemberAsync(context.UserId,
					context.DepartmentId.Value, bypassCache: true);
				if (member == null || member.IsDeleted || member.IsDisabled == true)
					throw new SessionCreationDeniedException("membership_inactive");

				if (TryGetDepartmentPolicyGate(out var policyGate) && now >= policyGate)
				{
					var policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(
						context.DepartmentId.Value, cancellationToken);
					if (policy?.MaxConcurrentSessions > 0)
					{
						var activeSessions = await _sessionsRepository.GetActiveByUserAsync(context.UserId, now);
						var managedCount = activeSessions.Count(session =>
							session.DepartmentId == context.DepartmentId && session.CreatedOn >= policyGate);
						if (managedCount >= policy.MaxConcurrentSessions)
							throw new SessionCreationDeniedException("maximum_sessions");
					}
				}
			}

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
				ExpiresOn = context.ExpiresOn > now ? context.ExpiresOn : now.AddHours(24),
				FirstIpAddress = CanonicalIp(context.IpAddress),
				LastIpAddress = CanonicalIp(context.IpAddress),
				LastCountry = Limit(location?.Country, 128),
				LastRegion = Limit(location?.Region, 128),
				LastCity = Limit(location?.City, 128),
				UserAgent = Limit(context.UserAgent, Math.Max(128, SessionSecurityConfig.UserAgentMaximumLength)),
				IsLegacyAdopted = context.IsLegacyAdopted
			};

			return await _sessionsRepository.InsertAsync(session, cancellationToken, true);
		}

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

			var session = await _sessionsRepository.GetByIdAsync(context.SessionId);
			if (session == null)
				return SessionValidationResult.Invalid("session_not_found");
			if (!string.Equals(session.UserId, context.UserId, StringComparison.Ordinal))
				return SessionValidationResult.Invalid("session_user_mismatch");
			if (session.State != (int)UserSessionState.Active)
				return SessionValidationResult.Invalid("session_revoked");
			if (session.ExpiresOn <= DateTime.UtcNow)
				return SessionValidationResult.Invalid("session_expired");
			if (session.AuthenticationGeneration != user.AuthenticationGeneration ||
				(context.AuthenticationGeneration.HasValue && context.AuthenticationGeneration.Value != user.AuthenticationGeneration))
				return SessionValidationResult.Invalid("authentication_generation_mismatch");
			if (session.DepartmentId.HasValue && context.DepartmentId.HasValue && session.DepartmentId != context.DepartmentId)
				return SessionValidationResult.Invalid("session_department_mismatch");
			if (session.DepartmentId.HasValue)
			{
				var member = await _departmentsService.GetDepartmentMemberAsync(context.UserId,
					session.DepartmentId.Value, bypassCache: true);
				if (member == null || member.IsDeleted || member.IsDisabled == true)
					return SessionValidationResult.Invalid("membership_inactive");

				if (TryGetDepartmentPolicyGate(out var policyGate) && session.CreatedOn >= policyGate)
				{
					var policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(
						session.DepartmentId.Value, cancellationToken);
					if (policy?.SessionTimeoutMinutes > 0 &&
						session.LastActiveOn <= DateTime.UtcNow.AddMinutes(-policy.SessionTimeoutMinutes))
						return SessionValidationResult.Invalid("session_idle_timeout");
				}
			}

			return SessionValidationResult.Valid(session);
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
			var writeBefore = occurredOn.AddMinutes(-Math.Max(1, SessionSecurityConfig.LastActivityWriteIntervalMinutes));
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
				IsLegacyAdopted = session.IsLegacyAdopted
			}).ToList();
		}

		public async Task<RevocationResult> RevokeSessionAsync(string actorUserId, string targetUserId, string sessionId,
			UserSessionRevocationReason reason, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var count = await _sessionsRepository.RevokeAsync(targetUserId, sessionId, actorUserId, (int)reason, now, cancellationToken);
			return new RevocationResult { RevokedSessionCount = count, RevokedOn = now };
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

		private static bool TryGetDepartmentPolicyGate(out DateTime gateUtc)
		{
			if (DateTimeOffset.TryParse(SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc,
				System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AssumeUniversal |
				System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
			{
				gateUtc = parsed.UtcDateTime;
				return true;
			}

			gateUtc = default;
			return false;
		}
	}
}
