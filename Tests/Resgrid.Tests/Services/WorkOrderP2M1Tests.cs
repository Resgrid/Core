using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public partial class WorkOrderP2M1Tests
	{
		private readonly ChecklistActor _actor = new ChecklistActor { DepartmentId = 77, UserId = "manager" };
		private Store _store;
		private WorkOrdersService _service;
		private Mock<IWorkOrderAuthorizationService> _auth;
		private Mock<IReadinessAccessService> _access;
		private Mock<IProtectedReadService> _read;
		private Mock<IProtectedWriteService> _write;
		private Mock<IRecordAttachmentScanner> _scanner;
		private Mock<IDomainEventOutboxService> _outbox;
		private Mock<IUnitOfWork> _uow;
		private List<DomainEventEnvelope> _events;
		[SetUp]
		public void Setup()
		{
			_store = new Store(); _events = new List<DomainEventEnvelope>();
			_auth = new Mock<IWorkOrderAuthorizationService>();
			_auth.Setup(a => a.ScopeAsync(It.IsAny<ChecklistActor>())).ReturnsAsync((ChecklistActor a) => new WorkOrderReadScope { All = a.UserId == "manager" || a.UserId == "verifier", UserId = a.UserId });
			_auth.Setup(a => a.CanManageAsync(It.IsAny<ChecklistActor>(), It.IsAny<int?>())).ReturnsAsync((ChecklistActor a, int? g) => a.UserId == "manager" || a.UserId == "verifier");
			_auth.Setup(a => a.CanContributeAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrder>())).ReturnsAsync((ChecklistActor a, WorkOrder o) => a.UserId == "manager" || a.UserId == "verifier" || a.UserId == o.AssignedToUserId);
			_auth.Setup(a => a.RecipientsAsync(77, It.IsAny<WorkOrder>())).ReturnsAsync((int d, WorkOrder o) => o.AssignedToUserId == null ? new List<string>() : new List<string> { o.AssignedToUserId });
			_access = new Mock<IReadinessAccessService>(); _access.Setup(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(true);
			_read = new Mock<IProtectedReadService>(); _read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
			_write = new Mock<IProtectedWriteService>(); _write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed()));
			_scanner = new Mock<IRecordAttachmentScanner>();
			var audit = new Mock<IAuditLogsRepository>();
			audit.Setup(a => a.InsertAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((AuditLog a, CancellationToken c, bool f) => { a.AuditLogId = 1; return a; });
			_outbox = new Mock<IDomainEventOutboxService>();
			_outbox.Setup(o => o.EnqueueAsync(77, "WorkOrders", It.IsAny<DomainEventEnvelope>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, string p, DomainEventEnvelope e, CancellationToken c) => { _events.Add(e); return new DomainEventOutboxEntry { DomainEventOutboxId = _events.Count }; });
			_uow = new Mock<IUnitOfWork>(); _uow.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { _store.Begin(); return (DbConnection)null; });
			_uow.Setup(u => u.DiscardChanges()).Callback(() => _store.Rollback());
			_service = new WorkOrdersService(_store, _auth.Object, _access.Object, _uow.Object, audit.Object, _outbox.Object, new Lazy<IProtectedReadService>(() => _read.Object), new Lazy<IProtectedWriteService>(() => _write.Object), _scanner.Object);
		}
		private static WorkOrderInput Input(bool safety = false) => new WorkOrderInput { RequestId = Guid.NewGuid().ToString("D"), Content = new WorkOrderContent { Title = "Synthetic equipment repair", Description = "PII-PHI-CANARY narrative", SafetyCritical = safety } };
		private async Task<WorkOrderDetail> Transition(int id, WorkOrderStatus status, ChecklistActor actor = null, string reason = null, string evidence = null)
		{
			actor ??= _actor; var current = await _service.GetAsync(actor, id);
			await _service.TransitionAsync(actor, id, new WorkOrderTransition { Revision = current.Order.Revision, Status = status, Reason = reason, Resolution = "Repaired", Cause = "Wear", VerificationEvidence = evidence });
			return await _service.GetAsync(actor, id);
		}
		private async Task<int> Assigned(bool safety = false)
		{
			var row = await _service.CreateAsync(_actor, Input(safety)); var id = row.Order.Id;
			row = await Transition(id, WorkOrderStatus.Accepted);
			await _service.AssignAsync(_actor, id, new WorkOrderAssignment { Revision = row.Order.Revision, UserId = "manager" });
			row = await _service.GetAsync(_actor, id); await _service.AcceptAssignmentAsync(_actor, id, row.Order.Revision); return id;
		}
		[Test]
		public async Task Request_retry_returns_one_number_and_one_creation_event_but_changed_retry_conflicts()
		{
			var input = Input(); var first = await _service.CreateAsync(_actor, input); var retry = await _service.CreateAsync(_actor, input);
			retry.Order.Id.Should().Be(first.Order.Id); retry.Order.Number.Should().EndWith("000001"); _events.Count(e => e.Trigger == WorkflowTriggerEventType.WorkOrderCreated).Should().Be(1);
			input.Content.Title = "Changed"; (await ((Func<Task>)(async () => await _service.CreateAsync(_actor, input))).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(409);
			_store.All<WorkOrder>().Should().HaveCount(1);
		}
		[Test]
		public async Task Lifecycle_keeps_completion_separate_from_independent_safety_verification()
		{
			var id = await Assigned(true); await Transition(id, WorkOrderStatus.InProgress); await Transition(id, WorkOrderStatus.Completed);
			(await ((Func<Task>)(async () => await Transition(id, WorkOrderStatus.Closed, evidence: "Test passed"))).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("IndependentVerificationRequired");
			var closed = await Transition(id, WorkOrderStatus.Closed, new ChecklistActor { DepartmentId = 77, UserId = "verifier" }, evidence: "Independent test passed");
			closed.VerifiedBy.Should().Be("verifier"); closed.CompletedOn.Should().NotBeNull(); closed.ClosedOn.Should().NotBeNull();
			_store.All<WorkOrder>().Single().RestoreUnitStateOnClose.Should().BeFalse();
		}
		[Test]
		public async Task Paid_access_is_rechecked_inside_transaction_and_expiry_preserves_history()
		{
			var row = await _service.CreateAsync(_actor, Input());
			_access.SetupSequence(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(true).ReturnsAsync(false);
			(await ((Func<Task>)(() => _service.CommentAsync(_actor, row.Order.Id, row.Order.Revision, "blocked"))).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(402);
			_store.All<WorkOrderActivity>().Should().HaveCount(1);
			_access.Setup(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
			(await _service.GetAsync(_actor, row.Order.Id)).CanWrite.Should().BeFalse();
			(await _service.ListAsync(_actor, new WorkOrderFilter())).Items.Should().HaveCount(1);
			_access.Verify(a => a.CanUseChecklistsAsync(It.IsAny<int>()), Times.Never);
		}
		[Test]
		public async Task Invalid_transition_stale_revision_and_cross_department_reads_are_rejected()
		{
			var row = await _service.CreateAsync(_actor, Input()); var id = row.Order.Id;
			(await ((Func<Task>)(async () => await Transition(id, WorkOrderStatus.Closed))).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("InvalidTransition");
			await _service.CommentAsync(_actor, id, 1, "first");
			(await ((Func<Task>)(() => _service.CommentAsync(_actor, id, 1, "stale"))).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(409);
			(await ((Func<Task>)(async () => await _service.GetAsync(new ChecklistActor { DepartmentId = 78, UserId = "manager" }, id))).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(404);
		}
		[Test]
		public async Task Assignment_acceptance_and_reasons_are_enforced_before_state_changes()
		{
			var row = await _service.CreateAsync(_actor, Input()); row = await Transition(row.Order.Id, WorkOrderStatus.Accepted);
			await _service.AssignAsync(_actor, row.Order.Id, new WorkOrderAssignment { Revision = row.Order.Revision, UserId = "manager" });
			(await ((Func<Task>)(async () => await Transition(row.Order.Id, WorkOrderStatus.InProgress))).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("AcceptAssignmentFirst");
			(await ((Func<Task>)(async () => await Transition(row.Order.Id, WorkOrderStatus.Cancelled))).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(400);
			await Transition(row.Order.Id, WorkOrderStatus.Cancelled, reason: "Duplicate equipment entry");
			(await ((Func<Task>)(async () => await Transition(row.Order.Id, WorkOrderStatus.Accepted))).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(400);
		}
		[Test]
		public async Task Labor_and_parts_are_protected_slots_and_part_corrections_preserve_the_original_line()
		{
			var id = await Assigned(); var row = await _service.GetAsync(_actor, id);
			await _service.AddLaborAsync(_actor, id, new WorkOrderLaborInput { Revision = row.Order.Revision, WorkDate = DateTime.UtcNow.Date, Content = new WorkOrderLaborContent { Hours = 2.5m, RatePerHour = 15m, Note = "PII-PHI-CANARY labor" } });
			row = await _service.GetAsync(_actor, id);
			await _service.AddPartAsync(_actor, id, new WorkOrderPartInput { Revision = row.Order.Revision, Content = new WorkOrderPartContent { Description = "PII-PHI-CANARY part", Quantity = 2.125m, UnitCost = 4.25m } });
			row = await _service.GetAsync(_actor, id); await _service.VoidPartAsync(_actor, id, row.Parts.Single().Id, row.Order.Revision, "Unused");
			row = await _service.GetAsync(_actor, id); row.Parts.Single().Content.Quantity.Should().Be(2.125m); row.Parts.Single().VoidedOn.Should().NotBeNull(); row.Labor.Single().Content.Hours.Should().Be(2.5m);
			JsonConvert.SerializeObject(_events).Should().NotContain("PII-PHI-CANARY");
		}
		[Test]
		public async Task Failed_protection_rolls_back_allocated_number_and_emits_no_event()
		{
			_write.SetReturnsDefault(Task.FromResult(new ProtectedWriteResult { Success = false }));
			await ((Func<Task>)(async () => await _service.CreateAsync(_actor, Input()))).Should().ThrowAsync<WorkOrderException>();
			_store.All<WorkOrder>().Should().BeEmpty(); _events.Should().BeEmpty(); _uow.Verify(u => u.CommitChanges(), Times.Never);
		}
		private sealed partial class Store : IWorkOrderRepository, IWorkOrderMaintenanceRepository
		{
			private Dictionary<Type, List<WorkOrderRow>> _rows = new Dictionary<Type, List<WorkOrderRow>>();
			private Dictionary<Type, List<WorkOrderRow>> _before;
			private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
			public IEnumerable<T> All<T>() where T : WorkOrderRow => _rows.TryGetValue(typeof(T), out var rows) ? rows.Cast<T>().Select(Copy) : Enumerable.Empty<T>();
			public void Begin() => _before = _rows.ToDictionary(p => p.Key, p => p.Value.Select(v => (WorkOrderRow)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(v), p.Key)).ToList());
			public void Rollback() { if (_before != null) _rows = _before; }
			public Task LockDepartmentAsync(int departmentId) => Task.CompletedTask;
			public Task<T> GetAsync<T>(int departmentId, int id, bool includeData = true) where T : WorkOrderRow => Task.FromResult(All<T>().SingleOrDefault(r => r.DepartmentId == departmentId && r.Id == id));
			public Task<WorkOrder> RequestAsync(int departmentId, string requestId) => Task.FromResult(All<WorkOrder>().SingleOrDefault(r => r.DepartmentId == departmentId && r.RequestId == requestId));
			public Task<List<WorkOrder>> ListAsync(int departmentId, WorkOrderReadScope scope, WorkOrderFilter filter) => Task.FromResult(All<WorkOrder>().Where(r => r.DepartmentId == departmentId && scope.Allows(r)).OrderByDescending(r => r.Id).Skip(filter.Page * 50).Take(51).ToList());
			public Task<List<T>> ChildrenAsync<T>(int departmentId, int orderId, int skip = 0) where T : WorkOrderRow => Task.FromResult(All<T>().Where(r => r.DepartmentId == departmentId && r.WorkOrderId == orderId).Skip(skip).Take(500).ToList());
			public Task<int> NextNumberAsync(int departmentId, int year) => Task.FromResult(All<WorkOrder>().Where(r => r.DepartmentId == departmentId && r.NumberYear == year).Select(r => r.NumberSequence).DefaultIfEmpty().Max() + 1);
			public Task AllocateAsync<T>(T row) where T : WorkOrderRow { if (!_rows.ContainsKey(typeof(T))) _rows[typeof(T)] = new List<WorkOrderRow>(); row.Content.Should().BeNull(); row.Id = _rows[typeof(T)].Count + 1; _rows[typeof(T)].Add(Copy(row)); return Task.CompletedTask; }
			public Task WriteAsync<T>(T row) where T : WorkOrderRow { var list = _rows[typeof(T)]; list[list.FindIndex(r => r.Id == row.Id && r.DepartmentId == row.DepartmentId)] = Copy(row); return Task.CompletedTask; }
			public Task<int> ClaimNotificationAsync(WorkOrderNotification row, DateTime now) => Task.FromResult(1);
			public Task<bool> FinishNotificationAsync(WorkOrderNotification row, int state, DateTime now) => Task.FromResult(true);
		}
	}
}
