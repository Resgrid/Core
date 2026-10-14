using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Nothing tells a removed member's phones and browsers that the membership is gone, so removal deletes the
	/// member's Novu subscribers in each department they left, or the devices keep receiving that department's calls.
	/// </summary>
	[TestFixture]
	public class MemberRemovalPushSubscriberTests
	{
		private const int Dept = 41;
		private const int OtherDept = 42;
		private const string Leaver = "leaver";

		private Mock<IDepartmentsService> _departments;
		private Mock<IUserSessionService> _sessions;
		private Mock<INovuProvider> _novu;
		private DeleteService _service;

		[SetUp]
		public void SetUp()
		{
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Code = "AAAA", ManagingUserId = "chief", TimeZone = "UTC" });
			_departments.Setup(d => d.GetDepartmentByIdAsync(OtherDept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = OtherDept, Code = "BBBB", ManagingUserId = "chief", TimeZone = "UTC" });
			_departments.Setup(d => d.DeleteUserAsync(Dept, Leaver, "chief", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentMember { DepartmentId = Dept, UserId = Leaver, IsDeleted = true });
			_departments.Setup(d => d.GetAllDepartmentsForUserAsync(Leaver)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = Dept, UserId = Leaver }, new DepartmentMember { DepartmentId = OtherDept, UserId = Leaver }
			});

			_sessions = new Mock<IUserSessionService>();

			_novu = new Mock<INovuProvider>();
			_novu.Setup(n => n.DeleteUserSubscriber(Leaver, It.IsAny<string>())).ReturnsAsync(true);
			_novu.Setup(n => n.DeleteICUserSubscriber(Leaver, It.IsAny<string>())).ReturnsAsync(true);

			_service = new DeleteService(Mock.Of<IAuthorizationService>(), _departments.Object, Mock.Of<ICallsService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUsersService>(),
				Mock.Of<IUserProfileService>(), Mock.Of<IMessageService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IWorkLogsService>(), Mock.Of<IUserStateService>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IDistributionListsService>(), Mock.Of<IShiftsService>(), Mock.Of<IUnitsService>(), Mock.Of<ICertificationService>(),
				Mock.Of<ILogService>(), Mock.Of<IInventoryService>(), Mock.Of<IEventAggregator>(), Mock.Of<IAddressService>(), Mock.Of<IQueueService>(), Mock.Of<IEmailService>(),
				Mock.Of<IDeleteRepository>(), Mock.Of<IAuditLogsRepository>(), Mock.Of<IScheduledTasksService>(), _sessions.Object,
				Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentMemberEmergencyContactService>(), novuProvider: _novu.Object);
		}

		[Test]
		public async Task Revoking_access_deletes_the_members_subscribers_in_that_department()
		{
			var revoked = await _service.RevokeDepartmentAccessAsync(Leaver, Dept, "chief");

			revoked.Should().BeTrue();
			_novu.Verify(n => n.DeleteUserSubscriber(Leaver, "AAAA"), Times.Once);
			_novu.Verify(n => n.DeleteICUserSubscriber(Leaver, "AAAA"), Times.Once);
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_membership_that_was_not_removed_keeps_its_subscribers()
		{
			_departments.Setup(d => d.DeleteUserAsync(Dept, Leaver, "chief", It.IsAny<CancellationToken>())).ReturnsAsync((DepartmentMember)null);

			var revoked = await _service.RevokeDepartmentAccessAsync(Leaver, Dept, "chief");

			revoked.Should().BeFalse();
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_novu_failure_does_not_undo_or_fail_the_removal()
		{
			_novu.Setup(n => n.DeleteUserSubscriber(Leaver, "AAAA")).ThrowsAsync(new TimeoutException());
			_novu.Setup(n => n.DeleteICUserSubscriber(Leaver, "AAAA")).ReturnsAsync(false);

			var revoked = await _service.RevokeDepartmentAccessAsync(Leaver, Dept, "chief");

			revoked.Should().BeTrue();
			_sessions.Verify(s => s.RevokeDepartmentSessionsAsync(Leaver, Dept, It.IsAny<UserSessionRevocationReason>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_subscribers_go_even_when_the_session_revocation_fails()
		{
			_sessions.Setup(s => s.RevokeDepartmentSessionsAsync(Leaver, Dept, It.IsAny<UserSessionRevocationReason>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("session store down"));

			Func<Task> act = () => _service.RevokeDepartmentAccessAsync(Leaver, Dept, "chief");

			await act.Should().ThrowAsync<InvalidOperationException>();
			_novu.Verify(n => n.DeleteUserSubscriber(Leaver, "AAAA"), Times.Once);
			_novu.Verify(n => n.DeleteICUserSubscriber(Leaver, "AAAA"), Times.Once);
		}

		[Test]
		public async Task Deleting_the_account_deletes_the_subscribers_in_every_department()
		{
			var result = await _service.DeleteUserAccountAsync(Dept, Leaver, Leaver, null, null);

			result.Should().Be(DeleteUserResults.NoFailure);
			_novu.Verify(n => n.DeleteUserSubscriber(Leaver, "AAAA"), Times.Once);
			_novu.Verify(n => n.DeleteICUserSubscriber(Leaver, "AAAA"), Times.Once);
			_novu.Verify(n => n.DeleteUserSubscriber(Leaver, "BBBB"), Times.Once);
			_novu.Verify(n => n.DeleteICUserSubscriber(Leaver, "BBBB"), Times.Once);
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_department_without_a_code_is_skipped()
		{
			_departments.Setup(d => d.GetDepartmentByIdAsync(OtherDept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = OtherDept, Code = null, ManagingUserId = "chief" });

			await _service.DeleteUserAccountAsync(Dept, Leaver, Leaver, null, null);

			_novu.Verify(n => n.DeleteUserSubscriber(Leaver, "AAAA"), Times.Once);
			_novu.Verify(n => n.DeleteUserSubscriber(Leaver, It.Is<string>(c => c != "AAAA")), Times.Never);
		}
	}
}
