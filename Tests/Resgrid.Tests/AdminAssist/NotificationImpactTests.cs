using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class NotificationImpactTests
	{
		private bool _broadcastDisabled;
		[SetUp] public void SetUp() { _broadcastDisabled = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast; Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false; }
		[TearDown] public void TearDown() => Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _broadcastDisabled;
		private static readonly AdminAssistActor Actor = new(7, "admin");
		private static NotificationMemberEvidence Member(int id, int? staffing = 0) => new() { DepartmentId = 7, MemberId = id, UserId = "member-" + id,
			ProfileId = id, Sms = true, Email = true, Push = true, StaffingKnown = staffing.HasValue, Staffing = staffing };
		private sealed class Fixture
		{
			public readonly Mock<IAdminAssistAccessService> Access = new();
			public readonly Mock<IAdminAssistRepository> Repository = new();
			public readonly Mock<IDepartmentSettingsRepository> Settings = new();
			public readonly Mock<INotificationImpactStore> Store = new();
			public NotificationImpactService Service => new(Access.Object, Repository.Object, new ConfigurationCatalog(), Settings.Object, Store.Object, TimeProvider.System);
			public Fixture(params NotificationMemberEvidence[] members)
			{
				Access.Setup(a => a.CanAccessAsync(Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentSetting { DepartmentId = 7,
					SettingType = (int)DepartmentSettingTypes.StaffingSuppressStaffingLevels,
					Setting = ObjectSerialization.Serialize(new DepartmentSuppressStaffingInfo { StaffingLevelsToSupress = new() { 2 } }) } });
				Store.Setup(s => s.ReadNotificationMembersAsync(7, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(members);
			}
		}
		private static ConfigurationImpactMetric Metric(ConfigurationImpactReport report, string suffix) => report.Metrics.Single(m => m.LabelKey == "Impact.Notification" + suffix);
		[Test]
		public async Task Suppression_comparison_is_a_bounded_declared_window_and_never_a_historical_forecast()
		{
			var f = new Fixture(Member(1), Member(2, 2));
			var report = await f.Service.PreviewAsync(Actor, new("0", true, 7, 10));
			Assert.That(Metric(report, "RecipientsMinimum").Before, Is.EqualTo(2)); Assert.That(Metric(report, "RecipientsMinimum").After, Is.EqualTo(1));
			Assert.That(Metric(report, "Suppressed").After, Is.EqualTo(1));
			Assert.That(Metric(report, "VolumeMinimum").After, Is.Zero); Assert.That(Metric(report, "VolumeMaximum").Before, Is.EqualTo(60)); Assert.That(Metric(report, "VolumeMaximum").After, Is.EqualTo(30));
			Assert.That(Metric(report, "HistoricalSample").State, Is.EqualTo(EvidenceState.NotApplicable));
			Assert.That(report.LimitKeys, Does.Contain("Impact.NotificationChannelScope"));
			f.Settings.Verify(s => s.GetAllByDepartmentIdAsync(7), Times.Exactly(2)); f.Settings.VerifyNoOtherCalls();
			Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(report), Does.Not.Contain("member-"));
		}
		[Test]
		public async Task Unknown_profiles_and_foreign_staffing_widen_ranges_without_reading_foreign_state()
		{
			var noProfile = Member(1); noProfile.ProfileId = null; noProfile.Sms = noProfile.Email = noProfile.Push = null;
			var f = new Fixture(noProfile, Member(2, null)); var report = await f.Service.PreviewAsync(Actor, new("0", true, 30, 1));
			Assert.That(Metric(report, "RecipientsMinimum").Before, Is.EqualTo(1)); Assert.That(Metric(report, "RecipientsMinimum").After, Is.Zero);
			Assert.That(Metric(report, "RecipientsMaximum").After, Is.EqualTo(2)); Assert.That(Metric(report, "StaffingUnknown").After, Is.EqualTo(1));
			Assert.That(Metric(report, "MissingProfiles").After, Is.EqualTo(1)); Assert.That(Metric(report, "VolumeMaximum").After, Is.EqualTo(6));
		}
		[TestCase(null, 1)][TestCase(true, 1)][TestCase(false, 0)]
		public async Task Contact_verification_uses_the_production_tristate_gate(bool? verified, int allowed)
		{
			var member = Member(1); member.MobileVerified = member.EmailVerified = verified; member.Push = false;
			var report = await new Fixture(member).Service.PreviewAsync(Actor, new("0", false, 1, 2));
			Assert.That(Metric(report, "SmsMinimum").After, Is.EqualTo(allowed)); Assert.That(Metric(report, "EmailMinimum").After, Is.EqualTo(allowed));
			Assert.That(Metric(report, "PushMaximum").After, Is.Zero); Assert.That(Metric(report, "NoChannels").After, Is.EqualTo(1 - allowed));
		}
		[Test]
		public async Task Missing_malformed_cross_tenant_and_duplicate_sources_never_become_zero_reachability()
		{
			var foreign = Member(1); foreign.DepartmentId = 8;
			foreach (var members in new[] { new[] { foreign }, new[] { Member(1), Member(1) } })
			{
				var report = await new Fixture(members).Service.PreviewAsync(Actor, new("0", true, 7, 1));
				Assert.That(Metric(report, "MemberSample").State, Is.EqualTo(EvidenceState.Unknown));
				Assert.That(report.Metrics.Any(m => m.LabelKey == "Impact.NotificationVolumeMaximum"), Is.False);
			}
			var f = new Fixture(Member(1)); f.Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentSetting { DepartmentId = 7, SettingType = 27, Setting = null } });
			Assert.That(Metric(await f.Service.PreviewAsync(Actor, new("0", false, 7, 1)), "MemberSample").State, Is.EqualTo(EvidenceState.Unknown));
		}
		[Test]
		public void Drift_revocation_and_stale_revision_discard_the_result()
		{
			var f = new Fixture(Member(1)); f.Store.SetupSequence(s => s.ReadNotificationMembersAsync(7, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Member(1) }).ReturnsAsync(new[] { Member(1), Member(2) });
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(Actor, new("0", true, 7, 1)));
			f = new Fixture(Member(1)); f.Access.SetupSequence(a => a.CanAccessAsync(Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.PreviewAsync(Actor, new("0", true, 7, 1)));
			f = new Fixture(Member(1)); Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(Actor, new("99", true, 7, 1))); f.Store.VerifyNoOtherCalls();
		}
		[TestCase(0, 1)][TestCase(31, 1)][TestCase(7, -1)][TestCase(7, 10001)]
		public void Invalid_scenario_is_rejected_before_source_reads(int days, int events)
		{
			var f = new Fixture(Member(1)); Assert.ThrowsAsync<ArgumentException>(async () => await f.Service.PreviewAsync(Actor, new("0", true, days, events))); f.Store.VerifyNoOtherCalls(); f.Settings.VerifyNoOtherCalls();
		}
		[Test]
		public void Shared_gate_preserves_missing_profile_and_opt_out_behavior()
		{
			Assert.That(NotificationChannelSelection.From(null), Is.EqualTo(new NotificationChannelSelection(true, true, true)));
			Assert.That(NotificationChannelSelection.From(new UserProfile()), Is.EqualTo(new NotificationChannelSelection(false, false, false)));
		}
	}
}
