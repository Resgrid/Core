using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// GetGroupIdsForAllUsersInDepartmentAsync is the department-wide form of GetGroupForUserAsync: one read of the
	/// department's membership rows, and for every user the group the single lookup returns.
	/// </summary>
	[TestFixture]
	public class DepartmentGroupsServiceGroupLookupTests
	{
		private const int DepartmentId = 7;

		// Membership rows in the order the department query returns them; "two-groups" has a row in 10 before one in 20.
		private static readonly List<DepartmentGroupMember> Rows = new List<DepartmentGroupMember>
		{
			new DepartmentGroupMember { DepartmentGroupMemberId = 1, DepartmentId = DepartmentId, DepartmentGroupId = 10, UserId = "a" },
			new DepartmentGroupMember { DepartmentGroupMemberId = 2, DepartmentId = DepartmentId, DepartmentGroupId = 10, UserId = "two-groups" },
			new DepartmentGroupMember { DepartmentGroupMemberId = 3, DepartmentId = DepartmentId, DepartmentGroupId = 20, UserId = "b", IsAdmin = true },
			new DepartmentGroupMember { DepartmentGroupMemberId = 4, DepartmentId = DepartmentId, DepartmentGroupId = 20, UserId = "two-groups" }
		};

		private Mock<IDepartmentGroupMembersRepository> _members;
		private DepartmentGroupsService _service;

		[SetUp]
		public void SetUp()
		{
			_members = new Mock<IDepartmentGroupMembersRepository>();
			_members.Setup(m => m.GetAllGroupMembersByDepartmentAsync(DepartmentId)).ReturnsAsync(Rows);
			_members.Setup(m => m.GetAllGroupMembersByUserAndDepartmentAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string userId, int _) => Rows.Where(r => r.UserId == userId));
			var groups = new Mock<IDepartmentGroupsRepository>();
			groups.Setup(g => g.GetGroupByGroupIdAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => new DepartmentGroup { DepartmentGroupId = id, DepartmentId = DepartmentId });

			_service = new DepartmentGroupsService(groups.Object, _members.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IAddressService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<ICacheProvider>(), Mock.Of<IIdentityRepository>(), Mock.Of<IUnitOfWork>());
		}

		[Test]
		public async Task Every_users_group_matches_the_single_lookup()
		{
			var groupIds = await _service.GetGroupIdsForAllUsersInDepartmentAsync(DepartmentId);

			foreach (var userId in new[] { "a", "b", "two-groups", "not-grouped", "A" })
			{
				var single = await _service.GetGroupForUserAsync(userId, DepartmentId);
				groupIds.TryGetValue(userId, out var bulk).Should().Be(single != null, userId);
				if (single != null)
					bulk.Should().Be(single.DepartmentGroupId, userId);
			}

			groupIds["two-groups"].Should().Be(10, "the first membership row wins, as in the single lookup");
			_members.Verify(m => m.GetAllGroupMembersByDepartmentAsync(DepartmentId), Times.Once);
		}
	}
}
