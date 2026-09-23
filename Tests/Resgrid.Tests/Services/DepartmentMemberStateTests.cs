using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Removed, disabled and hidden members are left out of automated reporting and notifications (the expired-certification
	/// report that named departed members). These pin the shared predicates and the department-level sets built on them.
	/// </summary>
	[TestFixture]
	public class DepartmentMemberStateTests
	{
		private const int Dept = 31;

		private static DepartmentMember Member(string userId, bool deleted = false, bool? disabled = null, bool? hidden = null, bool? admin = null, int departmentId = Dept)
			=> new DepartmentMember { DepartmentId = departmentId, UserId = userId, IsDeleted = deleted, IsDisabled = disabled, IsHidden = hidden, IsAdmin = admin };

		private static List<DepartmentMember> Roster() => new List<DepartmentMember>
		{
			Member("active", admin: true),
			Member("nulls"),
			Member("removed", deleted: true, admin: true),
			Member("disabled", disabled: true, admin: true),
			Member("hidden", hidden: true, admin: true),
			Member("owner", hidden: false, disabled: false),
			Member("elsewhere", departmentId: 99)
		};

		private static DepartmentsService Service(Mock<IDepartmentMembersRepository> members, Mock<IDepartmentsRepository> departments, IUserProfileService profiles = null, ICacheProvider cache = null)
			=> new DepartmentsService(departments.Object, members.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentCallEmailsRepository>(),
				Mock.Of<IDepartmentCallPruningRepository>(), cache ?? Mock.Of<ICacheProvider>(), Mock.Of<IUsersService>(), Mock.Of<IDepartmentSettingsService>(),
				profiles ?? Mock.Of<IUserProfileService>(), Mock.Of<ILimitsService>(), Mock.Of<IEventAggregator>(), Mock.Of<IIdentityRepository>(), Mock.Of<IDepartmentCallPruningRepository>());

		[Test]
		public void Current_excludes_removed_and_disabled_and_active_also_excludes_hidden()
		{
			var byId = Roster().ToDictionary(m => m.UserId);
			DepartmentMemberStateHelper.IsCurrentMember(byId["active"], Dept).Should().BeTrue();
			DepartmentMemberStateHelper.IsCurrentMember(byId["nulls"], Dept).Should().BeTrue("null flags read as false");
			DepartmentMemberStateHelper.IsCurrentMember(byId["hidden"], Dept).Should().BeTrue("hidden members can still act");
			DepartmentMemberStateHelper.IsCurrentMember(byId["removed"], Dept).Should().BeFalse();
			DepartmentMemberStateHelper.IsCurrentMember(byId["disabled"], Dept).Should().BeFalse();
			DepartmentMemberStateHelper.IsCurrentMember(byId["elsewhere"], Dept).Should().BeFalse("another department's row");
			DepartmentMemberStateHelper.IsCurrentMember(null, Dept).Should().BeFalse();

			DepartmentMemberStateHelper.IsActiveMember(byId["active"], Dept).Should().BeTrue();
			DepartmentMemberStateHelper.IsActiveMember(byId["nulls"], Dept).Should().BeTrue();
			DepartmentMemberStateHelper.IsActiveMember(byId["hidden"], Dept).Should().BeFalse();
			DepartmentMemberStateHelper.IsActiveMember(byId["removed"], Dept).Should().BeFalse();
			DepartmentMemberStateHelper.IsActiveMember(byId["disabled"], Dept).Should().BeFalse();
		}

		[Test]
		public async Task Active_member_ids_come_from_the_unlimited_roster_and_leave_out_removed_disabled_and_hidden()
		{
			var members = new Mock<IDepartmentMembersRepository>();
			members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(Dept)).ReturnsAsync(Roster());
			var ids = await Service(members, new Mock<IDepartmentsRepository>()).GetActiveMemberUserIdsAsync(Dept);

			ids.Should().BeEquivalentTo(new[] { "active", "nulls", "owner" });
			ids.Contains("ACTIVE").Should().BeTrue("user ids compare case-insensitively");
		}

		[Test]
		public async Task Picker_names_are_active_members_only_while_the_label_list_keeps_everyone_not_removed()
		{
			var members = new Mock<IDepartmentMembersRepository>();
			members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(Dept)).ReturnsAsync(Roster());
			var profiles = new Mock<IUserProfileService>();
			// The profile list (like its query) already leaves removed members out; disabled and hidden ones are still on it.
			profiles.Setup(p => p.GetAllProfilesForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new[] { "owner", "nulls", "disabled", "hidden", "active" }
				.ToDictionary(id => id, id => new UserProfile { UserId = id, FirstName = "First", LastName = id }));
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<List<PersonName>>>>(), It.IsAny<TimeSpan>()))
				.Returns((string key, Func<Task<List<PersonName>>> load, TimeSpan expiry) => load());
			var service = Service(members, new Mock<IDepartmentsRepository>(), profiles.Object, cache.Object);

			(await service.GetSelectablePersonnelNamesAsync(Dept)).Select(n => n.LastName).Should().Equal("active", "nulls", "owner");
			(await service.GetAllPersonnelNamesForDepartmentAsync(Dept)).Select(n => n.LastName).Should().BeEquivalentTo(new[] { "owner", "nulls", "disabled", "hidden", "active" },
				"history and kept values are still labelled from the full list");
		}

		[Test]
		public async Task Admin_lists_drop_removed_and_disabled_admins_and_the_active_list_drops_hidden_ones_too()
		{
			var departments = new Mock<IDepartmentsRepository>();
			departments.Setup(d => d.GetDepartmentWithMembersByIdAsync(Dept)).ReturnsAsync(new Department { DepartmentId = Dept, ManagingUserId = "owner", Members = Roster() });
			var service = Service(new Mock<IDepartmentMembersRepository>(), departments);

			(await service.GetAllAdminsForDepartmentAsync(Dept)).Select(a => a.UserId).Should().BeEquivalentTo(new[] { "active", "hidden", "owner" },
				"removal leaves IsAdmin set, so a removed or disabled admin must be filtered here");
			(await service.GetActiveAdminsForDepartmentAsync(Dept)).Select(a => a.UserId).Should().BeEquivalentTo(new[] { "active", "owner" });
		}
	}
}
