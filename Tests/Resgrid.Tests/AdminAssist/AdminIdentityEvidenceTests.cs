using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class AdminIdentityEvidenceTests
	{
		[TestCase(false)] [TestCase(true)]
		public async Task Hidden_current_admins_are_counted_group_admins_are_not_double_counted_and_missing_group_data_is_unknown(bool missingGroups)
		{
			var members = new Mock<IDepartmentMembersRepository>(); var groups = new Mock<IDepartmentGroupsRepository>(); var departments = new Mock<IDepartmentsService>(); var users = new Mock<IUsersService>(); var visibility = new Mock<IAuthorizationService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(7, true)).ReturnsAsync(new Department { DepartmentId = 7, ManagingUserId = "owner" });
			members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(7)).ReturnsAsync(new List<DepartmentMember> {
				new() { DepartmentId = 7, UserId = "owner", IsHidden = true }, new() { DepartmentId = 7, UserId = "admin", IsAdmin = true },
				new() { DepartmentId = 7, UserId = "group" }, new() { DepartmentId = 7, UserId = "disabled", IsAdmin = true, IsDisabled = true }
			});
			groups.Setup(g => g.GetAllGroupsByDepartmentIdAsync(7)).ReturnsAsync(new List<DepartmentGroup> { new() { DepartmentId = 7, DepartmentGroupId = 1, Members = missingGroups ? null : new List<DepartmentGroupMember> {
				new() { DepartmentId = 7, DepartmentGroupId = 1, UserId = "admin", IsAdmin = true }, new() { DepartmentId = 7, DepartmentGroupId = 1, UserId = "group", IsAdmin = true }
			} } });
			users.Setup(u => u.GetUserById(It.IsAny<string>(), true)).Returns((string id, bool _) => new IdentityUser { UserId = id, TwoFactorEnabled = id == "admin" });
			visibility.Setup(v => v.CanUserViewPersonAsync("viewer", It.IsAny<string>(), 7)).ReturnsAsync(true);
			var source = new AdminIdentityEvidenceSource(members.Object, groups.Object, departments.Object, users.Object, visibility.Object);
			var facts = (await source.ReadAsync(new(7, "viewer"), DateTime.UtcNow, CancellationToken.None)).ToDictionary(f => f.Id);
			Assert.That(facts["activeAdminCount"].Number, Is.EqualTo(2));
			Assert.That(facts["adminsWithoutMfa"].Number, Is.EqualTo(1));
			Assert.That(facts["groupOnlyAdminsWithoutMfa"].State, Is.EqualTo(missingGroups ? EvidenceState.Unknown : EvidenceState.Known));
			Assert.That(facts["groupOnlyAdminsWithoutMfa"].Number, Is.EqualTo(missingGroups ? (decimal?)null : 1));
			users.Verify(u => u.GetUserById("disabled", true), Times.Never);
		}
	}
}
