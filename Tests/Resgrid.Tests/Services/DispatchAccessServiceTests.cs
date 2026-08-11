using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Who may work the dispatch desk. This gate keeps private command, unit and responder traffic away
	/// from members the department hasn't authorized, so the interesting cases are the defaults (nothing
	/// configured must keep working) and the failure modes (an error must not hand access over).
	/// </summary>
	[TestFixture]
	public class DispatchAccessServiceTests
	{
		private const int DepartmentId = 1;
		private const string UserId = "user-1";

		private Mock<IPermissionsService> _permissionsService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<ICacheProvider> _cacheProvider;

		[SetUp]
		public void Setup()
		{
			_permissionsService = new Mock<IPermissionsService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();
			_cacheProvider = new Mock<ICacheProvider>();

			// No cached verdict so the evaluation always runs.
			_cacheProvider.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string)null);
			_cacheProvider.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);

			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync(UserId, DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId, IsAdmin = false });
			_personnelRolesService.Setup(x => x.GetRolesForUserAsync(UserId, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
		}

		private DispatchAccessService BuildService()
			=> new DispatchAccessService(
				_permissionsService.Object,
				_departmentsService.Object,
				_departmentGroupsService.Object,
				_personnelRolesService.Object,
				_cacheProvider.Object);

		private void GivenPermission(Permission permission)
			=> _permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.DispatchAppLogin))
				.ReturnsAsync(permission);

		[Test]
		public async Task everyone_is_allowed_when_the_department_has_not_configured_the_permission()
		{
			// The default has to stay open, or upgrading Core would lock every existing department out.
			GivenPermission(null);

			var result = await BuildService().CanUseDispatchAsync(DepartmentId, UserId);

			result.Should().BeTrue();
		}

		[Test]
		public async Task the_configured_permission_decides_when_one_exists()
		{
			var permission = new Permission { DepartmentId = DepartmentId, Action = (int)PermissionActions.DepartmentAdminsOnly };
			GivenPermission(permission);
			_permissionsService.Setup(x => x.IsUserAllowed(permission, false, false, It.IsAny<List<PersonnelRole>>())).Returns(false);

			var result = await BuildService().CanUseDispatchAsync(DepartmentId, UserId);

			result.Should().BeFalse();
		}

		[Test]
		public async Task the_departments_managing_user_counts_as_an_admin()
		{
			var permission = new Permission { DepartmentId = DepartmentId, Action = (int)PermissionActions.DepartmentAdminsOnly };
			GivenPermission(permission);
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync("owner", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "owner", DepartmentId = DepartmentId, IsAdmin = false });
			_personnelRolesService.Setup(x => x.GetRolesForUserAsync("owner", DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
			_permissionsService.Setup(x => x.IsUserAllowed(permission, true, false, It.IsAny<List<PersonnelRole>>())).Returns(true);

			var result = await BuildService().CanUseDispatchAsync(DepartmentId, "owner");

			result.Should().BeTrue();
		}

		[Test]
		public async Task someone_who_is_not_a_member_of_the_department_is_refused()
		{
			GivenPermission(new Permission { DepartmentId = DepartmentId, Action = (int)PermissionActions.Everyone });
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync("stranger", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((DepartmentMember)null);

			var result = await BuildService().CanUseDispatchAsync(DepartmentId, "stranger");

			result.Should().BeFalse();
		}

		[Test]
		public async Task a_non_member_is_refused_even_when_the_permission_is_not_configured()
		{
			// The open default means "everyone in the department", never "everyone on the platform" —
			// this gate is asked about a channel's department, not necessarily the caller's own.
			GivenPermission(null);
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync("stranger", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((DepartmentMember)null);

			var result = await BuildService().CanUseDispatchAsync(DepartmentId, "stranger");

			result.Should().BeFalse();
		}

		[TestCase(true, false)]
		[TestCase(false, true)]
		public async Task a_disabled_or_deleted_member_is_refused_even_when_the_permission_is_not_configured(bool disabled, bool deleted)
		{
			GivenPermission(null);
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync("former", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "former", DepartmentId = DepartmentId, IsDisabled = disabled, IsDeleted = deleted });

			var result = await BuildService().CanUseDispatchAsync(DepartmentId, "former");

			result.Should().BeFalse();
		}

		[Test]
		public async Task an_evaluation_failure_fails_closed()
		{
			// Erring open here would leak private traffic, which is the exact thing this gate exists for.
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.DispatchAppLogin))
				.ThrowsAsync(new Exception("db down"));

			var result = await BuildService().CanUseDispatchAsync(DepartmentId, UserId);

			result.Should().BeFalse();
		}

		[TestCase(null)]
		[TestCase("")]
		public async Task a_missing_user_is_refused(string userId)
		{
			var result = await BuildService().CanUseDispatchAsync(DepartmentId, userId);

			result.Should().BeFalse();
		}

		[Test]
		public async Task the_dispatch_audience_is_the_whole_department_by_default()
		{
			GivenPermission(null);
			_departmentsService.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { UserId = "a", DepartmentId = DepartmentId },
				new DepartmentMember { UserId = "b", DepartmentId = DepartmentId }
			});

			var result = await BuildService().GetDispatchUserIdsAsync(DepartmentId);

			result.Should().BeEquivalentTo(new[] { "a", "b" });
			// The open default must not cost a per-user evaluation.
			_departmentsService.Verify(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task the_dispatch_audience_skips_disabled_and_deleted_members()
		{
			GivenPermission(null);
			_departmentsService.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { UserId = "active", DepartmentId = DepartmentId },
				new DepartmentMember { UserId = "disabled", DepartmentId = DepartmentId, IsDisabled = true },
				new DepartmentMember { UserId = "deleted", DepartmentId = DepartmentId, IsDeleted = true }
			});

			var result = await BuildService().GetDispatchUserIdsAsync(DepartmentId);

			result.Should().BeEquivalentTo(new[] { "active" });
		}

		[Test]
		public async Task a_restricted_department_only_returns_the_authorized_members()
		{
			var permission = new Permission { DepartmentId = DepartmentId, Action = (int)PermissionActions.DepartmentAdminsOnly };
			GivenPermission(permission);
			_departmentsService.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { UserId = "dispatcher", DepartmentId = DepartmentId, IsAdmin = true },
				new DepartmentMember { UserId = "firefighter", DepartmentId = DepartmentId, IsAdmin = false }
			});

			foreach (var id in new[] { "dispatcher", "firefighter" })
			{
				var isAdmin = id == "dispatcher";
				_departmentsService.Setup(x => x.GetDepartmentMemberAsync(id, DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(new DepartmentMember { UserId = id, DepartmentId = DepartmentId, IsAdmin = isAdmin });
				_personnelRolesService.Setup(x => x.GetRolesForUserAsync(id, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
				_permissionsService.Setup(x => x.IsUserAllowed(permission, isAdmin, false, It.IsAny<List<PersonnelRole>>())).Returns(isAdmin);
			}

			var result = await BuildService().GetDispatchUserIdsAsync(DepartmentId);

			result.Should().BeEquivalentTo(new[] { "dispatcher" });
		}
	}

	/// <summary>
	/// The commander gate — same shared evaluation as dispatch, different permission. Covers the parts
	/// that are specific to it: the right permission is read, and the two gates never share a verdict.
	/// </summary>
	[TestFixture]
	public class CommandAccessServiceTests
	{
		private const int DepartmentId = 1;
		private const string UserId = "user-1";

		private Mock<IPermissionsService> _permissionsService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<ICacheProvider> _cacheProvider;
		private readonly List<string> _cacheKeys = new List<string>();

		[SetUp]
		public void Setup()
		{
			_permissionsService = new Mock<IPermissionsService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();
			_cacheProvider = new Mock<ICacheProvider>();
			_cacheKeys.Clear();

			_cacheProvider.Setup(x => x.GetStringAsync(It.IsAny<string>()))
				.Callback<string>(key => _cacheKeys.Add(key))
				.ReturnsAsync((string)null);
			_cacheProvider.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);

			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync(UserId, DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId, IsAdmin = false });
			_personnelRolesService.Setup(x => x.GetRolesForUserAsync(UserId, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
		}

		private CommandAccessService BuildService()
			=> new CommandAccessService(
				_permissionsService.Object,
				_departmentsService.Object,
				_departmentGroupsService.Object,
				_personnelRolesService.Object,
				_cacheProvider.Object);

		[Test]
		public async Task everyone_may_command_when_the_department_has_not_configured_the_permission()
		{
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CommandAppLogin))
				.ReturnsAsync((Permission)null);

			var result = await BuildService().CanUseCommandAsync(DepartmentId, UserId);

			result.Should().BeTrue();
		}

		[Test]
		public async Task it_reads_the_command_permission_not_the_dispatch_one()
		{
			var commandPermission = new Permission { DepartmentId = DepartmentId, Action = (int)PermissionActions.DepartmentAdminsOnly };
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CommandAppLogin))
				.ReturnsAsync(commandPermission);
			_permissionsService.Setup(x => x.IsUserAllowed(commandPermission, false, false, It.IsAny<List<PersonnelRole>>())).Returns(false);

			var result = await BuildService().CanUseCommandAsync(DepartmentId, UserId);

			result.Should().BeFalse();
			_permissionsService.Verify(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.DispatchAppLogin), Times.Never);
		}

		[Test]
		public async Task its_cache_key_is_distinct_from_the_dispatch_gate()
		{
			// Sharing a key would let a dispatch verdict answer a command question, and vice versa.
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CommandAppLogin))
				.ReturnsAsync((Permission)null);

			await BuildService().CanUseCommandAsync(DepartmentId, UserId);

			_cacheKeys.Should().Contain(key => key.StartsWith("commandaccess:"));
			_cacheKeys.Should().NotContain(key => key.StartsWith("dispatchaccess:"));
		}

		[Test]
		public async Task an_evaluation_failure_fails_closed()
		{
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CommandAppLogin))
				.ThrowsAsync(new Exception("db down"));

			var result = await BuildService().CanUseCommandAsync(DepartmentId, UserId);

			result.Should().BeFalse();
		}

		/// <summary>
		/// Assisting on a board is stricter than being allowed to command. The permission is open by
		/// default, and inferring "therefore every member may move resources on any board" from that open
		/// default would hand out authority nobody asked for — so assist requires the department to have
		/// deliberately narrowed who commands.
		/// </summary>
		[Test]
		public async Task assisting_is_not_granted_off_the_open_default()
		{
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CommandAppLogin))
				.ReturnsAsync((Permission)null);
			var service = BuildService();

			// The user may command — the permission is wide open — but that alone grants no board authority.
			(await service.CanUseCommandAsync(DepartmentId, UserId)).Should().BeTrue();
			(await service.CanAssistWithCommandAsync(DepartmentId, UserId)).Should().BeFalse();
		}

		[Test]
		public async Task assisting_is_not_granted_when_the_permission_is_explicitly_everyone()
		{
			// An explicit "Everyone" row is the same statement as no row: no opinion about who is trusted.
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CommandAppLogin))
				.ReturnsAsync(new Permission { DepartmentId = DepartmentId, Action = (int)PermissionActions.Everyone });

			var result = await BuildService().CanAssistWithCommandAsync(DepartmentId, UserId);

			result.Should().BeFalse();
		}

		[Test]
		public async Task assisting_is_granted_once_the_department_picks_who_commands()
		{
			var permission = new Permission { DepartmentId = DepartmentId, Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, Data = "7" };
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CommandAppLogin))
				.ReturnsAsync(permission);
			_permissionsService.Setup(x => x.IsUserAllowed(permission, false, false, It.IsAny<List<PersonnelRole>>())).Returns(true);

			var result = await BuildService().CanAssistWithCommandAsync(DepartmentId, UserId);

			result.Should().BeTrue();
		}

		[Test]
		public async Task assisting_is_refused_for_someone_the_narrowed_permission_excludes()
		{
			var permission = new Permission { DepartmentId = DepartmentId, Action = (int)PermissionActions.DepartmentAdminsOnly };
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CommandAppLogin))
				.ReturnsAsync(permission);
			_permissionsService.Setup(x => x.IsUserAllowed(permission, false, false, It.IsAny<List<PersonnelRole>>())).Returns(false);

			var result = await BuildService().CanAssistWithCommandAsync(DepartmentId, UserId);

			result.Should().BeFalse();
		}
	}
}
