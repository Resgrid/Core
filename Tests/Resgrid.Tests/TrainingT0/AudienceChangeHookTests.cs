using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>
	/// Membership writes queue the members they moved for worker 83 (group and role changes enqueue the user, deleting a group
	/// or role re-evaluates its former members), so dynamic audiences follow without a sweep of every department.
	/// </summary>
	[TestFixture]
	public class AudienceChangeHookTests
	{
		private const int DepartmentId = 10;

		private static DepartmentGroupsService Groups(Mock<IDepartmentGroupMembersRepository> members, IContentAudienceChangeQueue queue) =>
			new DepartmentGroupsService(Mock.Of<IDepartmentGroupsRepository>(), members.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IAddressService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<ICacheProvider>(), Mock.Of<IIdentityRepository>(), Mock.Of<IUnitOfWork>(), audienceChanges: queue);

		[Test]
		public async Task Leaving_every_group_queues_the_member()
		{
			var members = new Mock<IDepartmentGroupMembersRepository>();
			members.Setup(x => x.GetAllGroupMembersByUserAndDepartmentAsync("ff1", DepartmentId))
				.ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentGroupId = 3, DepartmentId = DepartmentId, UserId = "ff1" } });
			var queue = new Mock<IContentAudienceChangeQueue>();

			await Groups(members, queue.Object).DeleteUserFromGroupsAsync("ff1", DepartmentId);

			queue.Verify(x => x.EnqueueManyAsync(DepartmentId, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "ff1" })), ContentAudienceChangeSources.Group, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Saving_a_group_member_queues_them()
		{
			var members = new Mock<IDepartmentGroupMembersRepository>();
			var member = new DepartmentGroupMember { DepartmentGroupId = 3, DepartmentId = DepartmentId, UserId = "ff2" };
			members.Setup(x => x.SaveOrUpdateAsync(member, It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(member);
			var queue = new Mock<IContentAudienceChangeQueue>();

			await Groups(members, queue.Object).SaveGroupMember(member);

			queue.Verify(x => x.EnqueueManyAsync(DepartmentId, It.Is<IEnumerable<string>>(ids => ids.Contains("ff2")), ContentAudienceChangeSources.Group, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Deleting_a_groups_members_queues_every_former_member()
		{
			var members = new Mock<IDepartmentGroupMembersRepository>();
			members.Setup(x => x.GetAllGroupMembersByGroupIdAsync(3)).ReturnsAsync(new List<DepartmentGroupMember>
			{
				new DepartmentGroupMember { UserId = "a" }, new DepartmentGroupMember { UserId = "b" }
			});
			members.Setup(x => x.DeleteGroupMembersByGroupIdAsync(3, DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var queue = new Mock<IContentAudienceChangeQueue>();

			await Groups(members, queue.Object).DeleteGroupMembersByGroupIdAsync(3, DepartmentId);

			queue.Verify(x => x.EnqueueManyAsync(DepartmentId, It.Is<IEnumerable<string>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { "a", "b" })), ContentAudienceChangeSources.Group, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Without_a_queue_a_group_change_still_saves()
		{
			var members = new Mock<IDepartmentGroupMembersRepository>();
			members.Setup(x => x.GetAllGroupMembersByUserAndDepartmentAsync("ff1", DepartmentId)).ReturnsAsync(new List<DepartmentGroupMember>());

			(await Groups(members, null).DeleteUserFromGroupsAsync("ff1", DepartmentId)).Should().BeTrue();
		}

		[Test]
		public async Task Removing_a_member_from_every_role_queues_them()
		{
			var roleUsers = new Mock<IPersonnelRoleUsersRepository>();
			roleUsers.Setup(x => x.GetAllRoleUsersForUserAsync(DepartmentId, "ff1")).ReturnsAsync(new List<PersonnelRoleUser>());
			var queue = new Mock<IContentAudienceChangeQueue>();
			var service = new PersonnelRolesService(Mock.Of<IPersonnelRolesRepository>(), roleUsers.Object, Mock.Of<ISubscriptionsService>(),
				Mock.Of<IDepartmentMembersRepository>(), Mock.Of<IEventAggregator>(), Mock.Of<IUnitOfWork>(), audienceChanges: queue.Object);

			await service.RemoveUserFromAllRolesAsync("ff1", DepartmentId);

			queue.Verify(x => x.EnqueueManyAsync(DepartmentId, It.Is<IEnumerable<string>>(ids => ids.Contains("ff1")), ContentAudienceChangeSources.Role, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
