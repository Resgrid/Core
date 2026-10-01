using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 12 (section 6.4): security notices through the user-level outbox. A notice is sent at
	/// once where possible, retried with backoff, recorded as failed (and audited) when it can never be sent, and never
	/// carries anything that acts on the account.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SecurityNoticeServiceTests
	{
		private const string UserId = "user-1";

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private sealed record Sent(string To, string Subject, string Body);

		private Clock _clock;
		private InMemorySecurityNoticeRepository _rows;
		private IdentityUser _user;
		private UserProfile _profile;
		private Mock<IEmailSender> _email;
		private Mock<ISystemAuditsService> _audits;
		private List<Sent> _sent;
		private bool _deliver;
		private string _baseUrl;
		private bool _gate;

		[SetUp]
		public void SetUp()
		{
			_gate = TwoFactorConfig.SecurityNoticesEnabled;
			TwoFactorConfig.SecurityNoticesEnabled = true;
			_baseUrl = SystemBehaviorConfig.ResgridBaseUrl;
			SystemBehaviorConfig.ResgridBaseUrl = "https://app.resgrid.test/";
			_clock = new Clock();
			_rows = new InMemorySecurityNoticeRepository();
			_user = new IdentityUser { Id = UserId, UserName = "user1", Email = "user1@example.com" };
			_profile = new UserProfile { UserId = UserId, FirstName = "Sam", Language = "en" };
			_sent = new List<Sent>();
			_deliver = true;
			_email = new Mock<IEmailSender>();
			_email.Setup(e => e.SendEmail(It.IsAny<MailMessage>())).ReturnsAsync((MailMessage mail) =>
			{
				_sent.Add(new Sent(mail.To.Single().Address, mail.Subject, mail.Body));
				return _deliver;
			});
			_audits = new Mock<ISystemAuditsService>();
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.ResgridBaseUrl = _baseUrl;
			TwoFactorConfig.SecurityNoticesEnabled = _gate;
		}

		private SecurityNoticeService Service()
		{
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetProfileByUserIdAsync(UserId, It.IsAny<bool>())).ReturnsAsync(() => _profile);
			return new SecurityNoticeService(_rows, identity.Object, profiles.Object, _email.Object, _audits.Object, _clock);
		}

		private static SecurityNoticeRequest Request(SecurityNoticeKind kind = SecurityNoticeKind.PasskeyRemoved) => new()
		{
			UserId = UserId, Kind = kind, ClientApplication = UserSessionClientApplication.Unit, InstallationLabel = "Engine 7 tablet", Region = "Ontario, Canada"
		};

		[Test]
		public async Task Nothing_is_queued_or_sent_while_the_gate_is_off()
		{
			var service = Service();
			TwoFactorConfig.SecurityNoticesEnabled = false;
			await service.QueueAsync(Request());
			await service.QueueOnceAsync(Request(SecurityNoticeKind.ApprovalSuspended), TimeSpan.FromMinutes(15));
			_rows.Rows.Should().BeEmpty();

			TwoFactorConfig.SecurityNoticesEnabled = true;
			_deliver = false;
			await service.QueueAsync(Request());
			TwoFactorConfig.SecurityNoticesEnabled = false;
			_deliver = true;
			_clock.Now = _clock.Now.AddHours(1);
			(await service.DeliverDueAsync(10)).Should().Be(0, "turning it off stops retries too");
			_rows.Rows.Single().NoticeState.Should().Be(SecurityNoticeState.Pending);
		}

		[Test]
		public async Task A_notice_is_sent_at_once_and_says_what_happened_where_and_what_to_do()
		{
			await Service().QueueAsync(Request());

			var mail = _sent.Single();
			mail.To.Should().Be("user1@example.com");
			mail.Subject.Should().Be("Security change on your Resgrid account");
			mail.Body.Should().Contain("Hi Sam,");
			mail.Body.Should().Contain("A passkey was removed from your account.");
			mail.Body.Should().Contain("When: 2026-09-29 12:00 UTC");
			mail.Body.Should().Contain("Where: Unit, Engine 7 tablet, Ontario, Canada");
			mail.Body.Should().Contain("https://app.resgrid.test/Account/ForgotPassword", "the \"this wasn't me\" path is the ordinary password reset");
			_rows.Rows.Single().NoticeState.Should().Be(SecurityNoticeState.Sent);
			_rows.Rows.Single().Attempts.Should().Be(1);
		}

		[Test]
		public async Task A_notice_is_in_the_recipients_language_and_every_kind_has_its_own_sentence()
		{
			_profile.Language = "de";
			await Service().QueueAsync(Request(SecurityNoticeKind.TotpReplaced));
			_sent.Single().Subject.Should().Be("Sicherheitsänderung an Ihrem Resgrid-Konto");
			_sent.Single().Body.Should().Contain("Hallo Sam,").And.Contain("Authentifizierungs-App");

			_profile = null;
			foreach (var kind in Enum.GetValues<SecurityNoticeKind>())
			{
				_sent.Clear();
				await Service().QueueAsync(Request(kind));
				_sent.Single().Body.Should().NotContain("SecurityNotice", $"{kind} has a sentence of its own");
				_sent.Single().Body.Should().StartWith("Hi,", "no name, no empty greeting slot");
			}
		}

		[Test]
		public async Task A_failed_send_is_retried_with_backoff_and_the_last_failure_is_audited()
		{
			var maxAttempts = TwoFactorConfig.SecurityNoticeMaxAttempts;
			TwoFactorConfig.SecurityNoticeMaxAttempts = 3;
			try
			{
				_deliver = false;
				var service = Service();
				await service.QueueAsync(Request());

				var row = _rows.Rows.Single();
				row.NoticeState.Should().Be(SecurityNoticeState.Pending);
				row.Attempts.Should().Be(1);
				row.NextAttemptOnUtc.Should().Be(_clock.Now.AddMinutes(1), "backoff starts at a minute and doubles");
				row.LastFailure.Should().Be("delivery_failed");

				(await service.DeliverDueAsync(10)).Should().Be(0, "not due yet");
				_sent.Should().HaveCount(1);

				_clock.Now = _clock.Now.AddMinutes(2);
				(await service.DeliverDueAsync(10)).Should().Be(0);
				_rows.Rows.Single().Attempts.Should().Be(2);

				_clock.Now = _clock.Now.AddHours(1);
				await service.DeliverDueAsync(10);
				_rows.Rows.Single().NoticeState.Should().Be(SecurityNoticeState.Failed, "the attempts are used up");
				_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.SecurityNoticeFailed && !x.Successful &&
					x.UserId == UserId), It.IsAny<CancellationToken>()), Times.Once);

				_clock.Now = _clock.Now.AddDays(1);
				(await service.DeliverDueAsync(10)).Should().Be(0);
				_sent.Should().HaveCount(3, "a failed notice is not retried again");
			}
			finally
			{
				TwoFactorConfig.SecurityNoticeMaxAttempts = maxAttempts;
			}
		}

		[Test]
		public async Task A_retry_that_succeeds_marks_the_notice_sent()
		{
			_deliver = false;
			var service = Service();
			await service.QueueAsync(Request());
			_deliver = true;
			_clock.Now = _clock.Now.AddMinutes(5);

			(await service.DeliverDueAsync(10)).Should().Be(1);
			_rows.Rows.Single().NoticeState.Should().Be(SecurityNoticeState.Sent);
		}

		[Test]
		public async Task A_notice_with_nowhere_to_go_fails_at_once_and_is_audited()
		{
			_user.Email = null;
			await Service().QueueAsync(Request());

			_sent.Should().BeEmpty();
			_rows.Rows.Single().NoticeState.Should().Be(SecurityNoticeState.Failed);
			_rows.Rows.Single().LastFailure.Should().Be("no_destination");
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.SecurityNoticeFailed), It.IsAny<CancellationToken>()),
				Times.Once);
		}

		[Test]
		public async Task Queueing_never_fails_the_change_it_reports()
		{
			var rows = new Mock<ISecurityNoticeRepository>();
			rows.Setup(r => r.InsertAsync(It.IsAny<SecurityNotice>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
			var service = new SecurityNoticeService(rows.Object, Mock.Of<IIdentityUserRepository>(), Mock.Of<IUserProfileService>(), _email.Object,
				_audits.Object, _clock);

			await service.Invoking(s => s.QueueAsync(Request())).Should().NotThrowAsync();

			_email.Setup(e => e.SendEmail(It.IsAny<MailMessage>())).ThrowsAsync(new SmtpException());
			await Service().Invoking(s => s.QueueAsync(Request())).Should().NotThrowAsync();
			_rows.Rows.Single().NoticeState.Should().Be(SecurityNoticeState.Pending, "a sender fault is retried like any failure");
		}

		[Test]
		public async Task A_notice_claimed_by_another_sender_is_not_sent_twice()
		{
			_deliver = false;
			await Service().QueueAsync(Request());
			_clock.Now = _clock.Now.AddMinutes(5);
			_deliver = true;

			var first = Service();
			var second = Service();
			var claimed = await _rows.ClaimDueAsync("elsewhere", _clock.Now, _clock.Now.AddMinutes(5), 10);
			claimed.Should().ContainSingle();

			(await first.DeliverDueAsync(10) + await second.DeliverDueAsync(10)).Should().Be(0, "another sender holds the lease");
			_clock.Now = _clock.Now.AddMinutes(6);
			(await first.DeliverDueAsync(10)).Should().Be(1, "an abandoned lease expires");
			(await second.DeliverDueAsync(10)).Should().Be(0);
		}

		[Test]
		public async Task A_repeated_condition_is_announced_once_per_window()
		{
			var service = Service();
			await service.QueueOnceAsync(Request(SecurityNoticeKind.ApprovalSuspended), TimeSpan.FromMinutes(15));
			await service.QueueOnceAsync(Request(SecurityNoticeKind.ApprovalSuspended), TimeSpan.FromMinutes(15));
			_rows.Rows.Should().ContainSingle();

			_clock.Now = _clock.Now.AddMinutes(16);
			await service.QueueOnceAsync(Request(SecurityNoticeKind.ApprovalSuspended), TimeSpan.FromMinutes(15));
			_rows.Rows.Should().HaveCount(2);
		}
	}
}
