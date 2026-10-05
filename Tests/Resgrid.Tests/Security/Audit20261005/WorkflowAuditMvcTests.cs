using System;
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
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Models;
using Resgrid.WebCore.Models;
using WorkflowsLocalization = Resgrid.Localization.Areas.User.Workflows.Workflows;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, Workflows on the web (items 1.2, 2.17, 3.23 and the credential defence in depth): the condition
	/// evaluator applies the designer's permission, a step may only name our credential and only attach a Records export
	/// its saver could run, a credential that delivers such an export needs the same rights to change, and run details
	/// show rendered output to designers only.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class WorkflowAuditMvcTests
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

		[SetUp]
		public void SetUp()
		{
			_allowed = new HashSet<PermissionTypes> { PermissionTypes.CreateWorkflow, PermissionTypes.ManageWorkflowCredentials, PermissionTypes.ViewWorkflowRuns };
			_claims = new List<Claim>();

			_workflows = new Mock<IWorkflowService>();
			_workflows.Setup(w => w.GetWorkflowByIdAsync("ours", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new Workflow { WorkflowId = "ours", DepartmentId = OurDepartment });
			_workflows.Setup(w => w.CanAddStepAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_workflows.Setup(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowStep s, CancellationToken _) => { s.WorkflowStepId ??= "new-step"; return s; });
			_workflows.Setup(w => w.GetCredentialByIdAsync("our-cred", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new WorkflowCredential { WorkflowCredentialId = "our-cred", DepartmentId = OurDepartment, Name = "Bearer", CredentialType = (int)WorkflowCredentialType.HttpBearer });
			_workflows.Setup(w => w.GetCredentialByIdAsync("their-cred", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new WorkflowCredential { WorkflowCredentialId = "their-cred", DepartmentId = OtherDepartment });
			_workflows.Setup(w => w.SaveCredentialAsync(It.IsAny<WorkflowCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowCredential c, string _, CancellationToken __) => c);
			_workflows.Setup(w => w.GetWorkflowRunByIdAsync("our-run", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new WorkflowRun { WorkflowRunId = "our-run", WorkflowId = "ours", DepartmentId = OurDepartment });
			_workflows.Setup(w => w.GetLogsForRunAsync("our-run", It.IsAny<CancellationToken>())).ReturnsAsync(() => new List<WorkflowRunLog>
			{
				new WorkflowRunLog { WorkflowRunLogId = "log-1", WorkflowRunId = "our-run", Status = (int)WorkflowRunStatus.Failed, RenderedOutput = "Jane Doe, 123 Main St", ActionResult = "Email sent to jane@example.test", ErrorMessage = "SMTP 550" }
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
		public void TearDown() => Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;

		private WorkflowsController Controller()
		{
			var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, Officer), new Claim(ClaimTypes.PrimaryGroupSid, OurDepartment.ToString()) };
			claims.AddRange(_claims);
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(OurDepartment, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = OurDepartment, ManagingUserId = Officer, Code = "OURS" });

			var localizer = new Mock<IStringLocalizer<WorkflowsLocalization>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, name));

			return new WorkflowsController(_workflows.Object, departments.Object, _permissions.Object, Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IAuditService>(), Mock.Of<IEventAggregator>(), Mock.Of<ISubscriptionsService>(),
				_contextBuilder.Object, Mock.Of<IRecordsCutoverService>(), _exports.Object, Mock.Of<IProtectedWorkflowService>(), localizer.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		private void GrantRecordsExport(bool restricted = false)
		{
			_claims.Add(new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Export));
			_claims.Add(new Claim(ResgridClaimTypes.Resources.RecordReport, ResgridClaimTypes.Actions.Update));
			if (restricted)
				_claims.Add(new Claim(ResgridClaimTypes.Resources.RecordRestricted, ResgridClaimTypes.Actions.View));
		}

		private static SaveStepRequest Step(string actionConfig = null, string credentialId = null) =>
			new SaveStepRequest { WorkflowId = "ours", OutputTemplate = "x", ActionConfig = actionConfig, WorkflowCredentialId = credentialId, IsEnabled = true };

		// ── 1.2 ValidateCondition ───────────────────────────────────────────────────────────────

		[Test]
		public async Task ValidateCondition_needs_the_designer_permission()
		{
			_allowed.Remove(PermissionTypes.CreateWorkflow);

			var result = await Controller().ValidateCondition(new ValidateConditionWebRequest { ConditionExpression = "{{ user.email }}", SamplePayloadJson = "{}" }, default);

			result.Should().BeOfType<ForbidResult>();
			_contextBuilder.Verify(b => b.BuildContextAsync(It.IsAny<int>(), It.IsAny<WorkflowTriggerEventType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ValidateCondition_still_validates_for_designers()
		{
			var result = await Controller().ValidateCondition(new ValidateConditionWebRequest { ConditionExpression = "{{ 1 == 1 }}" }, default);

			result.Should().BeOfType<OkObjectResult>();
		}

		// ── Defence in depth: credential on a step ──────────────────────────────────────────────

		[TestCase("their-cred")]
		[TestCase("missing-cred")]
		public async Task SaveStep_refuses_a_credential_outside_the_department(string credentialId)
		{
			var result = await Controller().SaveStep(Step(credentialId: credentialId), default);

			result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be("StepCredentialNotFound");
			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveStep_accepts_our_credential()
		{
			(await Controller().SaveStep(Step(credentialId: "our-cred"), default)).Should().BeOfType<OkObjectResult>();
		}

		// ── 2.17 Records export on a step ───────────────────────────────────────────────────────

		[Test]
		public async Task SaveStep_refuses_a_records_export_without_the_records_export_rights()
		{
			var result = await Controller().SaveStep(Step("{\"recordsExportTemplateId\":\"plain\"}"), default);

			result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be("ExportAttachmentRequiresRecordsRights");
			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveStep_refuses_a_restricted_export_without_the_restricted_right()
		{
			GrantRecordsExport();

			var result = await Controller().SaveStep(Step("{\"recordsExportTemplateId\":\"restricted\"}"), default);

			result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be("ExportAttachmentRequiresRestrictedRights");
			_workflows.Verify(w => w.SaveWorkflowStepAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("plain", false)]
		[TestCase("restricted", true)]
		public async Task SaveStep_attaches_an_export_for_a_member_with_the_rights(string templateId, bool restricted)
		{
			GrantRecordsExport(restricted);

			(await Controller().SaveStep(Step("{\"recordsExportTemplateId\":\"" + templateId + "\"}"), default)).Should().BeOfType<OkObjectResult>();
		}

		[Test]
		public async Task CredentialEdit_refuses_a_credential_that_delivers_an_export_without_the_records_export_rights()
		{
			_workflows.Setup(w => w.GetWorkflowsByDepartmentIdAsync(OurDepartment, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<Workflow> { new Workflow { WorkflowId = "ours", DepartmentId = OurDepartment } });
			_workflows.Setup(w => w.GetStepsByWorkflowIdAsync("ours", It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkflowStep>
			{
				new WorkflowStep { WorkflowStepId = "s1", WorkflowId = "ours", WorkflowCredentialId = "our-cred", ActionConfig = "{\"recordsExportTemplateId\":\"plain\"}" }
			});
			var model = new WorkflowCredentialViewModel { WorkflowCredentialId = "our-cred", Name = "Bearer", CredentialType = WorkflowCredentialType.HttpBearer, HttpBearerToken = "token" };

			var controller = Controller();
			var refused = await controller.CredentialEdit(model, default);

			refused.Should().BeOfType<ViewResult>();
			controller.ModelState.IsValid.Should().BeFalse();
			_workflows.Verify(w => w.SaveCredentialAsync(It.IsAny<WorkflowCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			GrantRecordsExport();
			var saved = await Controller().CredentialEdit(model, default);

			saved.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Credentials");
			_workflows.Verify(w => w.SaveCredentialAsync(It.Is<WorkflowCredential>(c => c.WorkflowCredentialId == "our-cred"), "OURS", It.IsAny<CancellationToken>()), Times.Once);
		}

		// ── 3.23 run detail ─────────────────────────────────────────────────────────────────────

		[Test]
		public async Task RunDetail_hides_rendered_output_from_members_who_cannot_design_workflows()
		{
			_allowed.Remove(PermissionTypes.CreateWorkflow);
			var controller = Controller();

			(await controller.RunDetail("our-run", default)).Should().BeOfType<ViewResult>();

			var log = ((List<WorkflowRunLog>)controller.ViewBag.Logs)[0];
			log.RenderedOutput.Should().BeNull();
			log.ActionResult.Should().BeNull();
			log.ErrorMessage.Should().Be("SMTP 550");
			log.Status.Should().Be((int)WorkflowRunStatus.Failed);
		}

		[Test]
		public async Task RunDetail_shows_rendered_output_to_designers()
		{
			var controller = Controller();

			(await controller.RunDetail("our-run", default)).Should().BeOfType<ViewResult>();

			var log = ((List<WorkflowRunLog>)controller.ViewBag.Logs)[0];
			log.RenderedOutput.Should().Be("Jane Doe, 123 Main St");
			log.ActionResult.Should().Be("Email sent to jane@example.test");
		}
	}
}
