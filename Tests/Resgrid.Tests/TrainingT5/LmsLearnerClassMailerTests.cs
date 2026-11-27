using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Round 7B: class e-mails to external learners. Each is claimed before it is sent (once only), in the learner's language with the
	/// department's as the fallback, never to a learner who is not active or has no address, and reminders honour the unsubscribe (with the
	/// link in every reminder) while transactional mails do not. Department-branded and HTML-encoded.
	/// </summary>
	[TestFixture]
	public class LmsLearnerClassMailerTests
	{
		private const int Dept = 4801;
		private bool _doNotBroadcast;

		// The mailer sends only while the department may transmit. TestBase's static constructor turns DoNotBroadcast on for the whole run, so
		// the fixture sets it off here and puts it back after: otherwise these tests pass alone and fail after any TestBase fixture.
		[SetUp]
		public void SetUp()
		{
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
		}

		[TearDown]
		public void TearDown() => Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;

		private sealed class Harness
		{
			public readonly List<LmsExternalLearner> Learners = new();
			public readonly HashSet<string> Claims = new();
			public readonly List<MailMessage> Sent = new();
			public readonly List<LmsClassEnrollment> Roster = new();
			public readonly LmsClassSession Session = new LmsClassSession
			{
				LmsClassSessionId = 57, DepartmentId = Dept, Title = "CPR <b>class</b>", StartsOn = new DateTime(2026, 12, 3, 18, 0, 0, DateTimeKind.Utc),
				EndsOn = new DateTime(2026, 12, 3, 21, 0, 0, DateTimeKind.Utc), Location = "Station 3", ReminderHours = 24
			};
			public string ManagerLanguage = "fr";
			public readonly LmsLearnerClassMailer Mailer;

			public Harness()
			{
				var learners = new Mock<ILmsExternalLearnersRepository>();
				learners.Setup(l => l.GetByLearnerUserIdsAsync(Dept, It.IsAny<IEnumerable<string>>()))
					.ReturnsAsync((int d, IEnumerable<string> ids) => Learners.Where(l => ids.Contains(l.LearnerUserId, StringComparer.OrdinalIgnoreCase)).ToList());
				learners.Setup(l => l.SaveOrUpdateAsync(It.IsAny<LmsExternalLearner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsExternalLearner l, CancellationToken _, bool __) => l);
				var notices = new Mock<ILmsLearnerNoticesRepository>();
				notices.Setup(n => n.TryClaimAsync(It.IsAny<LmsLearnerNotice>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsLearnerNotice n, CancellationToken _) => Claims.Add(n.LmsExternalLearnerId + "|" + n.LmsClassSessionId + "|" + n.Kind + "|" + n.NoticeKey));
				notices.Setup(n => n.MarkAsync(Dept, It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				var portals = new Mock<ILmsLearnerPortalsRepository>();
				portals.Setup(p => p.GetByDepartmentAsync(Dept)).ReturnsAsync(new LmsLearnerPortal { DepartmentId = Dept, PortalKey = "abcdefghjkmnpqrs", IsEnabled = true, AccentColor = "#aa3300" });
				var enrollments = new Mock<ILmsClassEnrollmentsRepository>();
				enrollments.Setup(e => e.GetForSessionAsync(Dept, 57)).ReturnsAsync(() => Roster.ToList());
				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Lakeside Fire", ManagingUserId = "boss", TimeZone = "UTC" });
				var profiles = new Mock<IDepartmentProfileMediaService>();
				profiles.Setup(p => p.GetBrandingAsync(Dept)).ReturnsAsync(new DepartmentBranding { DepartmentId = Dept, DisplayName = "Lakeside Fire" });
				var users = new Mock<IUserProfileService>();
				users.Setup(u => u.GetProfileByUserIdAsync("boss", It.IsAny<bool>())).ReturnsAsync(() => new UserProfile { UserId = "boss", Language = ManagerLanguage });
				var email = new Mock<IEmailSender>();
				email.Setup(e => e.SendEmail(It.IsAny<MailMessage>())).ReturnsAsync((MailMessage m) => { Sent.Add(m); return true; });
				Mailer = new LmsLearnerClassMailer(learners.Object, notices.Object, portals.Object, enrollments.Object, departments.Object, profiles.Object, users.Object, email.Object);
			}

			public LmsExternalLearner Learner(int id, string language = null, LmsExternalLearnerStatuses status = LmsExternalLearnerStatuses.Active, bool optOut = false, string email = "pat@example.org")
			{
				var learner = new LmsExternalLearner
				{
					LmsExternalLearnerId = id, DepartmentId = Dept, LearnerUserId = LmsExternalLearnerIds.New(), Email = email, FirstName = "Pat", LastName = "Doe",
					Status = (int)status, Language = language, RemindersOptOut = optOut, UnsubscribeKey = "key-" + id
				};
				Learners.Add(learner);
				Roster.Add(new LmsClassEnrollment { LmsClassEnrollmentId = 100 + id, LmsClassSessionId = 57, DepartmentId = Dept, UserId = learner.LearnerUserId, Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = DateTime.UtcNow });
				return learner;
			}
		}

		[Test]
		public async Task Each_email_is_claimed_before_it_is_sent_so_it_goes_once()
		{
			var h = new Harness();
			var learner = h.Learner(1, "en");

			(await h.Mailer.SendAsync(Dept, h.Session, new[] { learner.LearnerUserId }, LmsLearnerNoticeKinds.Enrolled)).Should().Be(1);
			(await h.Mailer.SendAsync(Dept, h.Session, new[] { learner.LearnerUserId }, LmsLearnerNoticeKinds.Enrolled)).Should().Be(0, "the claim is taken");
			h.Sent.Should().ContainSingle().Which.To.Single().Address.Should().Be("pat@example.org");
			h.Sent.Single().IsBodyHtml.Should().BeTrue();
			h.Sent.Single().From.DisplayName.Should().Be("Lakeside Fire", "department-branded");
		}

		[Test]
		public async Task Never_to_a_learner_who_is_not_active_or_has_no_address_and_members_are_left_to_their_notices()
		{
			var h = new Harness();
			var suspended = h.Learner(2, status: LmsExternalLearnerStatuses.Suspended);
			var removed = h.Learner(3, status: LmsExternalLearnerStatuses.Removed);
			var erased = h.Learner(4, status: LmsExternalLearnerStatuses.Erased, email: null);

			(await h.Mailer.SendAsync(Dept, h.Session, new[] { suspended.LearnerUserId, removed.LearnerUserId, erased.LearnerUserId, "member-1" }, LmsLearnerNoticeKinds.Cancelled))
				.Should().Be(0);
			h.Sent.Should().BeEmpty();
			h.Claims.Should().BeEmpty("nothing is claimed for a learner who gets nothing");
		}

		[Test]
		public async Task Reminders_honour_the_unsubscribe_and_carry_its_link_while_transactional_mails_go_regardless()
		{
			var h = new Harness();
			var optedOut = h.Learner(5, "en", optOut: true);
			var subscribed = h.Learner(6, "en");

			(await h.Mailer.SendAsync(Dept, h.Session, new[] { optedOut.LearnerUserId, subscribed.LearnerUserId }, LmsLearnerNoticeKinds.Reminder)).Should().Be(1);
			h.Sent.Single().Body.Should().Contain("/Learn/abcdefghjkmnpqrs/Unsubscribe?key=key-6");

			(await h.Mailer.SendAsync(Dept, h.Session, new[] { optedOut.LearnerUserId }, LmsLearnerNoticeKinds.Cancelled)).Should().Be(1, "a cancellation is about their own place");
			h.Sent.Last().Body.Should().NotContain("Unsubscribe?key", "only reminders can be unsubscribed from");
		}

		[Test]
		public void The_learners_language_is_used_else_the_departments_else_English()
		{
			LmsLearnerMailText.CultureFor("de", "fr").TwoLetterISOLanguageName.Should().Be("de");
			LmsLearnerMailText.CultureFor(null, "fr").TwoLetterISOLanguageName.Should().Be("fr");
			LmsLearnerMailText.CultureFor("xx-YY", null).TwoLetterISOLanguageName.Should().Be("en");
			LmsLearnerMailText.CultureFor("pl-PL", null).TwoLetterISOLanguageName.Should().Be("pl");

			var h = new Harness();
			var german = h.Learner(7, "de");
			var noLanguage = h.Learner(8);
			var de = LmsLearnerClassMailer.Compose(LmsLearnerNoticeKinds.Cancelled, h.Session, h.Roster[0], h.Roster, german, LmsExternalLearnerRules.Branding(null, "Lakeside Fire", null), null,
				LmsLearnerMailText.CultureFor(german.Language, "fr"));
			de.Subject.Should().StartWith("Kurs abgesagt");
			var fr = LmsLearnerClassMailer.Compose(LmsLearnerNoticeKinds.Cancelled, h.Session, h.Roster[1], h.Roster, noLanguage, LmsExternalLearnerRules.Branding(null, "Lakeside Fire", null), null,
				LmsLearnerMailText.CultureFor(noLanguage.Language, "fr"));
			fr.Subject.Should().StartWith("Cours annulé");
		}

		[Test]
		public async Task The_department_language_is_the_fallback_when_sending()
		{
			var h = new Harness { ManagerLanguage = "es" };
			var learner = h.Learner(9);
			await h.Mailer.SendAsync(Dept, h.Session, new[] { learner.LearnerUserId }, LmsLearnerNoticeKinds.Enrolled);
			h.Sent.Single().Subject.Should().StartWith("Su plaza está confirmada");
		}

		[Test]
		public void The_mail_encodes_every_text_and_the_waitlist_mail_names_the_position()
		{
			var h = new Harness();
			var first = h.Learner(10, "en");
			var second = h.Learner(11, "en");
			h.Roster.ForEach(e => { e.Status = (int)LmsClassEnrollmentStatuses.Waitlisted; e.WaitlistPosition = (int)(e.LmsClassEnrollmentId - 100); });
			var mail = LmsLearnerClassMailer.Compose(LmsLearnerNoticeKinds.Waitlisted, h.Session, h.Roster[1], h.Roster, second, LmsExternalLearnerRules.Branding(null, "Lakeside <Fire>", null),
				null, CultureInfo.GetCultureInfo("en"));
			mail.Html.Should().NotContain("<b>class</b>").And.Contain("CPR &lt;b&gt;class&lt;/b&gt;").And.Contain("Lakeside &lt;Fire&gt;");
			mail.Html.Should().Contain("number 2 on the waitlist");
		}

		[Test]
		public void The_claim_key_makes_a_moved_class_remind_again_and_a_changed_place_tell_again()
		{
			var session = new LmsClassSession { LmsClassSessionId = 1, StartsOn = new DateTime(2026, 12, 1, 9, 0, 0), EndsOn = new DateTime(2026, 12, 1, 11, 0, 0), Location = "A" };
			var enrollment = new LmsClassEnrollment { LmsClassEnrollmentId = 4, EnrolledOn = new DateTime(2026, 11, 1) };
			var reminder = LmsLearnerNoticeRules.Key(LmsLearnerNoticeKinds.Reminder, session, enrollment);
			var changed = LmsLearnerNoticeRules.Key(LmsLearnerNoticeKinds.Changed, session, enrollment);
			var enrolled = LmsLearnerNoticeRules.Key(LmsLearnerNoticeKinds.Enrolled, session, enrollment);
			session.StartsOn = session.StartsOn.AddDays(1);
			LmsLearnerNoticeRules.Key(LmsLearnerNoticeKinds.Reminder, session, enrollment).Should().NotBe(reminder);
			LmsLearnerNoticeRules.Key(LmsLearnerNoticeKinds.Changed, session, enrollment).Should().NotBe(changed);
			LmsLearnerNoticeRules.Key(LmsLearnerNoticeKinds.Enrolled, session, enrollment).Should().Be(enrolled, "an enrolment notice is once per roster row");
			LmsLearnerNoticeRules.Key(LmsLearnerNoticeKinds.Cancelled, session, enrollment).Should().Be("cancelled");
			LmsLearnerNoticeRules.IsTransactional(LmsLearnerNoticeKinds.Reminder).Should().BeFalse();
			LmsLearnerNoticeRules.PlacementNotice(new LmsClassEnrollment { Status = (int)LmsClassEnrollmentStatuses.PendingApproval }).Should().BeNull("a pending request waits for the decision");
		}
	}
}
