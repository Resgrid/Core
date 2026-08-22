using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace PersonnelRolesServiceTests
	{
		public class with_the_personnel_roles_service : TestBase
		{
			protected IPersonnelRolesService _personnelRolesService;

			protected Mock<IPersonnelRolesRepository> _personnelRolesRepositoryMock;
			protected Mock<IPersonnelRoleUsersRepository> _personnelRoleUsersRepositoryMock;
			protected Mock<ISubscriptionsService> _subscriptionsServiceMock;
			protected Mock<IDepartmentMembersRepository> _departmentMembersRepositoryMock;
			protected Mock<IEventAggregator> _eventAggregatorMock;
			protected Mock<IUnitOfWork> _unitOfWorkMock;

			protected readonly List<string> _repositoryCallOrder = new List<string>();

			protected with_the_personnel_roles_service()
			{
				BuildService();
			}

			// Rebuild the mocks before every test so setups from one test never leak into the next
			// (NUnit reuses the fixture instance for every test in the fixture).
			protected override void Before_all_tests()
			{
				BuildService();
			}

			private void BuildService()
			{
				_repositoryCallOrder.Clear();

				_personnelRolesRepositoryMock = new Mock<IPersonnelRolesRepository>();
				_personnelRoleUsersRepositoryMock = new Mock<IPersonnelRoleUsersRepository>();
				_subscriptionsServiceMock = new Mock<ISubscriptionsService>();
				_departmentMembersRepositoryMock = new Mock<IDepartmentMembersRepository>();
				_eventAggregatorMock = new Mock<IEventAggregator>();
				_unitOfWorkMock = new Mock<IUnitOfWork>();

				_unitOfWorkMock.Setup(x => x.CreateOrGetConnection())
					.Callback(() => _repositoryCallOrder.Add("connection"))
					.Returns((DbConnection)null);

				_unitOfWorkMock.Setup(x => x.CommitChanges())
					.Callback(() => _repositoryCallOrder.Add("commit"));

				_unitOfWorkMock.Setup(x => x.DiscardChanges())
					.Callback(() => _repositoryCallOrder.Add("rollback"));

				_personnelRolesRepositoryMock
					.Setup(x => x.DeleteRoleDependenciesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
					.Callback(() => _repositoryCallOrder.Add("dependencies"))
					.ReturnsAsync(true);

				_personnelRolesRepositoryMock
					.Setup(x => x.DeleteAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>()))
					.Callback(() => _repositoryCallOrder.Add("role"))
					.ReturnsAsync(true);

				_personnelRolesService = new PersonnelRolesService(
					_personnelRolesRepositoryMock.Object,
					_personnelRoleUsersRepositoryMock.Object,
					_subscriptionsServiceMock.Object,
					_departmentMembersRepositoryMock.Object,
					_eventAggregatorMock.Object,
					_unitOfWorkMock.Object);
			}
		}

		[TestFixture]
		public class when_deleting_a_personnel_role : with_the_personnel_roles_service
		{
			[Test]
			public async Task dependent_rows_should_be_removed_before_the_role_row()
			{
				_personnelRolesRepositoryMock.Setup(x => x.GetRoleByRoleIdAsync(6787))
					.ReturnsAsync(new PersonnelRole { PersonnelRoleId = 6787, DepartmentId = 1, Name = "Paramedic" });

				var result = await _personnelRolesService.DeleteRoleByIdAsync(6787);

				result.Should().BeTrue();
				// CallDispatchRoles has a non-cascading FK on the role, so the cleanup has to land first
				// or the role delete throws a constraint violation.
				_repositoryCallOrder.Should().Equal("connection", "dependencies", "role", "commit");
			}

			[Test]
			public async Task both_deletes_should_share_one_transaction_that_commits_once()
			{
				_personnelRolesRepositoryMock.Setup(x => x.GetRoleByRoleIdAsync(6787))
					.ReturnsAsync(new PersonnelRole { PersonnelRoleId = 6787, DepartmentId = 1, Name = "Paramedic" });

				await _personnelRolesService.DeleteRoleByIdAsync(6787);

				// The dependency cleanup and the role delete have to land on the same connection, or a
				// failure on the second one leaves the first one already committed.
				_unitOfWorkMock.Verify(x => x.CreateOrGetConnection(), Times.Once);
				_unitOfWorkMock.Verify(x => x.CommitChanges(), Times.Once);
				_unitOfWorkMock.Verify(x => x.DiscardChanges(), Times.Never);
			}

			[Test]
			public void a_failed_role_delete_should_roll_back_the_dependency_cleanup()
			{
				_personnelRolesRepositoryMock.Setup(x => x.GetRoleByRoleIdAsync(6787))
					.ReturnsAsync(new PersonnelRole { PersonnelRoleId = 6787, DepartmentId = 1, Name = "Paramedic" });

				_personnelRolesRepositoryMock
					.Setup(x => x.DeleteAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>()))
					.ThrowsAsync(new Exception("constraint violation"));

				Assert.ThrowsAsync<Exception>(async () => await _personnelRolesService.DeleteRoleByIdAsync(6787));

				_unitOfWorkMock.Verify(x => x.DiscardChanges(), Times.Once);
				_unitOfWorkMock.Verify(x => x.CommitChanges(), Times.Never);
				// A rolled back delete never happened, so the visibility matrices must not be rebuilt.
				_eventAggregatorMock.Verify(x => x.SendMessage<SecurityRefreshEvent>(It.IsAny<SecurityRefreshEvent>()), Times.Never);
			}

			[Test]
			public async Task a_role_that_no_longer_exists_should_not_be_deleted()
			{
				_personnelRolesRepositoryMock.Setup(x => x.GetRoleByRoleIdAsync(6787))
					.ReturnsAsync((PersonnelRole)null);

				var result = await _personnelRolesService.DeleteRoleByIdAsync(6787);

				result.Should().BeFalse();
				_repositoryCallOrder.Should().BeEmpty();
			}
		}
	}
}
