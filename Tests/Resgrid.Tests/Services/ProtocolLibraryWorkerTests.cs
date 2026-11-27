using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Worker 84 (protocol lifecycle): scheduled versions go into force only while their approvals still match the content;
	/// a version past its expiry retires the protocol it holds in force; review reminders claim their stage before anything is
	/// sent and go to the owner or, when the owner left, the active admins; retention purges only what a kind's keep period
	/// allows and nothing under a legal hold; every step starts from a due-work query and never enumerates departments; one
	/// failing step does not stop the others.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class ProtocolLibraryWorkerTests
	{
		private const int DepartmentId = ProtocolLibraryWorld.DepartmentId;
		private const string OldHtml = "<h2>Scene safety</h2><p>Stay upwind of the spill.</p>";
		private const string NewHtml = "<h2>Scene safety</h2><p>Stay uphill and upwind of the spill.</p>";

		private ProtocolLibraryWorld _world;
		private DateTime _now;
		private bool _doNotBroadcast;

		[SetUp]
		public void SetUp()
		{
			// Reminders are only sent when the department may transmit (a static config switch).
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;

			_world = new ProtocolLibraryWorld();
			_now = DateTime.UtcNow;

			_world.Departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Mesa Flats", TimeZone = "Eastern Standard Time" });
			_world.Departments.Setup(x => x.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "owner-1", "admin-1", "admin-2" });
			_world.Departments.Setup(x => x.GetActiveAdminsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "admin-1" }, new IdentityUser { Id = "admin-2" } });
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;
		}

		/// <summary>Protocol 1 in force at 1.0 (version 90) and version 100 approved and scheduled for a moment that has come.</summary>
		private ProtocolVersion ScheduledRevision()
		{
			var protocol = _world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90);
			protocol.ProtocolText = OldHtml;
			_world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);
			var scheduled = _world.AddApprovedVersion(100, 1, NewHtml);
			scheduled.Major = 1;
			scheduled.Minor = 1;
			scheduled.EffectiveOn = _now.AddMinutes(-10);
			scheduled.PublishedByUserId = ProtocolLibraryWorld.ManagerId;
			_world.VersionsRepository.Setup(x => x.GetDueActivationsAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(() => new List<ProtocolVersion> { ProtocolLibraryWorld.Copy(_world.Versions[100]) });
			return scheduled;
		}

		#region Activation

		[Test]
		public async Task ActivateDueVersionsAsync_puts_a_scheduled_version_whose_approvals_still_match_in_force()
		{
			ScheduledRevision();

			var activated = await _world.Service.ActivateDueVersionsAsync(_now);

			activated.Should().Be(1);
			var published = _world.Versions[100];
			published.Status.Should().Be((int)ProtocolVersionStatuses.Published);
			published.EffectiveOn.Should().Be(_now.AddMinutes(-10));
			(published.Major, published.Minor).Should().Be((1, 1));
			_world.Versions[90].Status.Should().Be((int)ProtocolVersionStatuses.Superseded);

			var protocol = _world.Protocols[1];
			protocol.ProtocolText.Should().Be(published.ContentHtml);
			protocol.CurrentVersionId.Should().Be(100);
			protocol.Status.Should().Be((int)ProtocolStatuses.Published);
			protocol.NextReviewOn.Should().Be(_now.AddDays(365));
		}

		[Test]
		public async Task ActivateDueVersionsAsync_sends_a_version_whose_content_changed_since_approval_back_to_draft()
		{
			ScheduledRevision();
			_world.Versions[100].ContentHtml = "<p>Edited after approval</p>";

			var activated = await _world.Service.ActivateDueVersionsAsync(_now);

			activated.Should().Be(0);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Draft);
			_world.Versions[100].EffectiveOn.Should().BeNull();
			_world.Versions[90].Status.Should().Be((int)ProtocolVersionStatuses.Published);
			_world.Protocols[1].ProtocolText.Should().Be(OldHtml);
			_world.Protocols[1].CurrentVersionId.Should().Be(90);
			_world.ProtocolRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task ActivateDueVersionsAsync_does_not_activate_when_an_approval_signature_was_voided()
		{
			ScheduledRevision();
			_world.Signatures[600].VoidedOn = _now.AddHours(-1);

			(await _world.Service.ActivateDueVersionsAsync(_now)).Should().Be(0);

			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Draft);
			_world.Protocols[1].CurrentVersionId.Should().Be(90);
		}

		#endregion Activation

		#region Expiry

		[Test]
		public async Task ExpireDueVersionsAsync_retires_the_protocol_when_the_expiring_version_is_the_one_in_force()
		{
			var protocol = _world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90);
			protocol.NextReviewOn = _now.AddDays(40);
			var expiring = _world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);
			expiring.ExpiresOn = _now.AddMinutes(-1);
			_world.VersionsRepository.Setup(x => x.GetDueExpirationsAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(() => new List<ProtocolVersion> { ProtocolLibraryWorld.Copy(_world.Versions[90]) });

			var expired = await _world.Service.ExpireDueVersionsAsync(_now);

			expired.Should().Be(1);
			_world.Versions[90].Status.Should().Be((int)ProtocolVersionStatuses.Retired);
			_world.Versions[90].RetiredOn.Should().Be(_now);
			_world.Protocols[1].Status.Should().Be((int)ProtocolStatuses.Retired);
			_world.Protocols[1].NextReviewOn.Should().BeNull();
			_world.Events.Verify(x => x.SendMessage(It.Is<Resgrid.Model.Events.AuditEvent>(e => e.Type == AuditLogTypes.ProtocolRetired)), Times.Once);
		}

		[Test]
		public async Task ExpireDueVersionsAsync_of_a_version_not_in_force_retires_only_that_version()
		{
			_world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 95);
			_world.AddVersion(95, 1, ProtocolVersionStatuses.Published, NewHtml, 1, 1);
			var stray = _world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);
			stray.ExpiresOn = _now.AddMinutes(-1);
			_world.VersionsRepository.Setup(x => x.GetDueExpirationsAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(() => new List<ProtocolVersion> { ProtocolLibraryWorld.Copy(_world.Versions[90]) });

			(await _world.Service.ExpireDueVersionsAsync(_now)).Should().Be(1);

			_world.Versions[90].Status.Should().Be((int)ProtocolVersionStatuses.Retired);
			_world.Versions[95].Status.Should().Be((int)ProtocolVersionStatuses.Published);
			_world.Protocols[1].Status.Should().Be((int)ProtocolStatuses.Published);
			_world.Protocols[1].CurrentVersionId.Should().Be(95);
		}

		#endregion Expiry

		#region Review reminders

		private ProtocolReviewDue Due(int protocolId, string owner, DateTime nextReviewOn, ProtocolReviewReminderStages sent = ProtocolReviewReminderStages.None) => new ProtocolReviewDue
		{
			DispatchProtocolId = protocolId,
			DepartmentId = DepartmentId,
			Name = "Hazmat spill",
			Code = "HZ-" + protocolId,
			OwnerUserId = owner,
			NextReviewOn = nextReviewOn,
			ReviewReminderStage = (int)sent
		};

		private void RemindersDue(params ProtocolReviewDue[] due)
		{
			_world.Library.Setup(x => x.GetReviewRemindersDueAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(due.ToList());
		}

		private void VerifySent(string userId, Times times) =>
			_world.Communication.Verify(x => x.SendNotificationAsync(userId, DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), times);

		private void VerifyNothingSent() =>
			_world.Communication.Verify(x => x.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Never);

		[Test]
		public async Task SendReviewRemindersAsync_claims_the_stage_before_sending_to_the_active_owner()
		{
			var nextReview = _now.AddDays(20);
			RemindersDue(Due(1, "owner-1", nextReview));
			var order = new List<string>();
			_world.Library.Setup(x => x.MarkReviewReminderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.Callback(() => order.Add("claim")).ReturnsAsync(true);
			_world.Communication.Setup(x => x.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
					It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.Callback((string userId, int _, string __, string ___, Department ____, string _____, UserProfile ______, bool _______) => order.Add("send:" + userId))
				.ReturnsAsync(true);

			var sent = await _world.Service.SendReviewRemindersAsync(_now);

			sent.Should().Be(1);
			order.Should().Equal("claim", "send:owner-1");
			_world.Library.Verify(x => x.MarkReviewReminderAsync(DepartmentId, 1, nextReview, (int)ProtocolReviewReminderStages.ThirtyDays, It.IsAny<CancellationToken>()), Times.Once);
			_world.Communication.Verify(x => x.SendNotificationAsync("owner-1", DepartmentId, It.Is<string>(m => m.Contains("HZ-1")), "5550100", It.IsAny<Department>(),
				"Protocol review due", It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
			_world.Departments.Verify(x => x.GetActiveAdminsForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task SendReviewRemindersAsync_sends_nothing_when_another_run_already_claimed_the_stage()
		{
			RemindersDue(Due(1, "owner-1", _now.AddDays(5)));
			_world.Library.Setup(x => x.MarkReviewReminderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);

			var sent = await _world.Service.SendReviewRemindersAsync(_now);

			sent.Should().Be(0);
			VerifyNothingSent();
		}

		[TestCase("gone-1")]
		[TestCase(null)]
		public async Task SendReviewRemindersAsync_goes_to_the_active_admins_when_the_owner_is_not_an_active_member(string owner)
		{
			RemindersDue(Due(1, owner, _now.AddDays(-2), ProtocolReviewReminderStages.SevenDays));

			var sent = await _world.Service.SendReviewRemindersAsync(_now);

			sent.Should().Be(2);
			VerifySent("admin-1", Times.Once());
			VerifySent("admin-2", Times.Once());
			if (owner != null)
				VerifySent(owner, Times.Never());
			_world.Library.Verify(x => x.MarkReviewReminderAsync(DepartmentId, 1, It.IsAny<DateTime>(), (int)ProtocolReviewReminderStages.Overdue, It.IsAny<CancellationToken>()), Times.Once);
			_world.Communication.Verify(x => x.SendNotificationAsync(It.IsAny<string>(), DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				"Protocol review overdue", It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Exactly(2));
		}

		[Test]
		public async Task SendReviewRemindersAsync_skips_a_stage_that_was_already_sent()
		{
			RemindersDue(
				Due(1, "owner-1", _now.AddDays(20), ProtocolReviewReminderStages.ThirtyDays),
				Due(2, "owner-1", _now.AddDays(3), ProtocolReviewReminderStages.SevenDays),
				Due(3, "owner-1", _now.AddDays(-1), ProtocolReviewReminderStages.Overdue));

			var sent = await _world.Service.SendReviewRemindersAsync(_now);

			sent.Should().Be(0);
			_world.Library.Verify(x => x.MarkReviewReminderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyNothingSent();
		}

		[Test]
		public async Task SendReviewRemindersAsync_claims_but_does_not_send_when_the_department_may_not_transmit()
		{
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = true;
			RemindersDue(Due(1, "owner-1", _now.AddDays(20)));

			var sent = await _world.Service.SendReviewRemindersAsync(_now);

			sent.Should().Be(0);
			VerifyNothingSent();
		}

		#endregion Review reminders

		#region Retention

		private void RetentionWork(bool departmentLegalHold = false)
		{
			_world.Settings = new ProtocolLibrarySettings
			{
				LegalHold = departmentLegalHold,
				Retention = new List<ProtocolKindRetention>
				{
					new ProtocolKindRetention { Kind = (int)ProtocolKinds.Sog, KeepDays = 365 },
					new ProtocolKindRetention { Kind = (int)ProtocolKinds.Policy, KeepDays = 30 }
				}
			};
			_world.Library.Setup(x => x.GetDepartmentsWithRetentionWorkAsync(It.IsAny<int>())).ReturnsAsync(new List<int> { DepartmentId });

			_world.AddProtocol(1, kind: ProtocolKinds.Sog);
			_world.AddProtocol(2, kind: ProtocolKinds.Policy);
			_world.AddProtocol(3, kind: ProtocolKinds.Sog).LegalHold = true;
			// No retention listed for Dispatch: kept.
			_world.AddProtocol(4, kind: ProtocolKinds.Dispatch);

			Ended(11, 1, superseded: _now.AddDays(-400));
			Ended(12, 1, superseded: _now.AddDays(-100));
			Ended(21, 2, retired: _now.AddDays(-40));
			Ended(22, 2, retired: _now.AddDays(-10));
			Ended(31, 3, superseded: _now.AddDays(-1000));
			Ended(41, 4, retired: _now.AddDays(-1000));

			_world.VersionsRepository.Setup(x => x.GetPurgeCandidatesAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(() => _world.Versions.Values.Select(ProtocolLibraryWorld.Copy).ToList());
		}

		private void Ended(int versionId, int protocolId, DateTime? superseded = null, DateTime? retired = null)
		{
			var version = _world.AddVersion(versionId, protocolId, superseded.HasValue ? ProtocolVersionStatuses.Superseded : ProtocolVersionStatuses.Retired, OldHtml, 1, versionId);
			version.SupersededOn = superseded;
			version.RetiredOn = retired;
		}

		[Test]
		public async Task PurgeExpiredRetentionAsync_purges_only_versions_ended_longer_ago_than_their_kinds_keep_period()
		{
			RetentionWork();

			var purged = await _world.Service.PurgeExpiredRetentionAsync(_now);

			purged.Should().Be(2);
			_world.VersionsRepository.Verify(x => x.PurgeAsync(DepartmentId, 11, It.IsAny<CancellationToken>()), Times.Once);
			_world.VersionsRepository.Verify(x => x.PurgeAsync(DepartmentId, 21, It.IsAny<CancellationToken>()), Times.Once);
			_world.VersionsRepository.Verify(x => x.PurgeAsync(It.IsAny<int>(), It.IsIn(12, 22, 31, 41), It.IsAny<CancellationToken>()), Times.Never);
			_world.Versions.Keys.Should().BeEquivalentTo(new[] { 12, 22, 31, 41 });
			// The candidate query is bounded by the shortest keep period.
			_world.VersionsRepository.Verify(x => x.GetPurgeCandidatesAsync(DepartmentId, _now.AddDays(-30), It.IsAny<int>()), Times.Once);
		}

		[Test]
		public async Task PurgeExpiredRetentionAsync_under_a_department_legal_hold_does_nothing()
		{
			RetentionWork(departmentLegalHold: true);

			var purged = await _world.Service.PurgeExpiredRetentionAsync(_now);

			purged.Should().Be(0);
			_world.VersionsRepository.Verify(x => x.GetPurgeCandidatesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
			_world.VersionsRepository.Verify(x => x.PurgeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task PurgeExpiredRetentionAsync_without_a_settings_row_keeps_everything()
		{
			RetentionWork();
			_world.Settings = null;

			(await _world.Service.PurgeExpiredRetentionAsync(_now)).Should().Be(0);

			_world.VersionsRepository.Verify(x => x.PurgeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Retention

		#region No sweep

		[Test]
		public async Task A_worker_run_reads_only_the_due_queries_and_never_enumerates_departments()
		{
			// One piece of every kind of work, all in department 10.
			ScheduledRevision();
			_world.AddProtocol(2, ProtocolStatuses.Published, currentVersionId: 200);
			_world.AddVersion(200, 2, ProtocolVersionStatuses.Published, OldHtml, 1, 0).ExpiresOn = _now.AddMinutes(-1);
			_world.VersionsRepository.Setup(x => x.GetDueExpirationsAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(() => new List<ProtocolVersion> { ProtocolLibraryWorld.Copy(_world.Versions[200]) });
			RemindersDue(Due(3, "owner-1", _now.AddDays(10)));
			_world.Settings = new ProtocolLibrarySettings { Retention = { new ProtocolKindRetention { Kind = (int)ProtocolKinds.Sog, KeepDays = 365 } } };
			_world.Library.Setup(x => x.GetDepartmentsWithRetentionWorkAsync(It.IsAny<int>())).ReturnsAsync(new List<int> { DepartmentId });

			var result = await ProtocolLifecycleLogic.Process(_world.Service, _now);

			result.Failures.Should().Be(0);
			result.Activated.Should().Be(1);
			result.Expired.Should().Be(1);
			result.RemindersSent.Should().Be(1);

			_world.VersionsRepository.Verify(x => x.GetDueActivationsAsync(_now, ProtocolLibraryService.WorkerBatch), Times.Once);
			_world.VersionsRepository.Verify(x => x.GetDueExpirationsAsync(_now, ProtocolLibraryService.WorkerBatch), Times.Once);
			_world.Library.Verify(x => x.GetReviewRemindersDueAsync(_now, ProtocolLibraryService.WorkerBatch), Times.Once);
			_world.Library.Verify(x => x.GetDepartmentsWithRetentionWorkAsync(ProtocolLibraryService.WorkerBatch), Times.Once);

			// Departments are only ever read by id, for the department the due work named.
			_world.Departments.Verify(x => x.GetAllAsync(), Times.Never);
			_world.Departments.Verify(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()), Times.Once);
			_world.Departments.Verify(x => x.GetActiveMemberUserIdsAsync(DepartmentId), Times.Once);
			_world.Departments.VerifyNoOtherCalls();

			_world.SettingsRepository.Verify(x => x.GetDepartmentSettingByIdTypeAsync(DepartmentId, DepartmentSettingTypes.ProtocolLibrarySettings), Times.AtLeastOnce);
			_world.SettingsRepository.VerifyNoOtherCalls();

			_world.ProtocolsService.Verify(x => x.GetAllProtocolsForDepartmentAsync(It.IsAny<int>()), Times.Never);
			_world.ProtocolsService.Verify(x => x.GetProtocolSummariesForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
			_world.ProtocolRepository.Verify(x => x.GetAllAsync(), Times.Never);
			_world.ProtocolRepository.Verify(x => x.GetAllByDepartmentIdAsync(It.IsAny<int>()), Times.Never);
			_world.VersionsRepository.Verify(x => x.GetHeadersForDepartmentAsync(It.IsAny<int>()), Times.Never);
			_world.VersionsRepository.Verify(x => x.GetAllAsync(), Times.Never);
			_world.VersionsRepository.Verify(x => x.GetAllByDepartmentIdAsync(It.IsAny<int>()), Times.Never);
			// Protocols are free: no billing or entitlement gate, and no other module is consulted.
			_world.Calls.VerifyNoOtherCalls();
			_world.Shifts.VerifyNoOtherCalls();
		}

		#endregion No sweep

		#region Worker steps

		[Test]
		public async Task Process_runs_the_four_steps_in_order_with_activation_before_expiry()
		{
			var order = new List<string>();
			var library = new Mock<IProtocolLibraryService>(MockBehavior.Strict);
			library.Setup(x => x.ActivateDueVersionsAsync(_now, It.IsAny<CancellationToken>())).Callback(() => order.Add("activate")).ReturnsAsync(2);
			library.Setup(x => x.ExpireDueVersionsAsync(_now, It.IsAny<CancellationToken>())).Callback(() => order.Add("expire")).ReturnsAsync(3);
			library.Setup(x => x.SendReviewRemindersAsync(_now, It.IsAny<CancellationToken>())).Callback(() => order.Add("remind")).ReturnsAsync(4);
			library.Setup(x => x.PurgeExpiredRetentionAsync(_now, It.IsAny<CancellationToken>())).Callback(() => order.Add("purge")).ReturnsAsync(5);

			var result = await ProtocolLifecycleLogic.Process(library.Object, _now);

			order.Should().Equal("activate", "expire", "remind", "purge");
			result.Activated.Should().Be(2);
			result.Expired.Should().Be(3);
			result.RemindersSent.Should().Be(4);
			result.Purged.Should().Be(5);
			result.Failures.Should().Be(0);
		}

		[Test]
		public async Task Process_counts_a_failing_step_and_still_runs_the_later_ones()
		{
			var order = new List<string>();
			var library = new Mock<IProtocolLibraryService>(MockBehavior.Strict);
			library.Setup(x => x.ActivateDueVersionsAsync(_now, It.IsAny<CancellationToken>())).Callback(() => order.Add("activate")).ThrowsAsync(new InvalidOperationException("database away"));
			library.Setup(x => x.ExpireDueVersionsAsync(_now, It.IsAny<CancellationToken>())).Callback(() => order.Add("expire")).ReturnsAsync(1);
			library.Setup(x => x.SendReviewRemindersAsync(_now, It.IsAny<CancellationToken>())).Callback(() => order.Add("remind")).ThrowsAsync(new TimeoutException());
			library.Setup(x => x.PurgeExpiredRetentionAsync(_now, It.IsAny<CancellationToken>())).Callback(() => order.Add("purge")).ReturnsAsync(6);

			var result = await ProtocolLifecycleLogic.Process(library.Object, _now);

			order.Should().Equal("activate", "expire", "remind", "purge");
			result.Failures.Should().Be(2);
			result.Activated.Should().Be(0);
			result.Expired.Should().Be(1);
			result.RemindersSent.Should().Be(0);
			result.Purged.Should().Be(6);
		}

		[Test]
		public async Task Process_stops_when_the_run_is_cancelled()
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			var library = new Mock<IProtocolLibraryService>(MockBehavior.Strict);
			library.Setup(x => x.ActivateDueVersionsAsync(_now, It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cancellation.Token));

			Func<Task> run = () => ProtocolLifecycleLogic.Process(library.Object, _now, cancellation.Token);

			await run.Should().ThrowAsync<OperationCanceledException>();
			library.Verify(x => x.ExpireDueVersionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Worker steps
	}
}
