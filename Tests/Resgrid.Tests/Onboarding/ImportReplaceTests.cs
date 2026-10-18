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
using Resgrid.Services.Onboarding.Writers;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// Replace (wizard plan sections 5.2 and 12.4): support-assisted and second-approved, scoped to one data set of one kind of
	/// record; the records the file lacks are retired (soft-deleted), each with a rollback proof, and rollback restores them.
	/// </summary>
	[TestFixture]
	public class ImportReplaceTests
	{
		private const int DepartmentId = 7;
		private const string WorkspaceId = "W1";

		// ---- a replaceable writer over an in-memory store ------------------------------------------------------------------

		private sealed class Priority
		{
			public int Id { get; set; }
			public string Name { get; set; }
			public bool Deleted { get; set; }
			public bool InUse { get; set; }
		}

		private sealed class PriorityWriter : ImportRecordWriter<Priority>
		{
			public readonly List<Priority> Store = new();
			private static readonly IReadOnlyList<ImportFieldBinding<Priority>> Fields = new[] { new ImportFieldBinding<Priority>("name", r => r.Name, (r, v) => r.Name = v) };

			public override string EntityKey => "call-priorities";
			public override ImportEffects AffectedEffects => ImportEffects.None;
			protected override IReadOnlyList<ImportFieldBinding<Priority>> Bindings => Fields;
			protected override Task<List<Priority>> LoadAsync(int departmentId, CancellationToken cancellationToken) => Task.FromResult(Store.Where(p => !p.Deleted).ToList());
			protected override string KeyOf(Priority record) => record.Id.ToString();
			protected override string NaturalKeyOf(Priority record) => record.Name;
			protected override Priority New(ImportWriteContext context, ImportCanonicalRow row) => new Priority { Id = Store.Count + 100 };

			protected override Task<Priority> SaveAsync(ImportWriteContext context, Priority record, CancellationToken cancellationToken)
			{
				if (!Store.Contains(record))
					Store.Add(record);
				return Task.FromResult(record);
			}

			protected override Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, Priority record, CancellationToken cancellationToken)
			{
				record.Deleted = true;
				return Task.FromResult(ImportRollbackOutcome.SoftDeleted);
			}

			protected override bool CanRetireRecords => true;
			protected override bool IsRetired(Priority record) => record.Deleted;
			protected override Task<Priority> GetIncludingRetiredAsync(ImportWriteContext context, string key, CancellationToken cancellationToken) =>
				Task.FromResult(Store.FirstOrDefault(p => p.Id.ToString() == key));
			protected override Task<string> RetireBlockerAsync(ImportWriteContext context, Priority record, CancellationToken cancellationToken) =>
				Task.FromResult(record.InUse ? "IMP-REPLACE-IN-USE" : null);

			protected override Task RestoreRetiredAsync(ImportWriteContext context, Priority record, CancellationToken cancellationToken)
			{
				record.Deleted = false;
				return Task.CompletedTask;
			}
		}

		private static PriorityWriter Writer()
		{
			var writer = new PriorityWriter();
			writer.Store.AddRange(new[]
			{
				new Priority { Id = 1, Name = "High" }, new Priority { Id = 2, Name = "Low" }, new Priority { Id = 3, Name = "Old" },
				new Priority { Id = 4, Name = "Gone", Deleted = true }
			});
			return writer;
		}

		private static ImportWriteContext Context() => new() { DepartmentId = DepartmentId, Authority = ImportAuthority.ForAdministrator(DepartmentId, "ADMIN") };

		private static (ImportJournalWriter Writer, List<ImportChangeJournalEntry> Entries) Journal()
		{
			var entries = new List<ImportChangeJournalEntry>();
			return (new ImportJournalWriter((entry, _) =>
			{
				entry.Id = Guid.NewGuid().ToString();
				entries.Add(entry);
				return Task.CompletedTask;
			}), entries);
		}

		[Test]
		public async Task Only_live_records_the_file_lacks_are_candidates_and_records_in_use_cannot_be_retired()
		{
			var writer = Writer();
			writer.Store[2].InUse = true;

			var candidates = await writer.GetRetireCandidatesAsync(Context(), new[] { "1" }, CancellationToken.None);

			candidates.Select(c => (c.TargetKey, c.CanRetire)).Should().Equal(("2", true), ("3", false));
			candidates[1].BlockedCode.Should().Be("IMP-REPLACE-IN-USE");
		}

		[Test]
		public async Task A_retired_record_is_journaled_and_rollback_restores_it()
		{
			var writer = Writer();
			var (journal, entries) = Journal();

			(await writer.RetireAsync(Context(), "2", journal, CancellationToken.None)).Should().BeNull();
			writer.Store[1].Deleted.Should().BeTrue();
			var entry = entries.Single();
			entry.Operation.Should().Be((int)ImportJournalOperation.Retire);
			entry.TargetKey.Should().Be("2");

			var restored = await writer.RollbackAsync(Context(), entry, CancellationToken.None);

			restored.Outcome.Should().Be(ImportRollbackOutcome.Restored);
			writer.Store[1].Deleted.Should().BeFalse();
			(await writer.RollbackAsync(Context(), entry, CancellationToken.None)).Outcome.Should().Be(ImportRollbackOutcome.None, "it is already back");
		}

		[Test]
		public async Task A_retired_record_whose_name_was_taken_since_is_left_for_a_check_by_hand()
		{
			var writer = Writer();
			var (journal, entries) = Journal();
			await writer.RetireAsync(Context(), "2", journal, CancellationToken.None);
			writer.Store.Add(new Priority { Id = 9, Name = "LOW" });

			var result = await writer.RollbackAsync(Context(), entries.Single(), CancellationToken.None);

			result.Outcome.Should().Be(ImportRollbackOutcome.ManualReconciliation);
			result.Code.Should().Be("IMP-ROLLBACK-CHANGED-SINCE");
			writer.Store[1].Deleted.Should().BeTrue();
		}

		[Test]
		public async Task A_record_taken_into_use_is_not_retired()
		{
			var writer = Writer();
			writer.Store[1].InUse = true;
			var (journal, entries) = Journal();

			(await writer.RetireAsync(Context(), "2", journal, CancellationToken.None)).Should().Be("IMP-REPLACE-IN-USE");

			writer.Store[1].Deleted.Should().BeFalse();
			entries.Should().BeEmpty();
		}

		[Test]
		public async Task A_certification_type_people_hold_cannot_be_retired()
		{
			var certifications = new Mock<ICertificationService>();
			certifications.Setup(c => c.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentCertificationType>
			{
				new() { DepartmentCertificationTypeId = 5, DepartmentId = DepartmentId, Type = "EMT" },
				new() { DepartmentCertificationTypeId = 6, DepartmentId = DepartmentId, Type = "Driver" }
			});
			certifications.Setup(c => c.IsCertificationTypeInUseAsync(5)).ReturnsAsync(true);

			var candidates = await new CertificationTypesImportWriter(certifications.Object).GetRetireCandidatesAsync(Context(), Array.Empty<string>(), CancellationToken.None);

			candidates.Select(c => (c.TargetKey, c.BlockedCode)).Should().Equal(("5", "IMP-REPLACE-IN-USE"), ("6", null));
		}

		[Test]
		public void Only_restorable_kinds_offer_replace()
		{
			new CallPrioritiesImportWriter(Mock.Of<ICallsService>()).SupportsReplace.Should().BeTrue();
			new CertificationTypesImportWriter(Mock.Of<ICertificationService>()).SupportsReplace.Should().BeTrue();
			// Hard-deleted records cannot be brought back with their id, so they are never retired.
			new CallTypesImportWriter(Mock.Of<ICallsService>()).SupportsReplace.Should().BeFalse();
		}

		// ---- planning: the frozen retire list ---------------------------------------------------------------------------

		private sealed class PlanFixture
		{
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();
			public readonly Mock<IImportDatasetsRepository> Datasets = new();
			public readonly Mock<IImportIssuesRepository> Issues = new();
			public readonly Mock<IImportValidationService> Validation = new();
			public readonly Mock<IImportCatalog> Catalog = new();
			public readonly List<ImportIssue> Inserted = new();
			public readonly List<ImportIssue> Existing = new();
			public readonly List<ImportDataset> Live = new();
			public readonly ImportDataset Dataset = new()
			{
				Id = "D1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, EntityKey = "call-priorities", DestinationConfirmed = true,
				Status = (int)ImportDatasetStatus.Ready, Revision = 2
			};

			public PlanFixture(params string[] names)
			{
				Live.Add(Dataset);
				Workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>()))
					.ReturnsAsync(new ImportWorkspace { Id = WorkspaceId, DepartmentId = DepartmentId, DefaultWriteMode = (int)ImportWriteMode.Replace });
				Datasets.Setup(d => d.GetByIdAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(Dataset);
				Datasets.Setup(d => d.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Live);
				Catalog.Setup(c => c.GetContract("call-priorities")).Returns(new ImportEntityContract { Key = "call-priorities" });
				var rows = names.Select((name, i) =>
				{
					var row = new ImportCanonicalRow { StagedRowId = "S" + i, RowNumber = i + 2, ValidationState = ImportRowValidationState.Valid };
					row.Values["name"] = name;
					return row;
				}).ToList();
				Validation.Setup(v => v.ReadCanonicalRowsAsync(Dataset, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ImportDataset _, int after, int _, CancellationToken _) => rows.Where(r => r.RowNumber > after).ToList());
				Issues.Setup(i => i.GetForDatasetAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(() => Existing.ToList());
				Issues.Setup(i => i.InsertManyAsync(It.IsAny<IReadOnlyList<ImportIssue>>(), It.IsAny<CancellationToken>()))
					.Callback((IReadOnlyList<ImportIssue> list, CancellationToken _) => Inserted.AddRange(list)).Returns(Task.CompletedTask);
			}

			public ImportPlanService Service(params IImportTargetWriter[] writers)
			{
				var rows = new Mock<IImportStagedRowsRepository>();
				rows.Setup(r => r.GetDigestInputsAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportStagedRow>());
				var sources = new Mock<IImportSourcesRepository>();
				sources.Setup(s => s.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSource>());
				var externalIds = new Mock<IImportExternalIdsRepository>();
				externalIds.Setup(e => e.GetForSourceIdsAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new List<ImportExternalId>());
				Datasets.Setup(d => d.UpdateWithRevisionAsync(It.IsAny<ImportDataset>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				return new ImportPlanService(Workspaces.Object, Datasets.Object, sources.Object, rows.Object, Issues.Object, externalIds.Object,
					Mock.Of<IImportColumnMapsRepository>(), Mock.Of<IImportValueMapsRepository>(), Catalog.Object, Validation.Object, writers,
					Mock.Of<IDepartmentsService>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentSettingsService>(),
					Mock.Of<IUserDefinedFieldsService>());
			}

			public ImportIssue Consequence => Inserted.SingleOrDefault(i => i.Code == "IMP-CONSEQUENCE-UNDECIDED" && i.FieldKey == ImportPlanService.ReplaceRetiresConsequence);
		}

		[Test]
		public async Task Planning_freezes_the_records_the_file_lacks_as_a_consequence_to_acknowledge()
		{
			var fixture = new PlanFixture("High", "Urgent");

			await fixture.Service(Writer()).PlanDatasetAsync(DepartmentId, "D1", null);

			var consequence = fixture.Consequence;
			consequence.Should().NotBeNull();
			consequence.Severity.Should().Be((int)ImportIssueSeverity.Blocking, "it waits for the administrator's acknowledgement");
			ImportPlanService.RetireKeysOf(consequence).Should().Be("2,3", "High stays, Gone is already retired");
			JsonConvert.DeserializeObject<Dictionary<string, string>>(consequence.Parameters)["count"].Should().Be("2");
		}

		[Test]
		public async Task An_excluded_row_still_keeps_its_record()
		{
			// "Low" is in the file even though the planner never sees it as a candidate (excluded or blocked rows).
			var fixture = new PlanFixture("High", "Low");

			await fixture.Service(Writer()).PlanDatasetAsync(DepartmentId, "D1", null);

			ImportPlanService.RetireKeysOf(fixture.Consequence).Should().Be("3");
		}

		[Test]
		public async Task A_changed_retire_list_must_be_acknowledged_again()
		{
			var fixture = new PlanFixture("High");
			var stale = new ImportIssue
			{
				Id = "I1", DatasetId = "D1", Code = "IMP-CONSEQUENCE-UNDECIDED", FieldKey = ImportPlanService.ReplaceRetiresConsequence,
				Status = (int)ImportIssueStatus.Accepted, IdentityKey = ImportIssueFactory.IdentityKey("D1", null, ImportPlanService.ReplaceRetiresConsequence, "IMP-CONSEQUENCE-UNDECIDED"),
				Parameters = JsonConvert.SerializeObject(new Dictionary<string, string> { ["keys"] = "3" })
			};
			fixture.Existing.Add(stale);

			await fixture.Service(Writer()).PlanDatasetAsync(DepartmentId, "D1", null);

			fixture.Issues.Verify(i => i.DeleteAsync(stale, It.IsAny<CancellationToken>()), Times.Once);
			ImportPlanService.RetireKeysOf(fixture.Consequence).Should().Be("2,3");
			fixture.Consequence.Status.Should().Be((int)ImportIssueStatus.Open);
		}

		[Test]
		public async Task A_kind_that_cannot_be_retired_or_a_second_data_set_blocks_replace()
		{
			var unsupported = new PlanFixture("High");
			await unsupported.Service(new CallTypesImportWriterStub()).PlanDatasetAsync(DepartmentId, "D1", null);
			unsupported.Inserted.Should().Contain(i => i.Code == "IMP-REPLACE-UNSUPPORTED" && i.Severity == (int)ImportIssueSeverity.Blocking);

			var twoFiles = new PlanFixture("High");
			twoFiles.Live.Add(new ImportDataset { Id = "D2", WorkspaceId = WorkspaceId, EntityKey = "call-priorities", Status = (int)ImportDatasetStatus.Ready });
			await twoFiles.Service(Writer()).PlanDatasetAsync(DepartmentId, "D1", null);
			twoFiles.Inserted.Should().Contain(i => i.Code == "IMP-REPLACE-ONE-DATASET");
			twoFiles.Consequence.Should().BeNull();
		}

		/// <summary>A writer for the same destination that cannot retire (hard deletes).</summary>
		private sealed class CallTypesImportWriterStub : IImportTargetWriter
		{
			public string EntityKey => "call-priorities";
			public ImportEffects AffectedEffects => ImportEffects.None;
			public Task<ImportGateResult> CheckGateAsync(ImportWriteContext context, CancellationToken cancellationToken) => Task.FromResult(ImportGateResult.Ok());
			public Task<IReadOnlyDictionary<string, string>> FindExistingAsync(int departmentId, CancellationToken cancellationToken) =>
				Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
			public Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken) =>
				Task.FromResult(new ImportPlanResult { Rows = rows.Select(r => new ImportPlannedRow { StagedRowId = r.StagedRowId, Decision = ImportRowDecision.Create }).ToList() });
			public Task<ImportRowWriteResult> WriteRowAsync(ImportWriteContext context, ImportCanonicalRow row, ImportJournalWriter journal, CancellationToken cancellationToken) =>
				throw new NotSupportedException();
			public Task<ImportRollbackEntryResult> RollbackAsync(ImportWriteContext context, ImportChangeJournalEntry entry, CancellationToken cancellationToken) =>
				throw new NotSupportedException();
			public Task<ImportVerifyResult> VerifyAsync(ImportWriteContext context, IReadOnlyList<ImportExternalId> written, CancellationToken cancellationToken) =>
				throw new NotSupportedException();
		}

		// ---- approval: support-assisted, one data set ----------------------------------------------------------------------

		private sealed class ApprovalFixture
		{
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();
			public readonly Mock<IImportDatasetsRepository> Datasets = new();
			public readonly Mock<IImportRunsRepository> Runs = new();
			public readonly Mock<IImportSupportGrantsRepository> Grants = new();
			public readonly Mock<IDepartmentImportAccessService> Access = new();
			public readonly List<ImportSupportGrant> GrantRows = new();
			public readonly List<ImportDataset> Live = new();

			public ApprovalFixture()
			{
				Workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>()))
					.ReturnsAsync(new ImportWorkspace { Id = WorkspaceId, DepartmentId = DepartmentId, DefaultWriteMode = (int)ImportWriteMode.Replace });
				Runs.Setup(r => r.GetOpenForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>());
				Access.Setup(a => a.CanManageAsync(DepartmentId, It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(true);
				Grants.Setup(g => g.GetForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(() => GrantRows);
				Datasets.Setup(d => d.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Live);
			}

			public void Grant(ImportSupportAccessLevel level, ImportOperateActions actions = ImportOperateActions.Default) =>
				GrantRows.Add(new ImportSupportGrant
				{
					Id = "G1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Level = (int)level, AllowedActions = (int)actions,
					Status = (int)ImportGrantStatus.Active, GrantedOn = DateTime.UtcNow.AddDays(-1), ExpiresOn = DateTime.UtcNow.AddDays(3), OnBehalfOfUserId = "ADMIN"
				});

			public ImportApprovalService Service() => new(Workspaces.Object, Datasets.Object, Mock.Of<IImportStagedRowsRepository>(), Mock.Of<IImportIssuesRepository>(), Runs.Object,
				Mock.Of<IImportChangeJournalRepository>(), Mock.Of<IImportCatalog>(), Mock.Of<IImportPlanService>(), Mock.Of<IImportRunQueue>(), Mock.Of<IImportTimelineService>(),
				Access.Object, Mock.Of<IDepartmentSettingsService>(), null, Grants.Object, ImportConfigurationTests.NoConfiguration(),
				Mock.Of<IImportConfigurationChangesRepository>());
		}

		private static ImportApprovalRequest Replace() => new() { WriteMode = ImportWriteMode.Replace };

		private static async Task<string> RefusalCode(Func<Task> action)
		{
			var thrown = await FluentActions.Awaiting(action).Should().ThrowAsync<DepartmentImportException>();
			return thrown.Which.Code;
		}

		[Test]
		public async Task An_administrator_replaces_only_while_support_holds_prepare_or_more()
		{
			var fixture = new ApprovalFixture();
			(await RefusalCode(() => fixture.Service().ApproveAsync(DepartmentId, "ADMIN", WorkspaceId, Replace()))).Should().Be("import_replace_needs_support");

			fixture.Grant(ImportSupportAccessLevel.Inspect);
			(await RefusalCode(() => fixture.Service().ApproveAsync(DepartmentId, "ADMIN", WorkspaceId, Replace()))).Should().Be("import_replace_needs_support");

			fixture.GrantRows.Clear();
			fixture.Grant(ImportSupportAccessLevel.Prepare);
			// Past the Replace rules: nothing is ready in this workspace.
			(await RefusalCode(() => fixture.Service().ApproveAsync(DepartmentId, "ADMIN", WorkspaceId, Replace()))).Should().Be("import_nothing_ready");
		}

		[Test]
		public async Task Staff_replace_only_under_an_operate_grant_that_allows_it()
		{
			var fixture = new ApprovalFixture();
			var staff = new ImportAuthority { DepartmentId = DepartmentId, Kind = (int)ImportActorKind.ResgridStaff, StaffOperator = "op@example.test", OnBehalfOfUserId = "ADMIN", GrantId = "G1" };

			fixture.Grant(ImportSupportAccessLevel.Operate);
			(await RefusalCode(() => fixture.Service().RequestStaffCommitAsync(DepartmentId, staff, WorkspaceId, Replace()))).Should().Be("import_replace_not_allowed");

			fixture.GrantRows.Clear();
			fixture.Grant(ImportSupportAccessLevel.Operate, ImportOperateActions.Default | ImportOperateActions.Replace);
			(await RefusalCode(() => fixture.Service().RequestStaffCommitAsync(DepartmentId, staff, WorkspaceId, Replace()))).Should().Be("import_nothing_ready");
		}

		[Test]
		public async Task Replace_commits_exactly_one_data_set()
		{
			var fixture = new ApprovalFixture();
			fixture.Grant(ImportSupportAccessLevel.Prepare);
			fixture.Live.Add(new ImportDataset { Id = "D1", WorkspaceId = WorkspaceId, EntityKey = "call-priorities", Status = (int)ImportDatasetStatus.Ready });
			fixture.Live.Add(new ImportDataset { Id = "D2", WorkspaceId = WorkspaceId, EntityKey = "certification-types", Status = (int)ImportDatasetStatus.Ready });

			(await RefusalCode(() => fixture.Service().ApproveAsync(DepartmentId, "ADMIN", WorkspaceId, Replace()))).Should().Be("import_replace_one_dataset");
		}
	}
}
