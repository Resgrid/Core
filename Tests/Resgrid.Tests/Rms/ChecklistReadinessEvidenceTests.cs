using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Records;
using Resgrid.Services.Records.Evidence;

namespace Resgrid.Tests.Rms
{
	public partial class RecordsEvidenceServiceTests
	{
		[Test]
		public async Task Readiness_PDF_and_manifest_are_immutable_classified_evidence_after_source_edit_purge_and_revision_binding()
		{
			var source = new ReadinessEvidenceManifestV1 { DepartmentId = Dept, CallId = 501, CallUtc = DateTime.UtcNow.AddHours(-2), GeneratedUtc = DateTime.UtcNow,
				CoverageStartUtc = DateTime.UtcNow.AddDays(-30), CoverageEndUtc = DateTime.UtcNow.AddHours(-2),
				Checklists = new List<ChecklistReportEntry> { new() { OccurrenceId = "source-occurrence", VersionId = "pinned-version", Version = 3, Name = "Original evidence", Target = new ChecklistTarget { Id = "1", Type = ChecklistTargetType.Unit, Name = "Engine 1" } } } };
			var checklists = new Mock<IChecklistsService>(); checklists.Setup(c => c.GetReadinessPacketForCallAsync(It.IsAny<ChecklistActor>(), 501, 30)).ReturnsAsync(() => source);
			source.WorkOrders.Add(new Resgrid.Model.WorkOrders.ReadinessWorkOrderEvidence { WorkOrderId = 19, Revision = 3, SnapshotId = 29, Title = "Pinned maintenance", UnitId = 1, ActiveSafetyHoldIds = new() { 8 } });
			var access = new Mock<IReadinessAccessService>(); access.Setup(a => a.CanUseChecklistsAsync(Dept)).ReturnsAsync(false);
			var grant = Mock.Of<IProtectedGrantContext>(g => g.UserId == "author" && g.GrantToken == "synthetic-grant" && !g.IsWorkloadCaller);
			var pdf = new Mock<IPdfProvider>(); pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Returns(Encoding.ASCII.GetBytes("%PDF-1.4 synthetic evidence"));
			var adapter = new ReadinessPacketEvidenceAdapter(checklists.Object, access.Object, grant, pdf.Object);
			var service = new RecordsEvidenceService(_store.EvidenceRepo.Object, _store.RecordsRepo.Object, _incidents.ReportsRepo.Object,
				_store.AuditsRepo.Object, _store.UnitOfWork.Object, new[] { adapter }, _authorization.Object, Mock.Of<ICallsService>(), _references.Object,
				new PassthroughRecordsProtection(), new DomainEventOutboxService(_store.OutboxRepo.Object, Mock.Of<IEventAggregator>()), Mock.Of<Resgrid.Model.Repositories.IInventoryStore>());
			var artifact = await service.CaptureAsync(Request(RmsEvidenceKind.ReadinessPacket));
			artifact.Classification.Should().Be((int)RmsEvidenceClassification.Restricted);
			artifact.ManifestJson.Should().Contain("Original evidence").And.Contain("PdfSha256").And.Contain("ManifestSha256");
			artifact.ManifestJson.Should().Contain("Pinned maintenance").And.Contain("SnapshotId");
			var json = artifact.ManifestJson; var checksum = artifact.Checksum;
			await service.BindToRevisionAsync(Dept, _record.RmsOperationalRecordId, "finalized-revision");
			source.Checklists[0].Name = "Later edited source"; source.Checklists.Clear();
			source.WorkOrders.Clear();
			var retained = (await service.GetForRecordAsync(Dept, _record.RmsOperationalRecordId, "finalized-revision"))[0];
			retained.ManifestJson.Should().Be(json); retained.Checksum.Should().Be(checksum); (await service.VerifyAsync(Dept, retained.RmsEvidenceArtifactId)).Should().BeTrue();
			checklists.Verify(c => c.GetReadinessPacketForCallAsync(It.Is<ChecklistActor>(a => a.UserId == "author" && a.GrantToken == "synthetic-grant"), 501, 30), Times.Exactly(2));
			var request = Request(RmsEvidenceKind.ReadinessPacket); request.CapturedByUserId = "another-user";
			await FluentActions.Awaiting(() => adapter.CaptureAsync(request)).Should().ThrowAsync<UnauthorizedAccessException>();
		}
	}
}
