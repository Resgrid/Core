using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Localization;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.AccountSecurity;
using Resgrid.Web.Services.Models.v4.Mfa;
using Resgrid.Web.Services.Models.v4.MfaApproval;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 17 (plan section 6.5): the account's recent MFA activity, "This wasn't me", Responder
	/// approval installations that can be stopped, linked identities and where the authenticator was set up. Every verification,
	/// successful or denied, is recorded without ever failing it; denials are bounded; a report ends the session the
	/// verification served but never the reporter's; and a stopped installation is no longer asked.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class MfaActivityTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "session-9";

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private Clock _clock;
		private InMemoryMfaActivityRepository _rows;
		private InMemoryUserSessionsRepository _sessions;
		private Mock<IUserSessionService> _userSessions;
		private Mock<ISecurityNoticeService> _notices;
		private Mock<ISystemAuditsService> _audits;
		private int _deniedPerHour, _retentionDays;
		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp()
		{
			_deniedPerHour = TwoFactorConfig.MfaActivityDeniedPerHour;
			_retentionDays = TwoFactorConfig.MfaActivityRetentionDays;
			_previousAccessor = ApiClaims._httpContextAccessor;
			_clock = new Clock();
			_rows = new InMemoryMfaActivityRepository();
			_sessions = new InMemoryUserSessionsRepository();
			_sessions.Add(new UserSession
			{
				UserSessionId = "tablet", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Unit, DeviceName = "Engine 7 tablet",
				SharedMode = true, State = (int)UserSessionState.Active, ExpiresOn = _clock.Now.AddHours(12)
			});
			_userSessions = new Mock<IUserSessionService>();
			_userSessions.Setup(s => s.RevokeSessionAsync(UserId, UserId, It.IsAny<string>(), UserSessionRevocationReason.AccountCompromised,
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RevocationResult { RevokedSessionCount = 1 });
			_notices = new Mock<ISecurityNoticeService>();
			_audits = new Mock<ISystemAuditsService>();
		}

		[TearDown]
		public void TearDown()
		{
			TwoFactorConfig.MfaActivityDeniedPerHour = _deniedPerHour;
			TwoFactorConfig.MfaActivityRetentionDays = _retentionDays;
			ApiClaims._httpContextAccessor = _previousAccessor;
		}

		private MfaActivityService Service(IMfaActivityRepository rows = null) =>
			new(rows ?? _rows, _sessions, _userSessions.Object, _notices.Object, _audits.Object, _clock);

		private static MfaActivityEntry Entry(bool successful = false, string sessionId = null, string label = null) => new()
		{
			UserId = UserId, Method = MfaEvidenceMethod.Totp, Purpose = MfaEvidencePurpose.StepUp, Successful = successful,
			ClientApplication = UserSessionClientApplication.Unit, DepartmentId = DepartmentId, SessionId = sessionId, InstallationLabel = label
		};

		private MfaActivity Stored(DateTime occurredOnUtc, bool successful = true, string sessionId = "tablet", string userId = UserId)
		{
			var row = new MfaActivity
			{
				MfaActivityId = Guid.NewGuid().ToString(), UserId = userId, OccurredOnUtc = occurredOnUtc, Method = (int)MfaEvidenceMethod.Passkey,
				Purpose = (int)MfaEvidencePurpose.Login, Successful = successful, ClientApplication = (int)UserSessionClientApplication.Unit,
				InstallationLabel = "Engine 7 tablet", SessionId = sessionId
			};
			_rows.Rows.Add(row);
			return row;
		}

		// ---- Recording ---------------------------------------------------------------------------------------------------

		[Test]
		public async Task Denied_verifications_are_bounded_per_hour_and_successes_never_are()
		{
			TwoFactorConfig.MfaActivityDeniedPerHour = 3;
			var service = Service();

			for (var i = 0; i < 5; i++)
				await service.RecordAsync(Entry());
			_rows.Rows.Should().HaveCount(3, "guessing cannot fill the table; the lockout still counts every attempt");

			await service.RecordAsync(Entry(successful: true));
			_rows.Rows.Should().HaveCount(4, "a success is always recorded");

			_clock.Now = _clock.Now.AddMinutes(61);
			await service.RecordAsync(Entry());
			_rows.Rows.Should().HaveCount(5, "the bound is per hour");
			_rows.Rows.Should().OnlyContain(r => r.OccurredOnUtc <= _clock.Now && r.UserId == UserId);

			await service.RecordAsync(null);
			await service.RecordAsync(new MfaActivityEntry { UserId = " " });
			_rows.Rows.Should().HaveCount(5, "nothing without an account");
		}

		[Test]
		public async Task An_entry_takes_its_installation_and_shared_flag_from_the_session_it_was_for()
		{
			await Service().RecordAsync(Entry(sessionId: "tablet"));
			await Service().RecordAsync(Entry(sessionId: "tablet", label: "Named by the caller"));
			await Service().RecordAsync(Entry(sessionId: "gone"));

			_rows.Rows[0].InstallationLabel.Should().Be("Engine 7 tablet");
			_rows.Rows[0].SharedMode.Should().BeTrue("the session was shared, whatever the caller knew");
			_rows.Rows[0].SessionId.Should().Be("tablet");
			_rows.Rows[1].InstallationLabel.Should().Be("Named by the caller");
			_rows.Rows[2].InstallationLabel.Should().BeNull();
			_rows.Rows[2].SharedMode.Should().BeFalse();

			var row = MfaActivityRecords.Create(new MfaActivityEntry
			{
				UserId = UserId, InstallationLabel = "Console\r\n2 " + new string('x', 400), SessionId = "  ", ApproverSessionId = "responder-1"
			}, null, _clock.Now);
			row.InstallationLabel.Should().StartWith("Console  2 x").And.HaveLength(256, "labels are one bounded line");
			row.SessionId.Should().BeNull();
			row.ApproverSessionId.Should().Be("responder-1");
			row.MfaActivityId.Should().NotBeNullOrEmpty();
		}

		[Test]
		public async Task Recording_never_fails_the_verification_it_describes()
		{
			var broken = new Mock<IMfaActivityRepository>();
			broken.Setup(r => r.InsertAsync(It.IsAny<MfaActivity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
			broken.Setup(r => r.CountDeniedSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			await Service(broken.Object).Invoking(s => s.RecordAsync(Entry())).Should().NotThrowAsync();
			await Service(broken.Object).Invoking(s => s.RecordAsync(Entry(successful: true))).Should().NotThrowAsync();

			broken.Setup(r => r.InsertAsync(It.IsAny<MfaActivity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
			await Service(broken.Object).Invoking(s => s.RecordAsync(Entry(successful: true))).Should().ThrowAsync<OperationCanceledException>(
				"a cancelled request is still cancelled");
		}

		[Test]
		public async Task Recent_activity_is_the_accounts_own_newest_first_within_retention_and_at_most_a_hundred()
		{
			TwoFactorConfig.MfaActivityRetentionDays = 30;
			var newest = Stored(_clock.Now);
			var older = Stored(_clock.Now.AddDays(-29));
			Stored(_clock.Now.AddDays(-31));
			Stored(_clock.Now, userId: "someone-else");

			(await Service().GetRecentAsync(UserId)).Select(a => a.MfaActivityId).Should().Equal(newest.MfaActivityId, older.MfaActivityId);

			for (var i = 0; i < 120; i++)
				Stored(_clock.Now.AddMinutes(-i));
			(await Service().GetRecentAsync(UserId)).Should().HaveCount(100);
		}

		[Test]
		public async Task A_verification_is_recorded_as_activity_only_when_it_is_a_second_factor_or_a_recovery()
		{
			var evidence = new MfaEvidenceService(new InMemoryMfaEvidenceRepository(), new InMemoryUserMfaStateRepository(), Mock.Of<IUserPasskeyRepository>(),
				_sessions, _rows, _clock);
			var key = MfaEvidence.TrackedSessionKey("tablet");

			await evidence.RecordAsync(UserId, key, UserSessionClientApplication.Unit, MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password,
				MfaEvidencePurpose.Reauthentication, _clock.Now, 4);
			_rows.Rows.Should().BeEmpty("a password is not an MFA verification");

			await evidence.RecordAsync(UserId, key, UserSessionClientApplication.Unit, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Passkey,
				MfaEvidencePurpose.SharedUnlock, _clock.Now.AddSeconds(-5), 4, DepartmentId,
				MfaApprovalRequest.FactorReferenceFor("pk-1", "responder-1"));
			await evidence.RecordAsync(UserId, "web:cookie", UserSessionClientApplication.Web, MfaEvidenceKind.Recovery, MfaEvidenceMethod.RecoveryCode,
				MfaEvidencePurpose.Login, _clock.Now, 4);

			var passkey = _rows.Rows[0];
			passkey.Successful.Should().BeTrue();
			passkey.Method.Should().Be((int)MfaEvidenceMethod.Passkey);
			passkey.Purpose.Should().Be((int)MfaEvidencePurpose.SharedUnlock);
			passkey.OccurredOnUtc.Should().Be(_clock.Now.AddSeconds(-5), "when it was verified, not when it was written");
			passkey.SessionId.Should().Be("tablet");
			passkey.InstallationLabel.Should().Be("Engine 7 tablet");
			passkey.SharedMode.Should().BeTrue();
			passkey.DepartmentId.Should().Be(DepartmentId);
			passkey.ApproverSessionId.Should().BeNull("only an approval names an approver");

			var recovery = _rows.Rows[1];
			recovery.Method.Should().Be((int)MfaEvidenceMethod.RecoveryCode);
			recovery.SessionId.Should().BeNull("an untracked session key names no session");

			var failing = new Mock<IMfaActivityRepository>();
			failing.Setup(r => r.InsertAsync(It.IsAny<MfaActivity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
			var evidenceRows = new InMemoryMfaEvidenceRepository();
			await new MfaEvidenceService(evidenceRows, new InMemoryUserMfaStateRepository(), Mock.Of<IUserPasskeyRepository>(), _sessions, failing.Object, _clock)
				.RecordAsync(UserId, key, UserSessionClientApplication.Unit, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp, MfaEvidencePurpose.StepUp,
					_clock.Now, 4);
			evidenceRows.Rows.Should().ContainSingle("the evidence stands when its history row cannot be written");
		}

		// ---- "This wasn't me" --------------------------------------------------------------------------------------------

		[Test]
		public async Task A_report_ends_the_session_the_verification_served_says_so_once_and_tells_the_account_holder()
		{
			var used = Stored(_clock.Now.AddMinutes(-10), sessionId: "tablet");
			var request = new SharedSessionRequestInfo { UserName = "user1", IpAddress = "203.0.113.9", CorrelationId = "trace-1" };

			var report = await Service().ReportAsync(UserId, used.MfaActivityId, "my-phone", request);

			report.Outcome.Should().Be(MfaActivityReportOutcome.Reported);
			report.SessionEnded.Should().BeTrue();
			_rows.Rows.Single(r => r.MfaActivityId == used.MfaActivityId).ReportedOnUtc.Should().Be(_clock.Now);
			_userSessions.Verify(s => s.RevokeSessionAsync(UserId, UserId, "tablet", UserSessionRevocationReason.AccountCompromised, It.IsAny<CancellationToken>()),
				Times.Once);
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.MfaActivityReported && x.UserId == UserId &&
				x.Successful && x.IpAddress == "203.0.113.9" && x.CorrelationId == "trace-1"), It.IsAny<CancellationToken>()), Times.Once);
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.UserId == UserId && r.Kind == SecurityNoticeKind.ActivityReported &&
				r.ClientApplication == UserSessionClientApplication.Unit && r.InstallationLabel == "Engine 7 tablet"), It.IsAny<CancellationToken>()), Times.Once);

			(await Service().ReportAsync(UserId, used.MfaActivityId, "my-phone", request)).Outcome.Should().Be(MfaActivityReportOutcome.AlreadyReported);
			_userSessions.Verify(s => s.RevokeSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionRevocationReason>(),
				It.IsAny<CancellationToken>()), Times.Once, "a second report changes nothing");
			_notices.Verify(n => n.QueueAsync(It.IsAny<SecurityNoticeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_report_never_ends_the_reporting_session_and_a_denial_or_sign_in_in_progress_ends_nothing()
		{
			var mine = Stored(_clock.Now, sessionId: "my-phone");
			var denied = Stored(_clock.Now, successful: false, sessionId: "tablet");
			var noSession = Stored(_clock.Now, sessionId: null);

			foreach (var activity in new[] { mine, denied, noSession })
			{
				var report = await Service().ReportAsync(UserId, activity.MfaActivityId, "my-phone", null);
				report.Outcome.Should().Be(MfaActivityReportOutcome.Reported);
				report.SessionEnded.Should().BeFalse();
			}

			_userSessions.Verify(s => s.RevokeSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionRevocationReason>(),
				It.IsAny<CancellationToken>()), Times.Never);
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.ActivityReported), It.IsAny<CancellationToken>()),
				Times.Exactly(3), "every report is still told to the account holder");
		}

		[Test]
		public async Task Only_the_accounts_own_activity_inside_retention_can_be_reported()
		{
			TwoFactorConfig.MfaActivityRetentionDays = 30;
			var theirs = Stored(_clock.Now, userId: "someone-else");
			var old = Stored(_clock.Now.AddDays(-31));

			foreach (var id in new[] { theirs.MfaActivityId, old.MfaActivityId, "missing", "", null })
				(await Service().ReportAsync(UserId, id, "my-phone", null)).Outcome.Should().Be(MfaActivityReportOutcome.NotFound);

			_rows.Rows.Should().OnlyContain(r => r.ReportedOnUtc == null);
			_notices.Verify(n => n.QueueAsync(It.IsAny<SecurityNoticeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- API surfaces --------------------------------------------------------------------------------------------------

		private static DefaultHttpContext ApiContext(bool withSession = true, bool shared = false,
			UserSessionClientApplication client = UserSessionClientApplication.Unit)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ClaimTypes.Name, "user1")
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			if (withSession)
				http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
				{
					SessionId = SessionId, ClientApplication = (int)client, AuthenticationGeneration = 4, SharedMode = shared
				};
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;
			return http;
		}

		private static string ProblemType(IConvertToActionResult result) =>
			result.Convert() is ObjectResult { Value: ProblemDetails problem } ? problem.Type : null;

		private static int? Status(IConvertToActionResult result) => (result.Convert() as ObjectResult)?.StatusCode;

		private static Mock<UserManager<IdentityUser>> Users(IdentityUser user, bool codeValid = false)
		{
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
			users.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(false);
			users.Setup(m => m.CountRecoveryCodesAsync(user)).ReturnsAsync(8);
			users.Setup(m => m.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(codeValid);
			users.Setup(m => m.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);
			return users;
		}

		private static IdentityUser User() => new() { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };

		private static MfaPolicyService Policy()
		{
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, AllowPasskeysForLoginMfa = true });
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			return new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), gates.Object);
		}

		[Test]
		public async Task A_failed_step_up_is_recorded_as_denied_activity_for_the_session_that_tried()
		{
			var user = User();
			var users = Users(user);
			users.Setup(m => m.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), "123456")).ReturnsAsync(true);
			var activity = new Mock<IMfaActivityService>();
			var recorded = new List<MfaActivityEntry>();
			activity.Setup(a => a.RecordAsync(It.IsAny<MfaActivityEntry>(), It.IsAny<CancellationToken>()))
				.Callback((MfaActivityEntry entry, CancellationToken _) => recorded.Add(entry)).Returns(Task.CompletedTask);
			var passkeys = new Mock<IPasskeyService>();
			passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), It.IsAny<AuthenticationChallengePurpose>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(PasskeyAssertionResult.Of(PasskeyOutcome.VerificationFailed));
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			var controller = new MfaController(users.Object, Policy(), Mock.Of<IMfaEvidenceService>(), cache.Object, Mock.Of<ISystemAuditsService>(),
				passkeys.Object, Mock.Of<IDepartmentSsoService>(), Mock.Of<ISsoBrokerService>(), Mock.Of<IMfaApprovalService>(), activity.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = ApiContext(shared: true) }
			};

			ProblemType(await controller.VerifyStepUp(new VerifyStepUpInput { Operation = MfaStepUpOperations.SecurityChange, Code = "000000" },
				CancellationToken.None)).Should().Be("invalid_totp");
			ProblemType(await controller.VerifyStepUp(new VerifyStepUpInput
			{
				Operation = MfaStepUpOperations.SecurityChange, Method = MfaMethodNames.Passkey, RequestId = "req-1",
				Credential = JObject.Parse("{\"id\":\"abc\",\"type\":\"public-key\"}")
			}, CancellationToken.None)).Should().NotBeNull();

			(await controller.VerifyStepUp(new VerifyStepUpInput { Operation = MfaStepUpOperations.SecurityChange, Code = "123456" }, CancellationToken.None))
				.Value.Should().NotBeNull();

			recorded.Select(e => e.Method).Should().Equal(new[] { MfaEvidenceMethod.Totp, MfaEvidenceMethod.Passkey },
				"a success is recorded with its evidence, not here");
			recorded.Should().OnlyContain(e => !e.Successful && e.UserId == UserId && e.Purpose == MfaEvidencePurpose.StepUp &&
				e.ClientApplication == UserSessionClientApplication.Unit && e.SessionId == SessionId && e.SharedMode && e.DepartmentId == DepartmentId);
		}

		[Test]
		public async Task A_wrong_code_at_web_step_up_is_recorded_as_denied_activity_for_that_web_session()
		{
			var user = User();
			var users = Users(user);
			users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
			var activity = new Mock<IMfaActivityService>();
			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var controller = new Resgrid.Web.Areas.User.Controllers.TwoFactorController(users.Object, null, Mock.Of<ISystemAuditsService>(),
				System.Text.Encodings.Web.UrlEncoder.Default, localizer.Object, Mock.Of<IUserStore<IdentityUser>>(), new InMemoryUserMfaStateRepository(),
				Mock.Of<IUserSessionService>(), Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>(), new InMemoryUserPasskeyRepository(),
				Mock.Of<ISecurityNoticeService>(), activity.Object, Mock.Of<IPasskeyService>(), Mock.Of<IMfaApprovalService>(), Mock.Of<ISsoBrokerService>(),
				Mock.Of<ISsoReturnTargetRegistry>(), Mock.Of<IDepartmentSsoService>(), Mock.Of<IDepartmentsService>())
			{
				ControllerContext = new ControllerContext
				{
					HttpContext = new DefaultHttpContext
					{
						User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(SessionClaimTypes.SessionId, "web-session") }, "test"))
					}
				}
			};

			await controller.Verify2FA(new Resgrid.Web.Areas.User.Models.TwoFactor.StepUpVerifyViewModel { Code = "000000" }, CancellationToken.None);

			users.Verify(m => m.AccessFailedAsync(user), Times.Once);
			activity.Verify(a => a.RecordAsync(It.Is<MfaActivityEntry>(e => e.UserId == UserId && !e.Successful && e.Method == MfaEvidenceMethod.Totp &&
				e.Purpose == MfaEvidencePurpose.StepUp && e.ClientApplication == UserSessionClientApplication.Web && e.SessionId == "web-session"),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		private AccountSecurityController AccountController(IMfaActivityService activity, IUserSessionsRepository sessionRows = null,
			IExternalIdentityLinkService links = null, IMfaPolicyService policy = null, IDepartmentSsoService departmentSso = null,
			IReadOnlyList<UserPasskey> passkeys = null, IUserMfaStateRepository mfaState = null, bool withSession = true)
		{
			var passkeyService = new Mock<IPasskeyService>();
			passkeyService.Setup(p => p.GetActiveForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(passkeys ?? new List<UserPasskey>());
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Station 42" });
			return new AccountSecurityController(Users(User()).Object, passkeyService.Object, mfaState ?? new InMemoryUserMfaStateRepository(),
				Mock.Of<IUserStore<IdentityUser>>(), Mock.Of<IMfaEvidenceService>(), policy ?? Mock.Of<IMfaPolicyService>(), Mock.Of<IUserSessionService>(),
				links ?? Mock.Of<IExternalIdentityLinkService>(), Mock.Of<ISecurityNoticeService>(), Mock.Of<ISystemAuditsService>(),
				sessionRows ?? Mock.Of<IUserSessionsRepository>(), activity, departments.Object, departmentSso ?? Mock.Of<IDepartmentSsoService>())
			{
				ControllerContext = new ControllerContext { HttpContext = ApiContext(withSession) }
			};
		}

		private static UserSession Responder(string id, DateTime now, DateTime? stoppedOn = null, bool shared = false, long generation = 4,
			UserSessionClientApplication client = UserSessionClientApplication.Responder) => new()
		{
			UserSessionId = id, UserId = UserId, ClientApplication = (int)client, State = (int)UserSessionState.Active, ExpiresOn = DateTime.UtcNow.AddDays(1),
			AuthenticationGeneration = generation, DeviceName = id + " label", OperatingSystem = "Android", SharedMode = shared, ApprovalsDisabledOnUtc = stoppedOn,
			LastActiveOn = now
		};

		[Test]
		public async Task The_methods_view_shows_approval_installations_linked_identities_recent_activity_and_where_the_authenticator_came_from()
		{
			var now = DateTime.UtcNow;
			var activityRows = new List<MfaActivity>
			{
				new() { MfaActivityId = "a1", UserId = UserId, OccurredOnUtc = now.AddMinutes(-1), Method = (int)MfaEvidenceMethod.PasskeyApproval,
					Purpose = (int)MfaEvidencePurpose.StepUp, Successful = false, ClientApplication = (int)UserSessionClientApplication.Unit,
					InstallationLabel = "Engine 7 tablet", SharedMode = true, SessionId = "tablet", ApproverSessionId = "phone" },
				new() { MfaActivityId = "a2", UserId = UserId, OccurredOnUtc = now.AddMinutes(-2), Method = (int)MfaEvidenceMethod.Totp,
					Purpose = (int)MfaEvidencePurpose.Login, Successful = true, ClientApplication = (int)UserSessionClientApplication.Dispatch,
					InstallationLabel = "Console 2", SessionId = SessionId, ReportedOnUtc = now },
				new() { MfaActivityId = "a3", UserId = UserId, OccurredOnUtc = now.AddMinutes(-3), Method = (int)MfaEvidenceMethod.Federated,
					Purpose = (int)MfaEvidencePurpose.StepUp, Successful = true, ClientApplication = (int)UserSessionClientApplication.Web, DepartmentId = DepartmentId },
				new() { MfaActivityId = "a4", UserId = UserId, OccurredOnUtc = now.AddMinutes(-4), Method = (int)MfaEvidenceMethod.PasskeyApproval,
					Purpose = (int)MfaEvidencePurpose.Login, Successful = true, ClientApplication = (int)UserSessionClientApplication.Unit, ApproverSessionId = "phone" },
				new() { MfaActivityId = "a5", UserId = UserId, OccurredOnUtc = now.AddMinutes(-5), Method = (int)MfaEvidenceMethod.RecoveryCode,
					Purpose = (int)MfaEvidencePurpose.AdpStepUp, Successful = true, ClientApplication = (int)UserSessionClientApplication.Web }
			};
			var activity = new Mock<IMfaActivityService>();
			activity.Setup(a => a.GetRecentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(activityRows);
			var sessionRows = new Mock<IUserSessionsRepository>();
			sessionRows.Setup(s => s.GetActiveByUserAsync(UserId, It.IsAny<DateTime>())).ReturnsAsync(new List<UserSession>
			{
				Responder("phone", now),
				Responder("old-phone", now.AddDays(-1), stoppedOn: now.AddHours(-1)),
				Responder("shared-responder", now, shared: true),
				Responder("before-password-change", now, generation: 3),
				Responder("tablet", now, client: UserSessionClientApplication.Unit)
			});
			var links = new Mock<IExternalIdentityLinkService>();
			links.Setup(l => l.GetActiveLinksAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<UserExternalIdentityLink>
			{
				new() { UserId = UserId, DepartmentId = DepartmentId, ProviderType = (int)SsoProviderType.Oidc, LinkedOn = now.AddDays(-10), IsActive = true },
				new() { UserId = UserId, DepartmentId = 7, ProviderType = (int)SsoProviderType.Saml2, LinkedOn = now.AddDays(-20), IsActive = true }
			});
			var policy = new Mock<IMfaPolicyService>();
			policy.Setup(p => p.IsMethodAcceptedAsync(It.IsAny<int?>(), MfaMethodScope.Login, MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var mfaState = new InMemoryUserMfaStateRepository();
			await mfaState.TryConsumeTotpTimeStepAsync(UserId, 100, now.AddMinutes(-2));
			await mfaState.RecordTotpEnrollmentAsync(UserId, now.AddDays(-3), new TotpEnrollmentContext(true, (int)UserSessionClientApplication.Unit, "Engine 7 tablet"));
			var approving = new UserPasskey
			{
				UserPasskeyId = "pk-responder", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Responder, ApprovalEnabled = true,
				DisplayName = "Phone", CreatedOnUtc = now.AddDays(-5)
			};

			var data = (await AccountController(activity.Object, sessionRows.Object, links.Object, policy.Object, sso.Object, new[] { approving }, mfaState)
				.Methods(CancellationToken.None)).Value.Data;

			data.Totp.EnrolledClient.Should().Be("unit");
			data.Totp.EnrolledInstallation.Should().Be("Engine 7 tablet");
			data.Totp.SetUpOnSharedInstallation.Should().BeTrue();
			data.Totp.LastUsedClient.Should().Be("dispatch");
			data.Totp.LastUsedInstallation.Should().Be("Console 2");

			data.ApprovalInstallations.Select(i => i.InstallationId).Should().Equal(new[] { "phone", "old-phone" },
				"only current, personal Responder sessions; shared ones and those from before a password change never approve");
			var phone = data.ApprovalInstallations[0];
			phone.Label.Should().Be("phone label");
			phone.Platform.Should().Be("Android");
			phone.ApprovalsOn.Should().BeTrue();
			phone.StoppedOn.Should().BeNull();
			phone.LastDecision.Should().Be("denied", "its most recent decision");
			phone.LastDecisionOn.Should().Be(ApiPasskeysIso(now.AddMinutes(-1)));
			phone.IsCurrent.Should().BeFalse();
			var stopped = data.ApprovalInstallations[1];
			stopped.ApprovalsOn.Should().BeFalse();
			stopped.StoppedOn.Should().Be(ApiPasskeysIso(now.AddHours(-1)));
			stopped.LastDecision.Should().BeNull();

			data.LinkedIdentities.Should().HaveCount(2);
			var linked = data.LinkedIdentities.Single(l => l.DepartmentId == DepartmentId);
			linked.DepartmentName.Should().Be("Station 42");
			linked.ProviderType.Should().Be("oidc");
			linked.AcceptsProviderStepUp.Should().BeTrue();
			linked.LastProviderStepUpOn.Should().Be(ApiPasskeysIso(now.AddMinutes(-3)));
			var other = data.LinkedIdentities.Single(l => l.DepartmentId == 7);
			other.ProviderType.Should().Be("saml2");
			other.AcceptsProviderStepUp.Should().BeFalse("its mapping is not tested");
			other.LastProviderStepUpOn.Should().BeNull();

			data.RecentActivity.Select(a => a.ActivityId).Should().Equal("a1", "a2", "a3", "a4", "a5");
			data.RecentActivity.Select(a => a.Method).Should().Equal("passkey_approval", "totp", "federated", "passkey_approval", "recovery_code");
			data.RecentActivity.Select(a => a.Purpose).Should().Equal("step_up", "login", "step_up", "login", "adp_step_up");
			var denied = data.RecentActivity[0];
			denied.Successful.Should().BeFalse();
			denied.Client.Should().Be("unit");
			denied.Installation.Should().Be("Engine 7 tablet");
			denied.SharedInstallation.Should().BeTrue();
			denied.IsCurrentSession.Should().BeFalse();
			data.RecentActivity[1].IsCurrentSession.Should().BeTrue();
			data.RecentActivity[1].ReportedOn.Should().NotBeNull();

			// Without an approving passkey nothing is asked, whatever the installation says.
			var noPasskey = (await AccountController(activity.Object, sessionRows.Object).Methods(CancellationToken.None)).Value.Data;
			noPasskey.ApprovalInstallations.Should().OnlyContain(i => !i.ApprovalsOn);
			noPasskey.LinkedIdentities.Should().BeEmpty();
		}

		private static string ApiPasskeysIso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O");

		[Test]
		public async Task Reporting_activity_from_the_account_page_needs_a_session_and_says_what_happened()
		{
			var activity = new Mock<IMfaActivityService>();
			activity.Setup(a => a.ReportAsync(UserId, "a1", SessionId, It.IsAny<SharedSessionRequestInfo>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaActivityReport { Outcome = MfaActivityReportOutcome.Reported, SessionEnded = true });
			activity.Setup(a => a.ReportAsync(UserId, "a2", SessionId, It.IsAny<SharedSessionRequestInfo>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaActivityReport { Outcome = MfaActivityReportOutcome.AlreadyReported });
			activity.Setup(a => a.ReportAsync(UserId, "a3", SessionId, It.IsAny<SharedSessionRequestInfo>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaActivityReport { Outcome = MfaActivityReportOutcome.NotFound });
			var controller = AccountController(activity.Object);

			var reported = (await controller.ReportActivity(new ReportActivityInput { ActivityId = " a1 " }, CancellationToken.None)).Value.Data;
			reported.SessionEnded.Should().BeTrue();
			reported.NextSteps.Should().Equal("change_password", "review_methods");
			activity.Verify(a => a.ReportAsync(UserId, "a1", SessionId, It.Is<SharedSessionRequestInfo>(r => r.UserName == "user1"), It.IsAny<CancellationToken>()),
				Times.Once);

			var again = await controller.ReportActivity(new ReportActivityInput { ActivityId = "a2" }, CancellationToken.None);
			ProblemType(again).Should().Be("activity_already_reported");
			Status(again).Should().Be(StatusCodes.Status409Conflict);
			var missing = await controller.ReportActivity(new ReportActivityInput { ActivityId = "a3" }, CancellationToken.None);
			ProblemType(missing).Should().Be("activity_not_found");
			Status(missing).Should().Be(StatusCodes.Status404NotFound);
			ProblemType(await controller.ReportActivity(new ReportActivityInput(), CancellationToken.None)).Should().Be("invalid_request");
			ProblemType(await controller.ReportActivity(null, CancellationToken.None)).Should().Be("invalid_request");

			ProblemType(await AccountController(activity.Object, withSession: false).ReportActivity(new ReportActivityInput { ActivityId = "a1" },
				CancellationToken.None)).Should().Be("session_required");
			activity.Verify(a => a.ReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SharedSessionRequestInfo>(),
				It.IsAny<CancellationToken>()), Times.Exactly(3));
		}

		[Test]
		public async Task Stopping_approval_installations_needs_a_fresh_authenticator_or_passkey_never_an_approval()
		{
			var approvals = new Mock<IMfaApprovalService>();
			approvals.Setup(a => a.DisableInstallationsAsync(UserId, It.IsAny<string>(), It.IsAny<SharedSessionRequestInfo>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((2, 1));
			MfaEvidence latest = null;
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, MfaEvidence.TrackedSessionKey(SessionId), 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => latest);
			MfaApprovalController Controller(bool withSession = true) =>
				new(approvals.Object, Mock.Of<IMfaLoginTransactionService>(), Mock.Of<IDepartmentsService>(), Users(User()).Object, evidence.Object, Policy(),
					Mock.Of<IAdpStepUpService>())
				{
					ControllerContext = new ControllerContext { HttpContext = ApiContext(withSession) }
				};
			MfaEvidence Verified(MfaEvidenceMethod method, int minutesAgo = 1) => new()
			{
				UserId = UserId, Method = (int)method, Kind = (int)MfaEvidenceKind.SecondFactor, Purpose = (int)MfaEvidencePurpose.StepUp,
				ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4, VerifiedOnUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
				ExpiresOnUtc = DateTime.UtcNow.AddHours(1)
			};

			ProblemType(await Controller(withSession: false).DisableInstallations(new DisableApprovalInstallationsInput { All = true }, CancellationToken.None))
				.Should().Be(MfaApprovalOutcomes.ErrorCode(MfaApprovalOutcome.SessionRequired));
			foreach (var invalid in new[] { null, new DisableApprovalInstallationsInput(), new DisableApprovalInstallationsInput { InstallationId = "phone", All = true },
				new DisableApprovalInstallationsInput { InstallationId = "  " } })
				ProblemType(await Controller().DisableInstallations(invalid, CancellationToken.None)).Should().Be("invalid_request", "one installation, or all");

			foreach (var (step, why) in new[]
			{
				((MfaEvidence)null, "nothing verified"),
				(Verified(MfaEvidenceMethod.Totp, minutesAgo: 6), "older than five minutes"),
				(Verified(MfaEvidenceMethod.PasskeyApproval), "an approval cannot stop approvals"),
				(Verified(MfaEvidenceMethod.Federated), "provider step-up never changes account factors")
			})
			{
				latest = step;
				var refused = await Controller().DisableInstallations(new DisableApprovalInstallationsInput { InstallationId = "phone" }, CancellationToken.None);
				ProblemType(refused).Should().Be("step_up_required", why);
				Status(refused).Should().Be(StatusCodes.Status403Forbidden);
			}

			approvals.Verify(a => a.DisableInstallationsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SharedSessionRequestInfo>(),
				It.IsAny<CancellationToken>()), Times.Never);

			latest = Verified(MfaEvidenceMethod.Totp);
			var one = (await Controller().DisableInstallations(new DisableApprovalInstallationsInput { InstallationId = " phone " }, CancellationToken.None)).Value.Data;
			one.InstallationsStopped.Should().Be(2);
			one.PasskeysStopped.Should().Be(1);
			approvals.Verify(a => a.DisableInstallationsAsync(UserId, "phone", It.Is<SharedSessionRequestInfo>(r => r.UserName == "user1"),
				It.IsAny<CancellationToken>()), Times.Once);

			latest = Verified(MfaEvidenceMethod.Passkey);
			(await Controller().DisableInstallations(new DisableApprovalInstallationsInput { All = true }, CancellationToken.None)).Value.Should().NotBeNull();
			approvals.Verify(a => a.DisableInstallationsAsync(UserId, null, It.IsAny<SharedSessionRequestInfo>(), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
