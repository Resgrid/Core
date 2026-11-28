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
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderP2M1Tests
    {
        private sealed class MaintenanceClock : TimeProvider { public DateTime Utc = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc); public override DateTimeOffset GetUtcNow() => new(Utc); }
        private MaintenanceClock _maintenanceClock;
        private Mock<IChecklistRepository> _maintenanceChecklists;
        private HashSet<string> _inactiveMaintenanceMembers;
        private Mock<IUnitsService> _maintenanceUnitsMock;
        private void Maintenance()
        {
            _maintenanceClock = new(); _maintenanceChecklists = new();
            DbTransaction transaction = null;
            _uow.SetupGet(u => u.Transaction).Returns(() => transaction);
            _uow.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { _store.Begin(); transaction = Mock.Of<DbTransaction>(); return (DbConnection)null; });
            _uow.Setup(u => u.CommitChanges()).Callback(() => transaction = null);
            _uow.Setup(u => u.DiscardChanges()).Callback(() => { _store.Rollback(); transaction = null; });
            var audit = new Mock<IAuditLogsRepository>();
            audit.Setup(a => a.InsertAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((AuditLog a, CancellationToken c, bool b) => { a.AuditLogId = 1; return a; });
            var units = new Mock<IUnitsService>(); units.Setup(u => u.GetUnitByIdAsync(10)).ReturnsAsync(new Unit { UnitId = 10, DepartmentId = 77 }); _maintenanceUnitsMock = units;
            // Everyone a schedule names is an active member unless a test says otherwise.
            _inactiveMaintenanceMembers = new HashSet<string>();
            _auth.Setup(a => a.ActiveMemberIdsAsync(77)).ReturnsAsync(() => new HashSet<string>(_store.All<WorkOrderRecurrence>().SelectMany(r => r.AssignedToUserIds).Where(u => !_inactiveMaintenanceMembers.Contains(u))));
            _service = new WorkOrdersService(_store, _auth.Object, _access.Object, _uow.Object, audit.Object, _outbox.Object, new(() => _read.Object), new(() => _write.Object), _scanner.Object,
                _maintenanceClock, _store, checklists: _maintenanceChecklists.Object, maintenanceUnits: units.Object);
        }
        private WorkOrderRecurrenceInput Schedule() => new() { AnchorLocal = _maintenanceClock.Utc.AddDays(3), Template = Input(), TimeZoneId = "UTC", Calendar = MaintenanceCalendar.Monthly, LeadDays = 3 };
        [Test]
        public async Task PM_generation_is_idempotent_metadata_only_and_pins_the_original_template()
        {
            Maintenance(); var input = Schedule(); var id = await _service.SaveRecurrenceAsync(_actor, input);
            (await _service.SaveRecurrenceAsync(_actor, input)).Should().Be(id);
            _read.Invocations.Clear(); _write.Invocations.Clear();
            var sweep = await _service.GenerateMaintenanceAsync(77);
            sweep.Generated.Should().Be(1); sweep.Errors.Should().Be(0);
            _read.Invocations.Should().BeEmpty(); _write.Invocations.Should().BeEmpty("workers must not decrypt or seal customer content");
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(0);
            var order = _store.All<WorkOrder>().Single(); order.Content.Should().BeNull(); order.OriginalDueOn.Should().Be(input.AnchorLocal);
            var version = order.RecurrenceVersionId;
            input.Id = id; input.Revision = _store.All<WorkOrderRecurrence>().Single().Revision; input.Reason = "new procedure"; input.Template.Content.Title = "Revised template";
            await _service.SaveRecurrenceAsync(_actor, input);
            (await _service.GetAsync(_actor, order.Id)).Order.Title.Should().Be("Synthetic equipment repair");
            _store.All<WorkOrder>().Single().RecurrenceVersionId.Should().Be(version);
            _store.All<WorkOrderRecurrenceVersion>().Should().HaveCount(2);
            JsonConvert.SerializeObject(_events).Should().NotContain("CANARY");
        }
        [Test]
        public async Task Recurrence_requires_a_caller_retry_identity_and_keeps_its_target()
        {
            Maintenance(); var input=Schedule(); input.Template.RequestId=null;
            await FluentActions.Awaiting(()=>_service.SaveRecurrenceAsync(_actor,input)).Should().ThrowAsync<WorkOrderException>();
            input.Template.RequestId=Guid.NewGuid().ToString("D");
            var id=await _service.SaveRecurrenceAsync(_actor,input);
            input.Id=id; input.Revision=1; input.Reason="Move schedule"; input.Template.TargetUnitId=10;
            (await FluentActions.Awaiting(()=>_service.SaveRecurrenceAsync(_actor,input)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("TargetUnavailable");
        }
        [TestCase(false), TestCase(true)]
        public async Task Meter_or_condition_whichever_first_generates_once_and_readings_replay(bool condition)
        {
            Maintenance(); var input = Schedule(); input.AnchorLocal = _maintenanceClock.Utc.AddMonths(2); input.MeterUnit = MaintenanceMeterUnit.Hours; input.MeterBaseline = 100; input.MeterInterval = 50;
            input.Condition = MaintenanceCondition.AtOrAbove; input.ConditionThreshold = 90; input.ConditionUnit = "C";
            var id = await _service.SaveRecurrenceAsync(_actor, input);
            var reading = new WorkOrderReadingInput { Revision = 1, RequestId = Guid.NewGuid().ToString("D"), ObservedOn = _maintenanceClock.Utc, Source = "instrument CANARY", MeterValue = condition ? 110 : 150, ConditionValue = condition ? 95 : 70 };
            await _service.RecordReadingAsync(_actor, id, reading); await _service.RecordReadingAsync(_actor, id, reading);
            _store.All<WorkOrderMeterReading>().Should().ContainSingle();
            _maintenanceClock.Utc = _maintenanceClock.Utc.AddHours(2);
            await _service.RecordReadingAsync(_actor, id, new() { Revision = 2, RequestId = Guid.NewGuid().ToString("D"), ObservedOn = _maintenanceClock.Utc, Source = "Later observation", MeterValue = 160, ConditionValue = 96 });
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(1);
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(0);
            _store.All<WorkOrder>().Single().OriginalDueOn.Should().Be(reading.ObservedOn, "a later reading or worker outage must not erase the first threshold crossing");
            _events.Count(e => e.Trigger == WorkflowTriggerEventType.WorkOrderThresholdReached).Should().Be(1);
            JsonConvert.SerializeObject(_events).Should().NotContain("CANARY");
        }
        [Test]
        public async Task Meter_reset_requires_reason_keeps_immutable_provenance_and_advances_epoch()
        {
            Maintenance(); var input = Schedule(); input.AnchorLocal = _maintenanceClock.Utc.AddMonths(2); input.MeterUnit = MaintenanceMeterUnit.Kilometers; input.MeterBaseline = 1000; input.MeterInterval = 500;
            var id = await _service.SaveRecurrenceAsync(_actor, input);
            var reading = new WorkOrderReadingInput { Revision = 1, RequestId = Guid.NewGuid().ToString("D"), ObservedOn = _maintenanceClock.Utc, Source = "replacement instrument", MeterValue = 0, ResetMeter = true };
            await FluentActions.Awaiting(() => _service.RecordReadingAsync(_actor, id, reading)).Should().ThrowAsync<WorkOrderException>();
            reading.Note = "Replacement documented"; await _service.RecordReadingAsync(_actor, id, reading);
            var row = _store.All<WorkOrderRecurrence>().Single(); row.MeterEpoch.Should().Be(1); row.MeterBaseline.Should().Be(0); row.LastMeterValue.Should().Be(0);
            // The reset note is a private member: stored apart from the reading's metadata (review §8).
            var stored = _store.All<WorkOrderMeterReading>().Single();
            stored.Content.Should().NotContain("Replacement documented"); stored.ProtectedContent.Should().Contain("Replacement documented");
            input.Id = id; input.Revision = row.Revision; input.Reason = "rename"; input.MeterBaseline = row.MeterBaseline;
            await _service.SaveRecurrenceAsync(_actor, input);
            _store.All<WorkOrderRecurrence>().Single().MeterBaseline.Should().Be(0);
        }
        [TestCase(false), TestCase(true)]
        public async Task Calendar_completion_preserves_fixed_or_completion_anchor(bool completionBased)
        {
            Maintenance(); var input = Schedule(); input.AnchorLocal = _maintenanceClock.Utc.AddDays(-40); input.CompletionBased = completionBased; input.AssignedToUserId = _actor.UserId;
            var id = await _service.SaveRecurrenceAsync(_actor, input); await _service.GenerateMaintenanceAsync(77); var order = _store.All<WorkOrder>().Single();
            await _service.AcceptAssignmentAsync(_actor, order.Id, order.Revision); await Transition(order.Id, WorkOrderStatus.InProgress); await Transition(order.Id, WorkOrderStatus.Completed);
            await Transition(order.Id, WorkOrderStatus.Closed, evidence: "test passed");
            var row = _store.All<WorkOrderRecurrence>().Single(); row.PendingWorkOrderId.Should().BeNull();
            row.NextDueOn.Should().Be(completionBased ? _maintenanceClock.Utc.AddMonths(1) : input.AnchorLocal.AddMonths(1));
            row.NextDueOn.Should().BeAfter(order.OriginalDueOn.Value);
        }
        [Test]
        public async Task Deferral_preserves_original_due_and_overdue_escalates_once_after_delay()
        {
            Maintenance(); var input = Schedule(); input.AnchorLocal = _maintenanceClock.Utc.AddDays(-1); input.EscalateAfterMinutes = 60;
            await _service.SaveRecurrenceAsync(_actor, input); await _service.GenerateMaintenanceAsync(77); var order = _store.All<WorkOrder>().Single();
            (await _service.EscalateMaintenanceAsync(77)).Escalated.Should().Be(1);
            (await _service.EscalateMaintenanceAsync(77)).Escalated.Should().Be(0);
            await _service.DeferAsync(_actor, order.Id, new() { Revision = _store.All<WorkOrder>().Single().Revision, DueOn = _maintenanceClock.Utc.AddDays(1), Reason = "Approved outage" });
            var deferred = _store.All<WorkOrder>().Single(); deferred.OriginalDueOn.Should().Be(input.AnchorLocal); deferred.DueOn.Should().Be(_maintenanceClock.Utc.AddDays(1));
            (await _service.EscalateMaintenanceAsync(77)).Escalated.Should().Be(0);
            _maintenanceClock.Utc = deferred.DueOn.Value.AddMinutes(61);
            (await _service.EscalateMaintenanceAsync(77)).Escalated.Should().Be(1);
        }
        [Test]
        public async Task Reopening_a_closed_PM_order_pauses_its_cycle_and_prevents_a_duplicate()
        {
            Maintenance(); var input=Schedule(); input.AnchorLocal=_maintenanceClock.Utc.AddDays(-1); input.AssignedToUserId=_actor.UserId;
            await _service.SaveRecurrenceAsync(_actor,input); await _service.GenerateMaintenanceAsync(77);
            var order=_store.All<WorkOrder>().Single();
            await _service.AcceptAssignmentAsync(_actor,order.Id,order.Revision); await Transition(order.Id,WorkOrderStatus.InProgress); await Transition(order.Id,WorkOrderStatus.Completed);
            await Transition(order.Id,WorkOrderStatus.Closed,evidence:"Function test passed");
            await Transition(order.Id,WorkOrderStatus.Accepted,reason:"Defect recurred");
            var recurrence=_store.All<WorkOrderRecurrence>().Single(); recurrence.IsActive.Should().BeFalse(); recurrence.PendingWorkOrderId.Should().Be(order.Id);
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(0);
        }
        [Test]
        public async Task Manual_order_deferrals_preserve_every_old_and_new_due_in_protected_activity()
        {
            Maintenance(); var input=Input(); input.DueOn=_maintenanceClock.Utc;
            var order=await _service.CreateAsync(_actor,input);
            await _service.DeferAsync(_actor,order.Order.Id,new() {Revision=1,DueOn=_maintenanceClock.Utc.AddDays(1),Reason="First approved delay"});
            order=await _service.GetAsync(_actor,order.Order.Id);
            await _service.DeferAsync(_actor,order.Order.Id,new() {Revision=order.Order.Revision,DueOn=_maintenanceClock.Utc.AddDays(2),Reason="Second approved delay"});
            order=await _service.GetAsync(_actor,order.Order.Id);
            order.OriginalDueOn.Should().Be(input.DueOn);
            var history=order.Activities.Where(a=>a.Type==WorkOrderActivityType.Deferred).ToList();
            history.Should().HaveCount(2);
            history[0].OriginalDueOn.Should().Be(input.DueOn); history[0].RevisedDueOn.Should().Be(_maintenanceClock.Utc.AddDays(1));
            history[1].OriginalDueOn.Should().Be(history[0].RevisedDueOn); history[1].RevisedDueOn.Should().Be(_maintenanceClock.Utc.AddDays(2));
        }
        [Test]
        public async Task Cancellation_pauses_a_due_cycle_and_commercial_pause_blocks_generation_without_losing_history()
        {
            Maintenance(); await _service.SaveRecurrenceAsync(_actor, Schedule()); await _service.GenerateMaintenanceAsync(77);
            var order = _store.All<WorkOrder>().Single(); await Transition(order.Id, WorkOrderStatus.Cancelled, reason: "Review target");
            _store.All<WorkOrderRecurrence>().Single().IsActive.Should().BeFalse();
            _store.All<WorkOrderRecurrence>().Single().PendingWorkOrderId.Should().BeNull();
            _access.Setup(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(0);
            (await _service.RecurrencesAsync(_actor)).Should().HaveCount(1);
            await FluentActions.Awaiting(() => _service.SaveRecurrenceAsync(_actor, Schedule())).Should().ThrowAsync<WorkOrderException>();
        }
        [Test]
        public async Task A_unit_already_in_a_custom_out_of_service_status_is_not_taken_over_by_a_hold()
        {
            Maintenance();
            _maintenanceUnitsMock.Setup(u => u.GetCustomUnitStateBaseTypesAsync(77)).ReturnsAsync(new Dictionary<int, int> { [9001] = (int)ActionBaseTypes.Maintenance });
            _store.UnitStates.Add(new UnitState { UnitStateId = 1, UnitId = 10, State = 9001, Timestamp = _maintenanceClock.Utc.AddDays(-1) });
            var input = Input(true); input.TargetUnitId = 10; var order = await _service.CreateAsync(_actor, input);
            await _service.AddHoldAsync(_actor, order.Order.Id, new() { Revision = 1, Unit = true, Reason = "Defect" });
            _store.UnitStates.Should().ContainSingle("the department's own In Shop status is a manual restriction the hold must not overwrite");
            _store.All<WorkOrderSafetyHold>().Single().AppliedStateId.Should().BeNull();
        }
        [TestCase(false, false), TestCase(true, false), TestCase(true, true)]
        public async Task Multiple_unit_holds_require_independent_release_and_preserve_intervening_dispatch(bool intervening, bool backdated)
        {
            Maintenance(); _store.UnitStates.Add(new UnitState { UnitStateId = 1, UnitId = 10, State = (int)UnitStateTypes.Available, Timestamp = _maintenanceClock.Utc.AddDays(-1) });
            for (var i = 0; i < 2; i++) { var input = Input(true); input.TargetUnitId = 10; var order = await _service.CreateAsync(_actor, input); await _service.AddHoldAsync(_actor, order.Order.Id, new() { Revision = 1, Unit = true, Reason = "Defect " + i }); }
            var holds = _store.All<WorkOrderSafetyHold>().ToList(); holds[0].AppliedStateId.Should().Be(holds[1].AppliedStateId);
            var heldOrder=await _service.GetAsync(_actor,holds[0].WorkOrderId); heldOrder.Input.TargetUnitId=null;
            (await FluentActions.Awaiting(()=>_service.UpdateAsync(_actor,heldOrder.Order.Id,heldOrder.Input)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("TargetUnavailable");
            var release = new WorkOrderReleaseInput { Revision = 1, Qualification = "Qualified test technician", Evidence = "Independent verification", RestoreState = true };
            await FluentActions.Awaiting(() => _service.ReleaseHoldAsync(_actor, holds[0].Id, release)).Should().ThrowAsync<WorkOrderException>();
            var reviewer = new ChecklistActor { DepartmentId = 77, UserId = "verifier" };
            _access.Setup(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
            await _service.ReleaseHoldAsync(reviewer, holds[0].Id, release);
            _store.UnitStates.Last().State.Should().Be((int)UnitStateTypes.OutOfService);
            if (intervening) _store.UnitStates.Add(new UnitState { UnitStateId = 50, UnitId = 10, State = (int)UnitStateTypes.OutOfService, Timestamp = _maintenanceClock.Utc.AddSeconds(backdated ? -1 : 1) });
            await _service.ReleaseHoldAsync(reviewer, holds[1].Id, release);
            _store.All<WorkOrderSafetyHold>().Last().StateRestored.Should().Be(!intervening);
            _store.UnitStates.Last().State.Should().Be((int)(intervening ? UnitStateTypes.OutOfService : UnitStateTypes.Available));
        }
        [Test]
        public async Task Free_failure_intent_does_not_call_billing_and_replays_after_paid_recovery_even_awaiting_witness()
        {
            Maintenance(); var completion = new ChecklistCompletion { Id = Guid.NewGuid().ToString("D"), VersionId = Guid.NewGuid().ToString("D"), DepartmentId = 77, CreatedBy = "submitter", State = (int)ChecklistRunState.AwaitingWitness, TargetType = (int)ChecklistTargetType.Unit, TargetId = "10" };
            var version = new ChecklistDefinitionVersion { Id = completion.VersionId, DepartmentId = 77, CreatedBy = "manager" };
            var item = new ChecklistItem { Id = Guid.NewGuid().ToString("D"), CreateWorkOrderOnFail = true, SetUnitStateOnFail = true };
            _access.Invocations.Clear();
            await _uow.Object.CreateOrGetConnectionAsync(); await _service.RecordFailureIntentAsync(_actor, completion, version, item); await _service.RecordFailureIntentAsync(_actor, completion, version, item); _uow.Object.CommitChanges();
            _access.Invocations.Should().BeEmpty(); _store.All<WorkOrderFailureIntent>().Should().ContainSingle();
            _maintenanceChecklists.Setup(s => s.GetAsync<ChecklistCompletion>(77, completion.Id, CancellationToken.None)).ReturnsAsync(completion);
            _access.Setup(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(false); (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(0);
            _access.Setup(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(true); var sweep = await _service.GenerateMaintenanceAsync(77);
            sweep.Generated.Should().Be(1); sweep.Held.Should().Be(1); sweep.Errors.Should().Be(0);
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(0);
            _store.All<WorkOrder>().Single().SourceChecklistCompletionId.Should().Be(completion.Id);
            _store.All<WorkOrderFailureIntent>().Single().Content.Should().BeNull();
        }
        private async Task<ChecklistCompletion> RecordFailure(bool holdUnit = true)
        {
            var completion = new ChecklistCompletion { Id = Guid.NewGuid().ToString("D"), VersionId = Guid.NewGuid().ToString("D"), DepartmentId = 77, CreatedBy = "submitter", State = (int)ChecklistRunState.Submitted, TargetType = (int)ChecklistTargetType.Unit, TargetId = "10" };
            var version = new ChecklistDefinitionVersion { Id = completion.VersionId, DepartmentId = 77, CreatedBy = "manager" };
            var item = new ChecklistItem { Id = Guid.NewGuid().ToString("D"), CreateWorkOrderOnFail = true, SetUnitStateOnFail = holdUnit };
            await _uow.Object.CreateOrGetConnectionAsync(); await _service.RecordFailureIntentAsync(_actor, completion, version, item); _uow.Object.CommitChanges();
            _maintenanceChecklists.Setup(s => s.GetAsync<ChecklistCompletion>(77, completion.Id, CancellationToken.None)).ReturnsAsync(completion);
            return completion;
        }
        [Test]
        public async Task RF4_a_stale_failure_intent_is_never_replayed_unattended_and_costs_no_billing_call()
        {
            // ERT-D5 / R-D6: a failure recorded long before Readiness Pro was bought must not flood work orders and holds on activation.
            Maintenance(); await RecordFailure();
            _maintenanceClock.Utc = _maintenanceClock.Utc.AddHours(Resgrid.Config.ReadinessProConfig.FailureIntentMaxAgeHours + 1);
            _access.Invocations.Clear();
            var sweep = await _service.GenerateMaintenanceAsync(77);
            sweep.Generated.Should().Be(0); sweep.Held.Should().Be(0); sweep.Stale.Should().Be(1);
            _access.Verify(a => a.CanUseMaintenanceAsync(It.IsAny<int>()), Times.Never(), "a stale intent waits for review and must not cost a Billing API call");
            _store.All<WorkOrder>().Should().BeEmpty(); _store.UnitStates.Should().BeEmpty();
            _store.All<WorkOrderFailureIntent>().Single().ProcessedOn.Should().BeNull();
        }
        [Test]
        public async Task RF4_a_fresh_failure_intent_is_still_processed_with_its_hold()
        {
            Maintenance(); await RecordFailure();
            _maintenanceClock.Utc = _maintenanceClock.Utc.AddHours(Resgrid.Config.ReadinessProConfig.FailureIntentMaxAgeHours - 1);
            var sweep = await _service.GenerateMaintenanceAsync(77);
            sweep.Generated.Should().Be(1); sweep.Held.Should().Be(1); sweep.Stale.Should().Be(0);
        }
        [Test]
        public async Task RF4_review_queue_creates_a_work_order_without_a_hold_and_resolves_once()
        {
            Maintenance(); var completion = await RecordFailure();
            _maintenanceClock.Utc = _maintenanceClock.Utc.AddDays(30);
            var review = await _service.FailureReviewAsync(_actor);
            review.Total.Should().Be(1); var row = review.Rows.Single();
            row.CompletionId.Should().Be(completion.Id); row.HoldUnit.Should().BeTrue(); row.CanCreate.Should().BeTrue(); row.UnitId.Should().Be(10);
            var orderId = await _service.ResolveFailureReviewAsync(_actor, row.IntentId, true);
            var order = _store.All<WorkOrder>().Single(); order.Id.Should().Be(orderId); order.SourceChecklistCompletionId.Should().Be(completion.Id);
            _store.All<WorkOrderSafetyHold>().Should().BeEmpty("an old failure never applies a unit or asset hold");
            _store.UnitStates.Should().BeEmpty();
            var intent = _store.All<WorkOrderFailureIntent>().Single(); intent.ProcessedOn.Should().NotBeNull(); intent.WorkOrderId.Should().Be(orderId);
            (await _service.FailureReviewAsync(_actor)).Total.Should().Be(0);
            (await FluentActions.Awaiting(() => _service.ResolveFailureReviewAsync(_actor, row.IntentId, false)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("FailureReviewResolved");
        }
        [Test]
        public async Task RF4_dismissing_a_stale_failure_creates_nothing()
        {
            Maintenance(); await RecordFailure();
            _maintenanceClock.Utc = _maintenanceClock.Utc.AddDays(5);
            var id = (await _service.FailureReviewAsync(_actor)).Rows.Single().IntentId;
            (await _service.ResolveFailureReviewAsync(_actor, id, false)).Should().BeNull();
            _store.All<WorkOrder>().Should().BeEmpty();
            var intent = _store.All<WorkOrderFailureIntent>().Single(); intent.ProcessedOn.Should().NotBeNull(); intent.WorkOrderId.Should().BeNull();
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(0);
        }
        [Test]
        public async Task RF4_a_fresh_intent_cannot_be_resolved_from_the_queue_and_the_queue_needs_manage_rights()
        {
            Maintenance(); await RecordFailure();
            var id = _store.All<WorkOrderFailureIntent>().Single().Id;
            (await FluentActions.Awaiting(() => _service.ResolveFailureReviewAsync(_actor, id, true)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("FailureReviewNotStale");
            var member = new ChecklistActor { DepartmentId = 77, UserId = "member" };
            (await FluentActions.Awaiting(() => _service.FailureReviewAsync(member)).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(403);
            _access.Setup(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
            (await FluentActions.Awaiting(() => _service.FailureReviewAsync(_actor)).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(402);
        }
        [Test]
        public async Task RF4_a_stale_failure_whose_run_was_reopened_can_only_be_dismissed()
        {
            Maintenance(); var completion = await RecordFailure(); completion.State = (int)ChecklistRunState.InProgress;
            _maintenanceClock.Utc = _maintenanceClock.Utc.AddDays(5);
            var row = (await _service.FailureReviewAsync(_actor)).Rows.Single(); row.CanCreate.Should().BeFalse();
            (await FluentActions.Awaiting(() => _service.ResolveFailureReviewAsync(_actor, row.IntentId, true)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("FailureReviewSourceUnavailable");
            _store.All<WorkOrderFailureIntent>().Single().ProcessedOn.Should().BeNull("a refused create rolls back");
        }
        [Test]
        public async Task RF4_zero_hours_turns_expiry_off()
        {
            var saved = Resgrid.Config.ReadinessProConfig.FailureIntentMaxAgeHours;
            try
            {
                Resgrid.Config.ReadinessProConfig.FailureIntentMaxAgeHours = 0;
                Maintenance(); await RecordFailure(); _maintenanceClock.Utc = _maintenanceClock.Utc.AddDays(365);
                (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(1);
                FailureIntentAging.StaleBefore(DateTime.UtcNow).Should().BeNull();
            }
            finally { Resgrid.Config.ReadinessProConfig.FailureIntentMaxAgeHours = saved; }
        }
        private Mock<Resgrid.Model.Services.IInventoryCatalogService> AssetCatalog(params (string Id, Resgrid.Model.Inventories.InventoryAssetStatus Status)[] assets)
        {
            Maintenance();
            var catalog = new Mock<Resgrid.Model.Services.IInventoryCatalogService>();
            catalog.Setup(c => c.QueryAsync<Resgrid.Model.Inventories.InventoryAsset>(It.IsAny<Resgrid.Model.Inventories.InventoryActor>(), It.Is<Resgrid.Model.Inventories.InventoryQuery>(q => q.ItemId == ExtinguisherItem), 0))
                .ReturnsAsync(new Resgrid.Model.Inventories.InventoryPage<Resgrid.Model.Inventories.InventoryAsset> { Items = assets.Select((a, i) => new Resgrid.Model.Inventories.InventoryAsset { Id = a.Id, DepartmentId = 77, ItemId = ExtinguisherItem, Status = (int)a.Status, Content = i == 0 ? "{\"SerialNumber\":\"FX-011\",\"AssetTag\":\"CVFR-FX-101\"}" : null }).ToList() });
            var audit = new Mock<IAuditLogsRepository>();
            audit.Setup(a => a.InsertAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((AuditLog a, CancellationToken c, bool b) => { a.AuditLogId = 1; return a; });
            _service = new WorkOrdersService(_store, _auth.Object, _access.Object, _uow.Object, audit.Object, _outbox.Object, new(() => _read.Object), new(() => _write.Object), _scanner.Object,
                _maintenanceClock, _store, checklists: _maintenanceChecklists.Object, maintenanceUnits: _maintenanceUnitsMock.Object, inventoryCatalog: new Lazy<Resgrid.Model.Services.IInventoryCatalogService>(() => catalog.Object));
            return catalog;
        }
        private const string ExtinguisherItem = "6a4c1f0e-3f2d-4c8e-9b7a-1d2e3f4a5b6c";
        private AssetServiceSetupInput ExtinguisherService() => new() { ItemId = ExtinguisherItem, Presets = new() { AssetServicePreset.AnnualMaintenance, AssetServicePreset.HydrostaticTwelveYear }, AnchorLocal = _maintenanceClock.Utc.AddDays(30), TimeZoneId = "UTC", LeadDays = 30 };
        [Test]
        public async Task WE1_asset_service_setup_creates_one_completion_based_schedule_per_asset_and_preset_once()
        {
            var a = Guid.NewGuid().ToString("D"); var b = Guid.NewGuid().ToString("D"); var retired = Guid.NewGuid().ToString("D");
            AssetCatalog((a, Resgrid.Model.Inventories.InventoryAssetStatus.InService), (b, Resgrid.Model.Inventories.InventoryAssetStatus.Issued), (retired, Resgrid.Model.Inventories.InventoryAssetStatus.Retired));
            var result = await _service.SetupAssetServiceAsync(_actor, ExtinguisherService());
            result.Assets.Should().Be(3); result.Created.Should().Be(4); result.Skipped.Should().Be(1); result.Failed.Should().Be(0); result.Existing.Should().Be(0);
            var schedules = _store.All<WorkOrderRecurrence>().ToList();
            schedules.Should().HaveCount(4).And.OnlyContain(r => r.CompletionBased && r.Calendar == (int)MaintenanceCalendar.Yearly && (r.InventoryAssetId == a || r.InventoryAssetId == b));
            schedules.Select(r => r.Interval).OrderBy(i => i).Should().Equal(1, 1, 12, 12);
            schedules.Should().NotContain(r => r.InventoryAssetId == retired);
            WorkOrdersService.AssetServiceTitle("Annual maintenance", new Resgrid.Model.Inventories.InventoryAsset { Content = "{\"SerialNumber\":\"FX-011\",\"AssetTag\":\"CVFR-FX-101\"}" }).Should().Be("Annual maintenance — CVFR-FX-101", "the tag tells one extinguisher's schedule from another's");
            WorkOrdersService.AssetServiceTitle("Annual maintenance", new Resgrid.Model.Inventories.InventoryAsset { Content = "{\"SerialNumber\":\"FX-011\"}" }).Should().Be("Annual maintenance — FX-011");
            WorkOrdersService.AssetServiceTitle("Annual maintenance", new Resgrid.Model.Inventories.InventoryAsset()).Should().Be("Annual maintenance");

            // Running it again (for assets added later) never duplicates a schedule.
            var again = await _service.SetupAssetServiceAsync(_actor, ExtinguisherService());
            again.Created.Should().Be(0); again.Existing.Should().Be(4);
            _store.All<WorkOrderRecurrence>().Should().HaveCount(4);
        }
        [Test]
        public async Task WE1_asset_service_setup_needs_readiness_pro_department_wide_manage_and_valid_presets()
        {
            AssetCatalog((Guid.NewGuid().ToString("D"), Resgrid.Model.Inventories.InventoryAssetStatus.InService));
            var member = new ChecklistActor { DepartmentId = 77, UserId = "member" };
            (await FluentActions.Awaiting(() => _service.SetupAssetServiceAsync(member, ExtinguisherService())).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(403);
            var none = ExtinguisherService(); none.Presets.Clear();
            (await FluentActions.Awaiting(() => _service.SetupAssetServiceAsync(_actor, none)).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(400);
            var twice = ExtinguisherService(); twice.Presets.Add(AssetServicePreset.AnnualMaintenance);
            (await FluentActions.Awaiting(() => _service.SetupAssetServiceAsync(_actor, twice)).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(400);
            _access.Setup(x => x.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
            (await FluentActions.Awaiting(() => _service.SetupAssetServiceAsync(_actor, ExtinguisherService())).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(402);
            _store.All<WorkOrderRecurrence>().Should().BeEmpty();
        }
        [Test]
        public async Task Every_maintenance_outbox_event_fits_the_36_character_aggregate_id_column()
        {
            // DomainEventOutbox.AggregateId is nvarchar(36)/varchar(36) (M0153). "recurrence:" + id (47) failed every schedule save on a real database.
            Maintenance(); var input = Schedule(); input.AnchorLocal = _maintenanceClock.Utc.AddDays(1);
            var id = await _service.SaveRecurrenceAsync(_actor, input);
            await _service.GenerateMaintenanceAsync(77);
            _events.Should().NotBeEmpty();
            _events.Should().OnlyContain(e => e.AggregateId != null && e.AggregateId.Length <= 36);
            _events.Should().Contain(e => e.AggregateType == "WorkOrderRecurrence" && e.AggregateId == id);
        }
        [TestCase(AssetServicePreset.AnnualMaintenance, 1)]
        [TestCase(AssetServicePreset.SixYearMaintenance, 6)]
        [TestCase(AssetServicePreset.HydrostaticFiveYear, 5)]
        [TestCase(AssetServicePreset.HydrostaticTwelveYear, 12)]
        public void WE1_presets_are_yearly_intervals(AssetServicePreset preset, int years) => AssetServicePresets.Years(preset).Should().Be(years);
        [Test]
        public async Task Sweeps_with_nothing_due_do_not_call_billing()
        {
            Maintenance(); var input = Schedule(); input.AnchorLocal = _maintenanceClock.Utc.AddDays(30);
            await _service.SaveRecurrenceAsync(_actor, input); await Assigned();
            _access.Invocations.Clear();
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(0);
            (await _service.EscalateMaintenanceAsync(77)).Escalated.Should().Be(0);
            _access.Verify(a => a.CanUseMaintenanceAsync(It.IsAny<int>()), Times.Never(), "an active schedule or open order with nothing due must not cost a Billing API call every sweep");
            _maintenanceClock.Utc = _maintenanceClock.Utc.AddDays(28);
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(1);
            _access.Verify(a => a.CanUseMaintenanceAsync(77), Times.AtLeastOnce());
        }
        [Test]
        public async Task Pending_part_witness_blocks_terminal_transitions_and_preserves_open_order()
        {
            Maintenance(); var id = await Assigned(); await Transition(id, WorkOrderStatus.InProgress);
            var part = new WorkOrderPart { WorkOrderId = id, DepartmentId = 77, CreatedBy = "manager", InventoryOperationId = Guid.NewGuid().ToString("D") };
            await _store.AllocateAsync(part); part.Content = "{}"; await _store.WriteAsync(part);
            (await FluentActions.Awaiting(() => Transition(id, WorkOrderStatus.Completed)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("InventoryWitnessPending");
            _store.All<WorkOrder>().Single().Status.Should().Be((int)WorkOrderStatus.InProgress);
        }
        [Test]
        public void Calendar_clamps_month_end_and_leap_year_and_dst_has_one_occurrence()
        {
            var row = new WorkOrderRecurrence { Calendar = (int)MaintenanceCalendar.Monthly, Interval = 1, AnchorLocal = new(2024, 1, 31, 9, 0, 0), TimeZoneId = "UTC" };
            WorkOrdersService.NextMaintenanceDue(row, new DateTime(2024, 2, 1)).Should().Be(new DateTime(2024, 2, 29, 9, 0, 0, DateTimeKind.Utc));
            WorkOrdersService.NextMaintenanceDue(row, new DateTime(2024, 3, 1)).Should().Be(new DateTime(2024, 3, 31, 9, 0, 0, DateTimeKind.Utc));
            WorkOrdersService.MaintenanceUtc(new DateTime(2026, 3, 8, 2, 30, 0), "America/New_York").Should().Be(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc));
            WorkOrdersService.MaintenanceUtc(new DateTime(2026, 11, 1, 1, 30, 0), "America/New_York").Should().Be(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc));
        }
        [Test]
        public void Service_window_and_blackout_preserve_the_unadjusted_due()
        {
            var row = new WorkOrderRecurrence { TimeZoneId = "UTC", ServiceWeekdays = 62, ServiceStartMinute = 540, ServiceEndMinute = 1020, BlackoutFrom = new(2026,9,14), BlackoutUntil = new(2026,9,15) };
            WorkOrdersService.ServiceDue(row, new DateTime(2026,9,12,12,0,0,DateTimeKind.Utc)).Should().Be(new DateTime(2026,9,16,9,0,0,DateTimeKind.Utc));
        }
        private sealed partial class Store
        {
            public List<UnitState> UnitStates { get; } = new();
            public Task<List<T>> QueryMaintenanceAsync<T>(int d, string field = null, object value = null, int skip = 0, bool pendingOnly = false) where T : WorkOrderRow =>
                Task.FromResult(All<T>().Where(r => r.DepartmentId == d && (field == null || Equals(typeof(T).GetProperty(field).GetValue(r), value)) && (!pendingOnly || r is WorkOrderRecurrence { IsActive: true } || r is WorkOrderFailureIntent { ProcessedOn: null })).Skip(skip).Take(500).ToList());
            public Task<List<int>> MaintenanceDepartmentsAsync(int after) => Task.FromResult(new List<int>());
            public Task<List<WorkOrder>> OverdueAsync(int d, DateTime now) => Task.FromResult(All<WorkOrder>().Where(r => r.DepartmentId == d && r.Status < 5 && r.EscalatedOn == null && r.DueOn?.AddMinutes(r.EscalateAfterMinutes) < now).ToList());
            public Task<WorkOrderFailureIntent> FailureAsync(int d, string completion, string item) => Task.FromResult(All<WorkOrderFailureIntent>().SingleOrDefault(r => r.DepartmentId == d && r.CompletionId == completion && r.ItemId == item));
            public Task<List<WorkOrderSafetyHold>> ActiveHoldsAsync(int d, int? unit, string asset) => Task.FromResult(All<WorkOrderSafetyHold>().Where(r => r.DepartmentId == d && r.ReleasedOn == null && (unit.HasValue && r.UnitId == unit || asset != null && r.AssetId == asset)).ToList());
            public Task LockUnitAsync(int d, int unit) => Task.CompletedTask;
            public Task<int> LastAppendedUnitStateIdAsync(int d, int unit) => Task.FromResult(UnitStates.Where(r => r.UnitId == unit).Select(r => r.UnitStateId).DefaultIfEmpty().Max());
            public Task<UnitState> LatestUnitStateAsync(int d, int unit) => Task.FromResult(UnitStates.Where(r => r.UnitId == unit).OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.UnitStateId).FirstOrDefault());
            public Task<int> AppendUnitStateAsync(int d, int unit, int state, DateTime now, string setByUserId = null) { var id = UnitStates.Select(r => r.UnitStateId).DefaultIfEmpty().Max() + 1; UnitStates.Add(new UnitState { UnitStateId = id, UnitId = unit, State = state, Timestamp = now }); return Task.FromResult(id); }
        }
    }
}
