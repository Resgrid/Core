using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;
using Resgrid.Services.Records.Evidence;

namespace Resgrid.Tests.Services
{
    public partial class ChecklistWorkflowTests
    {
        [Test]
        public async Task P2M4_packet_emits_provenance_and_rechecks_work_order_disclosure_after_PDF_generation()
        {
            await SeedReportMonth();
            var reports = new Mock<IWorkOrderReportingService>();
            var section = new ReadinessWorkOrderSection { HistoryUnavailable = true, RestrictedScope = true, Items = { new() { WorkOrderId = 19, Revision = 3, SnapshotId = 99, UnitId = 1, Title = "Synthetic immutable maintenance", SourceActivityId = 27 } } };
            reports.Setup(r => r.ReadinessEvidenceAsync(It.IsAny<ChecklistActor>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int[]>(), It.IsAny<string[]>())).ReturnsAsync(section);
            PacketService(null, workOrders: reports.Object); _access.Setup(a => a.CanUseChecklistsAsync(77)).ReturnsAsync(false);
            var packet = await _service.GetReadinessPacketForCallAsync(_actor, 101);
            packet.WorkOrders.Should().ContainSingle(); packet.UnavailableSources.Should().Contain("HistoricalWorkOrdersUnavailable").And.Contain("RestrictedWorkOrders").And.NotContain("WorkOrdersUnavailable");
            reports.Verify(r => r.ReadinessEvidenceAsync(It.Is<ChecklistActor>(a => a.UserId == _actor.UserId && a.DepartmentId == 77), packet.CoverageStartUtc, packet.CallUtc, It.Is<int[]>(ids => ids.SequenceEqual(new[] { 1 })), It.IsAny<string[]>()), Times.Once);
            var pdf = new Mock<IPdfProvider>(); pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Returns((string html) =>
            {
                html.Should().Contain("Synthetic immutable maintenance");
                reports.Setup(r => r.ReadinessEvidenceAsync(It.IsAny<ChecklistActor>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int[]>(), It.IsAny<string[]>())).ReturnsAsync(new ReadinessWorkOrderSection());
                return Encoding.ASCII.GetBytes("%PDF-1.4 synthetic");
            });
            var grant = Mock.Of<IProtectedGrantContext>(g => g.UserId == _actor.UserId && !g.IsWorkloadCaller);
            var adapter = new ReadinessPacketEvidenceAdapter(_service, _access.Object, grant, pdf.Object);
            await FluentActions.Awaiting(() => adapter.CaptureAsync(new RecordEvidenceCaptureRequest { DepartmentId = 77, CallId = 101, CapturedByUserId = _actor.UserId })).Should().ThrowAsync<ChecklistException>();
            reports.Setup(r => r.ReadinessEvidenceAsync(It.IsAny<ChecklistActor>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int[]>(), It.IsAny<string[]>())).ThrowsAsync(new WorkOrderException(403, "ProtectedDataRequired"));
            (await FluentActions.Awaiting(() => _service.GetReadinessPacketForCallAsync(_actor, 101)).Should().ThrowAsync<ChecklistException>()).Which.Message.Should().StartWith("Unlock");
        }
    }
}
