using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistDatabaseTests
	{
		[Test, Order(98)]
		public async Task Concurrent_offline_start_and_submit_replays_create_one_completion_and_one_event_in_both_databases()
		{
			var actor = new ChecklistActor { DepartmentId = 77, UserId = "offline-author" }; var events = new ConcurrentBag<DomainEventEnvelope>();
			var auth = new Mock<IChecklistAuthorizationService>(); auth.Setup(a => a.CanManageAsync(It.IsAny<ChecklistActor>())).ReturnsAsync(true);
			auth.Setup(a => a.CanReadAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistCompletion>())).ReturnsAsync(true);
			auth.Setup(a => a.TargetAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistTargetType>(), It.IsAny<string>())).ReturnsAsync(new ChecklistTarget { Type = ChecklistTargetType.Department, Id = "77", Name = "Synthetic department" });
			var access = new Mock<IReadinessAccessService>(); access.Setup(a => a.CanUseChecklistsAsync(77)).ReturnsAsync(true);
			var read = new Mock<IProtectedReadService>(); read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
			var write = new Mock<IProtectedWriteService>(); write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed()));
			var audit = new Mock<IAuditLogsRepository>(); audit.Setup(a => a.InsertAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((AuditLog a, CancellationToken c, bool f) => { a.AuditLogId = 1; return a; });
			var outbox = new Mock<IDomainEventOutboxService>(); outbox.Setup(o => o.EnqueueAsync(77, "Checklists", It.IsAny<DomainEventEnvelope>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, string p, DomainEventEnvelope e, CancellationToken c) => { events.Add(e); return new DomainEventOutboxEntry { DomainEventOutboxId = 1 }; });
			var connections = Connections(); using var first = new UnitOfWork(connections); using var second = new UnitOfWork(connections);
			ChecklistsService Service(UnitOfWork unit) => new ChecklistsService(Repository(connections, unit), auth.Object, access.Object, unit, audit.Object, outbox.Object,
				new Lazy<IProtectedReadService>(() => read.Object), new Lazy<IProtectedWriteService>(() => write.Object), Mock.Of<IRecordAttachmentScanner>());
			var a = Service(first); var b = Service(second); var form = new ChecklistForm { Name = "Concurrent offline test", Sections = { new ChecklistSection { Name = "Checks", Items = { new ChecklistItem { Name = "Ready" } } } } };
			var definition = await a.SaveDefinitionAsync(actor, null, 0, form); await a.PublishAsync(actor, definition, 1);
			var version = (await a.GetDefinitionAsync(actor, definition)).Definition.CurrentVersionId; var id = Guid.NewGuid().ToString();
			(await Task.WhenAll(a.StartPinnedAsync(actor, definition, version, "77", id), b.StartPinnedAsync(actor, definition, version, "77", id))).Should().OnlyContain(v => v == id);
			ChecklistRunInput Input() => new ChecklistRunInput { Revision = 1, Answers = { new ChecklistAnswer { ItemId = form.Sections[0].Items[0].Id, Status = ChecklistAnswerStatus.Answered, Value = "pass" } } };
			(await Task.WhenAll(a.SaveRunAsync(actor, id, Input(), true), b.SaveRunAsync(actor, id, Input(), true))).Should().OnlyContain(v => v == 2);
			(await Repository(connections, first).ListAsync<ChecklistCompletion>(77, definition)).Should().ContainSingle();
			(await Repository(connections, first).ListAsync<ChecklistOccurrence>(77, definition)).Should().ContainSingle();
			events.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ChecklistCompleted);
		}
	}
}
