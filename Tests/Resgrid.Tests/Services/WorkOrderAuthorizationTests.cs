using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class WorkOrderAuthorizationTests
	{
		[TestCase(false, 403, "ProtectedDataRequired")]
		[TestCase(true, 403, "ProtectedDataRequired")]
		[TestCase(false, 403, "MembershipRequired")]
		[TestCase(true, 403, "MembershipRequired")]
		public async Task Inventory_asset_access_failures_use_the_work_order_error_contract(bool validateTarget, int statusCode, string code)
		{
			var actor = new ChecklistActor { DepartmentId = 77, UserId = "manager" };
			var assetId = Guid.NewGuid().ToString("D");
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(actor.UserId, actor.DepartmentId, true))
				.ReturnsAsync(new DepartmentMember { DepartmentId = actor.DepartmentId, UserId = actor.UserId });
			var assignments = new Mock<IChecklistAssignmentService>();
			assignments.Setup(a => a.ChoicesAsync(actor)).ReturnsAsync(new List<ChecklistAssignmentChoice>());
			var assets = new Mock<IChecklistAssetSource>();
			assets.Setup(a => a.IsAvailableAsync(actor.DepartmentId)).ReturnsAsync(true);
			assets.Setup(a => a.GetAsync(actor, assetId)).ThrowsAsync(new ChecklistException(statusCode, code));
			assets.Setup(a => a.ListAsync(actor)).ThrowsAsync(new ChecklistException(statusCode, code));
			var service = new WorkOrderAuthorizationService(departments.Object, Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IPermissionsService>(), Mock.Of<IUnitsService>(),
				Mock.Of<IAuthorizationService>(), assignments.Object, assets.Object);

			Func<Task> action = validateTarget
				? () => service.ValidateTargetAsync(actor, new WorkOrderInput { InventoryAssetId = assetId })
				: async () => { await service.ChoicesAsync(actor); };
			var error = (await action.Should().ThrowAsync<WorkOrderException>()).Which;
			error.StatusCode.Should().Be(statusCode);
			error.Code.Should().Be(code);
		}
	}
}
