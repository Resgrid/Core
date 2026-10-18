using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// Managed activation and invitation sends (wizard plan section 17.8): the department's say on a staff request — approve it
	/// only after both Resgrid staff members, which also ends the notice period, or cancel it — and the worker step, which
	/// re-checks the grant, the authorizing administrator, the department's approval and the notice before doing anything.
	/// </summary>
	[TestFixture]
	public class ImportManagedActivationTests
	{
		private const int DepartmentId = 7;
		private const string WorkspaceId = "W1";

		// NUnit shares one fixture instance across its tests: every mock is recreated in SetUp.
		private Mock<IImportRunsRepository> _runs;
		private Mock<IDepartmentImportAccessService> _access;
		private Mock<IImportTimelineService> _timeline;
		private Mock<IImportRunQueue> _queue;

		private ImportActivationService Activation() => new(Mock.Of<IImportWorkspacesRepository>(), Mock.Of<IImportDatasetsRepository>(), _runs.Object,
			Mock.Of<IImportExternalIdsRepository>(), Mock.Of<IImportReconciliationsRepository>(), Mock.Of<IImportStagedRowsRepository>(), Mock.Of<IImportInvitesRepository>(),
			Mock.Of<IImportValidationService>(), Mock.Of<IDepartmentsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IMemberAccountService>(), Mock.Of<IEmailService>(),
			Mock.Of<IUsersService>(), Mock.Of<ILimitsService>(), _access.Object, Mock.Of<IDepartmentSettingsService>(), _timeline.Object, null, _queue.Object);

		[SetUp]
		public void SetUp()
		{
			_runs = new Mock<IImportRunsRepository>();
			_access = new Mock<IDepartmentImportAccessService>();
			_timeline = new Mock<IImportTimelineService>();
			_queue = new Mock<IImportRunQueue>();
			_access.Setup(a => a.CanManageAsync(DepartmentId, "ADMIN", It.IsAny<bool>())).ReturnsAsync(true);
			_access.Setup(a => a.IsEnabledAsync(DepartmentId)).ReturnsAsync(true);
			_runs.Setup(r => r.UpdateAsync(It.IsAny<ImportRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((ImportRun r, CancellationToken _, bool _) => r);
		}

		private ImportRun Run(int status, string checker, DateTime? nextAttempt = null, DateTime? notBefore = null)
		{
			var run = new ImportRun
			{
				Id = "RUN1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Kind = (int)ImportRunKind.Activation, Status = status, NextAttemptOn = nextAttempt,
				Authority = JsonConvert.SerializeObject(new ImportAuthority
				{
					DepartmentId = DepartmentId, Kind = (int)ImportActorKind.ResgridStaff, OnBehalfOfUserId = "ADMIN", StaffOperator = "op@example.test",
					StaffChecker = checker, GrantId = "G1"
				}),
				Request = JsonConvert.SerializeObject(new ImportManagedActivationRequest
				{
					Kind = ImportManagedActivationKind.Invitations, People = 12, NotBefore = notBefore, Invitations = new ImportInvitationRequest()
				})
			};
			_runs.Setup(r => r.GetByIdAsync(DepartmentId, "RUN1", It.IsAny<CancellationToken>())).ReturnsAsync(run);
			_runs.Setup(r => r.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun> { run });
			return run;
		}

		// ---- the department's say ---------------------------------------------------------------------------------------------

		[Test]
		public async Task The_department_cannot_approve_before_the_second_staff_member_but_can_cancel()
		{
			var run = Run((int)ImportRunStatus.AwaitingChecker, checker: null);
			var service = Activation();

			var refused = await FluentActions.Awaiting(() => service.DecideManagedRunAsync(DepartmentId, "ADMIN", "RUN1", true)).Should()
				.ThrowAsync<DepartmentImportException>();
			refused.Which.Code.Should().Be("import_run_not_awaiting_checker");

			await service.DecideManagedRunAsync(DepartmentId, "ADMIN", "RUN1", false);
			run.Status.Should().Be((int)ImportRunStatus.Cancelled);
			_queue.Verify(q => q.NudgeAsync(It.IsAny<ImportRun>()), Times.Never);
		}

		[Test]
		public async Task An_administrator_starting_a_scheduled_run_early_ends_its_notice_and_is_recorded()
		{
			var run = Run((int)ImportRunStatus.Queued, checker: "ck@example.test", nextAttempt: DateTime.UtcNow.AddHours(20), notBefore: DateTime.UtcNow.AddHours(20));

			await Activation().DecideManagedRunAsync(DepartmentId, "ADMIN", "RUN1", true);

			run.Status.Should().Be((int)ImportRunStatus.Queued);
			run.NextAttemptOn.Should().BeNull();
			var request = JsonConvert.DeserializeObject<ImportManagedActivationRequest>(run.Request);
			request.DepartmentApprovedBy.Should().Be("ADMIN");
			request.DepartmentApprovedOn.Should().NotBeNull();
			_queue.Verify(q => q.NudgeAsync(run), Times.Once);
			_timeline.Verify(t => t.RecordAsync(DepartmentId, WorkspaceId, ImportActorKind.Administrator, "ADMIN", "ManagedActivationApproved", It.IsAny<object>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Someone_without_the_import_permission_has_no_say()
		{
			Run((int)ImportRunStatus.AwaitingChecker, checker: "ck@example.test");

			var refused = await FluentActions.Awaiting(() => Activation().DecideManagedRunAsync(DepartmentId, "MEMBER", "RUN1", true)).Should()
				.ThrowAsync<DepartmentImportException>();
			refused.Which.Code.Should().Be("import_not_allowed");
		}

		[Test]
		public async Task The_verify_tab_lists_what_each_request_is_waiting_for()
		{
			Run((int)ImportRunStatus.Queued, checker: "ck@example.test", nextAttempt: DateTime.UtcNow.AddHours(3));

			var view = (await Activation().GetManagedRunsAsync(DepartmentId, WorkspaceId)).Should().ContainSingle().Subject;

			view.InNoticePeriod.Should().BeTrue();
			view.WaitsForStaff.Should().BeFalse();
			view.WaitsForDepartment.Should().BeFalse();
			view.Request.People.Should().Be(12);
			view.Operator.Should().Be("op@example.test");
		}

		[Test]
		public async Task A_staff_step_without_a_recorded_checker_is_never_carried_out()
		{
			var authority = new ImportAuthority { Kind = (int)ImportActorKind.ResgridStaff, OnBehalfOfUserId = "ADMIN", StaffOperator = "op@example.test" };

			var refused = await FluentActions.Awaiting(() => Activation().InviteOnBehalfAsync(DepartmentId, authority, WorkspaceId, new ImportInvitationRequest()))
				.Should().ThrowAsync<DepartmentImportException>();
			refused.Which.Code.Should().Be("import_not_allowed");
		}

		// ---- the worker step --------------------------------------------------------------------------------------------------

		private sealed class HandlerFixture
		{
			public readonly Mock<IImportActivationService> Activation = new();
			public readonly Mock<IDepartmentImportAccessService> Access = new();
			public readonly Mock<IImportSupportGrantsRepository> Grants = new();
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();

			public HandlerFixture(ImportOperateActions actions = ImportOperateActions.Default, bool signOff = false, bool revoked = false)
			{
				Access.Setup(a => a.IsEnabledAsync(DepartmentId)).ReturnsAsync(true);
				Access.Setup(a => a.CanManageAsync(DepartmentId, "ADMIN", It.IsAny<bool>())).ReturnsAsync(true);
				Grants.Setup(g => g.GetForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSupportGrant>
				{
					new()
					{
						Id = "G1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Level = (int)ImportSupportAccessLevel.Operate, AllowedActions = (int)actions,
						Status = (int)(revoked ? ImportGrantStatus.Revoked : ImportGrantStatus.Active), GrantedOn = DateTime.UtcNow.AddDays(-1),
						ExpiresOn = DateTime.UtcNow.AddDays(5), RequireDepartmentSignOff = signOff
					}
				});
			}

			public ImportActivationRunHandler Handler() => new(Activation.Object, Access.Object, Grants.Object, Workspaces.Object);

			public static ImportRun Run(ImportManagedActivationRequest request, string checker = "ck@example.test") => new()
			{
				Id = "RUN1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Kind = (int)ImportRunKind.Activation,
				Authority = JsonConvert.SerializeObject(new ImportAuthority
				{
					DepartmentId = DepartmentId, Kind = (int)ImportActorKind.ResgridStaff, OnBehalfOfUserId = "ADMIN", StaffOperator = "op@example.test",
					StaffChecker = checker, GrantId = "G1"
				}),
				Request = JsonConvert.SerializeObject(request)
			};
		}

		private static readonly ImportManagedActivationRequest InviteEveryone = new()
		{
			Kind = ImportManagedActivationKind.Invitations, Invitations = new ImportInvitationRequest(), People = 12
		};

		private static Task<ImportStepResult> Execute(HandlerFixture fixture, ImportRun run) =>
			fixture.Handler().ExecuteAsync(run, new ImportRunLease("w1", _ => Task.FromResult(true), _ => Task.FromResult(true)), DateTime.UtcNow.AddSeconds(30),
				CancellationToken.None);

		[Test]
		public async Task A_twice_approved_invitation_run_invites_on_the_departments_behalf()
		{
			var fixture = new HandlerFixture();
			fixture.Activation.Setup(a => a.InviteOnBehalfAsync(DepartmentId, It.Is<ImportAuthority>(x => x.StaffChecker == "ck@example.test"), WorkspaceId,
				It.IsAny<ImportInvitationRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(12);
			var run = HandlerFixture.Run(InviteEveryone);

			var result = await Execute(fixture, run);

			result.Completed.Should().BeTrue();
			run.Counts.Should().Contain("12");
		}

		[Test]
		public async Task A_revoked_grant_pauses_the_run_before_anything_is_sent()
		{
			var fixture = new HandlerFixture(revoked: true);

			var result = await Execute(fixture, HandlerFixture.Run(InviteEveryone));

			result.Pause.Should().BeTrue();
			result.ErrorCode.Should().Be("IMP-APPROVER-NOT-AUTHORIZED");
			fixture.Activation.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Without_the_departments_approval_a_sign_off_grant_pauses_the_run()
		{
			var fixture = new HandlerFixture(signOff: true);

			var result = await Execute(fixture, HandlerFixture.Run(InviteEveryone));

			result.ErrorCode.Should().Be("IMP-DEPARTMENT-SIGNOFF-REQUIRED");
			fixture.Activation.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Nothing_happens_before_the_notice_the_department_was_given()
		{
			var fixture = new HandlerFixture();
			var early = new ImportManagedActivationRequest { Kind = ImportManagedActivationKind.Invitations, NotBefore = DateTime.UtcNow.AddHours(5) };

			var result = await Execute(fixture, HandlerFixture.Run(early));

			result.ErrorCode.Should().Be("IMP-NOTICE-PERIOD");
			fixture.Activation.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_run_without_a_staff_checker_fails()
		{
			var result = await Execute(new HandlerFixture(), HandlerFixture.Run(InviteEveryone, checker: null));

			result.Completed.Should().BeFalse();
			result.ErrorCode.Should().Be("IMP-APPROVER-NOT-AUTHORIZED");
		}

		[Test]
		public async Task An_activation_run_does_not_sign_off_twice()
		{
			var fixture = new HandlerFixture();
			fixture.Workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportWorkspace { Id = WorkspaceId, ActivatedOn = DateTime.UtcNow.AddHours(-1) });
			var activate = new ImportManagedActivationRequest { Kind = ImportManagedActivationKind.Activation, SignOff = true };

			var result = await Execute(fixture, HandlerFixture.Run(activate));

			result.Completed.Should().BeTrue();
			fixture.Activation.Verify(a => a.SignOffOnBehalfAsync(It.IsAny<int>(), It.IsAny<ImportAuthority>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
