using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.AccountSecurity;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 12: replacing the authenticator from an app (plan sections 6.2 and 7.5 rule 7), password
	/// reauthentication on the API, and what account deletion does to sign-in factors (section 6.4).
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AccountFactorLifecycleTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "unit-session";
		private const string NewKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

		private IHttpContextAccessor _previousAccessor;
		private IdentityUser _user;
		private Mock<UserManager<IdentityUser>> _users;
		private Dictionary<string, string> _tokens;
		private string _activeKey;
		private bool _hasPassword;
		private bool _localLoginAllowed;
		private bool _freshFirstFactor;
		private MfaEvidence _latestSecondFactor;
		private Mock<IMfaEvidenceService> _evidence;
		private Mock<IUserSessionService> _sessions;
		private Mock<ISecurityNoticeService> _notices;
		private InMemoryUserMfaStateRepository _mfaState;
		private AccountSecurityController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ApiClaims._httpContextAccessor;
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_tokens = new Dictionary<string, string>();
			_activeKey = "OLDKEYOLDKEYOLDKEYOLDKEYOLDKEY22";
			_hasPassword = true;
			_localLoginAllowed = true;
			_freshFirstFactor = true;
			_latestSecondFactor = new MfaEvidence { UserId = UserId, Method = (int)MfaEvidenceMethod.Totp, VerifiedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
			_mfaState = new InMemoryUserMfaStateRepository();

			_users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			_users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(() => _user);
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(true);
			_users.Setup(m => m.HasPasswordAsync(It.IsAny<IdentityUser>())).ReturnsAsync(() => _hasPassword);
			_users.Setup(m => m.CheckPasswordAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string password) => password == "correct horse");
			_users.Setup(m => m.GenerateNewAuthenticatorKey()).Returns(NewKey);
			_users.Setup(m => m.SetAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string provider, string name, string value) => { _tokens[provider + "/" + name] = value; return IdentityResult.Success; });
			_users.Setup(m => m.GetAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string provider, string name) => _tokens.GetValueOrDefault(provider + "/" + name));
			_users.Setup(m => m.RemoveAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser _, string provider, string name) => { _tokens.Remove(provider + "/" + name); return IdentityResult.Success; });
			_users.Setup(m => m.GenerateNewTwoFactorRecoveryCodesAsync(It.IsAny<IdentityUser>(), It.IsAny<int>())).ReturnsAsync(new[] { "NEWCD-00001" });
			_users.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.AccessFailedAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			_users.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);

			var keyStore = new Mock<IUserStore<IdentityUser>>();
			keyStore.As<IUserAuthenticatorKeyStore<IdentityUser>>()
				.Setup(s => s.SetAuthenticatorKeyAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((IdentityUser _, string key, CancellationToken _) => _activeKey = key).Returns(Task.CompletedTask);

			_evidence = new Mock<IMfaEvidenceService>();
			_evidence.Setup(e => e.HasFreshFirstFactorAsync(UserId, "sid:" + SessionId, 4, It.IsAny<TimeSpan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => _freshFirstFactor);
			_evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, "sid:" + SessionId, 4, It.IsAny<CancellationToken>())).ReturnsAsync(() => _latestSecondFactor);
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new DepartmentSecurityPolicy());
			var links = new Mock<IExternalIdentityLinkService>();
			links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _localLoginAllowed);
			_sessions = new Mock<IUserSessionService>();
			_notices = new Mock<ISecurityNoticeService>();

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
			{
				SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4
			};
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;

			_controller = new AccountSecurityController(_users.Object, Mock.Of<IPasskeyService>(), _mfaState, keyStore.Object, _evidence.Object,
				new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), gates.Object), _sessions.Object, links.Object, _notices.Object,
				Mock.Of<ISystemAuditsService>(), Mock.Of<IUserSessionsRepository>(r => r.GetByIdAsync(SessionId) == Task.FromResult(new UserSession
				{
					UserSessionId = SessionId, UserId = UserId, DeviceName = "Engine 7 tablet"
				})), Mock.Of<IMfaActivityService>(), Mock.Of<IDepartmentsService>(),
				Mock.Of<IDepartmentSsoService>())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[TearDown]
		public void TearDown() => ApiClaims._httpContextAccessor = _previousAccessor;

		private static string ProblemType(IConvertToActionResult result) =>
			result.Convert() is ObjectResult { Value: ProblemDetails problem } ? problem.Type : null;

		private static string NewCode() => TotpCalculator.ComputeCode(TotpCalculator.Base32Decode(NewKey), TotpCalculator.CurrentTimeStep(DateTime.UtcNow))
			.ToString("D6");

		private void NoEvidenceRecorded() =>
			_evidence.Verify(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<MfaEvidenceKind>(),
				It.IsAny<MfaEvidenceMethod>(), It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);

		// ---- Reauthentication ----------------------------------------------------------------------------------------

		[Test]
		public async Task A_confirmed_password_becomes_fresh_first_factor_evidence_for_this_session()
		{
			ProblemType(await _controller.Reauthenticate(new ReauthenticateInput { Password = "wrong" }, CancellationToken.None)).Should().Be("invalid_grant");
			_users.Verify(m => m.AccessFailedAsync(_user), Times.Once, "a wrong password counts toward the lockout");
			NoEvidenceRecorded();

			ProblemType(await _controller.Reauthenticate(new ReauthenticateInput { Password = "correct horse" }, CancellationToken.None)).Should().BeNull();
			_evidence.Verify(e => e.RecordAsync(UserId, "sid:" + SessionId, UserSessionClientApplication.Unit, MfaEvidenceKind.FirstFactor,
				MfaEvidenceMethod.Password, MfaEvidencePurpose.Reauthentication, It.IsAny<DateTime>(), 4, DepartmentId, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Accounts_that_sign_in_with_sso_confirm_through_their_provider()
		{
			_hasPassword = false;
			ProblemType(await _controller.Reauthenticate(new ReauthenticateInput { Password = "correct horse" }, CancellationToken.None))
				.Should().Be("sso_reauthentication_required");

			_hasPassword = true;
			_localLoginAllowed = false;
			ProblemType(await _controller.Reauthenticate(new ReauthenticateInput { Password = "correct horse" }, CancellationToken.None))
				.Should().Be("sso_reauthentication_required", "a department that requires SSO takes no password here");
			_users.Verify(m => m.CheckPasswordAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()), Times.Never);
			NoEvidenceRecorded();
		}

		// ---- Replacing the authenticator -----------------------------------------------------------------------------

		[Test]
		public async Task Replacing_the_authenticator_needs_a_fresh_first_factor_and_a_direct_second_factor()
		{
			_freshFirstFactor = false;
			ProblemType(await _controller.ReplaceTotpOptions(CancellationToken.None)).Should().Be("reauthentication_required");

			_freshFirstFactor = true;
			_latestSecondFactor = null;
			ProblemType(await _controller.ReplaceTotpOptions(CancellationToken.None)).Should().Be("step_up_required");

			_latestSecondFactor = new MfaEvidence { UserId = UserId, Method = (int)MfaEvidenceMethod.PasskeyApproval, VerifiedOnUtc = DateTime.UtcNow };
			ProblemType(await _controller.ReplaceTotpOptions(CancellationToken.None)).Should().Be("step_up_required",
				"account factors never accept Responder approval (plan section 7.6 row 14)");
			_latestSecondFactor = new MfaEvidence { UserId = UserId, Method = (int)MfaEvidenceMethod.Totp, VerifiedOnUtc = DateTime.UtcNow.AddMinutes(-6) };
			ProblemType(await _controller.ReplaceTotpOptions(CancellationToken.None)).Should().Be("step_up_required", "older than five minutes");
			_tokens.Should().BeEmpty();

			_latestSecondFactor = new MfaEvidence { UserId = UserId, Method = (int)MfaEvidenceMethod.Totp, VerifiedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
			var staged = (await _controller.ReplaceTotpOptions(CancellationToken.None)).Value.Data;
			staged.SharedKey.Replace(" ", "").ToUpperInvariant().Should().Be(NewKey);
			_activeKey.Should().NotBe(NewKey, "staged, not active");
		}

		[Test]
		public async Task A_replacement_commits_only_with_the_new_code_and_retires_everything_the_old_authenticator_authorized()
		{
			await _controller.ReplaceTotpOptions(CancellationToken.None);

			ProblemType(await _controller.ReplaceTotp(new ReplaceTotpInput { Code = "000000" }, CancellationToken.None)).Should().Be("invalid_totp");
			_activeKey.Should().NotBe(NewKey);
			_users.Verify(m => m.AccessFailedAsync(_user), Times.Once);
			_sessions.Verify(s => s.RevokeAllAfterCredentialChangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionRevocationReason>(),
				It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

			var done = (await _controller.ReplaceTotp(new ReplaceTotpInput { Code = NewCode() }, CancellationToken.None)).Value.Data;
			done.RecoveryCodes.Should().Equal("NEWCD-00001");
			done.SignInAgain.Should().BeTrue();
			_activeKey.Should().Be(NewKey);
			_user.AuthenticationGeneration.Should().Be(5);
			_sessions.Verify(s => s.RevokeAllAfterCredentialChangeAsync(UserId, UserId, UserSessionRevocationReason.MfaChanged, It.IsAny<DateTime>(),
				It.IsAny<CancellationToken>()), Times.Once);
			_evidence.Verify(e => e.RevokeForUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.TotpReplaced && r.UserId == UserId),
				It.IsAny<CancellationToken>()), Times.Once);

			ProblemType(await _controller.ReplaceTotp(new ReplaceTotpInput { Code = NewCode() }, CancellationToken.None)).Should().Be("setup_expired",
				"the staged key is spent");
			_mfaState.Totp[UserId].EnrolledInSharedMode.Should().BeFalse();
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.SharedInstallationFactor),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_authenticator_replaced_on_a_shared_installation_is_flagged_and_its_owner_warned()
		{
			// Plan section 6.5: its setup key was on a shared screen, so the account page flags it and a notice says so.
			_controller.HttpContext.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
			{
				SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4, SharedMode = true, SessionLockVersion = 0
			};
			await _controller.ReplaceTotpOptions(CancellationToken.None);
			ProblemType(await _controller.ReplaceTotp(new ReplaceTotpInput { Code = NewCode() }, CancellationToken.None)).Should().BeNull();

			_mfaState.Totp[UserId].EnrolledInSharedMode.Should().BeTrue();
			_mfaState.Totp[UserId].EnrolledClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			_mfaState.Totp[UserId].EnrolledInstallation.Should().Be("Engine 7 tablet", "the account page says where it was set up");
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.SharedInstallationFactor && r.UserId == UserId &&
				r.ClientApplication == UserSessionClientApplication.Unit), It.IsAny<CancellationToken>()), Times.Once);
		}

		// ---- Account deletion ----------------------------------------------------------------------------------------

		private static UserPasskey Passkey(string id, string userId, UserSessionClientApplication client) => new()
		{
			UserPasskeyId = id, UserId = userId, ClientApplication = (int)client, RpId = "unit.resgrid.test", CredentialId = RandomNumberGenerator.GetBytes(16),
			CredentialIdHash = RandomNumberGenerator.GetBytes(32), PublicKey = new byte[] { 1 }, UserHandle = new byte[] { 2 }, CreatedOnUtc = DateTime.UtcNow,
			StateVersion = 1
		};

		[Test]
		public async Task Deleting_an_account_retires_every_sign_in_factor_and_queued_notice_but_nobody_elses()
		{
			var now = DateTime.UtcNow;
			var passkeys = new InMemoryUserPasskeyRepository();
			passkeys.Rows.Add(Passkey("pk-unit", UserId, UserSessionClientApplication.Unit));
			passkeys.Rows.Add(Passkey("pk-responder", UserId, UserSessionClientApplication.Responder));
			passkeys.Rows.Add(Passkey("pk-other", "someone-else", UserSessionClientApplication.Unit));
			var evidence = new InMemoryMfaEvidenceRepository();
			await evidence.InsertAsync(new MfaEvidence { MfaEvidenceId = "e1", UserId = UserId, SessionKey = "sid:a", ExpiresOnUtc = now.AddHours(1) });
			var challenges = new InMemoryAuthenticationChallengeRepository();
			await challenges.InsertAsync(new AuthenticationChallenge
			{
				AuthenticationChallengeId = "c1", UserId = UserId, ExpiresOnUtc = now.AddMinutes(2), MaxAttempts = 5, State = (int)AuthenticationChallengeState.Pending
			});
			var approvals = new InMemoryMfaApprovalRequestRepository();
			await approvals.TryInsertPendingAsync(new MfaApprovalRequest
			{
				MfaApprovalRequestId = "a1", UserId = UserId, RequesterId = "s", MatchNumberHash = new byte[32], MaxAttempts = 3, CreatedOnUtc = now,
				ExpiresOnUtc = now.AddMinutes(2)
			}, now);
			var notices = new InMemorySecurityNoticeRepository();
			await notices.InsertAsync(new SecurityNotice { SecurityNoticeId = "n1", UserId = UserId, NextAttemptOnUtc = now, CreatedOnUtc = now });
			await notices.InsertAsync(new SecurityNotice { SecurityNoticeId = "n2", UserId = "someone-else", NextAttemptOnUtc = now, CreatedOnUtc = now });
			var recoveries = new InMemoryFactorRecoveryTransactionRepository();
			await recoveries.InsertAsync(new FactorRecoveryTransaction { FactorRecoveryTransactionId = "r1", UserId = UserId, SecretHash = new byte[32] });

			var activity = new InMemoryMfaActivityRepository();
			await activity.InsertAsync(new MfaActivity { MfaActivityId = "a1", UserId = UserId, OccurredOnUtc = DateTime.UtcNow });
			await activity.InsertAsync(new MfaActivity { MfaActivityId = "a2", UserId = "someone-else", OccurredOnUtc = DateTime.UtcNow });

			await new MfaAccountCleanupService(passkeys, evidence, challenges, approvals, notices, recoveries, activity, TimeProvider.System)
				.RemoveForDeletedAccountAsync(UserId, "admin-1");

			activity.Rows.Should().ContainSingle().Which.UserId.Should().Be("someone-else", "the account's MFA activity goes with it");

			passkeys.Rows.Where(p => p.UserId == UserId).Should().OnlyContain(p => !p.IsActive && p.RevocationReason == (int)PasskeyRevocationReason.AccountDeactivated,
				"revoked rows stay as tombstones, so the credential can never be registered again");
			passkeys.Rows.Single(p => p.UserPasskeyId == "pk-other").IsActive.Should().BeTrue();
			evidence.Rows.Single().RevokedOnUtc.Should().NotBeNull();
			challenges.Rows["c1"].ChallengeState.Should().Be(AuthenticationChallengeState.Canceled);
			(await approvals.GetAsync("a1")).RequestState.Should().Be(MfaApprovalRequestState.Canceled);
			notices.Rows.Select(n => n.SecurityNoticeId).Should().Equal("n2");
			recoveries.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task One_cleanup_step_failing_never_leaves_the_others_undone()
		{
			var passkeys = new InMemoryUserPasskeyRepository();
			passkeys.Rows.Add(Passkey("pk-unit", UserId, UserSessionClientApplication.Unit));
			var evidence = new Mock<IUserSessionMfaEvidenceRepository>();
			evidence.Setup(e => e.RevokeForUserAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
			var recoveries = new InMemoryFactorRecoveryTransactionRepository();
			await recoveries.InsertAsync(new FactorRecoveryTransaction { FactorRecoveryTransactionId = "r1", UserId = UserId, SecretHash = new byte[32] });

			await new MfaAccountCleanupService(passkeys, evidence.Object, new InMemoryAuthenticationChallengeRepository(), new InMemoryMfaApprovalRequestRepository(),
				new InMemorySecurityNoticeRepository(), recoveries, new InMemoryMfaActivityRepository(), TimeProvider.System).RemoveForDeletedAccountAsync(UserId, "admin-1");

			passkeys.Rows.Single().IsActive.Should().BeFalse();
			recoveries.Rows.Should().BeEmpty();
		}
	}
}
