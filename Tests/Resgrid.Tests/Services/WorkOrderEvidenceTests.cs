using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderP2M1Tests
    {
        [Test]
        public async Task Reopen_preserves_completed_and_verified_snapshots()
        {
            var id=await Assigned();var row=await _service.GetAsync(_actor,id);
            await _service.AcceptAssignmentAsync(_actor,id,row.Order.Revision);
            await Transition(id,WorkOrderStatus.InProgress);await Transition(id,WorkOrderStatus.Completed);
            await Transition(id,WorkOrderStatus.Closed,evidence:"SYNTHETIC-VERIFICATION");
            row=await Transition(id,WorkOrderStatus.Accepted,reason:"New defect");
            row.Input.Content.Resolution.Should().BeNull();row.Input.Content.VerificationEvidence.Should().BeNull();
            row.Activities.Should().Contain(a=>a.NewStatus==5 && a.Snapshot.Resolution=="Repaired");
            row.Activities.Should().Contain(a=>a.NewStatus==6 && a.Snapshot.VerificationEvidence=="SYNTHETIC-VERIFICATION");
            row.Activities.Should().Contain(a=>a.Type==WorkOrderActivityType.Assigned && a.AssignedToUserId=="manager");
        }
        [Test]
        public async Task Evidence_requires_clean_scan_preserves_withdrawn_bytes_and_checks_integrity()
        {
            var row=await _service.CreateAsync(_actor,Input());var id=row.Order.Id;
            var bytes=Encoding.ASCII.GetBytes("%PDF-1.4\n% synthetic fixture\n%%EOF");
            _scanner.Setup(s=>s.ScanAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<byte[]>(),It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult {State=RmsAttachmentScanState.Rejected});
            await FluentActions.Awaiting(()=>_service.AddFileAsync(_actor,id,1,"synthetic.pdf","application/pdf",bytes)).Should().ThrowAsync<WorkOrderException>().Where(e=>e.Code=="ScanRequired");
            _store.All<WorkOrderFile>().Should().BeEmpty();
            _scanner.Setup(s=>s.ScanAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<byte[]>(),It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult {State=RmsAttachmentScanState.Clean});
            await _service.AddFileAsync(_actor,id,1,"synthetic.pdf","application/pdf",bytes);row=await _service.GetAsync(_actor,id);
            var file=row.Files.Single();(await _service.GetFileAsync(_actor,file.Id)).Data.Should().Equal(bytes);
            await _service.WithdrawFileAsync(_actor,id,file.Id,row.Order.Revision,"Superseded");
            (await _service.GetFileAsync(_actor,file.Id)).Data.Should().Equal(bytes);
            var stored=await _store.GetAsync<WorkOrderFile>(77,file.Id);stored.Data[0]=0;await _store.WriteAsync(stored);
            await FluentActions.Awaiting(()=>_service.GetFileAsync(_actor,file.Id)).Should().ThrowAsync<WorkOrderException>().Where(e=>e.Code=="IntegrityFailed");
        }
    }
}
