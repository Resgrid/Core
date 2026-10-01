using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 8: the restricted login transaction. It holds only hashes, is bound to the client that
	/// started it, completes once and redeems once, and is voided by an account or department policy change.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class MfaLoginTransactionServiceTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private static readonly DateTime Start = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = Start;
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private Clock _clock;
		private InMemoryMfaLoginTransactionRepository _rows;
		private IdentityUser _user;
		private DepartmentSecurityPolicy _policy;
		private Mock<IPasskeyFeatureGates> _gates;
		private Mock<IPasskeyService> _passkeys;
		private MfaLoginTransactionService _service;

		[SetUp]
		public void SetUp()
		{
			_clock = new Clock();
			_rows = new InMemoryMfaLoginTransactionRepository();
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_policy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId, MfaPolicyVersion = 7 };
			_gates = new Mock<IPasskeyFeatureGates>();
			_passkeys = new Mock<IPasskeyService>();
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);
			_service = new MfaLoginTransactionService(_rows, new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), _gates.Object),
				sso.Object, identity.Object, _passkeys.Object, Mock.Of<IMfaApprovalService>(), _gates.Object, _clock);
		}

		private Task<MfaLoginTransactionStart> Begin(UserSessionClientApplication client = UserSessionClientApplication.Unit, bool shared = false,
			string label = null) =>
			_service.BeginAsync(new MfaLoginTransactionRequest
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				ClientApplication = client,
				ClientId = "unit-app",
				FirstFactorMethod = MfaEvidenceMethod.Password,
				FirstFactorVerifiedOnUtc = Start.AddSeconds(-2),
				AuthenticationGeneration = 4,
				Scopes = new[] { "openid", "offline_access" },
				TotpEnrolled = true,
				SharedModeRequested = shared,
				InstallationLabel = label
			});

		private async Task<MfaLoginTransaction> Opened(string secret) => (await _service.OpenAsync(secret, UserSessionClientApplication.Unit)).Transaction;

		[Test]
		public void The_gate_starts_off() => new MfaLoginTransactionService(null, null, null, null, null, null, null, TimeProvider.System).IsEnabled.Should().BeFalse();

		[Test]
		public async Task A_transaction_stores_only_a_hash_of_its_secret_and_what_the_first_factor_established()
		{
			var start = await Begin();

			start.Secret.Should().HaveLength(43, "32 random bytes in base64url");
			start.ExpiresInSeconds.Should().Be(TwoFactorConfig.LoginMfaTransactionLifetimeSeconds);
			start.Choice.AllowedMethods.Should().Equal(MfaMethodNames.Totp);
			start.Choice.Preferred.Should().Be(MfaMethodNames.Totp);

			var row = _rows.Rows.Single();
			row.SecretHash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(start.Secret)));
			typeof(MfaLoginTransaction).GetProperties().Where(p => p.PropertyType == typeof(string))
				.Select(p => (string)p.GetValue(row)).Should().NotContain(start.Secret);
			row.UserId.Should().Be(UserId);
			row.ClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			row.FirstFactorVerifiedOnUtc.Should().Be(Start.AddSeconds(-2));
			row.AuthenticationGeneration.Should().Be(4);
			row.MfaPolicyVersion.Should().Be(7);
			row.Scopes.Should().Be("openid offline_access");
			row.ExpiresOnUtc.Should().Be(Start.AddSeconds(TwoFactorConfig.LoginMfaTransactionLifetimeSeconds));
			row.MaxAttempts.Should().Be(TwoFactorConfig.LoginMfaTransactionMaxAttempts);
		}

		[Test]
		public async Task A_transaction_records_whether_its_session_will_be_shared_and_the_installations_label()
		{
			// What the member's Responder is shown when asked to approve this sign-in: the session's own rule, decided now.
			_gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(true);
			await Begin(shared: true, label: "  Engine 7 tablet ");
			_rows.Rows.Last().SharedMode.Should().BeTrue();
			_rows.Rows.Last().InstallationLabel.Should().Be("Engine 7 tablet");

			_gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(false);
			await Begin(shared: true, label: new string('x', 300));
			_rows.Rows.Last().SharedMode.Should().BeFalse("the installation's request needs the gate, as the session's does");
			_rows.Rows.Last().InstallationLabel.Should().HaveLength(256);

			_policy.SharedModeRequiredApps = (int)SharedModeApps.Unit;
			await Begin();
			_rows.Rows.Last().SharedMode.Should().BeTrue("the department requires it for this app, whatever the installation asked");
			_rows.Rows.Last().InstallationLabel.Should().BeNull();
			await Begin(UserSessionClientApplication.Dispatch);
			_rows.Rows.Last().SharedMode.Should().BeFalse();
		}

		[Test]
		public async Task A_passkey_is_offered_only_when_one_is_bound_to_this_app_and_accepted()
		{
			_passkeys.Setup(p => p.HasActiveForClientAsync(UserId, UserSessionClientApplication.Unit, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			(await Begin()).Choice.AllowedMethods.Should().Equal(MfaMethodNames.Totp);

			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			var start = await Begin();
			start.Choice.AllowedMethods.Should().Equal(MfaMethodNames.Totp, MfaMethodNames.Passkey);
			start.Choice.EnrolledMethods.Should().Equal(MfaMethodNames.Totp, MfaMethodNames.Passkey);

			(await Begin(UserSessionClientApplication.Responder)).Choice.EnrolledMethods.Should().Equal(new[] { MfaMethodNames.Totp },
				"the Unit passkey does not count for Responder");

			_policy.AllowPasskeysForLoginMfa = false;
			var transaction = await Opened((await Begin()).Secret);
			(await _service.IsMethodAcceptedAsync(transaction, MfaEvidenceMethod.Passkey)).Should().BeFalse();
			(await _service.IsMethodAcceptedAsync(transaction, MfaEvidenceMethod.Totp)).Should().BeTrue();
			(await _service.IsMethodAcceptedAsync(transaction, MfaEvidenceMethod.RecoveryCode)).Should().BeTrue();
			(await _service.IsMethodAcceptedAsync(transaction, MfaEvidenceMethod.PasskeyApproval)).Should().BeFalse();
		}

		[Test]
		public async Task A_transaction_opens_only_for_its_own_client_while_pending_and_current()
		{
			var start = await Begin();

			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.Usable);
			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Responder)).Outcome.Should().Be(MfaLoginTransactionOutcome.Invalid);
			(await _service.OpenAsync("guess", UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.Invalid);
			(await _service.OpenAsync(null, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.Invalid);
			(await _service.OpenAsync(new string('a', 500), UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.Invalid);

			_policy.MfaPolicyVersion = 8;
			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.PolicyChanged);
			_policy.MfaPolicyVersion = 7;

			_user.AuthenticationGeneration = 5;
			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.SessionRevoked,
				"a password change or revocation since the first factor voids it");
			_user = null;
			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.SessionRevoked);
		}

		[Test]
		public async Task A_transaction_expires_without_sliding()
		{
			var start = await Begin();

			_clock.Now = Start.AddSeconds(TwoFactorConfig.LoginMfaTransactionLifetimeSeconds);
			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.Expired);
			(await _service.CompleteAsync(_rows.Rows.Single(), MfaEvidenceMethod.Totp, null, _clock.Now)).Succeeded.Should().BeFalse();
		}

		[Test]
		public async Task Failed_attempts_of_any_method_spend_the_transaction()
		{
			var start = await Begin();
			var transaction = await Opened(start.Secret);

			for (var i = 0; i < TwoFactorConfig.LoginMfaTransactionMaxAttempts; i++)
				await _service.RecordFailedAttemptAsync(transaction);

			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.TooManyAttempts);
			(await _service.CompleteAsync(transaction, MfaEvidenceMethod.Totp, null, _clock.Now)).Succeeded.Should().BeFalse();
		}

		[Test]
		public async Task A_transaction_completes_once_and_its_code_redeems_once()
		{
			var start = await Begin();
			var transaction = await Opened(start.Secret);

			var completions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
				_service.CompleteAsync(transaction, MfaEvidenceMethod.Passkey, "passkey:pk-1", Start)));
			var completion = completions.Should().ContainSingle(c => c.Succeeded).Subject;
			completion.CompletionCode.Should().HaveLength(43);
			completion.ExpiresInSeconds.Should().Be(TwoFactorConfig.LoginMfaCompletionCodeLifetimeSeconds);

			var row = _rows.Rows.Single();
			row.CompletionMethod.Should().Be((int)MfaEvidenceMethod.Passkey);
			row.CompletionFactorReference.Should().Be("passkey:pk-1");
			row.CompletionCodeHash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(completion.CompletionCode)));
			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.AlreadyUsed);

			(await _service.RedeemAsync(start.Secret, "not-the-code", UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.Invalid);
			(await _service.RedeemAsync(start.Secret, completion.CompletionCode, UserSessionClientApplication.Responder)).Outcome
				.Should().Be(MfaLoginTransactionOutcome.Invalid, "the code is bound to the client that completed it");

			var redeemed = await _service.RedeemAsync(start.Secret, completion.CompletionCode, UserSessionClientApplication.Unit);
			redeemed.Outcome.Should().Be(MfaLoginTransactionOutcome.Usable);
			redeemed.Transaction.FirstFactorVerifiedOnUtc.Should().Be(Start.AddSeconds(-2));
			(await _service.RedeemAsync(start.Secret, completion.CompletionCode, UserSessionClientApplication.Unit)).Outcome
				.Should().Be(MfaLoginTransactionOutcome.AlreadyUsed, "a lost response means signing in again");
		}

		[Test]
		public async Task A_code_is_redeemable_only_briefly_and_only_while_the_account_and_policy_hold()
		{
			var start = await Begin();
			(await _service.RedeemAsync(start.Secret, "anything", UserSessionClientApplication.Unit)).Outcome
				.Should().Be(MfaLoginTransactionOutcome.Invalid, "nothing is redeemable before a second factor");

			var completion = await _service.CompleteAsync(await Opened(start.Secret), MfaEvidenceMethod.Totp, null, Start);

			_policy.MfaPolicyVersion = 8;
			(await _service.RedeemAsync(start.Secret, completion.CompletionCode, UserSessionClientApplication.Unit)).Outcome
				.Should().Be(MfaLoginTransactionOutcome.PolicyChanged);
			_policy.MfaPolicyVersion = 7;
			_user.AuthenticationGeneration = 5;
			(await _service.RedeemAsync(start.Secret, completion.CompletionCode, UserSessionClientApplication.Unit)).Outcome
				.Should().Be(MfaLoginTransactionOutcome.SessionRevoked);
			_user.AuthenticationGeneration = 4;

			_clock.Now = Start.AddSeconds(TwoFactorConfig.LoginMfaCompletionCodeLifetimeSeconds);
			(await _service.RedeemAsync(start.Secret, completion.CompletionCode, UserSessionClientApplication.Unit)).Outcome
				.Should().Be(MfaLoginTransactionOutcome.Expired);
			_rows.Rows.Single().TransactionState.Should().Be(MfaLoginTransactionState.Completed, "nothing was redeemed");
		}

		[Test]
		public async Task A_recovery_code_completion_is_marked_as_recovery()
		{
			var start = await Begin();
			(await _service.CompleteAsync(await Opened(start.Secret), MfaEvidenceMethod.RecoveryCode, null, Start)).Succeeded.Should().BeTrue();
			_rows.Rows.Single().IsRecovery.Should().BeTrue();
		}

		[Test]
		public async Task A_store_fault_refuses_the_sign_in()
		{
			var start = await Begin();
			_rows.ThrowOnRead = new TimeoutException();

			(await _service.OpenAsync(start.Secret, UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.Unavailable);
			(await _service.RedeemAsync(start.Secret, "code", UserSessionClientApplication.Unit)).Outcome.Should().Be(MfaLoginTransactionOutcome.Unavailable);
		}
	}
}
