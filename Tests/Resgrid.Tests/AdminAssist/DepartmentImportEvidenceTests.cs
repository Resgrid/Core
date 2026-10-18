using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>Import &amp; Migration evidence is scalar, department-scoped and zero for a department that never imported.</summary>
	[TestFixture]
	public class DepartmentImportEvidenceTests
	{
		private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

		private sealed class Fixture
		{
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();
			public readonly Mock<IImportDatasetsRepository> Datasets = new();
			public readonly Mock<IImportIssuesRepository> Issues = new();
			public readonly Mock<IImportRunsRepository> Runs = new();
			public readonly Mock<IImportReconciliationsRepository> Reconciliations = new();
			public readonly Mock<IImportArtifactsRepository> Artifacts = new();
			public readonly Mock<IImportExternalIdsRepository> ExternalIds = new();
			public readonly Mock<IDepartmentsService> Departments = new();

			public Fixture(params ImportWorkspace[] workspaces)
			{
				Workspaces.Setup(w => w.GetAllForDepartmentAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(workspaces.ToList());
				Datasets.Setup(d => d.GetAllForWorkspaceAsync(7, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportDataset>());
				Issues.Setup(i => i.GetAllForWorkspaceAsync(7, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportIssue>());
				Runs.Setup(r => r.GetAllForWorkspaceAsync(7, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>());
				Artifacts.Setup(a => a.GetAllForWorkspaceAsync(7, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportArtifact>());
				Reconciliations.Setup(r => r.GetForRunAsync(7, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportReconciliation>());
				ExternalIds.Setup(e => e.GetForRunAsync(7, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportExternalId>());
				Departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(7)).ReturnsAsync(new List<DepartmentMember>());
			}

			public async Task<Dictionary<string, decimal?>> ReadAsync()
			{
				var source = new DepartmentImportEvidenceSource(Workspaces.Object, Datasets.Object, Issues.Object, Runs.Object, Reconciliations.Object, Artifacts.Object,
					ExternalIds.Object, Departments.Object);
				var facts = await source.ReadAsync(new AdminAssistActor(7, "admin"), Now, CancellationToken.None);
				Assert.That(facts.Select(f => f.Id), Is.EquivalentTo(source.EvidenceIds));
				Assert.That(facts.All(f => f.State == EvidenceState.Known && f.Number.HasValue));
				return facts.ToDictionary(f => f.Id, f => f.Number);
			}
		}

		[Test]
		public async Task A_department_that_never_imported_reads_zero_everywhere()
		{
			var facts = await new Fixture().ReadAsync();
			Assert.That(facts.Values.All(v => v == 0), Is.True);
		}

		[Test]
		public async Task Stuck_issues_unexplained_differences_and_old_dormant_people_are_counted()
		{
			var workspace = new ImportWorkspace { Id = "W1", DepartmentId = 7, Status = (int)ImportWorkspaceStatus.Reviewing };
			var fixture = new Fixture(workspace);
			fixture.Issues.Setup(i => i.GetAllForWorkspaceAsync(7, "W1", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportIssue>
			{
				new() { Status = (int)ImportIssueStatus.Open, Severity = (int)ImportIssueSeverity.Blocking, CreatedOn = Now.AddDays(-8) },
				new() { Status = (int)ImportIssueStatus.Open, Severity = (int)ImportIssueSeverity.Blocking, CreatedOn = Now.AddDays(-1) },
				new() { Status = (int)ImportIssueStatus.Open, Severity = (int)ImportIssueSeverity.Warning, CreatedOn = Now.AddDays(-30) }
			});
			fixture.Runs.Setup(r => r.GetAllForWorkspaceAsync(7, "W1", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>
			{
				new() { Id = "R1", Kind = (int)ImportRunKind.Commit, Status = (int)ImportRunStatus.Completed, CompletedOn = Now.AddDays(-20) }
			});
			fixture.Reconciliations.Setup(r => r.GetForRunAsync(7, "R1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ImportReconciliation> { new() { State = (int)ImportReconciliationState.Unexplained } });
			fixture.ExternalIds.Setup(e => e.GetForRunAsync(7, "R1", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportExternalId>
			{
				new() { EntityType = "personnel", State = (int)ImportExternalIdState.Written, TargetKey = "U1" },
				new() { EntityType = "personnel", State = (int)ImportExternalIdState.Written, TargetKey = "U2" }
			});
			fixture.Departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(7)).ReturnsAsync(new List<DepartmentMember>
			{
				new() { UserId = "U1", ImportActivationState = (int)ImportActivationState.Dormant },
				new() { UserId = "U2", ImportActivationState = (int)ImportActivationState.Activated },
				new() { UserId = "OTHER", ImportActivationState = (int)ImportActivationState.Dormant }
			});

			var facts = await fixture.ReadAsync();

			Assert.That(facts["importWorkspaceOpen"], Is.EqualTo(1));
			Assert.That(facts["importBlockingIssueCount"], Is.EqualTo(1));
			Assert.That(facts["importUnreconciledRunCount"], Is.EqualTo(1));
			Assert.That(facts["importDormantMemberCount"], Is.EqualTo(1));
			Assert.That(facts["importOldestDormantDays"], Is.EqualTo(20));
		}
	}
}
