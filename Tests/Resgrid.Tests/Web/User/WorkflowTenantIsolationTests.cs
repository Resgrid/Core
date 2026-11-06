using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Models;
using WorkflowsLocalization = Resgrid.Localization.Areas.User.Workflows.Workflows;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The MVC workflow monitor actions took a workflow, run or step id from the request and handed it straight
	/// to IWorkflowService, which loads by id alone: any user with the run/workflow permissions in their own
	/// department could read another department's run history and health, cancel its runs, and delete or
	/// rewrite its steps. Every id-addressed action now proves the row (or, for a step, the workflow it belongs
	/// to) is in the caller's department before the service is asked to read or write it.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class WorkflowTenantIsolationTests
	{
		private const int OurDepartment = 9;
		private const int OtherDepartment = 10;
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
				.ReturnsAsync(new Workflow { WorkflowId = "theirs", DepartmentId = OtherDepartment });
			_workflows.Setup(w => w.GetWorkflowRunByIdAsync("our-run", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new WorkflowRun { WorkflowRunId = "our-run", WorkflowId = "ours", DepartmentId = OurDepartment });
			_workflows.Setup(w => w.GetWorkflowRunByIdAsync("their-run", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new WorkflowRun { WorkflowRunId = "their-run", WorkflowId = "theirs", DepartmentId = OtherDepartment });
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

			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(r => r.GetRolesForUserAsync(Officer, OurDepartment)).ReturnsAsync(new List<PersonnelRole>());

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Officer),
					new Claim(ClaimTypes.PrimaryGroupSid, OurDepartment.ToString())
				}, "test"))
			};
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			_controller = new WorkflowsController(_workflows.Object, departments.Object, permissions.Object,
				Mock.Of<IDepartmentGroupsService>(), roles.Object, Mock.Of<IAuditService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<ISubscriptionsService>(), Mock.Of<IWorkflowTemplateContextBuilder>(), Mock.Of<IRecordsCutoverService>(),
				Mock.Of<IRecordsExportService>(), Mock.Of<IProtectedWorkflowService>(), Mock.Of<IStringLocalizer<WorkflowsLocalization>>())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[TearDown]
		public void Cleanup() => Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;

		[TestCase("theirs")]
		[TestCase("missing")]
		public async Task Runs_for_a_workflow_outside_the_department_is_not_found_and_never_queried(string workflowId)
		{
			(await _controller.Runs(workflowId, 1, default)).Should().BeOfType<NotFoundResult>();

			_workflows.Verify(w => w.GetRunsByWorkflowIdAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Runs_for_our_workflow_still_lists_them()
		{
			(await _controller.Runs("ours", 2, default)).Should().BeOfType<ViewResult>();

			_workflows.Verify(w => w.GetRunsByWorkflowIdAsync("ours", 2, 50, It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase("theirs")]
		[TestCase("missing")]
		public async Task Health_for_a_workflow_outside_the_department_is_not_found_and_never_computed(string workflowId)
		{
			(await _controller.Health(workflowId, default)).Should().BeOfType<NotFoundResult>();

			_workflows.Verify(w => w.GetWorkflowHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Health_for_our_workflow_still_renders()
		{
			(await _controller.Health("ours", default)).Should().BeOfType<ViewResult>();

			_workflows.Verify(w => w.GetWorkflowHealthAsync("ours", It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase("their-run")]
		[TestCase("missing-run")]
		public async Task CancelRun_for_a_run_outside_the_department_is_not_found_and_never_cancels(string runId)
		{
			(await _controller.CancelRun(runId, default)).Should().BeOfType<NotFoundResult>();

			_workflows.Verify(w => w.CancelWorkflowRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task CancelRun_for_our_run_cancels_it()
		{
			(await _controller.CancelRun("our-run", default)).Should().BeOfType<RedirectToActionResult>()
				.Which.ActionName.Should().Be("Pending");

			_workflows.Verify(w => w.CancelWorkflowRunAsync("our-run", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RunDetail_for_a_run_outside_the_department_is_not_found()
		{
			(await _controller.RunDetail("their-run", default)).Should().BeOfType<NotFoundResult>();

			_workflows.Verify(w => w.GetLogsForRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("their-step")]
		[TestCase("missing-step")]
		public async Task DeleteStep_outside_the_department_is_not_found_and_never_deletes(string stepId)
		{
			(await _controller.DeleteStep(stepId, default)).Should().BeOfType<NotFoundObjectResult>();

			_workflows.Verify(w => w.DeleteWorkflowStepAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DeleteStep_in_our_workflow_deletes_it()
		{
			(await _controller.DeleteStep("our-step", default)).Should().BeOfType<OkObjectResult>();

			_workflows.Verify(w => w.DeleteWorkflowStepAsync("our-step", It.IsAny<CancellationToken>()), Times.Once);
		}

		/// <summary>
		/// The target workflow was already checked; the step id was not. Naming our workflow with another
		/// department's step id rewrote that step and moved it into our workflow.
		/// </summary>
		[TestCase("their-step")]
		[TestCase("missing-step")]
		public async Task SaveStep_cannot_update_a_step_outside_the_department(string stepId)
		{
			var request = new SaveStepRequest { WorkflowId = "ours", WorkflowStepId = stepId, OutputTemplate = "hello" };

			(await _controller.SaveStep(request, default)).Should().BeOfType<NotFoundObjectResult>();

			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveStep_updates_a_step_in_our_workflow()
		{
			var request = new SaveStepRequest { WorkflowId = "ours", WorkflowStepId = "our-step", OutputTemplate = "hello" };

			(await _controller.SaveStep(request, default)).Should().BeOfType<OkObjectResult>();

			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.Is<WorkflowStep>(s => s.WorkflowStepId == "our-step" && s.WorkflowId == "ours"), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Edit_settings_saves_without_posting_server_owned_creator_and_keeps_identity_and_steps()
		{
			// Arrange: MVC adds this error because the settings form omits the entity's required creator.
			var stored = new Workflow
			{
				WorkflowId = "ours", DepartmentId = OurDepartment, CreatedByUserId = "original-creator",
				TriggerEventType = (int)WorkflowTriggerEventType.CallAdded,
				Steps = new List<WorkflowStep> { new() { WorkflowStepId = "our-step", WorkflowId = "ours" } }
			};
			_workflows.Setup(w => w.GetWorkflowByIdAsync("ours", It.IsAny<CancellationToken>())).ReturnsAsync(stored);
			_controller.ModelState.AddModelError(nameof(Workflow.CreatedByUserId), "The CreatedByUserId field is required.");
			var posted = new Workflow
			{
				WorkflowId = "ours", Name = "Reviewed training workflow", IsEnabled = true,
				DepartmentId = OtherDepartment, TriggerEventType = (int)WorkflowTriggerEventType.CallClosed
			};

			// Act.
			var result = await _controller.Edit(posted, default);

			// Assert: settings change, while the stored ownership, event and step collection survive.
			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			stored.Name.Should().Be("Reviewed training workflow");
			stored.IsEnabled.Should().BeTrue();
			stored.DepartmentId.Should().Be(OurDepartment);
			stored.CreatedByUserId.Should().Be("original-creator");
			stored.TriggerEventType.Should().Be((int)WorkflowTriggerEventType.CallAdded);
			stored.Steps.Should().ContainSingle().Which.WorkflowStepId.Should().Be("our-step");
			_workflows.Verify(w => w.SaveWorkflowAsync(stored, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Edit_invalid_settings_preserves_errors_and_reloads_steps_credentials_and_locked_event()
		{
			// Arrange.
			_workflows.Setup(w => w.GetStepsByWorkflowIdAsync("ours", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<WorkflowStep> { new() { WorkflowStepId = "our-step", WorkflowId = "ours" } });
			_workflows.Setup(w => w.GetCredentialsByDepartmentIdAsync(OurDepartment, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<WorkflowCredential> { new() { WorkflowCredentialId = "credential", Name = "Training mail" } });
			_controller.ModelState.AddModelError(nameof(Workflow.CreatedByUserId), "Required");
			_controller.ModelState.AddModelError(nameof(Workflow.Name), "Name is required.");
			var posted = new Workflow { WorkflowId = "ours", Name = "", TriggerEventType = (int)WorkflowTriggerEventType.CallClosed };

			// Act.
			var result = await _controller.Edit(posted, default);

			// Assert: a correctable form is returned with its existing editor context and no write.
			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeSameAs(posted);
			_controller.ModelState[nameof(Workflow.Name)].Errors.Should().ContainSingle();
			posted.Steps.Should().ContainSingle().Which.WorkflowStepId.Should().Be("our-step");
			posted.TriggerEventType.Should().Be((int)WorkflowTriggerEventType.CallAdded);
			((string)_controller.ViewBag.CredentialsJson).Should().Contain("Training mail");
			_workflows.Verify(w => w.SaveWorkflowAsync(It.IsAny<Workflow>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("theirs")]
		[TestCase("missing")]
		[TestCase("")]
		public async Task Edit_settings_outside_the_department_is_not_found_and_never_saved(string workflowId)
		{
			// Arrange.
			var posted = new Workflow { WorkflowId = workflowId, Name = "Attempted update", IsEnabled = true };

			// Act.
			var result = await _controller.Edit(posted, default);

			// Assert.
			result.Should().BeOfType<NotFoundResult>();
			_workflows.Verify(w => w.SaveWorkflowAsync(It.IsAny<Workflow>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
