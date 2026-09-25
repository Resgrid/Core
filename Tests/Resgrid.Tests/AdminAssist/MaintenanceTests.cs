using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Model.Identity;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class MaintenanceTests
	{
		private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
		private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
		private Mock<IAdminAssistMaintenanceStore> _store;
		private Mock<IDepartmentsService> _departments;
		private Mock<ICommunicationService> _communication;
		private AdminAssistMaintenanceService _service;
		private Mock<IAdminAssistAccessService> _access;
		private Mock<IAdminAssistWorklistService> _worklist;
		private bool _wasEnabled;
		[SetUp]
		public void Setup()
		{
			_wasEnabled = Resgrid.Config.AdminAssistConfig.SendAdminDigests; Resgrid.Config.AdminAssistConfig.SendAdminDigests = true;
			_store = new(); _departments = new(); _communication = new();
			_store.Setup(s => s.TryLeaseAsync(7, It.IsAny<string>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(7)).ReturnsAsync(new List<IdentityUser> { new() { UserId = "admin" } });
			_departments.Setup(d => d.GetDepartmentByIdAsync(7, true)).ReturnsAsync(new Department { DepartmentId = 7, TimeZone = "UTC" });
			var preference = new AdminAssistPreferences { DepartmentId = 7, UserId = "admin", DigestEnabled = true, Revision = 1 };
			_store.Setup(s => s.GetDigestPreferencesAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { preference });
			_store.Setup(s => s.GetPreferencesAsync(7, "admin", It.IsAny<CancellationToken>())).ReturnsAsync(preference);
			_access = new(); _access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_worklist = new();
			var membership = new Mock<IRecordsAuthorizationService>(); membership.Setup(m => m.IsAssignableMemberAsync("admin", 7)).ReturnsAsync(true);
			var profiles = new Mock<IUserProfileService>(); profiles.Setup(p => p.GetProfileByUserIdAsync("admin", true)).ReturnsAsync(new UserProfile { UserId = "admin" });
			_service = new AdminAssistMaintenanceService(_store.Object, _access.Object, _worklist.Object, _departments.Object,
				membership.Object, _communication.Object, Mock.Of<IDepartmentSettingsService>(), profiles.Object, new Clock());
		}
		[TearDown] public void Reset() => Resgrid.Config.AdminAssistConfig.SendAdminDigests = _wasEnabled;
		[Test]
		public async Task Disabled_assist_skips_evaluation_and_digests_but_keeps_retention_cleanup()
		{
			_access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			await _service.RunDepartmentAsync(7, CancellationToken.None);
			_worklist.VerifyNoOtherCalls(); _communication.VerifyNoOtherCalls();
			_store.Verify(s => s.PurgeExpiredMetadataAsync(7, Now, It.IsAny<CancellationToken>()), Times.Once);
			_store.Verify(s => s.GetDigestPreferencesAsync(7, It.IsAny<CancellationToken>()), Times.Never);
		}
		[Test]
		public async Task No_administrator_still_runs_cleanup_and_releases_the_lease()
		{
			_departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(7)).ReturnsAsync(new List<IdentityUser>());
			await _service.RunDepartmentAsync(7, CancellationToken.None);
			_store.Verify(s => s.PurgeExpiredMetadataAsync(7, Now, It.IsAny<CancellationToken>()), Times.Once);
			_store.Verify(s => s.CompleteLeaseAsync(7, It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
			_communication.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Revocation_after_claim_suppresses_the_provider_handoff()
		{
			_store.Setup(s => s.ClaimDigestAsync(It.IsAny<AdminAssistPreferences>(), It.IsAny<string>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_store.Setup(s => s.GetPreferencesAsync(7, "admin", It.IsAny<CancellationToken>())).ReturnsAsync(new AdminAssistPreferences { DepartmentId = 7, UserId = "admin", Revision = 2, DigestEnabled = false });
			await _service.RunDepartmentAsync(7, CancellationToken.None);
			_communication.VerifyNoOtherCalls();
			_store.Verify(s => s.CompleteDigestAsync(7, "admin", "2026-09-21", "Suppressed", Now, It.IsAny<CancellationToken>()), Times.Once);
		}
		[Test]
		public async Task Ambiguous_provider_failure_is_not_retried_after_the_durable_weekly_claim()
		{
			_store.SetupSequence(s => s.ClaimDigestAsync(It.IsAny<AdminAssistPreferences>(), It.IsAny<string>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			_communication.Setup(c => c.SendNotificationAsync("admin", 7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), false))
				.ThrowsAsync(new InvalidOperationException("provider result unknown"));
			await _service.RunDepartmentAsync(7, CancellationToken.None);
			await _service.RunDepartmentAsync(7, CancellationToken.None);
			_communication.Verify(c => c.SendNotificationAsync("admin", 7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), false), Times.Once);
			_store.Verify(s => s.CompleteDigestAsync(7, "admin", "2026-09-21", "HandoffUnconfirmed", Now, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
