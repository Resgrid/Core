using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Challenge binding and lifetime rules (plan section 5.2): a challenge is usable only by exactly what it was issued to,
	/// and an unavailable store refuses the ceremony instead of approving it.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AuthenticationChallengeServiceTests
	{
		private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

		private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }

		private Mock<IAuthenticationChallengeRepository> _repository;
		private AuthenticationChallengeService _service;
		private int _maxOutstanding, _maxAttempts, _registrationLifetime, _assertionLifetime;

		[SetUp]
		public void SetUp()
		{
			_maxOutstanding = PasskeyConfig.MaxOutstandingChallengesPerUser;
			_maxAttempts = PasskeyConfig.ChallengeMaxAttempts;
			_registrationLifetime = PasskeyConfig.RegistrationChallengeLifetimeSeconds;
			_assertionLifetime = PasskeyConfig.AssertionChallengeLifetimeSeconds;
			_repository = new Mock<IAuthenticationChallengeRepository>();
			_service = new AuthenticationChallengeService(_repository.Object, new Clock());
		}

		[TearDown]
		public void TearDown()
		{
			PasskeyConfig.MaxOutstandingChallengesPerUser = _maxOutstanding;
			PasskeyConfig.ChallengeMaxAttempts = _maxAttempts;
			PasskeyConfig.RegistrationChallengeLifetimeSeconds = _registrationLifetime;
			PasskeyConfig.AssertionChallengeLifetimeSeconds = _assertionLifetime;
		}

		private static AuthenticationChallengeBinding Binding() => new()
		{
			UserId = "user-1",
			Purpose = AuthenticationChallengePurpose.LoginSecondFactor,
			ClientApplication = UserSessionClientApplication.Responder,
			ParentKind = AuthenticationChallengeParentKind.LoginTransaction,
			ParentId = "transaction-1",
			DepartmentId = 42,
			AuthenticationGeneration = 7,
			LockVersion = null
		};

		private static AuthenticationChallenge IssuedTo(AuthenticationChallengeBinding binding) => new()
		{
			AuthenticationChallengeId = "challenge-1",
			UserId = binding.UserId,
			Purpose = (int)binding.Purpose,
			ClientApplication = (int)binding.ClientApplication,
			RpId = "responder.resgrid.com",
			ParentKind = (int)binding.ParentKind,
			ParentId = binding.ParentId,
			DepartmentId = binding.DepartmentId,
			AuthenticationGeneration = binding.AuthenticationGeneration,
			LockVersion = binding.LockVersion,
			OptionsJson = "{}",
			CreatedOnUtc = Now.AddSeconds(-10),
			ExpiresOnUtc = Now.AddSeconds(110),
			Attempts = 0,
			MaxAttempts = 5,
			State = (int)AuthenticationChallengeState.Pending
		};

		[Test]
		public void A_challenge_matching_everything_it_was_issued_to_is_usable()
		{
			var binding = Binding();
			var result = AuthenticationChallengeService.Evaluate(IssuedTo(binding), binding, Now);

			result.IsUsable.Should().BeTrue();
			result.Challenge.Should().NotBeNull();
		}

		private static readonly (string Name, Action<AuthenticationChallengeBinding> Change)[] BindingChanges =
		{
			("another user", b => b.UserId = "user-2"),
			("another purpose", b => b.Purpose = AuthenticationChallengePurpose.AdpStepUp),
			("another app on the same device", b => b.ClientApplication = UserSessionClientApplication.Unit),
			("another parent kind", b => b.ParentKind = AuthenticationChallengeParentKind.Session),
			("another transaction", b => b.ParentId = "transaction-2"),
			("another department", b => b.DepartmentId = 43),
			("no department", b => b.DepartmentId = null),
			("a shared-session lock version", b => b.LockVersion = 3)
		};

		[TestCaseSource(nameof(BindingChanges))]
		public void A_challenge_never_moves_to_something_it_was_not_issued_to((string Name, Action<AuthenticationChallengeBinding> Change) change)
		{
			var challenge = IssuedTo(Binding());
			var caller = Binding();
			change.Change(caller);

			var result = AuthenticationChallengeService.Evaluate(challenge, caller, Now);

			result.Outcome.Should().Be(AuthenticationChallengeOutcome.BindingMismatch, change.Name);
			result.Challenge.Should().BeNull("a caller that does not match learns nothing about the challenge");
		}

		[Test]
		public void The_user_id_comparison_ignores_case_like_identity_does()
		{
			var challenge = IssuedTo(Binding());
			var caller = Binding();
			caller.UserId = "USER-1";

			AuthenticationChallengeService.Evaluate(challenge, caller, Now).IsUsable.Should().BeTrue();
		}

		[TestCase(AuthenticationChallengeState.Consumed, AuthenticationChallengeOutcome.AlreadyUsed)]
		[TestCase(AuthenticationChallengeState.Canceled, AuthenticationChallengeOutcome.AlreadyUsed)]
		[TestCase(AuthenticationChallengeState.Exhausted, AuthenticationChallengeOutcome.TooManyAttempts)]
		public void A_spent_challenge_is_never_usable_again(AuthenticationChallengeState state, AuthenticationChallengeOutcome expected)
		{
			var binding = Binding();
			var challenge = IssuedTo(binding);
			challenge.State = (int)state;

			AuthenticationChallengeService.Evaluate(challenge, binding, Now).Outcome.Should().Be(expected);
		}

		[Test]
		public void A_challenge_expires_at_its_expiry_instant()
		{
			var binding = Binding();
			var challenge = IssuedTo(binding);
			challenge.ExpiresOnUtc = Now;

			AuthenticationChallengeService.Evaluate(challenge, binding, Now).Outcome.Should().Be(AuthenticationChallengeOutcome.Expired);
			AuthenticationChallengeService.Evaluate(challenge, binding, Now.AddTicks(-1)).IsUsable.Should().BeTrue();
		}

		[Test]
		public void A_pending_challenge_at_its_attempt_limit_is_not_usable()
		{
			var binding = Binding();
			var challenge = IssuedTo(binding);
			challenge.Attempts = challenge.MaxAttempts;

			AuthenticationChallengeService.Evaluate(challenge, binding, Now).Outcome.Should().Be(AuthenticationChallengeOutcome.TooManyAttempts);
		}

		[Test]
		public void A_password_change_since_issue_makes_the_challenge_stale()
		{
			var challenge = IssuedTo(Binding());
			var caller = Binding();
			caller.AuthenticationGeneration = 8;

			AuthenticationChallengeService.Evaluate(challenge, caller, Now).Outcome.Should().Be(AuthenticationChallengeOutcome.Stale);
		}

		[Test]
		public void A_missing_challenge_is_not_found()
		{
			AuthenticationChallengeService.Evaluate(null, Binding(), Now).Outcome.Should().Be(AuthenticationChallengeOutcome.NotFound);
		}

		[Test]
		public async Task Creating_stores_a_pending_challenge_bound_to_the_caller()
		{
			PasskeyConfig.AssertionChallengeLifetimeSeconds = 120;
			PasskeyConfig.ChallengeMaxAttempts = 5;
			AuthenticationChallenge stored = null;
			_repository.Setup(r => r.InsertAsync(It.IsAny<AuthenticationChallenge>(), It.IsAny<CancellationToken>()))
				.Callback<AuthenticationChallenge, CancellationToken>((c, _) => stored = c)
				.Returns(Task.CompletedTask);
			var binding = Binding();

			var created = await _service.CreateAsync(binding, "responder.resgrid.com", "{\"challenge\":\"x\"}");

			created.Should().BeSameAs(stored);
			created.AuthenticationChallengeId.Should().NotBeNullOrWhiteSpace();
			created.ChallengeState.Should().Be(AuthenticationChallengeState.Pending);
			created.ExpiresOnUtc.Should().Be(Now.AddSeconds(120));
			created.MaxAttempts.Should().Be(5);
			AuthenticationChallengeService.Evaluate(created, binding, Now).IsUsable.Should().BeTrue();
		}

		[Test]
		public async Task Registration_challenges_use_the_registration_lifetime()
		{
			PasskeyConfig.RegistrationChallengeLifetimeSeconds = 300;
			var binding = Binding();
			binding.Purpose = AuthenticationChallengePurpose.PasskeyRegistration;

			var created = await _service.CreateAsync(binding, "responder.resgrid.com", "{}");

			created.ExpiresOnUtc.Should().Be(Now.AddSeconds(300));
		}

		[Test]
		public async Task A_misconfigured_tiny_lifetime_is_raised_to_a_usable_floor()
		{
			PasskeyConfig.AssertionChallengeLifetimeSeconds = 0;

			var created = await _service.CreateAsync(Binding(), "responder.resgrid.com", "{}");

			created.ExpiresOnUtc.Should().Be(Now.AddSeconds(30));
		}

		[Test]
		public async Task No_new_challenge_is_issued_past_the_outstanding_limit()
		{
			PasskeyConfig.MaxOutstandingChallengesPerUser = 10;
			_repository.Setup(r => r.CountPendingForUserAsync("user-1", Now, It.IsAny<CancellationToken>())).ReturnsAsync(10);

			var created = await _service.CreateAsync(Binding(), "responder.resgrid.com", "{}");

			created.Should().BeNull();
			_repository.Verify(r => r.InsertAsync(It.IsAny<AuthenticationChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_challenge_without_a_parent_is_refused()
		{
			var binding = Binding();
			binding.ParentId = null;

			var create = () => _service.CreateAsync(binding, "responder.resgrid.com", "{}");

			await create.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task An_unavailable_store_refuses_the_ceremony()
		{
			_repository.Setup(r => r.GetAsync("challenge-1", It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			var result = await _service.GetForCompletionAsync("challenge-1", Binding());

			result.Outcome.Should().Be(AuthenticationChallengeOutcome.Unavailable);
			result.IsUsable.Should().BeFalse();
		}

		[Test]
		public async Task A_failed_consume_is_never_treated_as_a_win()
		{
			_repository.Setup(r => r.TryConsumeAsync("challenge-1", Now, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			(await _service.TryConsumeAsync("challenge-1")).Should().BeFalse();
		}

		[Test]
		public async Task Completion_reads_the_stored_challenge_and_applies_the_binding()
		{
			var binding = Binding();
			_repository.Setup(r => r.GetAsync("challenge-1", It.IsAny<CancellationToken>())).ReturnsAsync(IssuedTo(binding));

			(await _service.GetForCompletionAsync("challenge-1", binding)).IsUsable.Should().BeTrue();

			var other = Binding();
			other.ClientApplication = UserSessionClientApplication.Dispatch;
			(await _service.GetForCompletionAsync("challenge-1", other)).Outcome.Should().Be(AuthenticationChallengeOutcome.BindingMismatch);
		}
	}
}
