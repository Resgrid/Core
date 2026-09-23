using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    [TestFixture]
    public sealed class WorkOrderNotificationTests
    {
        [TestCase(false, false),TestCase(true, false),TestCase(false, true),TestCase(true, true)]
        [TestCase(false, false, true), TestCase(false, true, true)]
        [TestCase(false, false, false, true)]
        public async Task Current_role_members_or_triage_managers_receive_localized_metadata_only_once(bool triage, bool revoked, bool multiple = false, bool hidden = false)
        {
            var store=new Mock<IWorkOrderRepository>(); var auth=new Mock<IWorkOrderAuthorizationService>();
            var access=new Mock<IReadinessAccessService>();access.Setup(a=>a.CanUseMaintenanceAsync(77)).ReturnsAsync(true);
            var uow=new Mock<IUnitOfWork>();uow.Setup(u=>u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DbConnection)null);
            var communication=new Mock<ICommunicationService>();communication.SetReturnsDefault(Task.FromResult(true));
            var departments=new Mock<IDepartmentsService>();departments.Setup(d=>d.GetDepartmentByIdAsync(77,true)).ReturnsAsync(new Department {DepartmentId=77});
            departments.Setup(d=>d.GetDepartmentMemberAsync(It.IsAny<string>(),77,true)).ReturnsAsync((string user,int department,bool fresh)=>new DepartmentMember {DepartmentId=department,UserId=user,IsDisabled=revoked && user!="requester",IsHidden=hidden && user=="tech1"});
            departments.Setup(d=>d.GetAllMembersForDepartmentUnlimitedAsync(77,true)).ReturnsAsync(new List<DepartmentMember> {
                new DepartmentMember {DepartmentId=77,UserId="requester"},new DepartmentMember {DepartmentId=77,UserId="manager"},
                new DepartmentMember {DepartmentId=77,UserId="tech1",IsHidden=hidden},new DepartmentMember {DepartmentId=77,UserId="tech2"}
            });
            auth.Setup(a=>a.CanManageAsync(It.IsAny<ChecklistActor>(),It.IsAny<int?>())).ReturnsAsync((ChecklistActor a,int? g)=>a.UserId=="manager");
            auth.Setup(a=>a.RecipientsAsync(77,It.IsAny<WorkOrder>())).ReturnsAsync(triage ? new List<string>() : new List<string>{"tech1","tech2"});
            auth.Setup(a=>a.ScopeAsync(It.IsAny<ChecklistActor>())).ReturnsAsync((ChecklistActor a)=>new WorkOrderReadScope {UserId=a.UserId,RoleIds=a.UserId.StartsWith("tech")?new[]{3}:Array.Empty<int>()});
            var profiles=new Mock<IUserProfileService>();profiles.Setup(p=>p.GetProfileByUserIdAsync(It.IsAny<string>(), false)).ReturnsAsync(new UserProfile {Language="fr"});
            var row=new WorkOrder {Id="00000000-0000-0000-0000-000000000019",NumberYear=2026,NumberSequence=19,DepartmentId=77,CreatedBy="requester",Status=triage?0:2,AssignedToRoleId=triage?null:3,IsProtected=true,Content="SYNTHETIC-PHI-CANARY"};
            if (multiple) { row.AssignedToRoleId = null; row.AssignedToUserIds = new() { "tech1" }; row.AssignedToRoleIds = new() { 2, 3 }; }
            store.Setup(s=>s.GetAsync<WorkOrder>(77,"00000000-0000-0000-0000-000000000019",true)).ReturnsAsync(row);
            var states=new Dictionary<string,int>();
            store.Setup(s=>s.ClaimNotificationAsync(It.IsAny<WorkOrderNotification>(),It.IsAny<DateTime>())).ReturnsAsync((WorkOrderNotification n,DateTime now)=>states.TryGetValue(n.UserId,out var state)?state:1);
            store.Setup(s=>s.FinishNotificationAsync(It.IsAny<WorkOrderNotification>(),It.IsAny<int>(),It.IsAny<DateTime>())).ReturnsAsync((WorkOrderNotification n,int state,DateTime now)=>{states[n.UserId]=state;return true;});
            var service=new WorkOrderNotificationService(store.Object,auth.Object,access.Object,uow.Object,communication.Object,departments.Object,Mock.Of<IDepartmentSettingsService>(),profiles.Object);
            var entry=new DomainEventOutboxEntry {DepartmentId=77,ProducerSubsystem="WorkOrders",AggregateId="00000000-0000-0000-0000-000000000019",EventId=Guid.NewGuid().ToString(),TriggerEventType=triage?70:72};
            await service.DispatchAsync(entry); await service.DispatchAsync(entry);
            departments.Verify(d=>d.GetAllMembersForDepartmentUnlimitedAsync(77,true),Times.Exactly(2));
            auth.Verify(a=>a.RecipientsAsync(77,It.IsAny<WorkOrder>()),Times.Exactly(2));
            var sends=communication.Invocations.Where(i=>i.Method.Name=="SendNotificationAsync").ToList();
            sends.Select(i=>(string)i.Arguments[0]).Should().BeEquivalentTo(revoked?new[]{"requester"}:triage?new[]{"requester","manager"}:hidden?new[]{"requester","tech2"}:new[]{"requester","tech1","tech2"});
            foreach(var send in sends) ((string)send.Arguments[2]).Should().StartWith("WO-2026-000019: ").And.Contain("nécessite votre attention")
                .And.NotContain("CANARY").And.NotContain("grant").And.NotContain("http").And.NotContain("/User/").And.NotContain(row.Id);
            // The order id reaches the device only as the push event code the Responder app deep-links on.
            foreach(var send in sends) { send.Arguments.Should().HaveCount(9); ((string)send.Arguments[8]).Should().Be("NWO:00000000-0000-0000-0000-000000000019"); ((bool)send.Arguments[7]).Should().BeFalse(); }
            access.Setup(a=>a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
            entry.EventId=Guid.NewGuid().ToString();await service.DispatchAsync(entry);
            communication.Invocations.Count.Should().Be(sends.Count);
        }
    }
}
