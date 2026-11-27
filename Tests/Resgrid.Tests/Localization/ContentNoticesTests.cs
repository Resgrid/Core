using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Localization.Areas.User.ContentNotices;
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Approvals;
using Resgrid.Services.Signatures;
using Resgrid.Services.Wellness;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// Round 9C: every e-mail / push text the Protocols + Training/LMS work sends comes from ContentNotices and is written in the recipient's
	/// language (the member's profile language, else the department default: its managing member's language, else English). The learner
	/// portal's account e-mails are checked in all ten locales.
	/// </summary>
	[TestFixture]
	public class ContentNoticesTests
	{
		private static readonly string[] Locales = { "en", "ar", "de", "el", "es", "fr", "it", "pl", "sv", "uk" };
		private const int Dept = 41;
		private const string Url = "https://example.test/Learn/abcdefghjkmnpqrs/Accept?token=t0k3n";

		private static string AreaFile(string locale)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			directory.Should().NotBeNull();
			return Path.Combine(directory!.FullName, "Core", "Resgrid.Localization", "Areas", "User", "ContentNotices", $"ContentNotices.{locale}.resx");
		}

		private static Dictionary<string, string> Strings(string locale) =>
			XDocument.Load(AreaFile(locale)).Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value") ?? string.Empty);

		private static string Fill(string text, CultureInfo culture, params object[] args) => string.Format(culture, text, args);

		#region The area

		[Test]
		public void Every_locale_has_every_key_with_the_same_placeholders_and_a_machine_translation_mark()
		{
			var english = Strings("en");
			var placeholder = new Regex(@"\{(\d+)(:[^}]*)?\}");
			english.Should().NotBeEmpty();
			foreach (var locale in Locales.Where(l => l != "en"))
			{
				var translated = Strings(locale);
				translated.Keys.Should().BeEquivalentTo(english.Keys, $"{locale} carries every notice");
				foreach (var pair in english)
					placeholder.Matches(translated[pair.Key]).Select(m => m.Value).OrderBy(v => v)
						.Should().Equal(placeholder.Matches(pair.Value).Select(m => m.Value).OrderBy(v => v), $"{locale} {pair.Key} keeps the placeholders");
				XDocument.Load(AreaFile(locale)).Root!.Elements("data").Should().OnlyContain(d => (string)d.Element("comment") == "machine-translated",
					"machine translations are marked for a later human review");
			}
		}

		[Test]
		public void The_culture_is_the_first_supported_language_else_english()
		{
			ContentNoticeText.CultureFor(null, "", "pt-BR", "de-AT", "fr").Name.Should().Be("de");
			ContentNoticeText.CultureFor("xx", null).Name.Should().Be("en");
			ContentNoticeText.SupportedCulture("uk-UA")!.Name.Should().Be("uk");
			ContentNoticeText.Get("No.Such.Key", CultureInfo.GetCultureInfo("de")).Should().Be("No.Such.Key");
		}

		#endregion The area

		#region Learner portal account e-mails

		[Test]
		public void Account_emails_are_written_from_each_locale([ValueSource(nameof(Locales))] string locale)
		{
			var culture = CultureInfo.GetCultureInfo(locale);
			var text = Strings(locale);

			var invite = LmsLearnerAccountMail.Compose(LmsLearnerAccountMailKinds.Invite, "Casey", "Station 9", Url, TimeSpan.FromDays(14), culture);
			invite.Subject.Should().Be(Fill(text["Account.Invite.Subject"], culture, "Station 9"));
			invite.Body.Should().StartWith(Fill(text["Account.Greeting"], culture, "Casey"))
				.And.Contain(Fill(text["Account.Invite.Body"], culture, "Station 9") + "\r\n" + Url + "\r\n")
				.And.EndWith(Fill(text["Account.Invite.Lifetime"], culture, 14));
			invite.Culture.Name.Should().Be(locale);

			var verify = LmsLearnerAccountMail.Compose(LmsLearnerAccountMailKinds.VerifyEmail, "Casey", "Station 9", Url, TimeSpan.FromHours(48), culture);
			verify.Subject.Should().Be(text["Account.Verify.Subject"]);
			verify.Body.Should().Contain(text["Account.Verify.Body"] + "\r\n" + Url).And.EndWith(Fill(text["Account.Verify.Lifetime"], culture, 48));

			var reset = LmsLearnerAccountMail.Compose(LmsLearnerAccountMailKinds.ResetPassword, "Casey", "Station 9", Url, TimeSpan.FromHours(2), culture);
			reset.Subject.Should().Be(text["Account.Reset.Subject"]);
			reset.Body.Should().Contain(text["Account.Reset.Body"] + "\r\n" + Url).And.EndWith(Fill(text["Account.Reset.Lifetime"], culture, 2));

			var exists = LmsLearnerAccountMail.Compose(LmsLearnerAccountMailKinds.AlreadyRegistered, null, "Station 9", Url, TimeSpan.Zero, culture);
			exists.Subject.Should().Be(text["Account.Exists.Subject"]);
			exists.Body.Should().StartWith(text["Account.Exists.Body"] + "\r\n" + Url).And.EndWith(text["Account.Exists.Ignore"]);

			var signIn = LmsLearnerAccountMail.Compose(LmsLearnerAccountMailKinds.SignInReminder, "Casey", "Station 9", Url, TimeSpan.Zero, culture);
			signIn.Body.Should().Contain(text["Account.SignIn.Body"] + "\r\n" + Url);

			foreach (var mail in new[] { invite, verify, reset, exists, signIn })
				(mail.Subject + mail.Body).Should().NotMatchRegex(@"\{\d", "every placeholder is filled");
			if (locale != "en")
				invite.Subject.Should().NotBe(Strings("en")["Account.Invite.Subject"].Replace("{0}", "Station 9"), "a real translation, not the English text");
		}

		[Test]
		public void An_unsupported_or_missing_language_writes_the_account_emails_in_english()
		{
			var english = Strings("en");
			foreach (var culture in new[] { ContentNoticeText.CultureFor("pt-BR"), ContentNoticeText.CultureFor((string)null), null })
				LmsLearnerAccountMail.Compose(LmsLearnerAccountMailKinds.ResetPassword, "Casey", "Station 9", Url, TimeSpan.FromHours(2), culture).Subject
					.Should().Be(english["Account.Reset.Subject"]);
		}

		#endregion Learner portal account e-mails

		#region Member notices: the recipient's language

		/// <summary>A department whose members' languages are given; the managing member decides the default.</summary>
		private sealed class World
		{
			public readonly Mock<ICommunicationService> Communication = new Mock<ICommunicationService>();
			public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
			public readonly Mock<IDepartmentSettingsService> Settings = new Mock<IDepartmentSettingsService>();
			public readonly Mock<IUserProfileService> Profiles = new Mock<IUserProfileService>();
			public readonly List<(string UserId, string Title, string Message, string Code)> Sent = new List<(string, string, string, string)>();

			public World(Dictionary<string, string> languages, string managerLanguage = null)
			{
				Departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>()))
					.ReturnsAsync(new Department { DepartmentId = Dept, Name = "Harbor Response", ManagingUserId = "manager", TimeZone = "Etc/UTC" });
				Departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(new HashSet<string>(languages.Keys, StringComparer.OrdinalIgnoreCase));
				Departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(Dept)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser>());
				foreach (var pair in languages)
					Profiles.Setup(p => p.GetProfileByUserIdAsync(pair.Key, It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = pair.Key, Language = pair.Value });
				Profiles.Setup(p => p.GetProfileByUserIdAsync("manager", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "manager", Language = managerLanguage });
				Communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
						It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()))
					.Callback((string userId, int _, string message, string _, Department _, string title, UserProfile _, bool _, string code) => Sent.Add((userId, title, message, code)))
					.ReturnsAsync(true);
				Communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
						It.IsAny<UserProfile>(), It.IsAny<bool>()))
					.Callback((string userId, int _, string message, string _, Department _, string title, UserProfile _, bool _) => Sent.Add((userId, title, message, null)))
					.ReturnsAsync(true);
			}

			public Lazy<ICommunicationService> Lazy => new Lazy<ICommunicationService>(() => Communication.Object);

			public (string Title, string Message, string Code) To(string userId) => Sent.Where(s => s.UserId == userId).Select(s => (s.Title, s.Message, s.Code)).Single();
		}

		private static T WithBroadcast<T>(Func<T> run)
		{
			var doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
			try
			{
				return run();
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = doNotBroadcast;
			}
		}

		private static string T(string locale, string key) => Strings(locale)[key];

		[Test]
		public void Protocol_notices_go_in_each_members_language_with_the_department_default_and_english_fallbacks()
		{
			var world = new World(new Dictionary<string, string> { ["de-member"] = "de", ["plain"] = null, ["odd"] = "xx" }, managerLanguage: "fr");
			var service = new ProtocolNotificationService(world.Lazy, world.Departments.Object, world.Settings.Object, Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPermissionsService>(), world.Profiles.Object);
			var protocol = new DispatchProtocol { DispatchProtocolId = 3, DepartmentId = Dept, Code = "P-7", Name = "Confined space entry" };
			var version = new ProtocolVersion { ProtocolVersionId = 12, DepartmentId = Dept, DispatchProtocolId = 3, Major = 1 };

			WithBroadcast(() => service.NotifyAssignedAsync(Dept, new[] { "de-member", "plain", "odd" }, protocol, version).Result).Should().Be(3);

			world.To("de-member").Title.Should().Be(T("de", "Protocol.Assigned.Title"));
			world.To("de-member").Message.Should().Contain("P-7").And.Contain("Confined space entry").And.Contain("/User/ProtocolAcknowledgments/Read?versionId=12");
			world.To("plain").Title.Should().Be(T("fr", "Protocol.Assigned.Title"), "no profile language: the department default (its managing member's)");
			world.To("odd").Title.Should().Be(T("fr", "Protocol.Assigned.Title"), "an unsupported language falls back to the department default");
			world.Sent.Should().OnlyContain(s => s.Code == "PA:12", "the event code is unchanged");

			var english = new World(new Dictionary<string, string> { ["plain"] = null });
			var englishService = new ProtocolNotificationService(english.Lazy, english.Departments.Object, english.Settings.Object, Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPermissionsService>(), english.Profiles.Object);
			WithBroadcast(() => englishService.NotifyBundleAsync(Dept, "plain", 4).Result).Should().Be(1);
			english.To("plain").Title.Should().Be("Protocols to acknowledge");
			english.To("plain").Message.Should().StartWith("You have 4 protocols to read and acknowledge.");
		}

		[Test]
		public void A_protocol_reminder_writes_the_due_date_the_readers_way()
		{
			var department = new Department { DepartmentId = Dept, TimeZone = "Etc/UTC" };
			var due = new DateTime(2026, 12, 3, 12, 0, 0, DateTimeKind.Utc);
			var protocol = new DispatchProtocol { Code = "P-7", Name = "Hot work" };
			var version = new ProtocolVersion { ProtocolVersionId = 5 };

			var german = ProtocolNotificationService.ReminderNotice(protocol, version, ProtocolAcknowledgmentReminderStages.Overdue, due, department, CultureInfo.GetCultureInfo("de"));
			german.Title.Should().Be(T("de", "Protocol.Reminder.OverdueTitle"));
			german.Message.Should().Contain(due.ToString("d", CultureInfo.GetCultureInfo("de")));

			var review = ProtocolLibraryService.ReviewReminderNotice("P-7", "Hot work", due, ProtocolReviewReminderStages.SevenDays, department, CultureInfo.GetCultureInfo("sv"));
			review.Title.Should().Be(T("sv", "Protocol.ReviewDue.Title"));
			review.Message.Should().Be(Fill(T("sv", "Protocol.ReviewDue.Message"), CultureInfo.GetCultureInfo("sv"), "P-7", "Hot work", due.ToString("d", CultureInfo.GetCultureInfo("sv"))));
		}

		[Test]
		public void Requirement_notices_go_in_the_members_language()
		{
			var world = new World(new Dictionary<string, string> { ["es-member"] = "es", ["en-member"] = "en" });
			var notifier = new LmsRequirementNotifier(world.Lazy, world.Departments.Object, world.Settings.Object, Mock.Of<IDepartmentGroupsService>(), world.Profiles.Object);
			var requirement = new LmsRequirement { LmsRequirementId = 8, DepartmentId = Dept, Name = "Lockout/tagout refresher" };

			WithBroadcast(() => notifier.NotifyAssignedAsync(Dept, "es-member", requirement, "LOTO", new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc)).Result).Should().Be(1);
			WithBroadcast(() => notifier.NotifyEnforcementAsync(Dept, "en-member", requirement, null, false).Result).Should().Be(1);

			world.To("es-member").Title.Should().Be(T("es", "Requirement.Assigned.Title"));
			world.To("es-member").Message.Should().Contain(new DateTime(2027, 1, 15).ToString("d", CultureInfo.GetCultureInfo("es"))).And.Contain("Lockout/tagout refresher");
			world.To("es-member").Code.Should().Be("LR:8");
			world.To("en-member").Message.Should().Contain("so your certification has expired");
		}

		[Test]
		public void Signature_approval_and_case_review_notices_go_in_the_members_language()
		{
			var world = new World(new Dictionary<string, string> { ["it-member"] = "it", ["pl-member"] = "pl", ["sv-member"] = "sv" });

			var signatures = new SignatureNotificationService(world.Lazy, world.Departments.Object, world.Settings.Object, world.Profiles.Object);
			var request = new SignatureRequestRecord { SignatureRequestId = 4, DepartmentId = Dept, Title = "Post orders, gate 3", ArtifactSha256 = new string('a', 64) };
			WithBroadcast(() => signatures.NotifyStepReadyAsync(request, new[] { "it-member" }, true).Result).Should().Be(1);
			world.To("it-member").Title.Should().Be(T("it", "Signature.Reminder.Title"));
			world.To("it-member").Message.Should().Be(SignatureNotificationService.StepReadyMessage(request, true, CultureInfo.GetCultureInfo("it")));

			var approvals = new ApprovalNotificationService(world.Lazy, world.Departments.Object, world.Settings.Object, world.Profiles.Object);
			var approval = new ApprovalRequest { ApprovalRequestId = 6, DepartmentId = Dept, Title = "Training request", Amount = 1250m, Currency = "EUR", ContentSha256 = new string('b', 64) };
			WithBroadcast(() => approvals.NotifyAwaitingAsync(approval, new[] { "pl-member" }, false).Result).Should().Be(1);
			world.To("pl-member").Title.Should().Be(T("pl", "Approval.Needed.Title"));
			world.To("pl-member").Message.Should().Contain(1250m.ToString("N2", CultureInfo.GetCultureInfo("pl")) + " EUR", "the amount is written the reader's way");

			var cases = new DispatchCaseReviewNotifier(world.Lazy, world.Departments.Object, world.Settings.Object, world.Profiles.Object);
			var review = new DispatchCaseReview { DispatchCaseReviewId = 9, DepartmentId = Dept, DispatcherUserId = "sv-member", CallNumber = "26-1", ProtocolCode = "P-2", ProtocolName = "Fall" };
			WithBroadcast(() => cases.NotifySentAsync(review).Result).Should().BeTrue();
			world.To("sv-member").Title.Should().Be(T("sv", "CaseReview.Sent.Title"));
			world.To("sv-member").Message.Should().Contain("26-1").And.Contain("/User/DispatchCaseReviews/Review/9");
			world.To("sv-member").Code.Should().Be("PQ:9");

			// The English text the tests and older callers read is unchanged.
			ApprovalNotificationService.DecidedMessage(new ApprovalRequest { ApprovalRequestId = 1, Title = "Fuel", Status = (int)ApprovalRequestStatuses.Approved })
				.Should().StartWith("Your approval request for Fuel was approved.");
		}

		[Test]
		public void Skill_notices_are_queued_as_keys_and_written_at_send_time_in_the_recipients_language()
		{
			var queued = LmsSkillNoticeText.Encode("Skill.TaskBookInitiated", "ENGB", "Engine boss", new DateTime(2027, 5, 1), "https://x.test/tb/3");
			LmsSkillNoticeText.IsEncoded(queued).Should().BeTrue();
			queued.Length.Should().BeLessThan(LmsSkillNoticeText.MaxStoredLength);

			var french = CultureInfo.GetCultureInfo("fr");
			LmsSkillNoticeText.Render(queued, french).Should().Be(Fill(T("fr", "Skill.TaskBookInitiated"), french, "ENGB", "Engine boss", new DateTime(2027, 5, 1), "https://x.test/tb/3"));
			LmsSkillNoticeText.Render(LmsSkillNoticeText.Encode("Skill.EvaluationPassedScore", "Ladder raise", 87.5m, "l"), french).Should().Contain("87,5");
			LmsSkillNoticeText.Render("Plain text queued before 9C", french).Should().Be("Plain text queued before 9C");
			LmsSkillNoticeText.Render("{\"k\":broken", french).Should().Be("{\"k\":broken", "an unreadable row is sent as it was stored");

			var german = CultureInfo.GetCultureInfo("de");
			var digest = LmsSkillNotificationService.DigestMessage(new[] { queued, "legacy" }, german);
			digest.Should().StartWith(T("de", "Skill.Digest").Replace("{0}", string.Empty)).And.Contain("legacy").And.Contain("ENGB");
		}

		[Test]
		public async Task The_skill_digest_is_sent_in_the_recipients_language()
		{
			var world = new World(new Dictionary<string, string> { ["uk-member"] = "uk" });
			var now = DateTime.UtcNow;
			var due = new List<LmsSkillNotification>
			{
				new LmsSkillNotification { LmsSkillNotificationId = 1, DepartmentId = Dept, UserId = "uk-member", Kind = 0, Text = LmsSkillNoticeText.Encode("Skill.TaskBookCertified", "ENGB", "Engine boss", "l1"), CreatedOn = now.AddMinutes(-2), DueOn = now },
				new LmsSkillNotification { LmsSkillNotificationId = 2, DepartmentId = Dept, UserId = "uk-member", Kind = 0, Text = LmsSkillNoticeText.Encode("Skill.TaskBookExpired", "FFT2", "Firefighter", "l2"), CreatedOn = now.AddMinutes(-1), DueOn = now }
			};
			var repository = new Mock<ILmsSkillNotificationsRepository>();
			repository.Setup(r => r.GetDueAsync(now, It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(due);
			repository.Setup(r => r.ClaimAsync(It.IsAny<int>(), now, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var service = new LmsSkillNotificationService(repository.Object, Mock.Of<ILmsSkillSettingsService>(), world.Departments.Object, world.Settings.Object, world.Lazy,
				world.Profiles.Object);

			var doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
			try
			{
				(await service.SendDueAsync(now)).Should().Be(1);
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = doNotBroadcast;
			}

			var uk = CultureInfo.GetCultureInfo("uk");
			world.To("uk-member").Title.Should().Be(T("uk", "Skill.DigestTitle"));
			world.To("uk-member").Message.Should().Contain(Fill(T("uk", "Skill.TaskBookCertified"), uk, "ENGB", "Engine boss", "l1"))
				.And.Contain(Fill(T("uk", "Skill.TaskBookExpired"), uk, "FFT2", "Firefighter", "l2"));
		}

		[Test]
		public void Wellness_and_training_notices_are_written_in_the_readers_language()
		{
			var greek = CultureInfo.GetCultureInfo("el");
			var anniversary = new DateTime(2026, 12, 9);
			MemberWellnessService.AnniversaryMessage(anniversary, 3, greek).Should().Be(Fill(T("el", "Wellness.Anniversary.Years"), greek, anniversary, 3))
				.And.Contain(anniversary.ToString("D", greek));
			MemberWellnessService.PeerReminderMessage("Mia", anniversary, 1, CultureInfo.GetCultureInfo("ar")).Should().StartWith(T("ar", "Wellness.PeerReminder.OneYear").Substring(0, 4));

			var polish = CultureInfo.GetCultureInfo("pl");
			var training = new Training { TrainingId = 2, Name = "Confined space awareness", ReminderMessage = "Bring your gas monitor." };
			var dueUtc = new DateTime(2026, 12, 20, 15, 0, 0, DateTimeKind.Utc);
			var department = new Department { DepartmentId = Dept, TimeZone = "Etc/UTC" };
			var notice = TrainingNoticeText.Render(new TrainingNoticePlan { Kind = TrainingNoticeKinds.DueReminder, LeadDays = 7, Title = "x", Message = "x" }, training, dueUtc, department, polish);
			notice.Title.Should().Be(T("pl", "Training.Due.Title"));
			notice.Message.Should().Be(Fill(T("pl", "Training.Due.DaysOn"), polish, training.Name, 7, dueUtc.ToString("d", polish)) + " Bring your gas monitor.",
				"the department's own reminder text is appended as written");
			TrainingNoticeText.Render(new TrainingNoticePlan { Kind = TrainingNoticeKinds.NewTraining }, training, null, department, ContentNoticeText.English).Title
				.Should().Be(TrainingNoticePlanner.NewTrainingTitle, "the English wording the planner documents is kept");
		}

		#endregion Member notices: the recipient's language
	}
}
