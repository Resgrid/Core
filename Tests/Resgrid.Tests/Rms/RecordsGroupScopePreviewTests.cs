using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The group-scoping impact preview (RMS plan section 5.7.1, "Turning it on"): before an administrator flips
	/// RecordsGroupVisibilityMode to GroupScoped they see how many Records become invisible to how many users, by
	/// group, and how many legacy rows stay department-wide because they have no anchor.
	/// </summary>
	[TestFixture]
	public class RecordsGroupScopePreviewTests
	{
		private const int Dept = 4;

		[Test]
		public async Task The_preview_counts_records_per_group_unanchored_records_and_ungrouped_users()
		{
			var records = new Mock<IRmsOperationalRecordsRepository>();
			records.Setup(r => r.CountAllAsync(Dept)).ReturnsAsync(40);
			records.Setup(r => r.CountWithoutGroupScopeAsync(Dept)).ReturnsAsync(6);

			var scopes = new Mock<IRmsRecordGroupScopesRepository>();
			scopes.Setup(s => s.CountRecordsByGroupAsync(Dept)).ReturnsAsync(new Dictionary<int, int> { { 1, 30 }, { 2, 12 } });

			var legacy = new Mock<IRmsLegacyStatsRepository>();
			legacy.Setup(l => l.GetLegacyStatsAsync(Dept)).ReturnsAsync(new RmsLegacyStats { LogCount = 100, LogsWithoutGroupCount = 25, UnitLogCount = 9 });

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = 1, Name = "Station 1" },
				new DepartmentGroup { DepartmentGroupId = 2, Name = "Station 2" },
				new DepartmentGroup { DepartmentGroupId = 3, Name = "Admin" }
			});
			groups.Setup(g => g.GetAllMembersForGroupAsync(1)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "a" }, new DepartmentGroupMember { UserId = "b" } });
			groups.Setup(g => g.GetAllMembersForGroupAsync(2)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "c" } });
			groups.Setup(g => g.GetAllMembersForGroupAsync(3)).ReturnsAsync(new List<DepartmentGroupMember>());

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetAllUsersForDepartmentAsync(Dept, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>
			{
				new IdentityUser { UserId = "a" }, new IdentityUser { UserId = "b" }, new IdentityUser { UserId = "c" }, new IdentityUser { UserId = "d" }, new IdentityUser { UserId = "e" }
			});
			departments.Setup(d => d.GetAllAdminsForDepartmentAsync(Dept)).ReturnsAsync(new List<IdentityUser> { new IdentityUser { UserId = "a" } });

			var service = new RecordsAuthorizationService(Mock.Of<IPermissionsService>(), departments.Object, groups.Object, Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IDepartmentSettingsService>(), records.Object, scopes.Object, Mock.Of<IRmsRecordParticipantsRepository>(), Mock.Of<ICacheProvider>(), legacy.Object);

			var preview = await service.PreviewGroupScopingAsync(Dept);

			preview.TotalRecords.Should().Be(40);
			preview.RecordsWithoutGroupAnchor.Should().Be(6);
			preview.RecordsHiddenFromUngroupedUsers.Should().Be(34);
			preview.LegacyLogsWithoutGroup.Should().Be(25);
			preview.LegacyUnitLogs.Should().Be(9);
			preview.UsersInDepartment.Should().Be(5);
			preview.UsersWithoutGroup.Should().Be(2, "d and e belong to no group");
			preview.DepartmentAdmins.Should().Be(1);
			preview.Groups.Select(g => (g.GroupName, g.MemberCount, g.RecordCount)).Should().Equal(("Station 1", 2, 30), ("Station 2", 1, 12), ("Admin", 0, 0));
		}
	}
}
