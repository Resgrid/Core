using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IAuthenticationChallengeService"/>
	public class AuthenticationChallengeService : IAuthenticationChallengeService
	{
		private readonly IAuthenticationChallengeRepository _challenges;
		private readonly TimeProvider _time;

		public AuthenticationChallengeService(IAuthenticationChallengeRepository challenges, TimeProvider time)
		{
			_challenges = challenges;
			_time = time;
		}

		public async Task<AuthenticationChallenge> CreateAsync(AuthenticationChallengeBinding binding, string rpId, string optionsJson,
			CancellationToken cancellationToken = default)
		{
			if (binding == null || string.IsNullOrWhiteSpace(binding.UserId) || string.IsNullOrWhiteSpace(binding.ParentId))
				throw new ArgumentException("A challenge must be bound to a user and a parent session or transaction.", nameof(binding));
			if (string.IsNullOrWhiteSpace(rpId) || string.IsNullOrWhiteSpace(optionsJson))
				throw new ArgumentException("A challenge needs its relying party and server options.");

			var now = _time.GetUtcNow().UtcDateTime;
			if (await _challenges.CountPendingForUserAsync(binding.UserId, now, cancellationToken) >= Math.Max(1, PasskeyConfig.MaxOutstandingChallengesPerUser))
				return null;

			var challenge = new AuthenticationChallenge
			{
				AuthenticationChallengeId = Guid.NewGuid().ToString(),
				UserId = binding.UserId,
				Purpose = (int)binding.Purpose,
				ClientApplication = (int)binding.ClientApplication,
				RpId = rpId,
				ParentKind = (int)binding.ParentKind,
				ParentId = binding.ParentId,
				DepartmentId = binding.DepartmentId,
				AuthenticationGeneration = binding.AuthenticationGeneration,
				LockVersion = binding.LockVersion,
				OptionsJson = optionsJson,
				CreatedOnUtc = now,
				ExpiresOnUtc = now.Add(LifetimeFor(binding.Purpose)),
				MaxAttempts = Math.Max(1, PasskeyConfig.ChallengeMaxAttempts),
				State = (int)AuthenticationChallengeState.Pending
			};

			await _challenges.InsertAsync(challenge, cancellationToken);
			return challenge;
		}

		public async Task<AuthenticationChallengeResult> GetForCompletionAsync(string challengeId, AuthenticationChallengeBinding binding,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(challengeId) || binding == null)
				return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.NotFound);

			AuthenticationChallenge challenge;
			try
			{
				challenge = await _challenges.GetAsync(challengeId, cancellationToken);
			}
			catch (Exception ex)
			{
				// Authoritative state is unavailable: the ceremony is refused, never treated as approved (plan section 3 item 4).
				Logging.LogException(ex, "Authentication challenge lookup failed; the ceremony was refused.");
				return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.Unavailable);
			}

			return Evaluate(challenge, binding, _time.GetUtcNow().UtcDateTime);
		}

		public async Task<bool> TryConsumeAsync(string challengeId, CancellationToken cancellationToken = default)
		{
			try
			{
				return await _challenges.TryConsumeAsync(challengeId, _time.GetUtcNow().UtcDateTime, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Authentication challenge consumption failed; the ceremony was refused.");
				return false;
			}
		}

		public Task RecordFailedAttemptAsync(string challengeId, CancellationToken cancellationToken = default)
			=> _challenges.RecordFailedAttemptAsync(challengeId, cancellationToken);

		public Task CancelPendingForUserAsync(string userId, CancellationToken cancellationToken = default)
			=> _challenges.CancelPendingForUserAsync(userId, cancellationToken);

		/// <summary>
		/// Decides whether a stored challenge may be completed by this caller. Everything it was issued to must match, so a
		/// request id alone never completes anything and a challenge cannot move between users, purposes or sessions.
		/// </summary>
		public static AuthenticationChallengeResult Evaluate(AuthenticationChallenge challenge, AuthenticationChallengeBinding binding, DateTime utcNow)
		{
			if (challenge == null)
				return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.NotFound);

			if (!string.Equals(challenge.UserId, binding.UserId, StringComparison.OrdinalIgnoreCase)
				|| challenge.Purpose != (int)binding.Purpose
				|| challenge.ClientApplication != (int)binding.ClientApplication
				|| challenge.ParentKind != (int)binding.ParentKind
				|| !string.Equals(challenge.ParentId, binding.ParentId, StringComparison.Ordinal)
				|| challenge.DepartmentId != binding.DepartmentId
				|| challenge.LockVersion != binding.LockVersion)
				return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.BindingMismatch);

			switch (challenge.ChallengeState)
			{
				case AuthenticationChallengeState.Consumed:
				case AuthenticationChallengeState.Canceled:
					return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.AlreadyUsed);
				case AuthenticationChallengeState.Exhausted:
					return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.TooManyAttempts);
			}

			if (challenge.ExpiresOnUtc <= utcNow)
				return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.Expired);

			if (challenge.Attempts >= challenge.MaxAttempts)
				return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.TooManyAttempts);

			if (challenge.AuthenticationGeneration != binding.AuthenticationGeneration)
				return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.Stale);

			return AuthenticationChallengeResult.Of(AuthenticationChallengeOutcome.Usable, challenge);
		}

		private static TimeSpan LifetimeFor(AuthenticationChallengePurpose purpose) =>
			purpose == AuthenticationChallengePurpose.PasskeyRegistration
				? TimeSpan.FromSeconds(Math.Max(30, PasskeyConfig.RegistrationChallengeLifetimeSeconds))
				: TimeSpan.FromSeconds(Math.Max(30, PasskeyConfig.AssertionChallengeLifetimeSeconds));
	}
}
