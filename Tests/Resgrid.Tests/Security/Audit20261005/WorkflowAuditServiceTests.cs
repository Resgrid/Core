using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Scriban.Runtime;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, Workflows (items 1.1, 1.2, 2.17): the service no longer updates a workflow or moves a step across
	/// departments by a trusted id, the template context never loads another department's users, units, groups or roles,
	/// and a step's Records export is the fixed template the step was saved (and authorized) with.
	/// </summary>
	[TestFixture]
	public class WorkflowAuditServiceTests
	{
		private const int OurDepartment = 9;
		private const int OtherDepartment = 10;

		private Mock<IWorkflowRepository> _workflowRepository;
		private Mock<IWorkflowStepRepository> _stepRepository;
		private Mock<IWorkflowCredentialRepository> _credentialRepository;
		private Mock<IWorkflowRunRepository> _runRepository;
		private Mock<IWorkflowRunLogRepository> _runLogRepository;
		private Mock<IEncryptionService> _encryption;
		private Mock<IWorkflowActionExecutorFactory> _executors;
		private Mock<IWorkflowActionExecutor> _executor;
		private Mock<IWorkflowTemplateContextBuilder> _contextBuilder;
		private Mock<IRecordsExportService> _exports;
		private List<WorkflowRunLog> _logs;
		private WorkflowActionContext _executed;
		private WorkflowService _service;

		[SetUp]
		public void SetUp()
		{
			_workflowRepository = new Mock<IWorkflowRepository>();
			_workflowRepository.Setup(r => r.GetByIdAsync("ours")).ReturnsAsync(() => new Workflow
			{
				WorkflowId = "ours", DepartmentId = OurDepartment, Name = "Stored", CreatedByUserId = "creator",
				CreatedOn = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), TriggerEventType = (int)WorkflowTriggerEventType.RecordFinalized, MaxRetryCount = 1
			});
			_workflowRepository.Setup(r => r.GetByIdAsync("ours-2")).ReturnsAsync(() => new Workflow { WorkflowId = "ours-2", DepartmentId = OurDepartment });
			_workflowRepository.Setup(r => r.GetByIdAsync("theirs")).ReturnsAsync(() => new Workflow { WorkflowId = "theirs", DepartmentId = OtherDepartment });

			_stepRepository = new Mock<IWorkflowStepRepository>();
			_credentialRepository = new Mock<IWorkflowCredentialRepository>();
			_runRepository = new Mock<IWorkflowRunRepository>();
			_runRepository.Setup(r => r.InsertAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowRun run, CancellationToken _, bool __) => run);

			_logs = new List<WorkflowRunLog>();
			_runLogRepository = new Mock<IWorkflowRunLogRepository>();
			_runLogRepository.Setup(r => r.InsertAsync(It.IsAny<WorkflowRunLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowRunLog log, CancellationToken _, bool __) => { _logs.Add(log); return log; });

			_encryption = new Mock<IEncryptionService>();

			_executed = null;
			_executor = new Mock<IWorkflowActionExecutor>();
			_executor.Setup(e => e.ExecuteAsync(It.IsAny<WorkflowActionContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowActionContext context, CancellationToken _) => { _executed = context; return WorkflowActionResult.Succeeded("sent"); });
			_executors = new Mock<IWorkflowActionExecutorFactory>();
			_executors.Setup(f => f.GetExecutor(It.IsAny<WorkflowActionType>())).Returns(_executor.Object);

			_contextBuilder = new Mock<IWorkflowTemplateContextBuilder>();
			_contextBuilder.Setup(b => b.BuildContextAsync(It.IsAny<int>(), It.IsAny<WorkflowTriggerEventType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new ScriptObject { ["pick"] = "tmpl-b" });

			_exports = new Mock<IRecordsExportService>();
			_exports.Setup(e => e.ResolveForWorkflowAsync(OurDepartment, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RmsRecordKind?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RmsExportRun { RmsExportRunId = "run-x", FileName = "export.csv", ContentType = "text/csv", Data = new byte[] { 1 } });

			_service = new WorkflowService(_workflowRepository.Object, _stepRepository.Object, _credentialRepository.Object, _runRepository.Object,
				_runLogRepository.Object, Mock.Of<IWorkflowDailyUsageRepository>(), _encryption.Object, _executors.Object, _contextBuilder.Object,
				Mock.Of<ISubscriptionsService>(), _exports.Object);
		}

		private void GivenStep(string actionConfig, string credentialId = null)
		{
			_stepRepository.Setup(r => r.GetAllByWorkflowIdAsync("ours")).ReturnsAsync(new List<WorkflowStep>
			{
				new WorkflowStep
				{
					WorkflowStepId = "step-1", WorkflowId = "ours", ActionType = (int)WorkflowActionType.UploadFileFtp, StepOrder = 1, IsEnabled = true,
					OutputTemplate = "body", ActionConfig = actionConfig, WorkflowCredentialId = credentialId
				}
			});
		}

		// ── 1.1 SaveWorkflowAsync ───────────────────────────────────────────────────────────────

		[Test]
		public async Task Updating_another_departments_workflow_throws_and_never_writes()
		{
			Func<Task> act = () => _service.SaveWorkflowAsync(new Workflow { WorkflowId = "theirs", DepartmentId = OurDepartment, Name = "Mine now" });

			await act.Should().ThrowAsync<InvalidOperationException>();
			_workflowRepository.Verify(r => r.UpdateAsync(It.IsAny<Workflow>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Updating_a_workflow_that_does_not_exist_throws_and_never_writes()
		{
			Func<Task> act = () => _service.SaveWorkflowAsync(new Workflow { WorkflowId = "missing", DepartmentId = OurDepartment });

			await act.Should().ThrowAsync<KeyNotFoundException>();
			_workflowRepository.Verify(r => r.UpdateAsync(It.IsAny<Workflow>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Updating_our_workflow_keeps_its_creator_and_creation_time()
		{
			await _service.SaveWorkflowAsync(new Workflow { WorkflowId = "ours", DepartmentId = OurDepartment, Name = "Renamed", CreatedByUserId = "someone-else" });

			_workflowRepository.Verify(r => r.UpdateAsync(It.Is<Workflow>(w => w.Name == "Renamed" && w.CreatedByUserId == "creator" &&
				w.CreatedOn == new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task A_step_cannot_be_moved_into_another_departments_workflow()
		{
			_stepRepository.Setup(r => r.GetByIdAsync("step-1")).ReturnsAsync(new WorkflowStep { WorkflowStepId = "step-1", WorkflowId = "ours" });

			Func<Task> act = () => _service.SaveWorkflowStepAsync(new WorkflowStep { WorkflowStepId = "step-1", WorkflowId = "theirs" });

			await act.Should().ThrowAsync<InvalidOperationException>();
			_stepRepository.Verify(r => r.UpdateAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task A_step_can_still_move_between_workflows_of_one_department()
		{
			_stepRepository.Setup(r => r.GetByIdAsync("step-1")).ReturnsAsync(new WorkflowStep { WorkflowStepId = "step-1", WorkflowId = "ours" });

			await _service.SaveWorkflowStepAsync(new WorkflowStep { WorkflowStepId = "step-1", WorkflowId = "ours-2" });

			_stepRepository.Verify(r => r.UpdateAsync(It.Is<WorkflowStep>(s => s.WorkflowId == "ours-2"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		// ── 2.17 / defence in depth: run time ───────────────────────────────────────────────────

		[Test]
		public async Task An_export_template_chosen_by_a_template_expression_is_refused_at_run_time()
		{
			GivenStep("{\"recordsExportTemplateId\":\"{{ pick }}\"}");

			await _service.ExecuteWorkflowAsync("ours", "{}", OurDepartment, "CODE");

			_exports.Verify(e => e.ResolveForWorkflowAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RmsRecordKind?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_executor.Verify(e => e.ExecuteAsync(It.IsAny<WorkflowActionContext>(), It.IsAny<CancellationToken>()), Times.Never);
			_logs.Should().ContainSingle(l => l.Status == (int)WorkflowRunStatus.Failed && l.ErrorMessage.Contains("fixed export template"));
		}

		[Test]
		public async Task A_fixed_export_template_still_renders_and_is_attached()
		{
			GivenStep("{\"recordsExportTemplateId\":\"tmpl-a\"}");

			await _service.ExecuteWorkflowAsync("ours", "{}", OurDepartment, "CODE");

			_exports.Verify(e => e.ResolveForWorkflowAsync(OurDepartment, "tmpl-a", It.IsAny<string>(), It.IsAny<RmsRecordKind?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_executed.Should().NotBeNull();
			_executed.Attachment.ExportRunId.Should().Be("run-x");
		}

		[Test]
		public async Task Another_departments_credential_is_never_decrypted_for_a_step()
		{
			GivenStep(null, "their-cred");
			_credentialRepository.Setup(r => r.GetByIdAsync("their-cred"))
				.ReturnsAsync(new WorkflowCredential { WorkflowCredentialId = "their-cred", DepartmentId = OtherDepartment, EncryptedData = "cipher" });

			await _service.ExecuteWorkflowAsync("ours", "{}", OurDepartment, "CODE");

			_encryption.Verify(e => e.DecryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
			_executed.DecryptedCredentialJson.Should().BeNull();
		}

		[Test]
		public async Task Our_credential_is_still_decrypted_for_a_step()
		{
			GivenStep(null, "our-cred");
			_credentialRepository.Setup(r => r.GetByIdAsync("our-cred"))
				.ReturnsAsync(new WorkflowCredential { WorkflowCredentialId = "our-cred", DepartmentId = OurDepartment, EncryptedData = "cipher" });
			_encryption.Setup(e => e.DecryptForDepartment("cipher", OurDepartment, "CODE")).Returns("{\"host\":\"ftp.example\"}");

			await _service.ExecuteWorkflowAsync("ours", "{}", OurDepartment, "CODE");

			_executed.DecryptedCredentialJson.Should().Be("{\"host\":\"ftp.example\"}");
		}

		// ── 2.17: the attachment rule ───────────────────────────────────────────────────────────

		[Test]
		public void The_attachment_rule_follows_the_records_export_pages()
		{
			var plain = new RmsExportTemplate { RmsExportTemplateId = "t", DepartmentId = OurDepartment };
			var restricted = new RmsExportTemplate { RmsExportTemplateId = "r", DepartmentId = OurDepartment, IncludeRestricted = true };

			WorkflowExportAttachmentRule.Check(plain, OurDepartment, true, true, false).Should().BeNull();
			WorkflowExportAttachmentRule.Check(plain, OurDepartment, false, true, true).Should().Be(WorkflowExportAttachmentRule.RecordsRightsRequired);
			WorkflowExportAttachmentRule.Check(plain, OurDepartment, true, false, true).Should().Be(WorkflowExportAttachmentRule.RecordsRightsRequired);
			WorkflowExportAttachmentRule.Check(restricted, OurDepartment, true, true, false).Should().Be(WorkflowExportAttachmentRule.RestrictedRightsRequired);
			WorkflowExportAttachmentRule.Check(restricted, OurDepartment, true, true, true).Should().BeNull();
			WorkflowExportAttachmentRule.Check(null, OurDepartment, true, true, true).Should().Be(WorkflowExportAttachmentRule.TemplateNotFound);
			WorkflowExportAttachmentRule.Check(new RmsExportTemplate { DepartmentId = OtherDepartment }, OurDepartment, true, true, true).Should().Be(WorkflowExportAttachmentRule.TemplateNotFound);
			WorkflowExportAttachmentRule.Check(new RmsExportTemplate { DepartmentId = OurDepartment, DeletedOn = DateTime.UtcNow }, OurDepartment, true, true, true).Should().Be(WorkflowExportAttachmentRule.TemplateNotFound);
		}

		[Test]
		public async Task A_step_without_an_export_needs_no_records_rights()
		{
			(await WorkflowExportAttachmentRule.CheckStepAsync(_exports.Object, OurDepartment, "{\"to\":\"a@b.example\"}", false, false, false)).Should().BeNull();
			(await WorkflowExportAttachmentRule.CheckStepAsync(_exports.Object, OurDepartment, null, false, false, false)).Should().BeNull();
		}

		[Test]
		public async Task Changing_a_credential_that_delivers_an_export_needs_the_records_rights()
		{
			var workflows = new Mock<IWorkflowService>();
			workflows.Setup(w => w.GetWorkflowsByDepartmentIdAsync(OurDepartment, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Workflow> { new Workflow { WorkflowId = "ours", DepartmentId = OurDepartment } });
			workflows.Setup(w => w.GetStepsByWorkflowIdAsync("ours", It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkflowStep>
			{
				new WorkflowStep { WorkflowStepId = "s1", WorkflowId = "ours", WorkflowCredentialId = "cred-1", ActionConfig = "{\"recordsExportTemplateId\":\"tmpl-a\"}" },
				new WorkflowStep { WorkflowStepId = "s2", WorkflowId = "ours", WorkflowCredentialId = "cred-2", ActionConfig = "{\"path\":\"/out\"}" }
			});
			_exports.Setup(e => e.GetTemplateAsync(OurDepartment, "tmpl-a")).ReturnsAsync(new RmsExportTemplate { RmsExportTemplateId = "tmpl-a", DepartmentId = OurDepartment, IncludeRestricted = true });

			(await WorkflowExportAttachmentRule.CheckCredentialAsync(workflows.Object, _exports.Object, OurDepartment, "cred-1", false, false, false))
				.Should().Be(WorkflowExportAttachmentRule.RecordsRightsRequired);
			(await WorkflowExportAttachmentRule.CheckCredentialAsync(workflows.Object, _exports.Object, OurDepartment, "cred-1", true, true, false))
				.Should().Be(WorkflowExportAttachmentRule.RestrictedRightsRequired);
			(await WorkflowExportAttachmentRule.CheckCredentialAsync(workflows.Object, _exports.Object, OurDepartment, "cred-1", true, true, true)).Should().BeNull();
			(await WorkflowExportAttachmentRule.CheckCredentialAsync(workflows.Object, _exports.Object, OurDepartment, "cred-2", false, false, false)).Should().BeNull();
		}
	}

	/// <summary>
	/// Audit 2026-10-05 item 1.2: ValidateCondition renders a caller-supplied payload, so every id the payload names is
	/// checked against the department being rendered before anything is loaded for it.
	/// </summary>
	[TestFixture]
	public class WorkflowAuditContextBuilderTests
	{
		private const int OurDepartment = 9;
		private const int OtherDepartment = 10;

		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPersonnelRolesService> _roles;
		private WorkflowTemplateContextBuilder _builder;

		[SetUp]
		public void SetUp()
		{
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(OurDepartment, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = OurDepartment, Name = "Ours" });
			_departments.Setup(d => d.GetDepartmentMemberAsync("member", OurDepartment, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { UserId = "member", DepartmentId = OurDepartment });
			_departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(OurDepartment))
				.ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { UserId = "member", DepartmentId = OurDepartment } });

			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((string id, bool _) => new UserProfile { UserId = id, FirstName = "First-" + id, MembershipEmail = id + "@example.test" });
			_profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>()))
				.ReturnsAsync((List<string> ids) => ids.Select(id => new UserProfile { UserId = id, FirstName = "First-" + id, MembershipEmail = id + "@example.test" }).ToList());

			_units = new Mock<IUnitsService>();
			_units.Setup(u => u.GetUnitByIdAsync(1)).ReturnsAsync(new Unit { UnitId = 1, DepartmentId = OurDepartment, Name = "Engine 1", VIN = "OURVIN" });
			_units.Setup(u => u.GetUnitByIdAsync(2)).ReturnsAsync(new Unit { UnitId = 2, DepartmentId = OtherDepartment, Name = "Their Engine", VIN = "THEIRVIN", PlateNumber = "THEIRPLATE" });

			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(g => g.GetGroupByIdAsync(11, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 11, DepartmentId = OurDepartment, Name = "Station 1", DispatchEmail = "st1@ours.test" });
			_groups.Setup(g => g.GetGroupByIdAsync(12, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 12, DepartmentId = OtherDepartment, Name = "Their Station", DispatchEmail = "dispatch@theirs.test" });

			_roles = new Mock<IPersonnelRolesService>();
			_roles.Setup(r => r.GetRolesForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<PersonnelRole>());
			_roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<PersonnelRole>());
			_roles.Setup(r => r.GetRoleByIdAsync(21)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 21, DepartmentId = OurDepartment, Name = "Medic" });
			_roles.Setup(r => r.GetRoleByIdAsync(22)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 22, DepartmentId = OtherDepartment, Name = "Their Role", Description = "secret" });

			var sensitive = new Mock<IDepartmentMemberSensitiveDataService>();
			sensitive.Setup(s => s.GetResolvedForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(new Dictionary<string, DepartmentMemberSensitiveData>());
			var media = new Mock<IDepartmentProfileMediaService>();
			media.Setup(m => m.GetEmailBrandingAsync(It.IsAny<int>())).ReturnsAsync((int id) => DepartmentEmailBranding.Disabled(id));

			_builder = new WorkflowTemplateContextBuilder(_departments.Object, Mock.Of<IDepartmentSettingsService>(), _profiles.Object, _groups.Object,
				_roles.Object, _units.Object, sensitive.Object, media.Object);
		}

		private async Task<ScriptObject> CallContext(string reportingUserId)
		{
			var call = new Call
			{
				CallId = 1, DepartmentId = OurDepartment, Name = "Fire", ReportingUserId = reportingUserId,
				Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "member" }, new CallDispatch { UserId = "stranger" } },
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 1 }, new CallDispatchUnit { UnitId = 2 } },
				GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { DepartmentGroupId = 11 }, new CallDispatchGroup { DepartmentGroupId = 12 } },
				RoleDispatches = new List<CallDispatchRole> { new CallDispatchRole { RoleId = 21 }, new CallDispatchRole { RoleId = 22 } }
			};
			var json = JsonConvert.SerializeObject(new CallAddedEvent { DepartmentId = OurDepartment, Call = call });
			return (ScriptObject)await _builder.BuildContextAsync(OurDepartment, WorkflowTriggerEventType.CallAdded, json, CancellationToken.None);
		}

		private static ScriptObject Item(ScriptObject call, string array, int index) => (ScriptObject)((ScriptArray)call[array])[index];

		[Test]
		public async Task Another_departments_user_named_as_the_triggering_user_is_never_loaded()
		{
			var context = await CallContext("stranger");

			((ScriptObject)context["user"])["email"].Should().Be(string.Empty);
			_profiles.Verify(p => p.GetProfileByUserIdAsync("stranger", It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task A_member_named_as_the_triggering_user_still_renders()
		{
			var context = await CallContext("member");

			((ScriptObject)context["user"])["email"].Should().Be("member@example.test");
		}

		[Test]
		public async Task Only_department_members_among_the_dispatched_users_are_enriched()
		{
			var call = (ScriptObject)(await CallContext(null))["call"];

			Item(call, "dispatches", 0)["email"].Should().Be("member@example.test");
			Item(call, "dispatches", 1)["email"].Should().Be(string.Empty);
			_profiles.Verify(p => p.GetSelectedUserProfilesAsync(It.Is<List<string>>(ids => ids.Contains("stranger"))), Times.Never);
		}

		[Test]
		public async Task Another_departments_units_groups_and_roles_render_blank()
		{
			var call = (ScriptObject)(await CallContext(null))["call"];

			Item(call, "unit_dispatches", 0)["vin"].Should().Be("OURVIN");
			Item(call, "unit_dispatches", 1)["unit_name"].Should().Be(string.Empty);
			Item(call, "unit_dispatches", 1)["vin"].Should().Be(string.Empty);
			Item(call, "unit_dispatches", 1)["plate_number"].Should().Be(string.Empty);

			Item(call, "group_dispatches", 0)["dispatch_email"].Should().Be("st1@ours.test");
			Item(call, "group_dispatches", 1)["dispatch_email"].Should().Be(string.Empty);
			Item(call, "group_dispatches", 1)["group_name"].Should().Be(string.Empty);

			Item(call, "role_dispatches", 0)["role_name"].Should().Be("Medic");
			Item(call, "role_dispatches", 1)["role_name"].Should().Be(string.Empty);
			Item(call, "role_dispatches", 1)["role_description"].Should().Be(string.Empty);
		}
	}
}
