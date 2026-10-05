using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Workflows;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, Workflows on v4 (items 1.1, 1.2, 2.8, 2.17, 3.23 and the credential defence in depth):
	/// Save loads the stored workflow instead of trusting the posted id; the designer reads, the condition evaluator and
	/// credential reads apply the same manage checks as their web pages; a step may only name our credential and only
	/// attach a Records export its saver could run; run logs show rendered output to designers only.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class WorkflowAuditApiTests
	{
		private const int OurDepartment = 9;
		private const int OtherDepartment = 10;
		private const string Officer = "officer";

		private Mock<IWorkflowService> _workflows;
		private Mock<IPermissionsService> _permissions;
		private Mock<IRecordsExportService> _exports;
		private Mock<IWorkflowTemplateContextBuilder> _contextBuilder;
		private HashSet<PermissionTypes> _allowed;
		private List<Claim> _claims;
		private List<string> _savedWorkflowIds;

		[SetUp]
		public void SetUp()
		{
			_allowed = new HashSet<PermissionTypes> { PermissionTypes.CreateWorkflow, PermissionTypes.ManageWorkflowCredentials, PermissionTypes.ViewWorkflowRuns };
			_claims = new List<Claim>();
			_savedWorkflowIds = new List<string>();

			_workflows = new Mock<IWorkflowService>();
			_workflows.Setup(w => w.GetWorkflowByIdAsync("ours", It.IsAny<CancellationToken>())).ReturnsAsync(() => new Workflow
			{
				WorkflowId = "ours", DepartmentId = OurDepartment, Name = "Stored", CreatedByUserId = "creator",
				CreatedOn = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), TriggerEventType = (int)WorkflowTriggerEventType.CallAdded
			});
			_workflows.Setup(w => w.GetWorkflowByIdAsync("theirs", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new Workflow { WorkflowId = "theirs", DepartmentId = OtherDepartment, Name = "Theirs" });
			_workflows.Setup(w => w.SaveWorkflowAsync(It.IsAny<Workflow>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Workflow w, CancellationToken _) => { _savedWorkflowIds.Add(w.WorkflowId); w.WorkflowId ??= "new-id"; return w; });
			_workflows.Setup(w => w.GetStepsByWorkflowIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkflowStep>());
			_workflows.Setup(w => w.CanAddWorkflowAsync(OurDepartment, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_workflows.Setup(w => w.CanAddStepAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_workflows.Setup(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowStep s, CancellationToken _) => s);
			_workflows.Setup(w => w.GetCredentialByIdAsync("our-cred", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new WorkflowCredential { WorkflowCredentialId = "our-cred", DepartmentId = OurDepartment, Name = "FTP", CreatedByUserId = "creator", CreatedOn = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
			_workflows.Setup(w => w.GetCredentialByIdAsync("their-cred", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new WorkflowCredential { WorkflowCredentialId = "their-cred", DepartmentId = OtherDepartment });
			_workflows.Setup(w => w.GetCredentialsByDepartmentIdAsync(OurDepartment, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<WorkflowCredential> { new WorkflowCredential { WorkflowCredentialId = "our-cred", DepartmentId = OurDepartment, Name = "FTP" } });
			_workflows.Setup(w => w.SaveCredentialAsync(It.IsAny<WorkflowCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowCredential c, string _, CancellationToken __) => c);
			_workflows.Setup(w => w.GetWorkflowRunByIdAsync("our-run", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new WorkflowRun { WorkflowRunId = "our-run", WorkflowId = "ours", DepartmentId = OurDepartment });
			_workflows.Setup(w => w.GetLogsForRunAsync("our-run", It.IsAny<CancellationToken>())).ReturnsAsync(() => new List<WorkflowRunLog>
			{
				new WorkflowRunLog { WorkflowRunLogId = "log-1", WorkflowRunId = "our-run", Status = (int)WorkflowRunStatus.Completed, RenderedOutput = "Jane Doe, 123 Main St", ActionResult = "Email sent to jane@example.test", ErrorMessage = null }
			});

			_exports = new Mock<IRecordsExportService>();
			_exports.Setup(e => e.GetTemplateAsync(OurDepartment, "plain")).ReturnsAsync(new RmsExportTemplate { RmsExportTemplateId = "plain", DepartmentId = OurDepartment });
			_exports.Setup(e => e.GetTemplateAsync(OurDepartment, "restricted")).ReturnsAsync(new RmsExportTemplate { RmsExportTemplateId = "restricted", DepartmentId = OurDepartment, IncludeRestricted = true });

			_permissions = new Mock<IPermissionsService>();
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(OurDepartment, It.IsAny<PermissionTypes>()))
				.ReturnsAsync((int _, PermissionTypes type) => new Permission { DepartmentId = OurDepartment, PermissionType = (int)type });
			_permissions.Setup(p => p.IsUserAllowed(It.IsAny<Permission>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>()))
				.Returns((Permission p, bool _, bool __, List<PersonnelRole> ___) => _allowed.Contains((PermissionTypes)p.PermissionType));

			_contextBuilder = new Mock<IWorkflowTemplateContextBuilder>();
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private DefaultHttpContext Context()
		{
			var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, Officer), new Claim(ClaimTypes.PrimaryGroupSid, OurDepartment.ToString()) };
			claims.AddRange(_claims);
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return http;
		}

		private IDepartmentsService Departments()
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(OurDepartment, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = OurDepartment, ManagingUserId = Officer, Code = "OURS" });
			return departments.Object;
		}

		private WorkflowsController Controller() => new WorkflowsController(_workflows.Object, Departments(), _permissions.Object,
			Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<ISubscriptionsService>(), _contextBuilder.Object,
			Mock.Of<IProtectedWorkflowService>(), _exports.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = Context() }
		};

		private WorkflowCredentialsController CredentialsController() => new WorkflowCredentialsController(_workflows.Object, Departments(), _permissions.Object,
			Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), _exports.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = Context() }
		};

		private void GrantRecordsExport(bool restricted = false)
		{
			_claims.Add(new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Export));
			_claims.Add(new Claim(ResgridClaimTypes.Resources.RecordReport, ResgridClaimTypes.Actions.Update));
			if (restricted)
				_claims.Add(new Claim(ResgridClaimTypes.Resources.RecordRestricted, ResgridClaimTypes.Actions.View));
		}

		private void GivenCredentialDeliversAnExport()
		{
			_workflows.Setup(w => w.GetWorkflowsByDepartmentIdAsync(OurDepartment, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<Workflow> { new Workflow { WorkflowId = "ours", DepartmentId = OurDepartment } });
			_workflows.Setup(w => w.GetStepsByWorkflowIdAsync("ours", It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkflowStep>
			{
				new WorkflowStep { WorkflowStepId = "s1", WorkflowId = "ours", WorkflowCredentialId = "our-cred", ActionConfig = "{\"recordsExportTemplateId\":\"plain\"}" }
			});
		}

		private static int? Status(IActionResult result) => (result as IStatusCodeActionResult)?.StatusCode;

		// ── 1.1 Save ────────────────────────────────────────────────────────────────────────────

		[TestCase("theirs")]
		[TestCase("missing")]
		public async Task Save_never_overwrites_a_workflow_outside_the_department(string workflowId)
		{
			var result = await Controller().Save(new SaveWorkflowInput { WorkflowId = workflowId, Name = "Hijacked" }, default);

			result.Result.Should().BeOfType<NotFoundResult>();
			_workflows.Verify(w => w.SaveWorkflowAsync(It.IsAny<Workflow>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Save_updates_the_stored_workflow_and_keeps_creator_creation_time_and_trigger()
		{
			var result = await Controller().Save(new SaveWorkflowInput
			{
				WorkflowId = "ours", Name = "Renamed", TriggerEventType = (int)WorkflowTriggerEventType.UnitAdded, MaxRetryCount = 2
			}, default);

			result.Result.Should().BeOfType<OkObjectResult>();
			_workflows.Verify(w => w.SaveWorkflowAsync(It.Is<Workflow>(x => x.WorkflowId == "ours" && x.DepartmentId == OurDepartment && x.Name == "Renamed" &&
				x.MaxRetryCount == 2 && x.CreatedByUserId == "creator" && x.CreatedOn == new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) &&
				x.TriggerEventType == (int)WorkflowTriggerEventType.CallAdded), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Save_still_creates_a_new_workflow_for_the_caller()
		{
			var result = await Controller().Save(new SaveWorkflowInput { Name = "New", TriggerEventType = (int)WorkflowTriggerEventType.UnitAdded }, default);

			result.Result.Should().BeOfType<OkObjectResult>();
			_workflows.Verify(w => w.SaveWorkflowAsync(It.Is<Workflow>(x => x.DepartmentId == OurDepartment && x.CreatedByUserId == Officer &&
				x.TriggerEventType == (int)WorkflowTriggerEventType.UnitAdded), It.IsAny<CancellationToken>()), Times.Once);
			_savedWorkflowIds.Should().ContainSingle().Which.Should().BeNullOrEmpty("a new workflow takes the insert path");
		}

		// ── 2.8 reads ───────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task Workflow_reads_need_the_designer_permission()
		{
			_allowed.Remove(PermissionTypes.CreateWorkflow);

			(await Controller().GetAll(default)).Result.Should().BeOfType<ForbidResult>();
			(await Controller().GetById("ours", default)).Result.Should().BeOfType<ForbidResult>();
			_workflows.Verify(w => w.GetWorkflowsByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_workflows.Verify(w => w.GetStepsByWorkflowIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Workflow_reads_still_work_for_designers()
		{
			_workflows.Setup(w => w.GetWorkflowsByDepartmentIdAsync(OurDepartment, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Workflow>());

			(await Controller().GetAll(default)).Result.Should().BeOfType<OkObjectResult>();
			(await Controller().GetById("ours", default)).Result.Should().BeOfType<OkObjectResult>();
		}

		[Test]
		public async Task Credential_reads_need_the_credentials_permission()
		{
			_allowed.Remove(PermissionTypes.ManageWorkflowCredentials);

			(await Controller().GetCredentials(default)).Result.Should().BeOfType<ForbidResult>();
			(await CredentialsController().GetAll(default)).Result.Should().BeOfType<ForbidResult>();
			(await CredentialsController().GetById("our-cred", default)).Result.Should().BeOfType<ForbidResult>();
			_workflows.Verify(w => w.GetCredentialsByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Credential_reads_still_work_for_credential_managers()
		{
			(await Controller().GetCredentials(default)).Result.Should().BeOfType<OkObjectResult>();
			(await CredentialsController().GetAll(default)).Result.Should().BeOfType<OkObjectResult>();
			(await CredentialsController().GetById("our-cred", default)).Result.Should().BeOfType<OkObjectResult>();
		}

		// ── 1.2 ValidateCondition ───────────────────────────────────────────────────────────────

		[Test]
		public async Task ValidateCondition_needs_the_designer_permission()
		{
			_allowed.Remove(PermissionTypes.CreateWorkflow);

			var result = await Controller().ValidateCondition(new ValidateConditionInput { ConditionExpression = "{{ user.email }}", SamplePayloadJson = "{}" }, default);

			result.Result.Should().BeOfType<ForbidResult>();
			_contextBuilder.Verify(b => b.BuildContextAsync(It.IsAny<int>(), It.IsAny<WorkflowTriggerEventType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ── Defence in depth: credential on a step ──────────────────────────────────────────────

		[TestCase("their-cred")]
		[TestCase("missing-cred")]
		public async Task SaveStep_refuses_a_credential_outside_the_department(string credentialId)
		{
			var result = await Controller().SaveStep(new SaveWorkflowStepInput { WorkflowId = "ours", OutputTemplate = "x", WorkflowCredentialId = credentialId }, default);

			result.Result.Should().BeOfType<NotFoundResult>();
			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveStep_accepts_our_credential()
		{
			var result = await Controller().SaveStep(new SaveWorkflowStepInput { WorkflowId = "ours", OutputTemplate = "x", WorkflowCredentialId = "our-cred" }, default);

			result.Result.Should().BeOfType<OkObjectResult>();
			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.Is<WorkflowStep>(s => s.WorkflowCredentialId == "our-cred" && s.CreatedByUserId == Officer), It.IsAny<CancellationToken>()), Times.Once);
		}

		// ── 2.17 Records export on a step ───────────────────────────────────────────────────────

		[Test]
		public async Task SaveStep_refuses_a_records_export_without_the_records_export_rights()
		{
			var result = await Controller().SaveStep(new SaveWorkflowStepInput { WorkflowId = "ours", OutputTemplate = "x", ActionConfig = "{\"recordsExportTemplateId\":\"plain\"}" }, default);

			Status(result.Result).Should().Be(StatusCodes.Status403Forbidden);
			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveStep_refuses_a_restricted_export_without_the_restricted_right()
		{
			GrantRecordsExport();

			var result = await Controller().SaveStep(new SaveWorkflowStepInput { WorkflowId = "ours", OutputTemplate = "x", ActionConfig = "{\"recordsExportTemplateId\":\"restricted\"}" }, default);

			Status(result.Result).Should().Be(StatusCodes.Status403Forbidden);
			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveStep_refuses_an_export_template_that_is_not_ours()
		{
			GrantRecordsExport(restricted: true);

			var result = await Controller().SaveStep(new SaveWorkflowStepInput { WorkflowId = "ours", OutputTemplate = "x", ActionConfig = "{\"recordsExportTemplateId\":\"elsewhere\"}" }, default);

			result.Result.Should().BeOfType<UnprocessableEntityObjectResult>();
			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("plain", false)]
		[TestCase("restricted", true)]
		public async Task SaveStep_attaches_an_export_for_a_member_with_the_rights(string templateId, bool restricted)
		{
			GrantRecordsExport(restricted);

			var result = await Controller().SaveStep(new SaveWorkflowStepInput { WorkflowId = "ours", OutputTemplate = "x", ActionConfig = "{\"recordsExportTemplateId\":\"" + templateId + "\"}" }, default);

			result.Result.Should().BeOfType<OkObjectResult>();
		}

		[Test]
		public async Task Changing_a_credential_that_delivers_an_export_needs_the_records_export_rights()
		{
			GivenCredentialDeliversAnExport();

			var save = await Controller().SaveCredential(new SaveCredentialInput { WorkflowCredentialId = "our-cred", Name = "FTP", PlaintextCredentialJson = "{}" }, default);
			var update = await CredentialsController().Update("our-cred", new WorkflowCredentialInput { Name = "FTP", PlaintextData = "{}" }, default);

			Status(save.Result).Should().Be(StatusCodes.Status403Forbidden);
			Status(update.Result).Should().Be(StatusCodes.Status403Forbidden);
			_workflows.Verify(w => w.SaveCredentialAsync(It.IsAny<WorkflowCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveCredential_update_changes_the_stored_row_and_keeps_its_creator()
		{
			GivenCredentialDeliversAnExport();
			GrantRecordsExport();

			var result = await Controller().SaveCredential(new SaveCredentialInput { WorkflowCredentialId = "our-cred", Name = "Renamed", CredentialType = 3, PlaintextCredentialJson = "{}" }, default);

			result.Result.Should().BeOfType<OkObjectResult>();
			_workflows.Verify(w => w.SaveCredentialAsync(It.Is<WorkflowCredential>(c => c.WorkflowCredentialId == "our-cred" && c.Name == "Renamed" &&
				c.CreatedByUserId == "creator" && c.CreatedOn == new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) && c.UpdatedByUserId == Officer), "OURS", It.IsAny<CancellationToken>()), Times.Once);
		}

		// ── 3.23 run logs ───────────────────────────────────────────────────────────────────────

		[Test]
		public async Task Run_logs_hide_rendered_output_from_members_who_cannot_design_workflows()
		{
			_allowed.Remove(PermissionTypes.CreateWorkflow);

			var result = await Controller().GetRunLogs("our-run", default);

			var log = ((GetWorkflowRunLogsResult)((OkObjectResult)result.Result).Value).Data.Single();
			log.RenderedOutput.Should().BeNull();
			log.ActionResult.Should().BeNull();
			log.Status.Should().Be((int)WorkflowRunStatus.Completed);
		}

		[Test]
		public async Task Run_logs_show_rendered_output_to_designers()
		{
			var result = await Controller().GetRunLogs("our-run", default);

			var log = ((GetWorkflowRunLogsResult)((OkObjectResult)result.Result).Value).Data.Single();
			log.RenderedOutput.Should().Be("Jane Doe, 123 Main St");
			log.ActionResult.Should().Be("Email sent to jane@example.test");
		}
	}
}
