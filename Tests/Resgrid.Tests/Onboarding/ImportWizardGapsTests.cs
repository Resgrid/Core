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
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Import.Catalog;
using Resgrid.Providers.Import.Storage;
using Resgrid.Services.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// The wizard steps the first cut left without a screen (wizard plan sections 5.2, 6.3, 6.4 and 10.2): the day/month answer
	/// once per column, the same value for every row, one column feeding two fields, a preview planned for the write mode
	/// that is approved, and a later import continuing an earlier one's Source IDs.
	/// </summary>
	[TestFixture]
	public class ImportWizardGapsTests
	{
		private const int DepartmentId = 7;
		private const string WorkspaceId = "W2";
		private const string EarlierId = "W1";

		private static readonly ImportEntityContract People = new ImportEntityContract
		{
			Key = "personnel",
			Version = "2026.10",
			NaturalKey = new List<string> { "email" },
			Fields = new List<ImportFieldContract>
			{
				new() { Key = "source_id", Type = ImportFieldTypes.Text, Unique = "in-file" },
				new() { Key = "first_name", Type = ImportFieldTypes.Text, Required = true },
				new() { Key = "last_name", Type = ImportFieldTypes.Text, Required = true },
				new() { Key = "email", Type = ImportFieldTypes.Email, Unique = "in-file" },
				new() { Key = "hired", Type = ImportFieldTypes.Date },
				new() { Key = "group", Type = ImportFieldTypes.Reference, References = "groups" },
				new()
				{
					Key = "status", Type = ImportFieldTypes.Choice,
					Choices = new List<ImportChoiceCode> { new() { Code = "active" }, new() { Code = "inactive" } }
				}
			}
		};

		// ---- the day/month answer ---------------------------------------------------------------------------------------

		[Test]
		public void The_day_month_answer_is_about_order_whatever_the_separator()
		{
			var hired = People.Field("hired");
			ImportConversion Read(string text, string format) =>
				ImportFieldConverter.Convert(hired, text, new ImportConversionContext { DateFormat = format });

			Read("03.04.2026", "dd/MM/yyyy").Value.Should().Be("2026-04-03");
			Read("03-04-2026", "MM/dd/yyyy").Value.Should().Be("2026-03-04");
			Read("3/4/2026", "dd/MM/yyyy").Value.Should().Be("2026-04-03");
			Read("2026-04-03", "MM/dd/yyyy").Value.Should().Be("2026-04-03");
			Read("13/04/2026", "MM/dd/yyyy").IssueCode.Should().Be("IMP-TYPE-DATE");
			Read("03/04/2026", null).IssueCode.Should().Be("IMP-TYPE-DATE-AMBIGUOUS");
		}

		// ---- the mapping: dates, the same value for every row, two fields from one column -------------------------------

		private sealed class MappingFixture
		{
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();
			public readonly Mock<IImportDatasetsRepository> Datasets = new();
			public readonly Mock<IImportColumnMapsRepository> ColumnMaps = new();
			public readonly Mock<IImportCatalog> Catalog = new();
			public readonly Mock<IImportRunQueue> Queue = new();
			public readonly ImportDataset Dataset = new() { Id = "D1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, EntityKey = "personnel", ColumnMapId = "M1", Revision = 5, Status = (int)ImportDatasetStatus.Ready };
			public ImportColumnMapBody Saved;

			public MappingFixture(ImportColumnMapBody body)
			{
				Workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new ImportWorkspace { Id = WorkspaceId });
				Datasets.Setup(d => d.GetByIdAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(Dataset);
				Datasets.Setup(d => d.UpdateWithRevisionAsync(Dataset, 5, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				ColumnMaps.Setup(c => c.GetByIdAsync(DepartmentId, "M1", It.IsAny<CancellationToken>()))
					.ReturnsAsync(new ImportColumnMap { Id = "M1", Version = 2, Body = JsonConvert.SerializeObject(body) });
				ColumnMaps.Setup(c => c.InsertAsync(It.IsAny<ImportColumnMap>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.Callback((ImportColumnMap m, CancellationToken _, bool _) => Saved = JsonConvert.DeserializeObject<ImportColumnMapBody>(m.Body))
					.ReturnsAsync((ImportColumnMap m, CancellationToken _, bool _) => m);
				Catalog.Setup(c => c.GetContract("personnel")).Returns(People);
				Catalog.Setup(c => c.InDependencyOrder()).Returns(new List<ImportEntityContract> { People });
			}

			public ImportMappingService Service() => new(Workspaces.Object, Datasets.Object, ColumnMaps.Object, Mock.Of<IImportValueMapsRepository>(),
				Mock.Of<IImportArtifactsRepository>(), Mock.Of<IImportStagedRowsRepository>(), Catalog.Object, Mock.Of<IImportKeyService>(),
				Mock.Of<IImportArtifactCipher>(), Mock.Of<IImportValidationService>(), Queue.Object, Mock.Of<IImportTimelineService>(),
				new List<IImportTargetWriter>());
		}

		private static ImportColumnMapBody NameMap() => new()
		{
			Columns = new List<ImportColumnBinding>
			{
				new() { SourceColumn = "Name", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "first_name", Transforms = new List<string> { "name-first" } },
				new() { SourceColumn = "Name", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "last_name", Transforms = new List<string> { "name-last" } },
				new() { SourceColumn = "Hired", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "hired" }
			}
		};

		[Test]
		public async Task One_column_feeds_two_fields_each_with_its_own_clean_up()
		{
			var fixture = new MappingFixture(new ImportColumnMapBody());

			await fixture.Service().SaveColumnMapAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", NameMap(), 5);

			fixture.Saved.Columns.Where(c => c.SourceColumn == "Name").Select(c => (c.FieldKey, c.Transforms.Single()))
				.Should().Equal(("first_name", "name-first"), ("last_name", "name-last"));
			ImportTransforms.Apply(fixture.Saved.Columns[0].Transforms, "Rivera, Alex").Should().Be("Alex");
			ImportTransforms.Apply(fixture.Saved.Columns[1].Transforms, "Rivera, Alex").Should().Be("Rivera");
		}

		[Test]
		public async Task A_columns_date_order_is_answered_once_and_can_be_taken_back()
		{
			var fixture = new MappingFixture(NameMap());
			var service = fixture.Service();

			await service.SetDateFormatAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "Hired", "dd/MM/yyyy");
			fixture.Saved.Formats.Should().Equal(new Dictionary<string, string> { ["Hired"] = "dd/MM/yyyy" });
			fixture.Saved.Columns.Should().HaveCount(3, "the rest of the matching is kept");
			fixture.Queue.Verify(q => q.QueueAsync(It.Is<ImportRun>(r => r.Kind == (int)ImportRunKind.Analysis), It.IsAny<CancellationToken>()), Times.Once);

			(await Refusal(() => service.SetDateFormatAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "Hired", "yyyy.dd.MM"))).Code
				.Should().Be("import_date_format_unknown");
			(await Refusal(() => service.SetDateFormatAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "Shoe size", "dd/MM/yyyy"))).Code
				.Should().Be("import_unknown_column");
		}

		[Test]
		public async Task The_same_value_for_every_row_is_checked_when_it_is_saved()
		{
			var fixture = new MappingFixture(NameMap());
			var service = fixture.Service();

			await service.SetConstantAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "status", "active");
			fixture.Saved.Constants.Should().Equal(new Dictionary<string, string> { ["status"] = "active" });

			(await Refusal(() => service.SetConstantAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "status", "retired"))).Code
				.Should().Be("import_constant_invalid");
			(await Refusal(() => service.SetConstantAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "hired", "someday"))).Code
				.Should().Be("import_constant_invalid");
			// Every row would collide.
			(await Refusal(() => service.SetConstantAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "source_id", "1"))).Code
				.Should().Be("import_constant_not_allowed");
			(await Refusal(() => service.SetConstantAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "email", "all@example.test"))).Code
				.Should().Be("import_constant_not_allowed");
			People.Field("group").AllowsConstant.Should().BeTrue();
		}

		[Test]
		public async Task An_empty_value_removes_the_same_value_for_every_row()
		{
			var body = NameMap();
			body.Constants["status"] = "active";
			var fixture = new MappingFixture(body);

			await fixture.Service().SetConstantAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, "D1", "status", " ");

			fixture.Saved.Constants.Should().BeEmpty();
		}

		// ---- the write mode the preview is planned for ------------------------------------------------------------------

		private sealed class ImportsFixture
		{
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();
			public readonly Mock<IImportSourcesRepository> Sources = new();
			public readonly Mock<IImportDatasetsRepository> Datasets = new();
			public readonly Mock<IImportRunsRepository> Runs = new();
			public readonly Mock<IImportRunQueue> Queue = new();
			public readonly Mock<IImportTimelineService> Timeline = new();
			public readonly Mock<IImportSupportGrantsRepository> Grants = new();
			public readonly List<ImportSource> SourceRows = new();
			public readonly Dictionary<string, List<ImportDataset>> DatasetsByWorkspace = new();
			public readonly Dictionary<string, ImportWorkspace> WorkspaceRows = new();
			public IImportCatalog Catalog = Mock.Of<IImportCatalog>();

			public ImportsFixture()
			{
				foreach (var (id, name) in new[] { (EarlierId, "Spring roster"), (WorkspaceId, "Autumn roster"), ("W0", "Abandoned") })
				{
					WorkspaceRows[id] = new ImportWorkspace { Id = id, DepartmentId = DepartmentId, Name = name, Revision = 3, Status = (int)ImportWorkspaceStatus.Draft };
					DatasetsByWorkspace[id] = new List<ImportDataset>();
				}

				Workspaces.Setup(w => w.GetByIdAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, string id, CancellationToken _) => WorkspaceRows.TryGetValue(id, out var w) ? w : null);
				Workspaces.Setup(w => w.GetAllForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => WorkspaceRows.Values.ToList());
				Workspaces.Setup(w => w.UpdateWithRevisionAsync(It.IsAny<ImportWorkspace>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Datasets.Setup(d => d.GetAllForWorkspaceAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, string id, CancellationToken _) => DatasetsByWorkspace.TryGetValue(id, out var list) ? list : new List<ImportDataset>());
				Datasets.Setup(d => d.UpdateWithRevisionAsync(It.IsAny<ImportDataset>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Runs.Setup(r => r.GetAllForWorkspaceAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>());
				Sources.Setup(s => s.GetAllForWorkspaceAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, string id, CancellationToken _) => SourceRows.Where(s => s.WorkspaceId == id).ToList());
				Sources.Setup(s => s.GetByIdAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, string id, CancellationToken _) => SourceRows.FirstOrDefault(s => s.Id == id));
				Sources.Setup(s => s.InsertAsync(It.IsAny<ImportSource>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.Callback((ImportSource s, CancellationToken _, bool _) => SourceRows.Add(s)).ReturnsAsync((ImportSource s, CancellationToken _, bool _) => s);
				Grants.Setup(g => g.GetForWorkspaceAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSupportGrant>());
				Sources.Setup(s => s.DeleteAsync(It.IsAny<ImportSource>(), It.IsAny<CancellationToken>()))
					.Callback((ImportSource s, CancellationToken _) => SourceRows.Remove(s)).ReturnsAsync(true);
			}

			public ImportDataset Add(string workspaceId, ImportDatasetStatus status, int created = 0, string sourceId = null)
			{
				var dataset = new ImportDataset
				{
					Id = Guid.NewGuid().ToString(), DepartmentId = DepartmentId, WorkspaceId = workspaceId, EntityKey = "personnel", ColumnMapId = "M",
					Status = (int)status, CreatedCount = created, SourceId = sourceId, Revision = 1, UpdatedOn = DateTime.UtcNow
				};
				DatasetsByWorkspace[workspaceId].Add(dataset);
				return dataset;
			}

			public DepartmentImportService Service() => new(Workspaces.Object, Sources.Object, Mock.Of<IImportArtifactsRepository>(), Datasets.Object, Runs.Object,
				Mock.Of<IImportIssuesRepository>(), Mock.Of<IImportMessagesRepository>(), Mock.Of<IImportSupportRequestsRepository>(),
				Mock.Of<IImportColumnMapsRepository>(), Catalog, Queue.Object, Timeline.Object, Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IImportArtifactStore>(), Grants.Object);
		}

		[Test]
		public async Task Changing_the_write_mode_withdraws_approvals_and_plans_the_preview_again()
		{
			var fixture = new ImportsFixture();
			var approved = fixture.Add(WorkspaceId, ImportDatasetStatus.Approved);
			approved.ApprovedRunId = "R1";
			var needsMapping = fixture.Add(WorkspaceId, ImportDatasetStatus.NeedsMapping);

			var workspace = await fixture.Service().SetWriteModeAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, ImportWriteMode.ReviewedUpdate);

			workspace.DefaultWriteMode.Should().Be((int)ImportWriteMode.ReviewedUpdate);
			approved.Status.Should().Be((int)ImportDatasetStatus.Analyzing);
			approved.ApprovedRunId.Should().BeNull();
			needsMapping.Status.Should().Be((int)ImportDatasetStatus.NeedsMapping, "nothing was planned for it yet");
			fixture.Queue.Verify(q => q.QueueAsync(It.Is<ImportRun>(r => r.DatasetIds.Contains(approved.Id)), It.IsAny<CancellationToken>()), Times.Once);
			fixture.Timeline.Verify(t => t.RecordAsync(DepartmentId, WorkspaceId, ImportActorKind.Administrator, "ADMIN", "WriteModeChanged", It.IsAny<object>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Replace_needs_support_access_and_no_mode_changes_under_an_open_import()
		{
			var fixture = new ImportsFixture();
			var service = fixture.Service();

			// Replace is support-assisted: without Resgrid support holding Prepare or more it is refused.
			(await Refusal(() => service.SetWriteModeAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, ImportWriteMode.Replace))).Code
				.Should().Be("import_replace_needs_support");
			(await Refusal(() => service.SetWriteModeAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, (ImportWriteMode)99))).Code
				.Should().Be("import_write_mode_unavailable");

			fixture.Runs.Setup(r => r.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ImportRun> { new() { Kind = (int)ImportRunKind.Commit, Status = (int)ImportRunStatus.AwaitingChecker } });
			(await Refusal(() => service.SetWriteModeAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, ImportWriteMode.ReviewedUpdate))).Code
				.Should().Be("import_run_open");

			// Choosing the mode it already has changes nothing.
			await service.SetWriteModeAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, ImportWriteMode.CreateOnly);
			fixture.Workspaces.Verify(w => w.UpdateWithRevisionAsync(It.IsAny<ImportWorkspace>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private sealed class ApprovalFixture
		{
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();
			public readonly Mock<IImportDatasetsRepository> Datasets = new();
			public readonly Mock<IImportStagedRowsRepository> Rows = new();
			public readonly Mock<IImportIssuesRepository> Issues = new();
			public readonly Mock<IImportRunsRepository> Runs = new();
			public readonly Mock<IImportCatalog> Catalog = new();
			public readonly Mock<IImportPlanService> Plan = new();
			public readonly Mock<IDepartmentImportAccessService> Access = new();
			public readonly Mock<IImportSupportGrantsRepository> Grants = new();
			public readonly ImportWorkspace Workspace = new() { Id = WorkspaceId, DepartmentId = DepartmentId, DefaultWriteMode = (int)ImportWriteMode.CreateOnly };

			public ApprovalFixture()
			{
				Workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(Workspace);
				Runs.Setup(r => r.GetOpenForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>());
				Runs.Setup(r => r.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>());
				Access.Setup(a => a.CanManageAsync(DepartmentId, "ADMIN", It.IsAny<bool>())).ReturnsAsync(true);
				Grants.Setup(g => g.GetForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSupportGrant>());
				Catalog.Setup(c => c.InDependencyOrder()).Returns(new List<ImportEntityContract> { People });
				Rows.Setup(r => r.GetDigestInputsAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportStagedRow>());
				Issues.Setup(i => i.GetForDatasetAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportIssue>());
				Plan.Setup(p => p.ReadCapacityAsync(DepartmentId)).ReturnsAsync(new ImportCapacity { Personnel = 100, Units = 100 });
			}

			public ImportApprovalService Service() => new(Workspaces.Object, Datasets.Object, Rows.Object, Issues.Object, Runs.Object,
				Mock.Of<IImportChangeJournalRepository>(), Catalog.Object, Plan.Object, Mock.Of<IImportRunQueue>(), Mock.Of<IImportTimelineService>(), Access.Object,
				Mock.Of<IDepartmentSettingsService>(), null, Grants.Object, ImportConfigurationTests.NoConfiguration(), Mock.Of<IImportConfigurationChangesRepository>());
		}

		[Test]
		public async Task Approval_must_name_the_write_mode_the_preview_was_planned_for()
		{
			var fixture = new ApprovalFixture();

			var refusal = await Refusal(() => fixture.Service().ApproveAsync(DepartmentId, "ADMIN", WorkspaceId,
				new ImportApprovalRequest { WriteMode = ImportWriteMode.ReviewedUpdate }));

			refusal.Code.Should().Be("import_write_mode_stale");
			fixture.Plan.Verify(p => p.ComputeApprovalDigestAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<ImportWriteMode>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task Nothing_is_approved_while_the_preview_is_being_recalculated()
		{
			var fixture = new ApprovalFixture();
			fixture.Datasets.Setup(d => d.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportDataset>
			{
				new() { Id = "D1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, EntityKey = "personnel", Status = (int)ImportDatasetStatus.Analyzing },
				new() { Id = "D2", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, EntityKey = "personnel", Status = (int)ImportDatasetStatus.Ready }
			});

			var preview = await fixture.Service().GetPreviewAsync(DepartmentId, WorkspaceId);

			preview.Blockers.Should().Contain("PreviewBlockerRecalculating");
			preview.CanApprove.Should().BeFalse();
		}

		// ---- continuing an earlier import ---------------------------------------------------------------------------------

		[Test]
		public async Task Only_earlier_workspaces_that_wrote_rows_can_be_continued()
		{
			var fixture = new ImportsFixture();
			fixture.Add(EarlierId, ImportDatasetStatus.Imported, created: 40);
			fixture.Add("W0", ImportDatasetStatus.Ready);
			fixture.Add(WorkspaceId, ImportDatasetStatus.Ready);

			var candidates = await fixture.Service().GetContinuationCandidatesAsync(DepartmentId, WorkspaceId);

			candidates.Should().ContainSingle().Which.Should().BeEquivalentTo(new { WorkspaceId = EarlierId, Name = "Spring roster", DatasetsImported = 1 });
		}

		[Test]
		public async Task A_continuation_takes_the_earlier_imports_key_and_plans_this_one_again()
		{
			var fixture = new ImportsFixture();
			fixture.Add(EarlierId, ImportDatasetStatus.Imported, created: 40);
			var ready = fixture.Add(WorkspaceId, ImportDatasetStatus.Ready);
			var service = fixture.Service();

			var source = await service.SetContinuationAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, EarlierId);

			source.ProductKey.Should().Be(ImportSourceProducts.Continuation);
			source.SourceSystemKey.Should().Be("workspace:" + EarlierId);
			source.Label.Should().Be("Spring roster");
			source.ConnectorReference.Should().Be(EarlierId);
			ready.Status.Should().Be((int)ImportDatasetStatus.Analyzing);
			(await ImportSourceKeys.ForDatasetAsync(fixture.Sources.Object, ready, CancellationToken.None)).Should().Be("workspace:" + EarlierId);

			// Stopping puts this workspace's files back under its own key.
			(await service.SetContinuationAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, null)).Should().BeNull();
			fixture.SourceRows.Should().BeEmpty();
			(await ImportSourceKeys.ForDatasetAsync(fixture.Sources.Object, ready, CancellationToken.None)).Should().Be("workspace:" + WorkspaceId);
		}

		[Test]
		public async Task A_continuation_of_a_continuation_keeps_the_first_imports_key()
		{
			var fixture = new ImportsFixture();
			fixture.SourceRows.Add(new ImportSource
			{
				Id = "S1", DepartmentId = DepartmentId, WorkspaceId = EarlierId, ProductKey = ImportSourceProducts.Continuation, SourceSystemKey = "workspace:W0"
			});
			fixture.Add(EarlierId, ImportDatasetStatus.Imported, created: 12);

			var source = await fixture.Service().SetContinuationAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, EarlierId);

			source.SourceSystemKey.Should().Be("workspace:W0");
		}

		[Test]
		public async Task A_declared_source_outranks_the_continuation()
		{
			var fixture = new ImportsFixture();
			fixture.SourceRows.Add(new ImportSource { Id = "C", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, ProductKey = ImportSourceProducts.Continuation, SourceSystemKey = "workspace:W1" });
			fixture.SourceRows.Add(new ImportSource { Id = "A", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, ProductKey = "aladtec", SourceSystemKey = "aladtec:7" });

			var declared = fixture.Add(WorkspaceId, ImportDatasetStatus.Ready, sourceId: "A");

			(await ImportSourceKeys.ForDatasetAsync(fixture.Sources.Object, declared, CancellationToken.None)).Should().Be("aladtec:7");
		}

		// ---- the products the department is moving from (plan section 7.6) ------------------------------------------------

		[Test]
		public async Task Choosing_a_product_records_it_once_and_changes_no_data_sets_key()
		{
			var fixture = new ImportsFixture { Catalog = ImportCatalog.Current };
			var ready = fixture.Add(WorkspaceId, ImportDatasetStatus.Ready);
			var service = fixture.Service();

			var source = await service.AddSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, " Aladtec ");

			source.Should().BeEquivalentTo(new { ProductKey = "aladtec", Label = "Aladtec", AdapterKey = "api-extraction", PackVersion = ImportCatalog.Current.GetPack("aladtec").Version, CreatedBy = "ADMIN" });
			(await ImportSourceKeys.ForDatasetAsync(fixture.Sources.Object, ready, CancellationToken.None)).Should().Be("workspace:" + WorkspaceId,
				"choosing a product only brings its glossary; no data set declares it");
			fixture.Timeline.Verify(t => t.RecordAsync(DepartmentId, WorkspaceId, ImportActorKind.Administrator, "ADMIN", "SourceAdded", It.IsAny<object>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

			(await Refusal(() => service.AddSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "aladtec"))).Code.Should().Be("import_source_exists");
			(await Refusal(() => service.AddSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "not-a-product"))).Code.Should().Be("import_source_unknown");
			(await Refusal(() => service.AddSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, ImportSourceProducts.Continuation))).Code.Should().Be("import_source_unknown");
			fixture.SourceRows.Should().ContainSingle();

			// A product with nothing documented is still listed, so the department can say where its data comes from.
			(await service.AddSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "gartan")).AdapterKey.Should().Be("none");
		}

		[Test]
		public async Task A_chosen_product_can_be_removed_unless_a_data_set_declares_it()
		{
			var fixture = new ImportsFixture { Catalog = ImportCatalog.Current };
			var service = fixture.Service();
			var aladtec = await service.AddSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "aladtec");
			var d4h = await service.AddSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "d4h");
			fixture.SourceRows.Add(new ImportSource { Id = "C", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, ProductKey = ImportSourceProducts.Continuation });
			aladtec.Id = "A";
			d4h.Id = "D";
			fixture.Add(WorkspaceId, ImportDatasetStatus.Ready, sourceId: "D");

			await service.RemoveSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "A");

			fixture.SourceRows.Select(s => s.Id).Should().BeEquivalentTo(new[] { "D", "C" });
			(await Refusal(() => service.RemoveSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "D"))).Code.Should().Be("import_source_in_use");
			(await Refusal(() => service.RemoveSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "C"))).Code.Should().Be("import_source_unknown",
				"the continuation has its own control, which plans the data sets again");
			fixture.WorkspaceRows[WorkspaceId].Status = (int)ImportWorkspaceStatus.Cancelled;
			(await Refusal(() => service.RemoveSourceAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "D"))).Code.Should().Be("import_workspace_closed");
		}

		[Test]
		public async Task A_workspace_that_wrote_rows_cannot_start_or_stop_continuing()
		{
			var fixture = new ImportsFixture();
			fixture.Add(EarlierId, ImportDatasetStatus.Imported, created: 40);
			fixture.Add(WorkspaceId, ImportDatasetStatus.Imported, created: 3);
			var service = fixture.Service();

			(await Refusal(() => service.SetContinuationAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, EarlierId))).Code
				.Should().Be("import_continuation_locked");
			fixture.SourceRows.Should().BeEmpty();
		}

		[Test]
		public async Task An_import_that_wrote_nothing_or_this_one_cannot_be_continued()
		{
			var fixture = new ImportsFixture();
			fixture.Add("W0", ImportDatasetStatus.Ready);
			var service = fixture.Service();

			(await Refusal(() => service.SetContinuationAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, "W0"))).Code
				.Should().Be("import_continuation_unknown");
			(await Refusal(() => service.SetContinuationAsync(DepartmentId, "ADMIN", ImportActorKind.Administrator, WorkspaceId, WorkspaceId))).Code
				.Should().Be("import_continuation_unknown");
		}

		// ---- references by an earlier import's Source IDs (L6) ---------------------------------------------------------

		[Test]
		public async Task A_continued_files_references_reach_records_the_earlier_import_wrote()
		{
			var cipher = new ImportArtifactCipher();
			var key = cipher.NewDataKey();
			var codec = new ImportStagingCodec(cipher);
			var dataset = new ImportDataset
			{
				Id = "D1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, ArtifactId = "A1", EntityKey = "personnel", ColumnMapId = "M1",
				DestinationConfirmed = true, DetectionConfidence = 100, Status = (int)ImportDatasetStatus.Analyzing, Revision = 1
			};
			var rows = new[] { new[] { "ID", "First", "Last", "Station" }, new[] { "P1", "Alex", "Rivera", "ST-1" }, new[] { "P2", "Sam", "Lee", "ST-9" } }
				.Select((cells, i) => new ImportStagedRow
				{
					Id = "R" + (i + 1), DepartmentId = DepartmentId, DatasetId = "D1", RowNumber = i + 1,
					SourcePayload = codec.Seal(key, ImportStagingCodec.Binding("D1", i + 1, "source"), cells)
				}).ToList();
			var map = new ImportColumnMapBody
			{
				SourceIdentityColumn = "ID",
				Columns = new List<ImportColumnBinding>
				{
					new() { SourceColumn = "ID", Kind = (int)ImportColumnBindingKind.SourceIdentity, FieldKey = "source_id" },
					new() { SourceColumn = "First", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "first_name" },
					new() { SourceColumn = "Last", Kind = (int)ImportColumnBindingKind.Field, FieldKey = "last_name" },
					new() { SourceColumn = "Station", Kind = (int)ImportColumnBindingKind.Reference, FieldKey = "group", ReferencesEntity = "groups" }
				}
			};

			var datasets = new Mock<IImportDatasetsRepository>();
			datasets.Setup(d => d.GetByIdAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(dataset);
			datasets.Setup(d => d.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportDataset> { dataset });
			var artifacts = new Mock<IImportArtifactsRepository>();
			artifacts.Setup(a => a.GetByIdAsync(DepartmentId, "A1", It.IsAny<CancellationToken>())).ReturnsAsync(new ImportArtifact { Id = "A1", WrappedDataKey = "wrapped" });
			var keys = new Mock<IImportKeyService>();
			keys.Setup(k => k.UnwrapAsync(DepartmentId, "wrapped")).ReturnsAsync(key);
			var columnMaps = new Mock<IImportColumnMapsRepository>();
			columnMaps.Setup(c => c.GetByIdAsync(DepartmentId, "M1", It.IsAny<CancellationToken>())).ReturnsAsync(new ImportColumnMap { Id = "M1", Body = JsonConvert.SerializeObject(map) });
			var staged = new Mock<IImportStagedRowsRepository>();
			staged.Setup(r => r.GetPageAsync(DepartmentId, "D1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, string _, int after, int take, CancellationToken _) => rows.Where(r => r.RowNumber > after).Take(take).ToList());
			staged.Setup(r => r.GetDigestInputsAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(() => rows.ToList());
			var issues = new Mock<IImportIssuesRepository>();
			issues.Setup(i => i.GetForDatasetAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportIssue>());
			var inserted = new List<ImportIssue>();
			issues.Setup(i => i.InsertManyAsync(It.IsAny<IReadOnlyList<ImportIssue>>(), It.IsAny<CancellationToken>()))
				.Callback((IReadOnlyList<ImportIssue> list, CancellationToken _) => inserted.AddRange(list)).Returns(Task.CompletedTask);
			var catalog = new Mock<IImportCatalog>();
			catalog.Setup(c => c.GetContract("personnel")).Returns(People);
			catalog.Setup(c => c.GetContract("groups")).Returns(new ImportEntityContract { Key = "groups", NaturalKey = new List<string> { "name" } });
			var sources = new Mock<IImportSourcesRepository>();
			sources.Setup(s => s.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSource>
			{
				new() { Id = "C", WorkspaceId = WorkspaceId, ProductKey = ImportSourceProducts.Continuation, SourceSystemKey = "workspace:" + EarlierId }
			});
			var externalIds = new Mock<IImportExternalIdsRepository>();
			externalIds.Setup(e => e.GetForSourceIdsAsync(DepartmentId, "workspace:" + EarlierId, "groups", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ImportExternalId>
				{
					new() { SourceSystemKey = "workspace:" + EarlierId, EntityType = "groups", SourceId = "ST-1", TargetKey = "42", State = (int)ImportExternalIdState.Written },
					new() { SourceSystemKey = "workspace:" + EarlierId, EntityType = "groups", SourceId = "ST-9", TargetKey = "43", State = (int)ImportExternalIdState.Released }
				});
			var valueMaps = new Mock<IImportValueMapsRepository>();
			valueMaps.Setup(v => v.GetForDatasetAsync(DepartmentId, "D1", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportValueMap>());
			var groups = new Mock<IImportTargetWriter>();
			groups.SetupGet(w => w.EntityKey).Returns("groups");
			groups.Setup(w => w.FindExistingAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string>());

			var validation = new ImportValidationService(Mock.Of<IImportWorkspacesRepository>(), datasets.Object, artifacts.Object, sources.Object, staged.Object,
				issues.Object, columnMaps.Object, valueMaps.Object, catalog.Object, keys.Object, cipher, Mock.Of<IDepartmentsService>(),
				Mock.Of<IAddressService>(), Mock.Of<IPhoneNumberProcesserProvider>(), new[] { groups.Object }, externalIds.Object);

			await validation.ValidateDatasetAsync(DepartmentId, "D1", null);

			var alex = codec.Open<ImportCanonicalPayload>(key, ImportStagingCodec.Binding("D1", 2, "canonical"), rows[1].CanonicalPayload);
			alex.References["group"].Should().Equal(ImportReferenceToken.ForExisting("42"));
			rows[1].ValidationState.Should().Be((int)ImportRowValidationState.Valid);
			// A released claim (the record was rolled back) is not a target any more.
			rows[2].ValidationState.Should().Be((int)ImportRowValidationState.Blocked);
			inserted.Should().Contain(i => i.Code == "IMP-REF-UNRESOLVED" && i.RowNumber == 3);
		}

		private static async Task<DepartmentImportException> Refusal(Func<Task> action)
		{
			var thrown = await FluentActions.Awaiting(action).Should().ThrowAsync<DepartmentImportException>();
			return thrown.Which;
		}
	}
}
