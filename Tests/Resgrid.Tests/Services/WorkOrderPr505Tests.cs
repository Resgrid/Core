using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class WorkOrderP2M1Tests
	{
		[TestCase(null), TestCase(""), TestCase("not-a-guid")]
		public async Task Creation_requires_a_caller_owned_retry_identifier(string requestId)
		{
			var input = Input(); input.RequestId = requestId;
			await FluentActions.Awaiting(() => _service.CreateAsync(_actor, input)).Should().ThrowAsync<WorkOrderException>().Where(e => e.StatusCode == 400);
			_store.All<WorkOrder>().Should().BeEmpty();
			JsonConvert.DeserializeObject<WorkOrderInput>("{}").RequestId.Should().BeNull();
		}

		[TestCase(null), TestCase(125)]
		public async Task Requester_edits_preserve_the_existing_approved_cost(decimal? submittedCost)
		{
			var input = Input(); input.Content.ApprovedCost = 125;
			var detail = await _service.CreateAsync(_actor, input);
			_auth.Setup(a => a.CanManageAsync(_actor, It.IsAny<int?>())).ReturnsAsync(false);
			detail.Input.Content.ApprovedCost = submittedCost; detail.Input.Content.Title = "Updated title";
			await _service.UpdateAsync(_actor, detail.Order.Id, detail.Input);
			var updated = await _service.GetAsync(_actor, detail.Order.Id);
			updated.Input.Content.ApprovedCost.Should().Be(125);
			updated.Input.Content.Title.Should().Be("Updated title");
			updated.Input.Content.ApprovedCost = 999;
			await FluentActions.Awaiting(() => _service.UpdateAsync(_actor, detail.Order.Id, updated.Input)).Should().ThrowAsync<WorkOrderException>().Where(e => e.StatusCode == 403);
		}

		[TestCase("fr-FR"), TestCase("ar-SA"), TestCase("th-TH")]
		public void Workflow_dates_keep_the_same_UTC_instant_for_typed_and_string_values(string culture)
		{
			var previous = CultureInfo.CurrentCulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
				var expected = new DateTime(2026, 9, 3, 12, 34, 56, DateTimeKind.Utc);
				foreach (var value in new JToken[] { new JValue(expected), new JValue(new DateTimeOffset(expected).ToOffset(TimeSpan.FromHours(2))), new JValue("2026-09-03T14:34:56+02:00") })
				{
					var result = JObject.Parse(WorkOrderWorkflowPayload.Routing(new JObject { ["DueOn"] = value }));
					((DateTimeOffset)result["DueOn"]).UtcDateTime.Should().Be(expected);
				}
			}
			finally { CultureInfo.CurrentCulture = previous; }
		}
	}

	[TestFixture]
	public class WorkOrderAuthorizationContextTests
	{
		[TestCase(PermissionActions.DepartmentAdminsOnly, false, false, false, false)]
		[TestCase(PermissionActions.DepartmentAdminsOnly, true, false, false, true)]
		[TestCase(PermissionActions.DepartmentAndGroupAdmins, false, true, false, true)]
		[TestCase(PermissionActions.DepartmentAndGroupAdmins, false, false, true, false)]
		[TestCase(PermissionActions.DepartmentAdminsAndSelectRoles, false, false, true, true)]
		[TestCase(PermissionActions.DepartmentAdminsAndSelectRoles, false, true, false, false)]
		[TestCase(PermissionActions.DepartmentAndGroupAdminsAndSelectRoles, false, true, false, true)]
		[TestCase(PermissionActions.DepartmentAndGroupAdminsAndSelectRoles, false, false, true, true)]
		[TestCase(PermissionActions.Everyone, false, false, false, true)]
		[TestCase((PermissionActions)999, false, true, true, false)]
		public async Task Scope_and_choices_preserve_permission_roles_group_locks_and_fresh_revocation(PermissionActions action, bool admin, bool groupAdmin, bool hasRole, bool allowed)
		{
			var actor = new ChecklistActor { DepartmentId = 77, UserId = "member" };
			var member = new DepartmentMember { DepartmentId = 77, UserId = actor.UserId, IsAdmin = admin };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(actor.UserId, 77, true)).ReturnsAsync(member);
			departments.Setup(d => d.GetDepartmentByIdAsync(77, true)).ReturnsAsync(new Department { DepartmentId = 77 });
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupForUserAsync(actor.UserId, 77)).ReturnsAsync(new DepartmentGroup { DepartmentId = 77, DepartmentGroupId = 10,
				Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = actor.UserId, IsAdmin = groupAdmin } } });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(r => r.GetRolesForUserAsync(actor.UserId, 77)).ReturnsAsync(hasRole ? new List<PersonnelRole> { new PersonnelRole { DepartmentId = 77, PersonnelRoleId = 3 } } : new List<PersonnelRole>());
			var permissions = new Mock<IPermissionsService>();
			permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(77, It.IsAny<PermissionTypes>())).ReturnsAsync(new Permission { Action = (int)action, Data = "3", LockToGroup = true });
			var assignments = new Mock<IChecklistAssignmentService>();
			assignments.Setup(a => a.ChoicesAsync(actor)).ReturnsAsync(new List<ChecklistAssignmentChoice> { new() { Type = 3, Id = "10", Name = "Own group" }, new() { Type = 3, Id = "20", Name = "Other group" } });
			var service = new WorkOrderAuthorizationService(departments.Object, groups.Object, roles.Object, permissions.Object, Mock.Of<IUnitsService>(), Mock.Of<IAuthorizationService>(), assignments.Object);
			var scope = await service.ScopeAsync(actor);
			departments.Verify(d => d.GetDepartmentMemberAsync(actor.UserId, 77, true), Times.Once);
			departments.Verify(d => d.GetDepartmentByIdAsync(77, true), Times.Once);
			groups.Verify(g => g.GetGroupForUserAsync(actor.UserId, 77), Times.Once);
			roles.Verify(r => r.GetRolesForUserAsync(actor.UserId, 77), Times.Once);
			permissions.Verify(p => p.GetPermissionByDepartmentTypeAsync(77, PermissionTypes.ViewAllWorkOrders), Times.Once);
			permissions.Verify(p => p.GetPermissionByDepartmentTypeAsync(77, PermissionTypes.ManageWorkOrders), Times.AtMostOnce);
			scope.All.Should().Be(admin); scope.GroupId.Should().Be(allowed ? 10 : null);
			(await service.CanManageAsync(actor, 10)).Should().Be(allowed);
			(await service.CanManageAsync(actor, 20)).Should().Be(admin);
			(await service.ChoicesAsync(actor)).Groups.Should().HaveCount(admin ? 2 : 1);
			member.IsDisabled = true;
			await FluentActions.Awaiting(() => service.ScopeAsync(actor)).Should().ThrowAsync<WorkOrderException>().Where(e => e.Code == "MembershipRequired");
			await FluentActions.Awaiting(() => service.ChoicesAsync(actor)).Should().ThrowAsync<WorkOrderException>().Where(e => e.Code == "MembershipRequired");
		}
	}
}
