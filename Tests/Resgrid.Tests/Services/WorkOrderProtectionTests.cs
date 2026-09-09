using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Services
{
    public partial class DepartmentDataMigrationEngineTests
    {
        [Test]
        public async Task Work_order_catalog_upgrade_is_retry_safe_and_offboards_all_text_and_binary_fields()
        {
            _bulk.Seed("Calls","CallId");
            var bindings=AdpTableBindings.ForVersionRange(new ProtectedFieldCatalog(),17,18);
            bindings.Should().HaveCount(5); bindings.Sum(b=>b.Columns.Count).Should().Be(6);
            var bytes=Encoding.UTF8.GetBytes("SYNTHETIC-PHI-FILE");
            foreach(var b in bindings)
            {
                b.PkIsNumeric.Should().BeTrue();
                var row=Row(("Id",19),("DepartmentId",DeptId),("IsProtected",false),("AssignedToUserId","routing-member"));
                foreach(var c in b.Columns) row[c.ColumnName]=c.StorageKind==ProtectedFieldStorageKind.Binary ? bytes : "SYNTHETIC-PHI-CANARY";
                _bulk.Seed(b.TableName,b.PkColumn,row);
            }
            var upgrade=Context(DepartmentDataProtectionMigrationKind.CatalogUpgrade);upgrade.FromCatalogVersion=17;upgrade.CatalogVersion=18;
            (await _engine.RunEncryptionNightAsync(upgrade,CancellationToken.None)).Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables);
            var envelopes=new Dictionary<string,string>();
            foreach(var b in bindings)
            {
                var row=_bulk.Table(b.TableName).Single(); var content=(string)row["Content"];
                content.Should().StartWith("rgdp:").And.NotContain("CANARY"); envelopes[b.TableName]=content;
                _crypto.DecryptText(_dek,content,DeptId,b.Columns.First(c=>c.ColumnName=="Content").FieldId,"19").Should().Be("SYNTHETIC-PHI-CANARY");
                row["AssignedToUserId"].Should().Be("routing-member");
                if(b.TableName=="WorkOrderFiles") ProtectedReadService.IsBinaryEnveloped((byte[])row["Data"]).Should().BeTrue();
            }
            (await _engine.RunEncryptionNightAsync(upgrade,CancellationToken.None)).Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables);
            foreach(var b in bindings) _bulk.Table(b.TableName).Single()["Content"].Should().Be(envelopes[b.TableName]);
            var offboarding=Context(DepartmentDataProtectionMigrationKind.Offboarding);offboarding.CatalogVersion=18;
            (await _engine.RunDecryptionNightAsync(offboarding,CancellationToken.None)).Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables);
            foreach(var b in bindings) _bulk.Table(b.TableName).Single()["Content"].Should().Be("SYNTHETIC-PHI-CANARY");
            ((byte[])_bulk.Table("WorkOrderFiles").Single()["Data"]).Should().Equal(bytes);
        }
    }
    public partial class ChecklistEventDeliveryTests
    {
        [TestCase(70),TestCase(71),TestCase(72)]
        public async Task Work_order_events_encrypt_history_and_replay_only_safe_Workflow_routing(int trigger)
        {
            var notifications=new Mock<IWorkOrderNotificationService>();
            _outbox=new DomainEventOutboxService(_store.OutboxRepo.Object,_bus,new Lazy<IProtectedProjectionService>(()=>_projection),_history.Lazy,new Lazy<IWorkOrderNotificationService>(()=>notifications.Object));
            _policy.Setup(p=>p.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
            var entry=await _outbox.EnqueueAsync(42,"WorkOrders",new DomainEventEnvelope {
                AggregateType="WorkOrder",AggregateId="19",Trigger=(WorkflowTriggerEventType)trigger,EventName=((WorkflowTriggerEventType)trigger).ToString(),
                Payload=new {WorkOrderId=19,Revision=3,Status=2,Priority=3,Title="SYNTHETIC-PHI-CANARY",AssignedToUserId="PII-CANARY",AssignedToRoleId=4,Data=new byte[]{1,2,3}}
            });
            entry.PayloadJson.Should().StartWith("rgdp:").And.NotContain("CANARY");
            var original=entry.PayloadJson; var delivered=new List<string>();
            _bus.AddAsyncListener<DomainEventDispatchedEvent>(e=>{delivered.Add(e.PayloadJson);return Task.CompletedTask;});
            notifications.Setup(n=>n.DispatchAsync(It.IsAny<DomainEventOutboxEntry>())).ThrowsAsync(new InvalidOperationException("SYNTHETIC-PHI-CANARY"));
            (await _outbox.DispatchAfterCommitAsync(new[]{entry.DomainEventOutboxId})).Should().Be(0);
            entry.LastError.Should().NotContain("CANARY");
            notifications.Setup(n=>n.DispatchAsync(It.IsAny<DomainEventOutboxEntry>())).Returns(Task.CompletedTask);
            (await _outbox.DispatchAfterCommitAsync(new[]{entry.DomainEventOutboxId})).Should().Be(1);
            delivered.Should().HaveCount(2);
            foreach(var json in delivered) { json.Should().NotContain("CANARY").And.NotContain("AQID").And.NotContain("rgdp:"); JObject.Parse(json)["WorkOrderId"].Value<int>().Should().Be(19); }
            entry.PayloadJson.Should().Be(original);
            _history.Broker.Invocations.Should().OnlyContain(i=>i.Method.Name=="EncryptAsync");
        }
    }
}
