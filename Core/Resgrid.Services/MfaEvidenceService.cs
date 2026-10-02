using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IMfaEvidenceService"/>
	public class MfaEvidenceService : IMfaEvidenceService
	{
		private readonly IUserSessionMfaEvidenceRepository _evidence;
		private readonly IUserMfaStateRepository _mfaState;
		private readonly IUserPasskeyRepository _passkeys;
		private readonly IUserSessionsRepository _sessions;
		private readonly IMfaActivityRepository _activity;
		private readonly TimeProvider _time;

		public MfaEvidenceService(IUserSessionMfaEvidenceRepository evidence, IUserMfaStateRepository mfaState, IUserPasskeyRepository passkeys,
			IUserSessionsRepository sessions, IMfaActivityRepository activity, TimeProvider time)
		{
			_activity = activity;
			_evidence = evidence;
			_mfaState = mfaState;
			_passkeys = passkeys;
			_sessions = sessions;
			_time = time;
		}

		public async Task RecordAsync(string userId, string sessionKey, UserSessionClientApplication client, MfaEvidenceKind kind,
			MfaEvidenceMethod method, MfaEvidencePurpose purpose, DateTime verifiedOnUtc, long authenticationGeneration,
			int? departmentId = null, string factorReference = null, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId))
				throw new ArgumentException("A user id is required.", nameof(userId));
			if (string.IsNullOrWhiteSpace(sessionKey))
				throw new ArgumentException("Evidence must belong to a session.", nameof(sessionKey));

			// A factor used to recover an account is recorded, but only ever as recovery evidence (plan section 6.1).
			if (method == MfaEvidenceMethod.RecoveryCode && kind != MfaEvidenceKind.Recovery)
				throw new ArgumentException("Recovery-code use can only be recorded as recovery evidence.", nameof(kind));

			var retention = TimeSpan.FromHours(Math.Max(1, TwoFactorConfig.MfaEvidenceRetentionHours));
			await _evidence.InsertAsync(new MfaEvidence
			{
				MfaEvidenceId = Guid.NewGuid().ToString(),
				UserId = userId,
				SessionKey = sessionKey,
				ClientApplication = (int)client,
				Kind = (int)kind,
				Method = (int)method,
				Purpose = (int)purpose,
				DepartmentId = departmentId,
				VerifiedOnUtc = verifiedOnUtc,
				ExpiresOnUtc = verifiedOnUtc.Add(retention),
				AuthenticationGeneration = authenticationGeneration,
				FactorReference = factorReference
			}, cancellationToken);

			// Every verified second factor or recovery code is the account's recent activity (plan section 6.5). History only,
			// so a failure here never fails the verification it records.
			if (kind is MfaEvidenceKind.SecondFactor or MfaEvidenceKind.Recovery)
			{
				try
				{
					var sessionId = MfaEvidence.TrackedSessionId(sessionKey);
					MfaApprovalRequest.TryParseFactorReference(method == MfaEvidenceMethod.PasskeyApproval ? factorReference : null, out _, out var approverSessionId);
					await _activity.InsertAsync(MfaActivityRecords.Create(new MfaActivityEntry
					{
						UserId = userId, Method = method, Purpose = purpose, Successful = true, ClientApplication = client, DepartmentId = departmentId,
						SessionId = sessionId, ApproverSessionId = approverSessionId
					}, sessionId == null ? null : await _sessions.GetByIdAsync(sessionId), verifiedOnUtc), cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Framework.Logging.LogException(ex, "MFA activity could not be recorded.");
				}
			}

			// The last successful second factor becomes the user's default choice on any installation (plan section 7.5
			// rule 5). A display default only, so a failure here never fails the verification it follows.
			if (kind == MfaEvidenceKind.SecondFactor)
			{
				try
				{
					await _mfaState.SetPreferredMethodAsync(userId, (int)method, _time.GetUtcNow().UtcDateTime, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Framework.Logging.LogException(ex, "MFA method preference update failed.");
				}
			}
		}

		public async Task<MfaEvidence> GetLatestFirstFactorAsync(string userId, string sessionKey, long currentGeneration,
			CancellationToken cancellationToken = default)
			=> await CountsForSessionAsync(await GetLatestAsync(userId, sessionKey, MfaEvidenceKind.FirstFactor, currentGeneration, cancellationToken));

		public async Task<bool> HasFreshFirstFactorAsync(string userId, string sessionKey, long currentGeneration, TimeSpan maxAge,
			DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var latest = await GetLatestFirstFactorAsync(userId, sessionKey, currentGeneration, cancellationToken);
			return IsFresh(latest, maxAge, utcNow);
		}

		public async Task<MfaEvidence> GetLatestSecondFactorAsync(string userId, string sessionKey, long currentGeneration,
			CancellationToken cancellationToken = default)
			=> await StillCountsAsync(await CountsForSessionAsync(
				await GetLatestAsync(userId, sessionKey, MfaEvidenceKind.SecondFactor, currentGeneration, cancellationToken)), cancellationToken);

		public async Task<MfaEvidence> GetLatestStepUpAsync(string userId, string sessionKey, long currentGeneration,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionKey))
				return null;

			return await StillCountsAsync(await CountsForSessionAsync(await _evidence.GetLatestForPurposeAsync(userId, sessionKey,
				MfaEvidenceKind.SecondFactor, MfaEvidencePurpose.StepUp, currentGeneration, _time.GetUtcNow().UtcDateTime, cancellationToken)),
				cancellationToken);
		}

		/// <summary>
		/// On a shared session nothing counts while it is locked, and nothing verified before its last lock counts after an
		/// unlock (plan section 12.5.3), however the lock happened. The latest evidence is the only candidate, so when it is
		/// from before the lock there is none.
		/// </summary>
		private async Task<MfaEvidence> CountsForSessionAsync(MfaEvidence evidence)
		{
			var sessionId = MfaEvidence.TrackedSessionId(evidence?.SessionKey);
			if (sessionId == null)
				return evidence;

			return SharedSessionRules.EvidenceCounts(await _sessions.GetByIdAsync(sessionId), evidence.VerifiedOnUtc) ? evidence : null;
		}

		/// <summary>
		/// A Responder approval counts only while its passkey and Responder session do (plan section 7.9 revocation); when
		/// the latest evidence is an approval that no longer counts, there is no evidence and the user verifies again.
		/// </summary>
		private async Task<MfaEvidence> StillCountsAsync(MfaEvidence evidence, CancellationToken cancellationToken)
		{
			if (evidence?.Method != (int)MfaEvidenceMethod.PasskeyApproval)
				return evidence;

			return await ApprovalApprovers.IsValidAsync(_passkeys, _sessions, evidence.UserId, evidence.FactorReference, evidence.AuthenticationGeneration,
				_time.GetUtcNow().UtcDateTime, cancellationToken)
				? evidence
				: null;
		}

		public Task RevokeForUserAsync(string userId, CancellationToken cancellationToken = default)
			=> _evidence.RevokeForUserAsync(userId, _time.GetUtcNow().UtcDateTime, cancellationToken);

		public Task RevokeForFactorAsync(string userId, string factorReference, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(factorReference)
				? Task.CompletedTask
				: _evidence.RevokeForFactorAsync(userId, factorReference, _time.GetUtcNow().UtcDateTime, cancellationToken);

		/// <summary>
		/// Fresh means verified within <paramref name="maxAge"/> and not in the future beyond a small clock skew. A future
		/// timestamp is never treated as fresh evidence.
		/// </summary>
		public static bool IsFresh(MfaEvidence evidence, TimeSpan maxAge, DateTime utcNow)
		{
			if (evidence == null)
				return false;

			var age = utcNow - evidence.VerifiedOnUtc;
			return age >= TimeSpan.FromSeconds(-30) && age <= maxAge;
		}

		private Task<MfaEvidence> GetLatestAsync(string userId, string sessionKey, MfaEvidenceKind kind, long currentGeneration,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionKey))
				return Task.FromResult<MfaEvidence>(null);

			return _evidence.GetLatestAsync(userId, sessionKey, kind, currentGeneration, _time.GetUtcNow().UtcDateTime, cancellationToken);
		}
	}
}
