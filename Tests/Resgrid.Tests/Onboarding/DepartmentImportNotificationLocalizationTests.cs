using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// Import notices reach each administrator in their own language (their profile's, English when unset or unsupported),
	/// and still carry only the outcome, a code and a link, never a value from the import.
	/// </summary>
	[TestFixture]
	public class DepartmentImportNotificationLocalizationTests
	{
		private static readonly ImportWorkspace Workspace = new() { Id = "W1", DepartmentId = 7, Name = "Spring roster", OwnerUserId = "OWNER" };

		[Test]
		public void English_notices_read_as_before()
		{
			var run = new ImportRun { Kind = (int)ImportRunKind.Commit, Status = (int)ImportRunStatus.Completed };

			DepartmentImportNotificationService.BuildRunMessage(run, Workspace).Should()
				.StartWith("The import in \"Spring roster\" has finished. Open Import & Migration: ").And.EndWith("/User/DepartmentImport/Workspace/W1");
			DepartmentImportNotificationService.BuildReminderMessage(Workspace).Should().StartWith("Your import \"Spring roster\" has had no activity for a while.");
			DepartmentImportNotificationService.BuildSupportMessage(Workspace, "managed-invitations;12;2026-10-20T08:00:00Z").Should()
				.StartWith("Resgrid will invite 12 people from \"Spring roster\" on 2026-10-20 08:00 UTC. Any administrator");
		}

		[Test]
		public void A_notice_is_written_in_the_recipients_language()
		{
			var german = CultureInfo.GetCultureInfo("de");
			var run = new ImportRun { Kind = (int)ImportRunKind.Rollback, Status = (int)ImportRunStatus.Failed, LastErrorCode = "IMP-ROLLBACK-STALE" };

			DepartmentImportNotificationService.BuildRunMessage(run, Workspace, german).Should()
				.StartWith("Das Rückgängigmachen in „Spring roster“ konnte nicht abgeschlossen werden (Code IMP-ROLLBACK-STALE).");
			DepartmentImportNotificationService.BuildSupportMessage(Workspace, "suggestion", CultureInfo.GetCultureInfo("fr")).Should()
				.StartWith("Le support Resgrid a proposé une correction pour « Spring roster » : ");
			DepartmentImportNotificationService.Text("Notify.SupportTitle", CultureInfo.GetCultureInfo("sv")).Should().Be("Support för import och migrering");
		}

		[Test]
		public void Every_notice_formats_in_every_language_with_the_name_and_the_link()
		{
			var activities = new[] { "contract-authorization", "artifact-downloaded", "sample-viewed", "signoff-needed", "managed-run", "suggestion", "status", "grant",
				"message", "staff-views;3;2", "managed-activation;0;-", "managed-invitations;4;2026-10-20T08:00:00Z" };
			foreach (var language in SupportedLocales.GetSupportedCultures())
			{
				var culture = DepartmentImportNotificationService.CultureFor(language);
				culture.TwoLetterISOLanguageName.Should().Be(language);
				foreach (ImportRunKind kind in Enum.GetValues(typeof(ImportRunKind)))
					foreach (var status in new[] { ImportRunStatus.Completed, ImportRunStatus.Paused, ImportRunStatus.Failed, ImportRunStatus.Cancelled, ImportRunStatus.Running })
					{
						var text = DepartmentImportNotificationService.BuildRunMessage(new ImportRun { Kind = (int)kind, Status = (int)status, LastErrorCode = "IMP-X" }, Workspace, culture);
						text.Should().Contain("Spring roster").And.Contain("IMP-X").And.Contain("/DepartmentImport/Workspace/W1").And.NotContain("Notify.", $"{language} {kind} {status}");
					}

				foreach (var activity in activities)
					DepartmentImportNotificationService.BuildSupportMessage(Workspace, activity, culture).Should()
						.Contain("Spring roster").And.Contain("/DepartmentImport/").And.NotContain("Notify.", $"{language} {activity}");
				DepartmentImportNotificationService.BuildReminderMessage(Workspace, culture).Should().Contain("Spring roster").And.NotContain("Notify.");
			}
		}

		[TestCase(null, "en")]
		[TestCase("", "en")]
		[TestCase("pt", "en")]
		[TestCase("not-a-language", "en")]
		[TestCase("de-DE", "de")]
		[TestCase("uk", "uk")]
		public void An_unset_or_unsupported_language_falls_back_to_english(string language, string expected) =>
			DepartmentImportNotificationService.CultureFor(language).TwoLetterISOLanguageName.Should().Be(expected);

		[Test]
		public async Task Each_administrator_gets_the_notice_in_their_own_language()
		{
			var workspaces = new Mock<IImportWorkspacesRepository>();
			workspaces.Setup(w => w.GetByIdAsync(7, "W1", It.IsAny<CancellationToken>())).ReturnsAsync(Workspace);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(7)).ReturnsAsync(new List<IdentityUser> { new() { Id = "A-DE" }, new() { Id = "A-EN" } });
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetProfileByUserIdAsync("A-DE", false)).ReturnsAsync(new UserProfile { UserId = "A-DE", Language = "de" });
			profiles.Setup(p => p.GetProfileByUserIdAsync("A-EN", false)).ReturnsAsync(new UserProfile { UserId = "A-EN" });
			var sent = new Dictionary<string, (string Message, string Title)>();
			var communication = new Mock<ICommunicationService>();
			communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), 7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
					It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.Callback((string user, int _, string message, string _, Department _, string title, UserProfile _, bool _) => sent[user] = (message, title))
				.ReturnsAsync(true);

			var service = new DepartmentImportNotificationService(Mock.Of<IOutboundQueueProvider>(), Mock.Of<IImportRunsRepository>(), workspaces.Object, departments.Object,
				Mock.Of<IDepartmentSettingsService>(), profiles.Object, communication.Object);

			(await service.DeliverSupportNoticeAsync(7, "W1|contract-authorization|all")).Should().BeTrue();

			sent["A-DE"].Message.Should().StartWith("Resgrid wurde vertraglich beauftragt, die Migration für „Spring roster“");
			sent["A-DE"].Title.Should().Be("Support für Import & Migration");
			sent["A-EN"].Message.Should().StartWith("Resgrid has been authorized under contract to run the migration for \"Spring roster\"");
			sent["A-EN"].Title.Should().Be("Import & Migration support");
		}
	}
}
