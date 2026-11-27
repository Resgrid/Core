using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Workers.Framework.Logic;
using static Resgrid.Tests.TrainingT5.LmsDataExitFixtures;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// The data exit service (R-C12): only owners and admins export, it works after a lapse and with the LMS off (no gate is ever asked),
	/// worker 99 builds into the store (or the database without one), the link expires and the zip is removed, and every request and
	/// download is audited.
	/// </summary>
	[TestFixture]
	public class LmsDataExportServiceTests
	{
		private Mock<ILmsDataExportsRepository> _exports;
		private Mock<ILmsDataExportBlobsRepository> _blobs;
		private Mock<ILmsDataExportSourceRepository> _source;
		private Mock<ILmsMediaStore> _store;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IDepartmentMemberSensitiveDataService> _sensitiveData = new Mock<IDepartmentMemberSensitiveDataService>();
		private Mock<IEventAggregator> _events;
		private List<LmsDataExport> _rows;
		private readonly List<(string Key, byte[] Bytes)> _stored = new List<(string, byte[])>();

		[SetUp]
		public void SetUp()
		{
			_rows = new List<LmsDataExport>();
			_stored.Clear();
			_exports = new Mock<ILmsDataExportsRepository>();
			_exports.Setup(e => e.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int _) => _rows.Where(r => r.DepartmentId == d).ToList());
			_exports.Setup(e => e.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _rows.FirstOrDefault(r => r.DepartmentId == d && r.LmsDataExportId == id));
			_exports.Setup(e => e.InsertAsync(It.IsAny<LmsDataExport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsDataExport e, CancellationToken _, bool __) => { e.LmsDataExportId = _rows.Count + 1; _rows.Add(e); return e; });
			_exports.Setup(e => e.UpdateAsync(It.IsAny<LmsDataExport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LmsDataExport e, CancellationToken _, bool __) => e);
			_exports.Setup(e => e.GetQueuedAsync(It.IsAny<int>())).ReturnsAsync(() => _rows.Where(r => r.Status == (int)LmsDataExportStatuses.Queued).ToList());
			_exports.Setup(e => e.TryClaimAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_exports.Setup(e => e.GetExpiredUnpurgedAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime now, int _) => _rows.Where(r => r.Status == (int)LmsDataExportStatuses.Completed && r.PurgedOn == null && r.ExpiresOn <= now).ToList());

			_blobs = new Mock<ILmsDataExportBlobsRepository>();
			_source = new Mock<ILmsDataExportSourceRepository>(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty };
			var snapshot = Snapshot();
			_source.Setup(s => s.GetDepartmentRowsAsync<LearningRecord>(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot.LearningRecords.Where(r => r.DepartmentId == Dept).ToList());
			_source.Setup(s => s.GetDepartmentRowsAsync<LmsCourse>(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot.Courses.Where(r => r.DepartmentId == Dept).ToList());
			_source.Setup(s => s.GetDepartmentRowsAsync<LmsQuestionBank>(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot.Banks.Where(r => r.DepartmentId == Dept).ToList());
			_source.Setup(s => s.GetDepartmentRowsAsync<LmsQuestion>(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot.Questions.Where(r => r.DepartmentId == Dept).ToList());

			_store = new Mock<ILmsMediaStore>();
			_store.SetupGet(s => s.IsConfigured).Returns(true);
			_store.Setup(s => s.KeyFor(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).Returns((int d, int a, string p) => "lms/" + d + "/shared/" + p);
			_store.Setup(s => s.GetSignedUrl(It.IsAny<string>(), It.IsAny<DateTime>())).Returns((string key, DateTime until) => "https://store.example/" + key + "?until=" + until.ToString("o"));
			_store.Setup(s => s.PutAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string key, Stream content, string _, CancellationToken __) => { using var copy = new MemoryStream(); content.CopyTo(copy); _stored.Add((key, copy.ToArray())); })
				.Returns(Task.CompletedTask);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Station Nine" });
			_departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(Dept)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = Dept, UserId = Member },
				new DepartmentMember { DepartmentId = Dept, UserId = Former, IsDeleted = true }
			});
			_departments.Setup(d => d.GetAllUsersForDepartmentUnlimitedAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = Member, Email = "mia@example.org" } });
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetAllProfilesForDepartmentIncDisabledDeletedAsync(Dept)).ReturnsAsync(new Dictionary<string, UserProfile>
			{
				[Member] = new UserProfile { UserId = Member, FirstName = "Mia", LastName = "Member" }
			});
			_profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile> { new UserProfile { UserId = Former, FirstName = "Fred", LastName = "Former" } });
			_events = new Mock<IEventAggregator>();
		}

		private LmsDataExportService Service() => new LmsDataExportService(_exports.Object, _blobs.Object, _source.Object, _store.Object, _departments.Object, _profiles.Object, _events.Object, _sensitiveData.Object);

		private static LmsActor Actor(bool admin = true, bool current = true, LmsDepartmentGate gate = null) => new LmsActor
		{
			DepartmentId = Dept, UserId = admin ? Admin : Member, IsCurrentMember = current, IsDepartmentAdmin = admin,
			CanManageCourses = !admin, Gate = gate ?? new LmsDepartmentGate { DepartmentId = Dept, ModuleOn = true, FlagOn = true, AddonActive = true }
		};

		private static LmsDepartmentGate Lapsed => new LmsDepartmentGate { DepartmentId = Dept, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = Now.AddDays(-40) };

		private void VerifyAudit(AuditLogTypes type, Times times) => _events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.Type == type && a.DepartmentId == Dept)), times);

		[Test]
		public async Task Only_a_current_owner_or_admin_may_export()
		{
			(await Service().RequestAsync(Actor(admin: false), LmsDataExportScopes.Full, true)).Error.Should().Be(LmsDataExitErrors.NotAuthorized, "Manage Courses is not enough");
			(await Service().RequestAsync(Actor(current: false), LmsDataExportScopes.Full, true)).Error.Should().Be(LmsDataExitErrors.NotAuthorized);
			(await Service().RequestAsync(null, LmsDataExportScopes.Full, true)).Error.Should().Be(LmsDataExitErrors.NotAuthorized);
			(await Service().GetExportsAsync(Actor(admin: false))).Should().BeEmpty();
			(await Service().GetDownloadAsync(Actor(admin: false), 1, Now)).Error.Should().Be(LmsDataExitErrors.NotAuthorized);
			_rows.Should().BeEmpty();
		}

		[Test]
		public async Task Exporting_works_after_a_lapse_and_with_the_lms_switched_off()
		{
			var lapsed = await Service().RequestAsync(Actor(gate: Lapsed), LmsDataExportScopes.Full, true);
			lapsed.Succeeded.Should().BeTrue();
			var dark = await Service().RequestAsync(Actor(gate: new LmsDepartmentGate { DepartmentId = Dept }), LmsDataExportScopes.RecordsOnly, true);
			dark.Succeeded.Should().BeTrue("data exit never depends on the flag or the add-on (§17.3 rules 1–2)");
			dark.Export.IncludeMedia.Should().BeFalse("a records export copies no media");
			_rows.Should().OnlyContain(r => r.Status == (int)LmsDataExportStatuses.Queued && r.RequestedByUserId == Admin);
			VerifyAudit(AuditLogTypes.LmsDataExportRequested, Times.Exactly(2));

			typeof(LmsDataExportService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType)
				.Should().NotContain(new[] { typeof(ILmsGateService), typeof(ILmsAccessService), typeof(ILmsBillingService) }, "no add-on gate is ever asked");
		}

		[Test]
		public async Task At_most_two_exports_are_queued_or_running()
		{
			await Service().RequestAsync(Actor(), LmsDataExportScopes.Full, false);
			await Service().RequestAsync(Actor(), LmsDataExportScopes.Full, false);
			(await Service().RequestAsync(Actor(), LmsDataExportScopes.Full, false)).Error.Should().Be(LmsDataExitErrors.TooManyActive);
			_rows[0].Status = (int)LmsDataExportStatuses.Completed;
			(await Service().RequestAsync(Actor(), LmsDataExportScopes.Full, false)).Succeeded.Should().BeTrue();
		}

		[Test]
		public async Task Worker_99_builds_the_zip_into_the_store_and_the_link_expires_after_the_window()
		{
			await Service().RequestAsync(Actor(gate: Lapsed), LmsDataExportScopes.Full, false);
			var run = await LmsDataExportLogic.Process(Service(), Now);
			run.Completed.Should().Be(1);

			var export = _rows.Single();
			export.Status.Should().Be((int)LmsDataExportStatuses.Completed);
			export.StoredInDatabase.Should().BeFalse();
			export.ObjectKey.Should().StartWith("lms/501/shared/exports/1/training-export-501-");
			export.ExpiresOn.Should().BeCloseTo(export.CompletedOn.Value.AddDays(7), TimeSpan.FromSeconds(1));
			export.Counts["learningRecords"].Should().Be(4);
			export.Counts["questionBanks"].Should().Be(1);
			var (key, bytes) = _stored.Single();
			key.Should().Be(export.ObjectKey);
			export.SizeBytes.Should().Be(bytes.LongLength);
			export.Sha256.Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
			var files = Entries(bytes);
			files.Keys.Should().Contain(new[] { "manifest.json", "records/learning-records.csv", "question-banks/question-banks.json" });
			Text(files["records/members.csv"]).Should().Contain("Fred,Former", "a removed member's records keep their name");
			_source.Verify(s => s.GetDepartmentRowsAsync<LearningRecord>(It.Is<int>(d => d != Dept), It.IsAny<CancellationToken>()), Times.Never());

			// The link: a short signed URL before the expiry, refused after it, and the zip removed by the next run past it.
			var download = await Service().GetDownloadAsync(Actor(gate: Lapsed), export.LmsDataExportId, export.CompletedOn.Value.AddDays(6));
			download.Succeeded.Should().BeTrue();
			download.DownloadUrl.Should().StartWith("https://store.example/" + export.ObjectKey);
			_store.Verify(s => s.GetSignedUrl(export.ObjectKey, It.Is<DateTime>(d => d <= export.ExpiresOn.Value && d <= export.CompletedOn.Value.AddDays(6).AddMinutes(15))), Times.Once());
			export.DownloadCount.Should().Be(1);
			VerifyAudit(AuditLogTypes.LmsDataExportDownloaded, Times.Once());

			(await Service().GetDownloadAsync(Actor(), export.LmsDataExportId, export.ExpiresOn.Value.AddSeconds(1))).Error.Should().Be(LmsDataExitErrors.Expired);
			var purge = await Service().RunDueAsync(export.ExpiresOn.Value.AddMinutes(1));
			purge.Purged.Should().Be(1);
			_store.Verify(s => s.DeletePrefixAsync(export.ObjectKey, It.IsAny<CancellationToken>()), Times.Once());
			export.Status.Should().Be((int)LmsDataExportStatuses.Expired);
			export.PurgedOn.Should().NotBeNull();
			(await Service().GetDownloadAsync(Actor(), export.LmsDataExportId, export.CompletedOn.Value)).Error.Should().Be(LmsDataExitErrors.Expired, "a purged zip is gone");
		}

		[Test]
		public async Task Without_a_store_the_zip_is_kept_in_the_database_and_downloaded_from_there()
		{
			_store.SetupGet(s => s.IsConfigured).Returns(false);
			byte[] kept = null;
			_blobs.Setup(b => b.InsertAsync(It.IsAny<LmsDataExportBlob>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsDataExportBlob b, CancellationToken _, bool __) => { kept = b.Data; return b; });
			_blobs.Setup(b => b.GetDataAsync(Dept, 1)).ReturnsAsync(() => kept);

			await Service().RequestAsync(Actor(), LmsDataExportScopes.RecordsOnly, false);
			(await Service().RunDueAsync(Now)).Completed.Should().Be(1);
			var export = _rows.Single();
			export.StoredInDatabase.Should().BeTrue();
			export.ObjectKey.Should().BeNull();
			kept.Should().NotBeNull();

			var download = await Service().GetDownloadAsync(Actor(), 1, export.CompletedOn.Value.AddHours(1));
			download.Bytes.Should().Equal(kept);
			download.FileName.Should().Contain("records-");
			Entries(kept).Keys.Should().NotContain("question-banks/question-banks.json", "the records scope has no keys");

			await Service().RunDueAsync(export.ExpiresOn.Value.AddMinutes(1));
			_blobs.Verify(b => b.DeleteForExportAsync(Dept, 1, It.IsAny<CancellationToken>()), Times.AtLeastOnce());
		}

		[Test]
		public async Task A_failing_build_is_retried_and_then_stays_failed()
		{
			_source.Setup(s => s.GetDepartmentRowsAsync<LearningRecord>(Dept, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("database unavailable"));
			await Service().RequestAsync(Actor(), LmsDataExportScopes.Full, false);
			var export = _rows.Single();

			for (var attempt = 1; attempt <= LmsDataExitRules.MaxAttempts; attempt++)
				await Service().RunDueAsync(Now.AddMinutes(attempt * 5));

			export.Attempts.Should().Be(LmsDataExitRules.MaxAttempts);
			export.Status.Should().Be((int)LmsDataExportStatuses.Failed);
			export.Error.Should().Be("database unavailable");
			(await Service().GetDownloadAsync(Actor(), export.LmsDataExportId, Now)).Error.Should().Be(LmsDataExitErrors.NotReady);
			_events.Verify(e => e.SendMessage(It.IsAny<TrainingAdminWorkflowEvent>()), Times.Never(), "a failed export is never ready");
		}

		[Test]
		public async Task A_built_export_raises_data_export_ready_once_for_the_member_who_asked()
		{
			// Round 7C, workflow trigger 269: once, by the run that claimed and built it.
			var raised = new List<TrainingAdminWorkflowEvent>();
			_events.Setup(e => e.SendMessage(It.IsAny<TrainingAdminWorkflowEvent>())).Callback((TrainingAdminWorkflowEvent e) => raised.Add(e));
			await Service().RequestAsync(Actor(), LmsDataExportScopes.Full, false);

			(await Service().RunDueAsync(Now)).Completed.Should().Be(1);
			(await Service().RunDueAsync(Now.AddMinutes(5))).Completed.Should().Be(0);

			raised.Should().ContainSingle();
			var ready = raised.Single();
			var export = _rows.Single();
			ready.Trigger.Should().Be(WorkflowTriggerEventType.LmsDataExportReady);
			(ready.DepartmentId, ready.ExportId, ready.ExportScope, ready.SizeBytes).Should().Be((Dept, export.LmsDataExportId, "Full", export.SizeBytes));
			ready.UserId.Should().Be(export.RequestedByUserId);
			ready.ExpiresOn.Should().Be(export.ExpiresOn);
		}

		[Test]
		public void The_download_rules_follow_the_status_and_the_expiry()
		{
			var export = new LmsDataExport { Status = (int)LmsDataExportStatuses.Completed, ExpiresOn = Now.AddDays(1) };
			LmsDataExitRules.IsDownloadable(export, Now).Should().BeTrue();
			LmsDataExitRules.IsDownloadable(export, Now.AddDays(1)).Should().BeFalse("the link ends at its expiry");
			LmsDataExitRules.DownloadRefusal(export, Now.AddDays(2)).Should().Be(LmsDataExitErrors.Expired);
			LmsDataExitRules.DownloadRefusal(new LmsDataExport { Status = (int)LmsDataExportStatuses.Running }, Now).Should().Be(LmsDataExitErrors.NotReady);
			LmsDataExitRules.DownloadRefusal(null, Now).Should().Be(LmsDataExitErrors.NotFound);
			LmsDataExitRules.ExpiresOn(Now, 0).Should().Be(Now.AddDays(7));
			LmsDataExitRules.ExpiresOn(Now, 90).Should().Be(Now.AddDays(30));
			LmsDataExitRules.Slug("Hose & Ladder — Übung 2").Should().Be("hose-ladder-ubung-2");
		}

		[Test]
		public void Hosted_media_is_copied_within_the_cap_and_otherwise_linked()
		{
			var assets = new[]
			{
				new LmsMediaAsset { LmsMediaAssetId = 1, DepartmentId = Dept, Kind = (int)LmsMediaKinds.Video, Status = (int)LmsMediaStatuses.Ready, Mp4Key = "lms/501/1/video.mp4" },
				new LmsMediaAsset { LmsMediaAssetId = 2, DepartmentId = Dept, Kind = (int)LmsMediaKinds.Video, Status = (int)LmsMediaStatuses.Archived, Mp4Key = "lms/501/2/video.mp4" },
				new LmsMediaAsset { LmsMediaAssetId = 3, DepartmentId = Dept, Kind = (int)LmsMediaKinds.Video, Status = (int)LmsMediaStatuses.Failed, SourceKey = "lms/501/3/source.mov" },
				new LmsMediaAsset { LmsMediaAssetId = 4, DepartmentId = Other, Kind = (int)LmsMediaKinds.Video, Status = (int)LmsMediaStatuses.Ready, Mp4Key = "lms/502/4/video.mp4" }
			};
			var plan = LmsDataExportService.PlanMedia(assets, Dept, true, _store.Object, key => key.Contains("/1/") ? 700 : 600, Now.AddDays(7), 1000);

			plan.Keys.Should().BeEquivalentTo(new[] { 1, 2 }, "archived media stays exportable; failed uploads and other departments are not exported");
			plan[1].ZipPath.Should().Be("courses/media/video-1.mp4");
			plan[2].ZipPath.Should().BeNull("the second file would pass the cap, so it is a link");
			plan[2].SignedUrl.Should().StartWith("https://store.example/lms/501/2/video.mp4");
			LmsDataExportService.PlanMedia(assets, Dept, false, _store.Object, _ => 1, Now, 1000).Values.Should().OnlyContain(m => m.ZipPath == null);
		}
	}
}
