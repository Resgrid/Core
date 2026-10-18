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
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// The staff rules BackOffice relies on but does not hold (wizard plan section 17): the role for each action, the
	/// department's active grant with its level, allowed actions and expiry, two-person control for managed migration,
	/// and the metadata-only projection below Observe.
	/// </summary>
	[TestFixture]
	public class DepartmentImportStaffServiceTests
	{
		private const int DepartmentId = 7;
		private const string WorkspaceId = "W1";

		private static readonly StaffActor Support = new StaffActor("support@example.test", StaffActor.SupportRole);
		private static readonly StaffActor Operator = new StaffActor("operator@example.test", StaffActor.MigrationRole);
		private static readonly StaffActor Checker = new StaffActor("checker@example.test", StaffActor.MigrationRole);
		private static readonly StaffActor ReadOnly = new StaffActor("viewer@example.test", StaffActor.ReadOnlyRole);
		private static readonly StaffActor SuperAdmin = new StaffActor("admin@example.test", StaffActor.SuperAdminRole);

		private sealed class Fixture
		{
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();
			public readonly Mock<IImportConfigurationService> Configuration = new();
			public readonly Mock<IImportDatasetsRepository> Datasets = new();
			public readonly Mock<IImportColumnMapsRepository> ColumnMaps = new();
			public readonly Mock<IImportIssuesRepository> Issues = new();
			public readonly Mock<IImportRunsRepository> Runs = new();
			public readonly Mock<IImportReconciliationsRepository> Reconciliations = new();
			public readonly Mock<IImportSupportRequestsRepository> Requests = new();
			public readonly Mock<IImportMessagesRepository> Messages = new();
			public readonly Mock<IImportSuggestionsRepository> Suggestions = new();
			public readonly Mock<IImportSupportGrantsRepository> Grants = new();
			public readonly Mock<IImportArtifactsRepository> Artifacts = new();
			public readonly Mock<IImportEventsRepository> Events = new();
			public readonly Mock<IImportArtifactStore> Store = new();
			public readonly Mock<IImportTimelineService> Timeline = new();
			public readonly Mock<IDepartmentImportNotificationService> Notifications = new();
			public readonly Mock<IImportRunQueue> Queue = new();
			public readonly Mock<IImportApprovalService> Approval = new();
			public readonly Mock<IImportPlanService> Plan = new();
			public readonly Mock<IImportMappingService> Mapping = new();
			public readonly Mock<IDepartmentImportAccessService> Access = new();
			public readonly Mock<IDepartmentsService> Departments = new();
			public readonly Mock<IImportUploadService> Uploads = new();
			public readonly Mock<IDepartmentImportService> Imports = new();
			public readonly Mock<IImportIssueService> IssueService = new();
			public readonly Mock<IImportActivationService> Activation = new();
			public readonly Mock<IImportArtifactCipher> Cipher = new();
			public readonly Mock<IImportKeyService> Keys = new();
			public readonly Mock<IDepartmentDataProtectionService> Protection = new();

			public readonly ImportWorkspace Workspace = new()
			{
				Id = WorkspaceId, DepartmentId = DepartmentId, Name = "Spring roster", InternalReference = "HR-2026", Mode = (int)ImportWorkspaceMode.ExistingAdd
			};

			public Fixture()
			{
				Workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(Workspace);
				Grants.Setup(g => g.GetForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSupportGrant>());
				Runs.Setup(r => r.UpdateAsync(It.IsAny<ImportRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((ImportRun r, CancellationToken _, bool _) => r);
				Runs.Setup(r => r.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>());
				Datasets.Setup(d => d.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportDataset>());
				Artifacts.Setup(a => a.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportArtifact>());
				Requests.Setup(r => r.GetForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSupportRequest>());
				Messages.Setup(m => m.GetPageAsync(DepartmentId, WorkspaceId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportMessage>());
				Suggestions.Setup(s => s.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSuggestion>());
				Events.Setup(e => e.GetPageAsync(DepartmentId, WorkspaceId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportEvent>());
				ColumnMaps.Setup(c => c.GetForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportColumnMap>());
			}

			public DepartmentImportStaffService Service() => new(Workspaces.Object, Datasets.Object, ColumnMaps.Object, Issues.Object, Runs.Object,
				Reconciliations.Object, Requests.Object, Messages.Object, Suggestions.Object, Grants.Object, Artifacts.Object, Events.Object, Store.Object,
				Timeline.Object, Notifications.Object, Queue.Object, Approval.Object, Plan.Object, Mapping.Object, Access.Object, Departments.Object,
				Uploads.Object, Imports.Object, IssueService.Object, Activation.Object, Cipher.Object, Keys.Object, Protection.Object,
				Mock.Of<IImportConfigurationChangesRepository>(), Configuration.Object);

			public ImportSupportGrant Grant(ImportSupportAccessLevel level, ImportOperateActions actions = ImportOperateActions.Default, bool signOff = false,
				DateTime? expires = null)
			{
				var grant = new ImportSupportGrant
				{
					Id = "G1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Level = (int)level, AllowedActions = (int)actions,
					Status = (int)ImportGrantStatus.Active, GrantedOn = DateTime.UtcNow.AddDays(-1), ExpiresOn = expires ?? DateTime.UtcNow.AddDays(5),
					OnBehalfOfUserId = "ADMIN", RequireDepartmentSignOff = signOff
				};
				Grants.Setup(g => g.GetForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSupportGrant> { grant });
				return grant;
			}

			/// <summary>Data set D1 in this workspace, bound to a contract with an internal, a personal, a restricted and a protected field.</summary>
			public ImportMappingView Dataset(string datasetId = "D1", string workspaceId = WorkspaceId, string entityKey = "personnel")
			{
				Datasets.Setup(d => d.GetByIdAsync(DepartmentId, datasetId, It.IsAny<CancellationToken>()))
					.ReturnsAsync(new ImportDataset { Id = datasetId, DepartmentId = DepartmentId, WorkspaceId = workspaceId, EntityKey = entityKey, Revision = 4 });
				var view = new ImportMappingView
				{
					Dataset = new ImportDataset { Id = datasetId, DepartmentId = DepartmentId, WorkspaceId = workspaceId, EntityKey = entityKey },
					Contract = new ImportEntityContract
					{
						Key = entityKey,
						Fields = new List<ImportFieldContract>
						{
							new() { Key = "rank", Type = "choice" },
							new() { Key = "email", Sensitivity = "Personal" },
							new() { Key = "medical_notes", Sensitivity = "Restricted" },
							new() { Key = "ssn", Sensitivity = "Restricted", Protected = "departmentmembersensitivedata.identificationnumber" }
						}
					},
					Body = new ImportColumnMapBody
					{
						Columns = new List<ImportColumnBinding>
						{
							new() { SourceColumn = "Rank", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "rank" },
							new() { SourceColumn = "Email", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "email" },
							new() { SourceColumn = "Medical", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "medical_notes" },
							new() { SourceColumn = "SSN", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "ssn" }
						}
					},
					Profile = new ImportDatasetProfile
					{
						Columns = new List<ImportColumnProfile>
						{
							new() { Header = "Rank", Examples = new List<string> { "Captain" } },
							new() { Header = "Email", Examples = new List<string> { "alice@example.test" } },
							new() { Header = "Medical", Examples = new List<string> { "asthma" } },
							new() { Header = "SSN", Examples = new List<string> { "123-45-6789" } },
							new() { Header = "Notes", Examples = new List<string> { "call mum" }, LooksSensitive = true }
						}
					}
				};
				Mapping.Setup(m => m.GetMappingAsync(DepartmentId, datasetId, It.IsAny<CancellationToken>())).ReturnsAsync(view);
				return view;
			}

			public void Checklist(int dormant = 3, bool canSignOff = true, int existingAccounts = 0)
			{
				Activation.Setup(a => a.GetChecklistAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new ImportActivationChecklist
				{
					PeopleDormant = dormant,
					ExistingAccountsToInvite = existingAccounts,
					Dormant = Enumerable.Range(1, dormant).Select(i => new ImportActivationPerson { UserId = "U" + i }).ToList(),
					PendingAdministrators = new List<ImportActivationPerson> { new() { UserId = "U1" } },
					CanSignOff = canSignOff
				});
			}

			public ImportSupportGrant ContractGrant(ImportOperateActions actions = ImportOperateActions.Default, bool signOff = false)
			{
				var grant = Grant(ImportSupportAccessLevel.Operate, actions, signOff);
				grant.AuthorizationKind = (int)ImportGrantAuthorizationKind.Contract;
				return grant;
			}

			public ImportRun ActivationRun(string runId, ImportManagedActivationRequest request, string checker = null)
			{
				var run = StaffRun(runId, ImportRunKind.Activation, checker);
				run.Request = JsonConvert.SerializeObject(request);
				return run;
			}

			public ImportRun StaffRun(string runId, ImportRunKind kind = ImportRunKind.Commit, string checker = null)
			{
				var run = new ImportRun
				{
					Id = runId, DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Kind = (int)kind, Status = (int)ImportRunStatus.AwaitingChecker,
					DatasetIds = "[\"D1\"]", WriteMode = (int)ImportWriteMode.CreateOnly, ApprovalDigest = "digest-1",
					Authority = JsonConvert.SerializeObject(new ImportAuthority
					{
						DepartmentId = DepartmentId, Kind = (int)ImportActorKind.ResgridStaff, OnBehalfOfUserId = "ADMIN", StaffOperator = Operator.Identity,
						StaffChecker = checker, GrantId = "G1"
					})
				};
				Runs.Setup(r => r.GetByIdAsync(DepartmentId, runId, It.IsAny<CancellationToken>())).ReturnsAsync(run);
				return run;
			}
		}

		private static async Task<DepartmentImportException> Refusal(Func<Task> action)
		{
			var thrown = await FluentActions.Awaiting(action).Should().ThrowAsync<DepartmentImportException>();
			return thrown.Which;
		}

		// ---- roles ------------------------------------------------------------------------------------------------------

		[Test]
		public async Task A_read_only_staff_member_cannot_message_or_run_recovery_actions()
		{
			var fixture = new Fixture();
			var service = fixture.Service();

			(await Refusal(() => service.PostMessageAsync(ReadOnly, DepartmentId, WorkspaceId, "hello", null))).Code.Should().Be("import_not_allowed");
			(await Refusal(() => service.EscalateAsync(ReadOnly, DepartmentId, WorkspaceId, "note"))).Code.Should().Be("import_not_allowed");
		}

		[TestCase(ImportHelpdeskState.Created, ImportHelpdeskState.Pending)]
		[TestCase(ImportHelpdeskState.Pending, ImportHelpdeskState.Pending)]
		[TestCase(ImportHelpdeskState.Failed, ImportHelpdeskState.NotConfigured)]
		[TestCase(ImportHelpdeskState.NotConfigured, ImportHelpdeskState.NotConfigured)]
		public async Task A_reply_on_a_ticketed_request_is_queued_for_its_helpdesk_ticket(ImportHelpdeskState requestState, ImportHelpdeskState expected)
		{
			var fixture = new Fixture();
			fixture.Requests.Setup(r => r.GetByIdAsync(DepartmentId, "R1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportSupportRequest { Id = "R1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, HelpdeskState = (int)requestState });

			var message = await fixture.Service().PostMessageAsync(Support, DepartmentId, WorkspaceId, "Please re-export the roster.", "R1");

			message.SupportRequestId.Should().Be("R1");
			message.HelpdeskState.Should().Be((int)expected);
		}

		[Test]
		public async Task A_reply_without_a_request_stays_in_the_workspace()
		{
			var fixture = new Fixture();

			var message = await fixture.Service().PostMessageAsync(Support, DepartmentId, WorkspaceId, "Hello from Resgrid support.", null);

			message.HelpdeskState.Should().Be((int)ImportHelpdeskState.NotConfigured);
			fixture.Requests.Verify(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_reply_cannot_attach_to_another_workspaces_request()
		{
			var fixture = new Fixture();
			fixture.Requests.Setup(r => r.GetByIdAsync(DepartmentId, "R2", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportSupportRequest { Id = "R2", DepartmentId = DepartmentId, WorkspaceId = "W9", HelpdeskState = (int)ImportHelpdeskState.Created });

			(await Refusal(() => fixture.Service().PostMessageAsync(Support, DepartmentId, WorkspaceId, "hello", "R2"))).Code.Should().Be("import_request_not_found");
			(await Refusal(() => fixture.Service().PostMessageAsync(Support, DepartmentId, WorkspaceId, "hello", "R404"))).Code.Should().Be("import_request_not_found");
			fixture.Messages.Verify(m => m.InsertAsync(It.IsAny<ImportMessage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Support_cannot_request_a_managed_commit_even_under_an_operate_grant()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate);

			(await Refusal(() => fixture.Service().RequestManagedCommitAsync(Support, DepartmentId, WorkspaceId, new ImportApprovalRequest())))
				.Code.Should().Be("import_not_allowed");
			fixture.Approval.Verify(a => a.RequestStaffCommitAsync(It.IsAny<int>(), It.IsAny<ImportAuthority>(), It.IsAny<string>(), It.IsAny<ImportApprovalRequest>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- grants -----------------------------------------------------------------------------------------------------

		[Test]
		public async Task A_managed_commit_needs_an_operate_grant_that_allows_commits()
		{
			var fixture = new Fixture();
			var service = fixture.Service();

			fixture.Grant(ImportSupportAccessLevel.Prepare);
			(await Refusal(() => service.RequestManagedCommitAsync(Operator, DepartmentId, WorkspaceId, new ImportApprovalRequest()))).Code
				.Should().Be("import_grant_level_invalid");

			fixture.Grant(ImportSupportAccessLevel.Operate, ImportOperateActions.Prepare | ImportOperateActions.Rollback);
			(await Refusal(() => service.RequestManagedCommitAsync(Operator, DepartmentId, WorkspaceId, new ImportApprovalRequest()))).Code
				.Should().Be("import_grant_level_invalid");
		}

		[Test]
		public async Task An_expired_grant_counts_as_no_grant()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate, expires: DateTime.UtcNow.AddMinutes(-1));

			(await Refusal(() => fixture.Service().RequestManagedCommitAsync(Operator, DepartmentId, WorkspaceId, new ImportApprovalRequest()))).Code
				.Should().Be("import_grant_level_invalid");
		}

		[Test]
		public async Task A_managed_commit_is_requested_with_the_staff_authority_of_the_grant()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			ImportAuthority authority = null;
			fixture.Approval.Setup(a => a.RequestStaffCommitAsync(DepartmentId, It.IsAny<ImportAuthority>(), WorkspaceId, It.IsAny<ImportApprovalRequest>(), It.IsAny<CancellationToken>()))
				.Callback((int _, ImportAuthority a, string _, ImportApprovalRequest _, CancellationToken _) => authority = a)
				.ReturnsAsync(new ImportRun { Id = "RUN1", WorkspaceId = WorkspaceId });

			await fixture.Service().RequestManagedCommitAsync(Operator, DepartmentId, WorkspaceId, new ImportApprovalRequest());

			authority.IsStaff.Should().BeTrue();
			authority.StaffOperator.Should().Be(Operator.Identity);
			authority.StaffChecker.Should().BeNull();
			authority.OnBehalfOfUserId.Should().Be("ADMIN");
			authority.GrantId.Should().Be("G1");
		}

		// ---- two-person control -----------------------------------------------------------------------------------------

		[Test]
		public async Task The_operator_cannot_approve_their_own_run()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			fixture.StaffRun("RUN1");

			(await Refusal(() => fixture.Service().ApproveManagedRunAsync(new StaffActor("OPERATOR@example.test", StaffActor.SuperAdminRole), DepartmentId, "RUN1", "ok")))
				.Code.Should().Be("import_checker_must_differ");
		}

		[Test]
		public async Task A_different_checker_queues_the_run_when_the_digest_still_matches()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			var run = fixture.StaffRun("RUN1");
			fixture.Plan.Setup(p => p.ComputeApprovalDigestAsync(DepartmentId, It.IsAny<IReadOnlyList<string>>(), ImportWriteMode.CreateOnly, It.IsAny<CancellationToken>()))
				.ReturnsAsync("digest-1");

			await fixture.Service().ApproveManagedRunAsync(Checker, DepartmentId, "RUN1", "preview reviewed");

			run.Status.Should().Be((int)ImportRunStatus.Queued);
			JsonConvert.DeserializeObject<ImportAuthority>(run.Authority).StaffChecker.Should().Be(Checker.Identity);
			run.SecondApprovedBy.Should().Be("staff:" + Checker.Identity);
			fixture.Queue.Verify(q => q.NudgeAsync(run), Times.Once);
		}

		[Test]
		public async Task With_department_sign_off_the_checked_run_keeps_waiting_for_the_department()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate, signOff: true);
			var run = fixture.StaffRun("RUN1");
			fixture.Plan.Setup(p => p.ComputeApprovalDigestAsync(DepartmentId, It.IsAny<IReadOnlyList<string>>(), ImportWriteMode.CreateOnly, It.IsAny<CancellationToken>()))
				.ReturnsAsync("digest-1");

			await fixture.Service().ApproveManagedRunAsync(Checker, DepartmentId, "RUN1", "preview reviewed");

			run.Status.Should().Be((int)ImportRunStatus.AwaitingChecker);
			fixture.Queue.Verify(q => q.NudgeAsync(It.IsAny<ImportRun>()), Times.Never);
		}

		[Test]
		public async Task A_changed_staging_after_the_request_refuses_the_approval()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			var run = fixture.StaffRun("RUN1");
			fixture.Plan.Setup(p => p.ComputeApprovalDigestAsync(DepartmentId, It.IsAny<IReadOnlyList<string>>(), ImportWriteMode.CreateOnly, It.IsAny<CancellationToken>()))
				.ReturnsAsync("digest-2");

			(await Refusal(() => fixture.Service().ApproveManagedRunAsync(Checker, DepartmentId, "RUN1", "preview reviewed"))).Code.Should().Be("IMP-APPROVAL-STALE");
			run.Status.Should().Be((int)ImportRunStatus.AwaitingChecker);
		}

		[Test]
		public async Task A_run_that_already_has_its_staff_checker_cannot_be_checked_again()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate, signOff: true);
			fixture.StaffRun("RUN1", checker: Checker.Identity);

			(await Refusal(() => fixture.Service().ApproveManagedRunAsync(SuperAdmin, DepartmentId, "RUN1", "again"))).Code.Should().Be("import_run_not_awaiting_checker");
		}

		[Test]
		public async Task A_contract_authorization_cannot_be_approved_by_the_superadmin_who_requested_it()
		{
			var fixture = new Fixture();
			fixture.Grants.Setup(g => g.GetByIdAsync(DepartmentId, "G9", It.IsAny<CancellationToken>())).ReturnsAsync(new ImportSupportGrant
			{
				Id = "G9", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Status = (int)ImportGrantStatus.PendingApproval, RequestedByStaff = SuperAdmin.Identity
			});

			(await Refusal(() => fixture.Service().ApproveContractAuthorizationAsync(SuperAdmin, DepartmentId, "G9", true))).Code.Should().Be("import_checker_must_differ");
			(await Refusal(() => fixture.Service().ApproveContractAuthorizationAsync(Operator, DepartmentId, "G9", true))).Code.Should().Be("import_not_allowed");
		}

		// ---- recovery -----------------------------------------------------------------------------------------------------

		[Test]
		public async Task A_live_lease_is_never_released()
		{
			var fixture = new Fixture();
			fixture.Runs.Setup(r => r.GetByIdAsync(DepartmentId, "RUN1", It.IsAny<CancellationToken>())).ReturnsAsync(new ImportRun
			{
				Id = "RUN1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Status = (int)ImportRunStatus.Running, LeaseOwner = "worker-a",
				LeaseExpiresOn = DateTime.UtcNow.AddMinutes(2)
			});

			(await Refusal(() => fixture.Service().ReleaseExpiredLeaseAsync(Support, DepartmentId, "RUN1"))).Code.Should().Be("import_run_in_progress");
			fixture.Runs.Verify(r => r.SaveLeasedAsync(It.IsAny<ImportRun>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- staff uploads ------------------------------------------------------------------------------------------------

		[Test]
		public async Task Staff_uploads_need_prepare_and_are_labeled_staff_authored()
		{
			var fixture = new Fixture();
			var service = fixture.Service();

			fixture.Grant(ImportSupportAccessLevel.Inspect);
			(await Refusal(() => service.StartUploadAsync(Support, DepartmentId, WorkspaceId, "roster.csv", 10, null, "text/csv"))).Code
				.Should().Be("import_grant_level_invalid");

			fixture.Grant(ImportSupportAccessLevel.Prepare);
			fixture.Uploads.Setup(u => u.StartAsync(DepartmentId, "staff:" + Support.Identity, WorkspaceId, "roster.csv", 10, null, "text/csv", null, true,
				It.IsAny<CancellationToken>())).ReturnsAsync(new ImportUploadStatus { ArtifactId = "A1" }).Verifiable();

			(await service.StartUploadAsync(Support, DepartmentId, WorkspaceId, "roster.csv", 10, null, "text/csv")).ArtifactId.Should().Be("A1");
			fixture.Uploads.Verify();
		}

		[Test]
		public async Task Staff_cannot_continue_an_upload_the_department_started()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Prepare);
			fixture.Artifacts.Setup(a => a.GetByIdAsync(DepartmentId, "A1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportArtifact { Id = "A1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, StaffAuthored = false });

			(await Refusal(() => fixture.Service().AppendUploadAsync(Support, DepartmentId, WorkspaceId, "A1", 1, new byte[] { 1 }, "x"))).Code
				.Should().Be("import_artifact_not_found");
		}

		// ---- projection -----------------------------------------------------------------------------------------------------

		[Test]
		public async Task Below_observe_staff_see_metadata_without_names()
		{
			var fixture = new Fixture();
			fixture.Datasets.Setup(d => d.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportDataset>
			{
				new() { Id = "D1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Name = "Roster - Smith family.csv", EntityKey = "personnel", RowCount = 40, ColumnMapId = "M1" }
			});
			fixture.Issues.Setup(i => i.GetForDatasetAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportIssue>
			{
				new() { Code = "IMP-EMAIL-INVALID", RowNumber = 12, Severity = (int)ImportIssueSeverity.Blocking, Status = (int)ImportIssueStatus.Open }
			});
			fixture.Artifacts.Setup(a => a.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportArtifact>
			{
				new() { Id = "A1", OriginalName = "Roster - Smith family.csv", SizeBytes = 2048 }
			});

			var view = await fixture.Service().GetWorkspaceAsync(ReadOnly, DepartmentId, WorkspaceId);

			view.Level.Should().Be((int)ImportSupportAccessLevel.None);
			view.Workspace.Name.Should().BeNull();
			view.Workspace.InternalReference.Should().BeNull();
			fixture.Workspace.Name.Should().Be("Spring roster", "the projection copies, it never edits the stored workspace");
			view.Datasets.Single().Name.Should().BeNull();
			view.Datasets.Single().RowCount.Should().Be(40);
			view.Issues.Single().Rows.Should().BeEmpty();
			view.Issues.Single().OpenCount.Should().Be(1);
			view.Artifacts.Single().FileName.Should().BeNull();
			view.Artifacts.Single().SizeBytes.Should().Be(2048);
		}

		[Test]
		public async Task A_suggestion_s_payload_carries_column_headers_and_is_hidden_below_observe()
		{
			var fixture = new Fixture();
			var stored = new ImportSuggestion
			{
				Id = "S1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Kind = (int)ImportSuggestionKind.ColumnMap,
				Payload = "{\"Columns\":[{\"SourceColumn\":\"Smith family roster\"}]}", Note = "Map the roster columns", Status = (int)ImportSuggestionStatus.Proposed
			};
			fixture.Suggestions.Setup(s => s.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new List<ImportSuggestion> { stored });

			// The grant that allowed the suggestion has expired: kind, status and note stay, the headers do not.
			var hidden = (await fixture.Service().GetWorkspaceAsync(ReadOnly, DepartmentId, WorkspaceId)).Suggestions.Single();
			hidden.Payload.Should().BeNull();
			hidden.Note.Should().Be("Map the roster columns");
			hidden.Kind.Should().Be((int)ImportSuggestionKind.ColumnMap);
			stored.Payload.Should().NotBeNull("the projection copies, it never edits the stored suggestion");

			fixture.Grant(ImportSupportAccessLevel.Observe);
			(await fixture.Service().GetWorkspaceAsync(ReadOnly, DepartmentId, WorkspaceId)).Suggestions.Single().Payload.Should().Be(stored.Payload);
		}

		// ---- workbench: matching ------------------------------------------------------------------------------------------

		[Test]
		public async Task Matching_needs_a_prepare_grant_and_a_support_role()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			var service = fixture.Service();

			fixture.Grant(ImportSupportAccessLevel.Inspect);
			(await Refusal(() => service.GetMappingAsync(Support, DepartmentId, WorkspaceId, "D1"))).Code.Should().Be("import_grant_level_invalid");

			fixture.Grant(ImportSupportAccessLevel.Prepare);
			(await Refusal(() => service.GetMappingAsync(ReadOnly, DepartmentId, WorkspaceId, "D1"))).Code.Should().Be("import_not_allowed");
			(await service.GetMappingAsync(Support, DepartmentId, WorkspaceId, "D1")).Should().NotBeNull();
		}

		[Test]
		public async Task A_data_set_of_another_workspace_is_never_reached_through_this_grant()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Prepare);
			fixture.Dataset("D9", workspaceId: "OTHER");

			(await Refusal(() => fixture.Service().SaveColumnMapAsync(Support, DepartmentId, WorkspaceId, "D9", new ImportColumnMapBody(), 1))).Code
				.Should().Be("import_dataset_not_found");
			fixture.Mapping.Verify(m => m.SaveColumnMapAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ImportActorKind>(), It.IsAny<string>(),
				It.IsAny<ImportColumnMapBody>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Source_examples_are_masked_by_sensitivity_and_protected_ones_never_leave_core()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Prepare);

			var prepare = (await fixture.Service().GetMappingAsync(Support, DepartmentId, WorkspaceId, "D1")).Profile.Columns.ToDictionary(c => c.Header);
			prepare["Rank"].Examples.Should().Equal("Captain");
			prepare["Email"].Examples.Single().Should().StartWith("a").And.NotContain("example");
			prepare["Medical"].Examples.Single().Should().NotBe("asthma");
			prepare["SSN"].Examples.Should().BeEmpty();
			prepare["Notes"].Examples.Single().Should().NotBe("call mum");

			fixture.Grant(ImportSupportAccessLevel.Operate);
			var operate = (await fixture.Service().GetMappingAsync(Operator, DepartmentId, WorkspaceId, "D1")).Profile.Columns.ToDictionary(c => c.Header);
			operate["Email"].Examples.Should().Equal("alice@example.test");
			operate["Medical"].Examples.Single().Should().NotBe("asthma");
			operate["SSN"].Examples.Should().BeEmpty();
		}

		[Test]
		public async Task A_column_feeding_several_fields_is_masked_as_its_most_sensitive_one()
		{
			var fixture = new Fixture();
			var view = fixture.Dataset();
			// "Rank" also feeds the protected and the personal field: its examples are what the protected one allows.
			view.Body.Columns.Add(new ImportColumnBinding { SourceColumn = "Rank", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "email" });
			view.Body.Columns.Add(new ImportColumnBinding { SourceColumn = "Rank", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "ssn" });
			fixture.Grant(ImportSupportAccessLevel.Operate);

			var operate = (await fixture.Service().GetMappingAsync(Operator, DepartmentId, WorkspaceId, "D1")).Profile.Columns.ToDictionary(c => c.Header);

			operate["Rank"].Examples.Should().BeEmpty();
		}

		[Test]
		public async Task A_staff_column_map_is_saved_as_resgrid_staff()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Prepare);
			var body = new ImportColumnMapBody();
			fixture.Mapping.Setup(m => m.SaveColumnMapAsync(DepartmentId, "staff:" + Support.Identity, ImportActorKind.ResgridStaff, "D1", body, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportColumnMap { Id = "M2" }).Verifiable();

			(await fixture.Service().SaveColumnMapAsync(Support, DepartmentId, WorkspaceId, "D1", body, 4)).Id.Should().Be("M2");
			fixture.Mapping.Verify();
		}

		[Test]
		public async Task Value_matching_is_refused_for_restricted_and_protected_fields()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			var service = fixture.Service();

			(await Refusal(() => service.GetValueCandidatesAsync(Operator, DepartmentId, WorkspaceId, "D1", "medical_notes"))).Code.Should().Be("import_field_protected");
			(await Refusal(() => service.GetValueCandidatesAsync(Operator, DepartmentId, WorkspaceId, "D1", "ssn"))).Code.Should().Be("import_field_protected");
			fixture.Mapping.Verify(m => m.GetValueCandidatesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Value_decisions_are_copied_to_other_data_sets_of_the_same_destination_only()
		{
			var fixture = new Fixture();
			fixture.Dataset("D1");
			fixture.Dataset("D2");
			fixture.Grant(ImportSupportAccessLevel.Prepare);
			fixture.Mapping.Setup(m => m.GetValueCandidatesAsync(DepartmentId, "D1", "rank", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportValueCandidate>
			{
				new() { SourceValue = "Capt", Decision = ImportValueMapAction.MapToExisting, DecidedCode = "R1" },
				new() { SourceValue = "Lt" }
			});
			fixture.Mapping.Setup(m => m.GetValueCandidatesAsync(DepartmentId, "D2", "rank", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportValueCandidate>
			{
				new() { SourceValue = "CAPT" }, new() { SourceValue = "Chief" }
			});
			IReadOnlyList<ImportValueDecision> copied = null;
			fixture.Mapping.Setup(m => m.SaveValueMapsAsync(DepartmentId, "staff:" + Support.Identity, ImportActorKind.ResgridStaff, "D2", "rank",
					It.IsAny<IReadOnlyList<ImportValueDecision>>(), It.IsAny<CancellationToken>()))
				.Callback((int _, string _, ImportActorKind _, string _, string _, IReadOnlyList<ImportValueDecision> d, CancellationToken _) => copied = d)
				.ReturnsAsync(1);

			(await fixture.Service().ApplyValueMapsAsync(Support, DepartmentId, WorkspaceId, "D1", "rank", new[] { "D2" })).Should().Be(1);
			copied.Should().ContainSingle().Which.Should().BeEquivalentTo(new { SourceValue = "CAPT", Action = ImportValueMapAction.MapToExisting, TargetCode = "R1" });

			fixture.Dataset("D3", entityKey: "units");
			(await Refusal(() => fixture.Service().ApplyValueMapsAsync(Support, DepartmentId, WorkspaceId, "D1", "rank", new[] { "D3" }))).Code
				.Should().Be("import_unknown_destination");
		}

		// ---- workbench: fixing -----------------------------------------------------------------------------------------------

		[Test]
		public async Task Fixing_needs_an_operate_grant_and_the_migration_role()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			var service = fixture.Service();

			fixture.Grant(ImportSupportAccessLevel.Operate);
			(await Refusal(() => service.GetIssueGroupsAsync(Support, DepartmentId, WorkspaceId, "D1", true))).Code.Should().Be("import_not_allowed");

			fixture.Grant(ImportSupportAccessLevel.Prepare);
			(await Refusal(() => service.ResolveIssuesAsync(Operator, DepartmentId, WorkspaceId, ImportStaffIssueAction.Accept, new[] { "I1" }, null))).Code
				.Should().Be("import_grant_level_invalid");
			fixture.IssueService.Verify(i => i.AcceptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ImportActorKind>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Revealing_a_row_withholds_protected_cells_and_tells_the_department_why()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			fixture.IssueService.Setup(i => i.GetRowAsync(DepartmentId, "ROW1", It.IsAny<CancellationToken>())).ReturnsAsync(new ImportRowView
			{
				StagedRowId = "ROW1", DatasetId = "D1", RowNumber = 12,
				Headers = new List<string> { "Rank", "Email", "SSN" },
				Cells = new List<string> { "Capt", "alice@example", "123-45-6789" }
			});

			(await Refusal(() => fixture.Service().RevealRowAsync(Operator, DepartmentId, WorkspaceId, "ROW1", " "))).Code.Should().Be("import_request_subject_required");

			var row = await fixture.Service().RevealRowAsync(Operator, DepartmentId, WorkspaceId, "ROW1", "fix the invalid email on row 12");

			row.Cells.Select(c => c.Value).Should().Equal("Capt", "alice@example", null);
			row.Cells.Last().Withheld.Should().BeTrue();
			fixture.Timeline.Verify(t => t.RecordAsync(DepartmentId, WorkspaceId, ImportActorKind.ResgridStaff, Operator.Identity, "StaffValuesViewed",
				It.Is<object>(d => JsonConvert.SerializeObject(d).Contains("fix the invalid email")), null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Staff_never_write_a_protected_field()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			fixture.IssueService.Setup(i => i.GetRowAsync(DepartmentId, "ROW1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportRowView { StagedRowId = "ROW1", DatasetId = "D1", Headers = new List<string> { "SSN" }, Cells = new List<string> { "x" } });
			fixture.Issues.Setup(i => i.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ImportIssue> { new() { Id = "I1", DatasetId = "D1", FieldKey = "ssn", ColumnName = "SSN" } });
			var service = fixture.Service();

			(await Refusal(() => service.EditValueAsync(Operator, DepartmentId, WorkspaceId, "ROW1", "SSN", "000"))).Code.Should().Be("import_field_protected");
			(await Refusal(() => service.ResolveIssuesAsync(Operator, DepartmentId, WorkspaceId, ImportStaffIssueAction.SetValue, new[] { "I1" }, ""))).Code
				.Should().Be("import_field_protected");
			fixture.IssueService.Verify(i => i.EditValueAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ImportActorKind>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Issue_parameters_of_protected_fields_are_cleared()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			fixture.IssueService.Setup(i => i.GetIssuesAsync(DepartmentId, WorkspaceId, "D1", "IMP-X", null, 0, 50, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ImportIssue>
				{
					new() { Id = "I1", DatasetId = "D1", FieldKey = "ssn", Parameters = "{\"value\":\"123-45-6789\"}" },
					new() { Id = "I2", DatasetId = "D1", FieldKey = "email", Parameters = "{\"value\":\"alice@\"}" }
				});

			var issues = await fixture.Service().GetIssuesAsync(Operator, DepartmentId, WorkspaceId, "D1", "IMP-X", 0, 50);

			issues.Single(i => i.Id == "I1").Parameters.Should().BeNull();
			issues.Single(i => i.Id == "I2").Parameters.Should().NotBeNull();
		}
	
		// ---- managed activation and invitations ---------------------------------------------------------------------------------

		private static ImportManagedActivationRequest Invitations(params string[] userIds) => new()
		{
			Kind = ImportManagedActivationKind.Invitations,
			Invitations = new ImportInvitationRequest { UserIds = userIds.ToList() }
		};

		[Test]
		public async Task Invitations_need_an_operate_grant_that_allows_sending_them()
		{
			var fixture = new Fixture();
			fixture.Checklist();
			fixture.Grant(ImportSupportAccessLevel.Operate, ImportOperateActions.Prepare | ImportOperateActions.Commit);

			(await Refusal(() => fixture.Service().RequestManagedActivationAsync(Operator, DepartmentId, WorkspaceId, Invitations()))).Code
				.Should().Be("import_grant_level_invalid");
			(await Refusal(() => fixture.Service().RequestManagedActivationAsync(Support, DepartmentId, WorkspaceId, Invitations()))).Code
				.Should().Be("import_not_allowed");
		}

		[Test]
		public async Task Under_contract_every_administrator_is_told_now_and_nothing_happens_for_24_hours()
		{
			var fixture = new Fixture();
			fixture.Checklist(dormant: 3, existingAccounts: 2);
			fixture.ContractGrant();
			ImportRun inserted = null;
			fixture.Runs.Setup(r => r.InsertAsync(It.IsAny<ImportRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((ImportRun r, CancellationToken _, bool _) => inserted = r).ReturnsAsync((ImportRun r, CancellationToken _, bool _) => r);
			var request = Invitations();
			request.Invitations.IncludeExistingAccounts = true;

			await fixture.Service().RequestManagedActivationAsync(Operator, DepartmentId, WorkspaceId, request);

			inserted.Kind.Should().Be((int)ImportRunKind.Activation);
			inserted.Status.Should().Be((int)ImportRunStatus.AwaitingChecker);
			var stored = JsonConvert.DeserializeObject<ImportManagedActivationRequest>(inserted.Request);
			stored.People.Should().Be(5);
			stored.NotBefore.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
			fixture.Notifications.Verify(n => n.NotifyAdministratorsAsync(DepartmentId, WorkspaceId,
				It.Is<string>(a => a.StartsWith("managed-invitations;5;")), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_department_granted_request_has_no_notice_period()
		{
			var fixture = new Fixture();
			fixture.Checklist();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			ImportRun inserted = null;
			fixture.Runs.Setup(r => r.InsertAsync(It.IsAny<ImportRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((ImportRun r, CancellationToken _, bool _) => inserted = r).ReturnsAsync((ImportRun r, CancellationToken _, bool _) => r);

			await fixture.Service().RequestManagedActivationAsync(Operator, DepartmentId, WorkspaceId, Invitations("U2", "NOT-IMPORTED"));

			var stored = JsonConvert.DeserializeObject<ImportManagedActivationRequest>(inserted.Request);
			stored.NotBefore.Should().BeNull();
			stored.Invitations.UserIds.Should().Equal("U2");
			stored.People.Should().Be(1);
		}

		[Test]
		public async Task Requests_are_refused_when_there_is_nothing_to_do_or_one_is_already_open()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate);
			var service = fixture.Service();

			fixture.Checklist(dormant: 0);
			(await Refusal(() => service.RequestManagedActivationAsync(Operator, DepartmentId, WorkspaceId, Invitations()))).Code.Should().Be("import_nothing_to_invite");

			fixture.Checklist(canSignOff: false);
			(await Refusal(() => service.RequestManagedActivationAsync(Operator, DepartmentId, WorkspaceId,
				new ImportManagedActivationRequest { Kind = ImportManagedActivationKind.Activation, SignOff = true }))).Code.Should().Be("import_signoff_not_ready");

			fixture.Checklist();
			fixture.Runs.Setup(r => r.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>
			{
				new() { Id = "OPEN", Kind = (int)ImportRunKind.Activation, Status = (int)ImportRunStatus.Queued }
			});
			(await Refusal(() => service.RequestManagedActivationAsync(Operator, DepartmentId, WorkspaceId, Invitations()))).Code.Should().Be("import_run_open");
		}

		[Test]
		public async Task A_checked_contract_request_is_queued_for_the_end_of_its_notice_and_not_started_early()
		{
			var fixture = new Fixture();
			fixture.ContractGrant();
			var notBefore = DateTime.UtcNow.AddHours(20);
			var run = fixture.ActivationRun("RUN5", new ImportManagedActivationRequest { Kind = ImportManagedActivationKind.Invitations, NotBefore = notBefore, People = 3 });

			await fixture.Service().ApproveManagedRunAsync(Checker, DepartmentId, "RUN5", "plan matches the engagement");

			run.Status.Should().Be((int)ImportRunStatus.Queued);
			run.NextAttemptOn.Should().BeCloseTo(notBefore, TimeSpan.FromSeconds(1));
			JsonConvert.DeserializeObject<ImportAuthority>(run.Authority).StaffChecker.Should().Be(Checker.Identity);
			fixture.Queue.Verify(q => q.NudgeAsync(It.IsAny<ImportRun>()), Times.Never);
		}

		[Test]
		public async Task An_invitation_run_cannot_be_checked_under_a_grant_that_no_longer_allows_invitations()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Operate, ImportOperateActions.Prepare | ImportOperateActions.Activate);
			fixture.ActivationRun("RUN5", new ImportManagedActivationRequest { Kind = ImportManagedActivationKind.Invitations });

			(await Refusal(() => fixture.Service().ApproveManagedRunAsync(Checker, DepartmentId, "RUN5", "ok"))).Code.Should().Be("import_grant_level_invalid");
		}

		[Test]
		public async Task Staff_can_withdraw_an_approved_run_during_its_notice_period()
		{
			var fixture = new Fixture();
			fixture.ContractGrant();
			var run = fixture.ActivationRun("RUN5", new ImportManagedActivationRequest { Kind = ImportManagedActivationKind.Invitations }, Checker.Identity);
			run.Status = (int)ImportRunStatus.Queued;
			run.NextAttemptOn = DateTime.UtcNow.AddHours(10);

			await fixture.Service().RejectManagedRunAsync(Operator, DepartmentId, "RUN5", "department asked to wait");

			run.Status.Should().Be((int)ImportRunStatus.Cancelled);
		}
	
		// ---- sampled values (Inspect) -----------------------------------------------------------------------------------------

		private static void SampleRows(Fixture fixture, int rows, string datasetOfRows = "D1")
		{
			fixture.IssueService.Setup(i => i.GetIssuesAsync(DepartmentId, WorkspaceId, "D1", "IMP-EMAIL-INVALID", null, 0, 200, It.IsAny<CancellationToken>()))
				.ReturnsAsync(Enumerable.Range(1, rows).Select(n => new ImportIssue
				{
					Id = "I" + n, DatasetId = "D1", StagedRowId = "ROW" + n, RowNumber = n + 1, ColumnName = "Email", FieldKey = "email", Code = "IMP-EMAIL-INVALID"
				}).ToList());
			fixture.IssueService.Setup(i => i.GetRowAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, string id, CancellationToken _) => new ImportRowView
				{
					StagedRowId = id, DatasetId = datasetOfRows, RowNumber = 2,
					Headers = new List<string> { "Rank", "Email", "Medical", "SSN", "Notes" },
					Cells = new List<string> { "Captain", "alice@example", "asthma", "123-45-6789", "call mum" }
				});
		}

		[Test]
		public async Task Samples_need_inspect_a_support_role_and_a_reason()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			SampleRows(fixture, 1);
			var service = fixture.Service();

			fixture.Grant(ImportSupportAccessLevel.Observe);
			(await Refusal(() => service.GetIssueSamplesAsync(Support, DepartmentId, WorkspaceId, "D1", "IMP-EMAIL-INVALID", "diagnose"))).Code
				.Should().Be("import_grant_level_invalid");

			fixture.Grant(ImportSupportAccessLevel.Inspect);
			(await Refusal(() => service.GetIssueSamplesAsync(Support, DepartmentId, WorkspaceId, "D1", "IMP-EMAIL-INVALID", " "))).Code
				.Should().Be("import_request_subject_required");
			(await Refusal(() => service.GetIssueSamplesAsync(ReadOnly, DepartmentId, WorkspaceId, "D1", "IMP-EMAIL-INVALID", "diagnose"))).Code
				.Should().Be("import_not_allowed");
			fixture.IssueService.Verify(i => i.GetRowAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Samples_mask_personal_values_and_never_return_restricted_or_protected_ones()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Inspect);
			SampleRows(fixture, 1);

			var sample = await fixture.Service().GetIssueSamplesAsync(Support, DepartmentId, WorkspaceId, "D1", "IMP-EMAIL-INVALID", "request R7: invalid emails");

			var cells = sample.Rows.Single().Cells.ToDictionary(c => c.Column);
			cells["Rank"].Value.Should().Be("Captain");
			cells["Email"].Masked.Should().BeTrue();
			cells["Email"].Value.Should().StartWith("a").And.NotContain("example");
			cells["Medical"].Withheld.Should().BeTrue();
			cells["Medical"].Value.Should().BeNull();
			cells["SSN"].Withheld.Should().BeTrue();
			cells["SSN"].Value.Should().BeNull();
			cells["Notes"].Masked.Should().BeTrue();
			sample.Rows.Single().IssueColumn.Should().Be("Email");

			fixture.Timeline.Verify(t => t.RecordAsync(DepartmentId, WorkspaceId, ImportActorKind.ResgridStaff, Support.Identity, "StaffSampleViewed",
				It.Is<object>(d => JsonConvert.SerializeObject(d).Contains("request R7")), null, It.IsAny<CancellationToken>()), Times.Once);
			fixture.Notifications.Verify(n => n.NotifySupportActivityAsync(DepartmentId, WorkspaceId, "sample-viewed", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_sample_shows_at_most_twenty_rows_of_a_group()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Inspect);
			SampleRows(fixture, 25);

			var sample = await fixture.Service().GetIssueSamplesAsync(Support, DepartmentId, WorkspaceId, "D1", "IMP-EMAIL-INVALID", "diagnose");

			sample.IssueCount.Should().Be(25);
			sample.Rows.Should().HaveCount(DepartmentImportStaffService.SampleRowsPerGroup);
			fixture.IssueService.Verify(i => i.GetRowAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(20));
		}

		[Test]
		public async Task A_row_of_another_data_set_never_appears_in_a_sample()
		{
			var fixture = new Fixture();
			fixture.Dataset();
			fixture.Grant(ImportSupportAccessLevel.Inspect);
			SampleRows(fixture, 2, datasetOfRows: "D9");

			var sample = await fixture.Service().GetIssueSamplesAsync(Support, DepartmentId, WorkspaceId, "D1", "IMP-EMAIL-INVALID", "diagnose");

			sample.Rows.Should().BeEmpty();
		}
	
		// ---- raw file download (Prepare) ------------------------------------------------------------------------------------------

		private static ImportArtifact Upload(Fixture fixture, string workspaceId = WorkspaceId, int scan = (int)ImportArtifactScanState.Clean, DateTime? purgedOn = null)
		{
			var artifact = new ImportArtifact
			{
				Id = "A1", DepartmentId = DepartmentId, WorkspaceId = workspaceId, Kind = (int)ImportArtifactKind.Upload, OriginalName = "Spring Roster (final).csv",
				ObjectKey = "imports/7/W1/A1", WrappedDataKey = "wrapped", UploadState = (int)ImportArtifactUploadState.Completed, ScanState = scan,
				SizeBytes = 1234, PurgedOn = purgedOn
			};
			fixture.Artifacts.Setup(a => a.GetByIdAsync(DepartmentId, "A1", It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
			fixture.Store.SetupGet(s => s.IsConfigured).Returns(true);
			fixture.Keys.Setup(k => k.UnwrapAsync(DepartmentId, "wrapped")).ReturnsAsync("key");
			fixture.Cipher.Setup(c => c.OpenReadAsync(fixture.Store.Object, "imports/7/W1/A1", "key", "A1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new System.IO.MemoryStream(new byte[] { 1, 2, 3 }));
			return artifact;
		}

		[Test]
		public async Task Downloading_a_file_needs_prepare_a_support_role_and_a_reason()
		{
			var fixture = new Fixture();
			Upload(fixture);
			var service = fixture.Service();

			fixture.Grant(ImportSupportAccessLevel.Inspect);
			(await Refusal(() => service.OpenArtifactAsync(Support, DepartmentId, WorkspaceId, "A1", "rebuild the map"))).Code.Should().Be("import_grant_level_invalid");

			fixture.Grant(ImportSupportAccessLevel.Prepare);
			(await Refusal(() => service.OpenArtifactAsync(Support, DepartmentId, WorkspaceId, "A1", " "))).Code.Should().Be("import_request_subject_required");
			(await Refusal(() => service.OpenArtifactAsync(ReadOnly, DepartmentId, WorkspaceId, "A1", "rebuild the map"))).Code.Should().Be("import_not_allowed");
			fixture.Cipher.Verify(c => c.OpenReadAsync(It.IsAny<IImportArtifactStore>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_department_with_advanced_data_protection_never_has_its_raw_files_downloaded()
		{
			var fixture = new Fixture();
			Upload(fixture);
			fixture.Grant(ImportSupportAccessLevel.Operate);
			fixture.Protection.Setup(p => p.ShouldEncryptNewWritesAsync(DepartmentId)).ReturnsAsync(true);

			(await Refusal(() => fixture.Service().OpenArtifactAsync(Operator, DepartmentId, WorkspaceId, "A1", "rebuild the map"))).Code
				.Should().Be("import_download_adp_protected");
		}

		[Test]
		public async Task Only_a_completed_clean_upload_of_this_workspace_is_downloadable()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Prepare);
			var service = fixture.Service();

			Upload(fixture, scan: (int)ImportArtifactScanState.Infected);
			(await Refusal(() => service.OpenArtifactAsync(Support, DepartmentId, WorkspaceId, "A1", "x"))).Code.Should().Be("import_download_not_clean");

			Upload(fixture, purgedOn: DateTime.UtcNow.AddDays(-1));
			(await Refusal(() => service.OpenArtifactAsync(Support, DepartmentId, WorkspaceId, "A1", "x"))).Code.Should().Be("import_artifact_not_found");

			Upload(fixture, workspaceId: "OTHER");
			(await Refusal(() => service.OpenArtifactAsync(Support, DepartmentId, WorkspaceId, "A1", "x"))).Code.Should().Be("import_artifact_not_found");
		}

		[Test]
		public async Task A_download_carries_a_watermarked_name_and_is_on_the_departments_timeline()
		{
			var fixture = new Fixture();
			Upload(fixture);
			fixture.Grant(ImportSupportAccessLevel.Prepare);

			using var download = await fixture.Service().OpenArtifactAsync(Support, DepartmentId, WorkspaceId, "A1", "request R9: rebuild the column map");

			download.FileName.Should().MatchRegex(@"^Spring-Roster--final\.resgrid-staff-support-\d{8}T\d{4}Z\.csv$");
			download.Length.Should().Be(1234);
			download.Content.Length.Should().Be(3);
			fixture.Timeline.Verify(t => t.RecordAsync(DepartmentId, WorkspaceId, ImportActorKind.ResgridStaff, Support.Identity, "StaffArtifactDownloaded",
				It.Is<object>(d => JsonConvert.SerializeObject(d).Contains("request R9")), null, It.IsAny<CancellationToken>()), Times.Once);
			fixture.Notifications.Verify(n => n.NotifySupportActivityAsync(DepartmentId, WorkspaceId, "artifact-downloaded", It.IsAny<CancellationToken>()), Times.Once);
		}
	
		// ---- dashboard tile -------------------------------------------------------------------------------------------------------

		[Test]
		public async Task The_dashboard_tile_counts_open_requests_stalled_failed_and_waiting_runs()
		{
			var fixture = new Fixture();
			var now = DateTime.UtcNow;
			fixture.Requests.Setup(r => r.GetQueueAsync(true, 0, 500, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSupportRequest>
			{
				new() { Id = "R1", OpenedOn = now.AddHours(-30) }, new() { Id = "R2", OpenedOn = now.AddHours(-2) }
			});
			var waiting = fixture.StaffRun("RUN-W");
			var checkedRun = fixture.StaffRun("RUN-C", checker: Checker.Identity);
			waiting.UpdatedOn = checkedRun.UpdatedOn = now;
			fixture.Runs.Setup(r => r.GetRecentAsync(It.IsAny<DateTime>(), 1000, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>
			{
				waiting, checkedRun,
				new() { Id = "F1", Status = (int)ImportRunStatus.Failed, UpdatedOn = now.AddHours(-3) },
				new() { Id = "F2", Status = (int)ImportRunStatus.Failed, UpdatedOn = now.AddDays(-3) }
			});
			fixture.Runs.Setup(r => r.GetStalledAsync(It.IsAny<DateTime>(), 500, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ImportRun> { new() { Id = "S1" } });
			fixture.Workspaces.Setup(w => w.GetQueueAsync(It.IsAny<DateTime>(), null, 0, 500, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ImportWorkspace> { new() { Id = "W1", NeedsEngineering = true }, new() { Id = "W2" } });

			var tile = await fixture.Service().GetDashboardAsync(ReadOnly);

			tile.OpenSupportRequests.Should().Be(2);
			tile.OldestOpenRequestOn.Should().BeCloseTo(now.AddHours(-30), TimeSpan.FromSeconds(1));
			tile.StalledRuns.Should().Be(1);
			tile.FailedRunsLast24Hours.Should().Be(1);
			tile.AwaitingChecker.Should().Be(1);
			tile.NeedsEngineering.Should().Be(1);
		}

		// ---- configuration (plan 7.5) ------------------------------------------------------------------------------------

		[Test]
		public async Task Configuration_is_read_from_Observe_and_drafted_from_Prepare_as_Resgrid_staff()
		{
			var fixture = new Fixture();
			var service = fixture.Service();
			var decision = new ImportConfigurationDecision { ItemKey = "terminology.module.units", Value = "Apparatus" };

			(await Refusal(() => service.GetConfigurationAsync(Support, DepartmentId, WorkspaceId))).Code.Should().Be("import_grant_level_invalid", "no grant, no values");

			fixture.Grant(ImportSupportAccessLevel.Observe);
			await service.GetConfigurationAsync(ReadOnly, DepartmentId, WorkspaceId);
			fixture.Configuration.Verify(c => c.GetForStaffAsync(DepartmentId, WorkspaceId, false, It.IsAny<CancellationToken>()), Times.Once,
				"Observe shows no staged values, so no map centre worked out from them");
			(await Refusal(() => service.DecideConfigurationAsync(Support, DepartmentId, WorkspaceId, decision))).Code.Should().Be("import_grant_level_invalid");

			fixture.Grant(ImportSupportAccessLevel.Inspect);
			await service.GetConfigurationAsync(ReadOnly, DepartmentId, WorkspaceId);
			fixture.Configuration.Verify(c => c.GetForStaffAsync(DepartmentId, WorkspaceId, true, It.IsAny<CancellationToken>()), Times.Once);

			fixture.Grant(ImportSupportAccessLevel.Prepare);
			(await Refusal(() => service.DecideConfigurationAsync(ReadOnly, DepartmentId, WorkspaceId, decision))).Code.Should().Be("import_not_allowed",
				"drafting needs a Support, Migration or SuperAdmin role");
			await service.DecideConfigurationAsync(Support, DepartmentId, WorkspaceId, decision);
			await service.AcceptConfigurationSuggestionsAsync(Support, DepartmentId, WorkspaceId, "modules");
			await service.ResetConfigurationAsync(Support, DepartmentId, WorkspaceId, "terminology.module.units");

			fixture.Configuration.Verify(c => c.DecideAsync(DepartmentId, "staff:support@example.test", ImportActorKind.ResgridStaff, WorkspaceId, decision,
				It.IsAny<CancellationToken>()), Times.Once);
			fixture.Configuration.Verify(c => c.AcceptSuggestionsAsync(DepartmentId, "staff:support@example.test", ImportActorKind.ResgridStaff, WorkspaceId, "modules",
				It.IsAny<CancellationToken>()), Times.Once);
			fixture.Configuration.Verify(c => c.ResetAsync(DepartmentId, "staff:support@example.test", ImportActorKind.ResgridStaff, WorkspaceId,
				"terminology.module.units", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Staff_see_the_products_from_Observe_without_the_source_row_and_choose_them_from_Prepare()
		{
			var fixture = new Fixture();
			fixture.Imports.Setup(i => i.GetSourcesAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSource>
			{
				new() { Id = "C", ProductKey = ImportSourceProducts.Continuation, Label = "Spring roster" },
				new() { Id = "S1", ProductKey = "aladtec", Label = "Aladtec", EncryptedCredentials = "secret", CreatedOn = new DateTime(2026, 10, 1) }
			});
			fixture.Imports.Setup(i => i.AddSourceAsync(DepartmentId, "staff:support@example.test", ImportActorKind.ResgridStaff, WorkspaceId, "d4h", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportSource { Id = "S2", ProductKey = "d4h", Label = "D4H Team Manager", EncryptedCredentials = "secret" });
			var service = fixture.Service();

			(await service.GetWorkspaceAsync(ReadOnly, DepartmentId, WorkspaceId)).Products.Should().BeEmpty("no grant, no products");
			fixture.Grant(ImportSupportAccessLevel.Observe);
			(await service.GetWorkspaceAsync(ReadOnly, DepartmentId, WorkspaceId)).Products.Should().ContainSingle()
				.Which.Should().BeEquivalentTo(new ImportStaffProductRow { Id = "S1", ProductKey = "aladtec", Label = "Aladtec", CreatedOn = new DateTime(2026, 10, 1) });
			(await Refusal(() => service.AddProductAsync(Support, DepartmentId, WorkspaceId, "d4h"))).Code.Should().Be("import_grant_level_invalid");

			fixture.Grant(ImportSupportAccessLevel.Prepare);
			(await Refusal(() => service.AddProductAsync(ReadOnly, DepartmentId, WorkspaceId, "d4h"))).Code.Should().Be("import_not_allowed");
			(await service.AddProductAsync(Support, DepartmentId, WorkspaceId, "d4h")).Should().BeEquivalentTo(new { Id = "S2", ProductKey = "d4h" });
			await service.RemoveProductAsync(Support, DepartmentId, WorkspaceId, "S1");

			fixture.Imports.Verify(i => i.RemoveSourceAsync(DepartmentId, "staff:support@example.test", ImportActorKind.ResgridStaff, WorkspaceId, "S1",
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_staff_logo_upload_is_proposed_as_the_department_s_logo()
		{
			var fixture = new Fixture();
			fixture.Grant(ImportSupportAccessLevel.Prepare);
			fixture.Configuration.Setup(c => c.UploadLogoAsync(DepartmentId, "staff:support@example.test", ImportActorKind.ResgridStaff, WorkspaceId, "logo.png", "image/png",
					It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportArtifact { Id = "LOGO1" });

			await fixture.Service().UploadConfigurationLogoAsync(Support, DepartmentId, WorkspaceId, "logo.png", "image/png", new byte[] { 1 });

			fixture.Configuration.Verify(c => c.DecideAsync(DepartmentId, "staff:support@example.test", ImportActorKind.ResgridStaff, WorkspaceId,
				It.Is<ImportConfigurationDecision>(d => d.ItemKey == "branding.logo" && d.Value == "LOGO1" && d.Accept), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
