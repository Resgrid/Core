using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
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
					_eventAggregatorMock.Object);
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
				_repositoryCallOrder.Should().Equal("dependencies", "role");
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
