using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.BadgeCounts;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The member counts (certifications, deployments, checklists, shifts, trades, work orders): each counts nothing when its
	/// department module is switched off or its feature flag is off, and passes the department's date and the active unit
	/// to its COUNT.
	/// </summary>
	[TestFixture]
	public class BadgeCountMemberProviderTests
	{
		private const int DepartmentId = 4;
		private const string UserId = "member-a";
		private static readonly DateTime LocalToday = new DateTime(2026, 11, 7);

		private Mock<IBadgeCountsRepository> _repository;
		private Mock<IFeatureToggleService> _flags;
		private DepartmentModuleSettings _modules;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<IBadgeCountsRepository>();
			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(x => x.IsEnabledAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			_modules = new DepartmentModuleSettings();
		}

		private BadgeCountContext Member(int? unitId = null)
		{
			var context = new BadgeCountContext { DepartmentId = DepartmentId, UserId = UserId, ActiveUnitId = unitId, NowUtc = new DateTime(2026, 11, 7, 15, 0, 0, DateTimeKind.Utc) };
			context.UseLookups(() => Task.FromResult(_modules), () => Task.FromResult(LocalToday));
			return context;
		}

		[Test]
		public async Task certifications_count_expired_and_expiring_within_the_longest_reminder_lead()
		{
			var certifications = new Mock<ICertificationService>();
			certifications.Setup(x => x.GetCertificationSettingsAsync(DepartmentId)).ReturnsAsync(new DepartmentCertificationSettings { NotifyLeadDaysCsv = "30,90,7" });
			_repository.Setup(x => x.GetCertificationAttentionAsync(DepartmentId, UserId, LocalToday, LocalToday.AddDays(90))).ReturnsAsync(new CertificationAttentionCounts { Expired = 1, Expiring = 2 });

			var count = await new CertificationsBadgeCountProvider(_repository.Object, certifications.Object, _flags.Object).GetCountAsync(Member());

			count.Count.Should().Be(3);
			count.Breakdown.Should().Equal(new Dictionary<string, int> { ["expired"] = 1, ["expiring"] = 2 });
		}

		[Test]
		public async Task certifications_count_nothing_with_the_module_off()
		{
			_modules.CertificationsDisabled = true;

			(await new CertificationsBadgeCountProvider(_repository.Object, Mock.Of<ICertificationService>(), _flags.Object).GetCountAsync(Member())).Should().BeNull();
			_repository.VerifyNoOtherCalls();
		}

		[Test]
		public async Task deployments_need_the_operations_flag()
		{
			_flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

			(await new DeploymentsBadgeCountProvider(_repository.Object, _flags.Object).GetCountAsync(Member())).Should().BeNull();

			_flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			_repository.Setup(x => x.CountOpenDeploymentsForUserAsync(DepartmentId, UserId)).ReturnsAsync(2);

			(await new DeploymentsBadgeCountProvider(_repository.Object, _flags.Object).GetCountAsync(Member())).Count.Should().Be(2);
		}

		[Test]
		public async Task checklists_count_the_member_or_the_active_unit()
		{
			var member = Member();
			var crew = Member(12);
			_repository.Setup(x => x.CountDueChecklistsAsync(DepartmentId, UserId, null, member.NowUtc)).ReturnsAsync(3);
			_repository.Setup(x => x.CountDueChecklistsAsync(DepartmentId, UserId, 12, crew.NowUtc)).ReturnsAsync(5);
			var provider = new ChecklistsBadgeCountProvider(_repository.Object, _flags.Object);

			(await provider.GetCountAsync(member)).Count.Should().Be(3);
			(await provider.GetCountAsync(crew)).Count.Should().Be(5);

			_modules.ChecklistsDisabled = true;
			(await provider.GetCountAsync(member)).Should().BeNull();
		}

		[Test]
		public async Task shifts_count_the_department_local_day()
		{
			_repository.Setup(x => x.CountShiftDaysForUserAsync(DepartmentId, UserId, LocalToday, LocalToday.AddDays(1))).ReturnsAsync(1);

			(await new ShiftsBadgeCountProvider(_repository.Object, _flags.Object).GetCountAsync(Member())).Count.Should().Be(1);
		}

		[Test]
		public async Task shift_trades_sum_requests_awaiting_me_and_my_own()
		{
			_repository.Setup(x => x.GetShiftTradeCountsAsync(DepartmentId, UserId, LocalToday)).ReturnsAsync(new ShiftTradeCounts { AwaitingMe = 2, Mine = 1 });

			var count = await new ShiftTradesBadgeCountProvider(_repository.Object, _flags.Object).GetCountAsync(Member());

			count.Count.Should().Be(3);
			count.Breakdown.Should().Equal(new Dictionary<string, int> { ["awaitingMe"] = 2, ["mine"] = 1 });

			_modules.ShiftsDisabled = true;
			(await new ShiftTradesBadgeCountProvider(_repository.Object, _flags.Object).GetCountAsync(Member())).Should().BeNull();
		}

		[Test]
		public async Task work_orders_count_assignments_to_the_member_and_their_roles()
		{
			var workOrders = new Mock<IWorkOrderCountRepository>();
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(UserId, DepartmentId)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 7 }, new PersonnelRole { PersonnelRoleId = 7 } });
			workOrders.Setup(x => x.CountOpenAssignedAsync(DepartmentId, UserId, It.Is<int[]>(ids => ids.Length == 1 && ids[0] == 7))).ReturnsAsync(4);

			(await new WorkOrdersBadgeCountProvider(workOrders.Object, roles.Object, _flags.Object).GetCountAsync(Member())).Count.Should().Be(4);
		}

		[Test]
		public async Task the_registry_loads_module_settings_and_the_local_day_once_per_request()
		{
			var settings = new Mock<IDepartmentSettingsService>();
			var departments = new Mock<IDepartmentsService>();
			settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, false)).ReturnsAsync(new DepartmentModuleSettings());
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			_repository.Setup(x => x.CountShiftDaysForUserAsync(DepartmentId, UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(1);
			_repository.Setup(x => x.GetShiftTradeCountsAsync(DepartmentId, UserId, It.IsAny<DateTime>())).ReturnsAsync(new ShiftTradeCounts());
			var service = new BadgeCountsService(new IBadgeCountProvider[] { new ShiftsBadgeCountProvider(_repository.Object, _flags.Object), new ShiftTradesBadgeCountProvider(_repository.Object, _flags.Object) }, settings.Object, departments.Object);

			var counts = await service.GetBadgeCountsAsync(new BadgeCountContext { DepartmentId = DepartmentId, UserId = UserId, NowUtc = new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc) }, null, _ => Task.FromResult(true));

			counts.Should().HaveCount(2);
			settings.Verify(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, false), Times.Once);
			departments.Verify(x => x.GetDepartmentByIdAsync(DepartmentId, false), Times.Once);
			// 02:00 UTC on the 8th is still the 7th in New York.
			_repository.Verify(x => x.CountShiftDaysForUserAsync(DepartmentId, UserId, new DateTime(2026, 11, 7), new DateTime(2026, 11, 8)), Times.Once);
		}
	}
}
