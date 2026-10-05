using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Workflows;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 SaveStep checked that the target workflow is the caller's but not that the step being updated is:
	/// naming our workflow with another department's step id rewrote that step and moved it into our workflow.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class WorkflowStepTenantIsolationApiTests
	{
		private const int OurDepartment = 9;
		private const string Officer = "officer";

		private Mock<IWorkflowService> _workflows;
		private WorkflowsController _controller;

		[SetUp]
		public void Setup()
		{
			_workflows = new Mock<IWorkflowService>();
			_workflows.Setup(w => w.GetWorkflowByIdAsync("ours", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Workflow { WorkflowId = "ours", DepartmentId = OurDepartment });
			_workflows.Setup(w => w.GetWorkflowByIdAsync("theirs", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Workflow { WorkflowId = "theirs", DepartmentId = 10 });
			_workflows.Setup(w => w.GetStepByIdAsync("our-step", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new WorkflowStep { WorkflowStepId = "our-step", WorkflowId = "ours" });
			_workflows.Setup(w => w.GetStepByIdAsync("their-step", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new WorkflowStep { WorkflowStepId = "their-step", WorkflowId = "theirs" });
			_workflows.Setup(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowStep s, CancellationToken _) => s);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(OurDepartment, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = OurDepartment, ManagingUserId = Officer });

			var permissions = new Mock<IPermissionsService>();
			permissions.Setup(p => p.IsUserAllowed(It.IsAny<Permission>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>()))
				.Returns(true);

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Officer),
					new Claim(ClaimTypes.PrimaryGroupSid, OurDepartment.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			_controller = new WorkflowsController(_workflows.Object, departments.Object, permissions.Object,
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<ISubscriptionsService>(),
				Mock.Of<IWorkflowTemplateContextBuilder>(), Mock.Of<IProtectedWorkflowService>(), Mock.Of<IRecordsExportService>())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[TearDown]
		public void Cleanup() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		[TestCase("their-step")]
		[TestCase("missing-step")]
		public async Task SaveStep_cannot_update_a_step_outside_the_department(string stepId)
		{
			var input = new SaveWorkflowStepInput { WorkflowId = "ours", WorkflowStepId = stepId, OutputTemplate = "hello" };

			(await _controller.SaveStep(input, default)).Result.Should().BeOfType<NotFoundResult>();

			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveStep_updates_a_step_in_our_workflow()
		{
			var input = new SaveWorkflowStepInput { WorkflowId = "ours", WorkflowStepId = "our-step", OutputTemplate = "hello" };

			(await _controller.SaveStep(input, default)).Result.Should().BeOfType<OkObjectResult>();

			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.Is<WorkflowStep>(s => s.WorkflowStepId == "our-step"), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
