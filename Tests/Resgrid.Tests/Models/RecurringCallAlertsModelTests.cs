using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// Recurring calls, second part (M0320): who counts as available when a call goes out, the schedule's validation of the new
	/// settings, the workflow trigger each event maps to, and the audit wording.
	/// </summary>
	[TestFixture]
	public class RecurringCallAlertsModelTests
	{
		private static readonly Dictionary<int, int> NoCustom = new Dictionary<int, int>();

		[TestCase(UnitStateTypes.Available, true)]
		[TestCase(UnitStateTypes.Delayed, true)]
		[TestCase(UnitStateTypes.Responding, false)]
		[TestCase(UnitStateTypes.OnScene, false)]
		[TestCase(UnitStateTypes.OutOfService, false)]
		[TestCase(UnitStateTypes.Unavailable, false)]
		[TestCase(UnitStateTypes.Released, true)]
		public void A_unit_is_available_unless_committed_or_out_of_service(UnitStateTypes state, bool available)
		{
			RecurringCallResourceAvailability.IsUnitAvailable(new UnitState { UnitId = 1, State = (int)state }, NoCustom).Should().Be(available);
		}

		[Test]
		public void A_unit_with_no_status_or_a_status_without_a_meaning_counts_as_available()
		{
			RecurringCallResourceAvailability.IsUnitAvailable(null, NoCustom).Should().BeTrue();
			// Custom option 900 with no base type (-1): cannot be judged, so it never holds a call up.
			RecurringCallResourceAvailability.IsUnitAvailable(new UnitState { State = 900 }, new Dictionary<int, int> { [900] = -1 }).Should().BeTrue();
		}

		[Test]
		public void A_member_is_unavailable_when_their_staffing_says_so()
		{
			RecurringCallResourceAvailability.IsPersonAvailable(null, new UserState { State = (int)UserStateTypes.Unavailable }, NoCustom, NoCustom, null).Should().BeFalse();
			RecurringCallResourceAvailability.IsPersonAvailable(null, new UserState { State = (int)UserStateTypes.OnShift }, NoCustom, NoCustom, null).Should().BeTrue();
			RecurringCallResourceAvailability.IsPersonAvailable(null, null, NoCustom, NoCustom, null).Should().BeTrue("no status and no staffing on file");
		}

		[Test]
		public void A_member_responding_to_another_call_is_unavailable()
		{
			RecurringCallResourceAvailability.IsPersonAvailable(new ActionLog { ActionTypeId = (int)ActionTypes.RespondingToScene }, null, NoCustom, NoCustom, null)
				.Should().BeFalse();
		}

		[Test]
		public void Custom_status_options_are_read_by_their_meaning()
		{
			var states = new List<CustomState>
			{
				new CustomState { Type = (int)CustomStateTypes.Unit, Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = 40, BaseType = 7 } } },
				new CustomState { Type = (int)CustomStateTypes.Staffing, Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = 41, BaseType = 2 } } }
			};

			RecurringCallResourceAvailability.CustomBaseTypes(states, CustomStateTypes.Unit).Should().Equal(new Dictionary<int, int> { [40] = 7 });
			RecurringCallResourceAvailability.CustomBaseTypes(states, CustomStateTypes.Staffing).Should().Equal(new Dictionary<int, int> { [41] = 2 });
			RecurringCallResourceAvailability.CustomBaseTypes(states, CustomStateTypes.Personnel).Should().BeEmpty();
		}

		private static RecurringCall Valid()
		{
			return new RecurringCall
			{
				Name = "IFT",
				Frequency = (int)RecurringCallFrequencies.Daily,
				RepeatInterval = 1,
				Times = "08:00",
				StartsOn = new DateTime(2026, 11, 17),
				LeadTimeMinutes = 1440
			};
		}

		[Test]
		public void Dispatcher_mode_ignores_the_lead_time_but_checks_the_alert_window()
		{
			var series = Valid();
			series.CreationMode = (int)RecurringCallCreationModes.Dispatcher;
			series.LeadTimeMinutes = 0;
			series.ManualAlertMinutes = 15;
			RecurringCallRecurrence.Validate(series).Should().BeEmpty();

			series.ManualAlertMinutes = RecurringCallRecurrence.MaxManualAlertMinutes + 1;
			RecurringCallRecurrence.Validate(series).Should().Equal(RecurringCallRecurrence.Errors.ManualAlertInvalid);

			series.CreationMode = 9;
			RecurringCallRecurrence.Validate(series).Should().Equal(RecurringCallRecurrence.Errors.CreationModeInvalid);
		}

		[TestCase("pat.lee@mercy.example", true)]
		[TestCase("pat.lee", false)]
		[TestCase("pat@lee", false)]
		[TestCase("a b@mercy.example", false)]
		public void The_schedule_contact_email_must_look_like_one(string email, bool valid)
		{
			var series = Valid();
			series.ScheduleContactEmail = email;

			RecurringCallRecurrence.Validate(series).Contains(RecurringCallRecurrence.Errors.ContactEmailInvalid).Should().Be(!valid);
		}

		[Test]
		public void Each_event_maps_to_its_workflow_trigger()
		{
			new RecurringCallScheduleEvent { Change = RecurringCallScheduleChanges.Created }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallScheduleCreated);
			new RecurringCallScheduleEvent { Change = RecurringCallScheduleChanges.Updated }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallScheduleUpdated);
			new RecurringCallScheduleEvent { Change = RecurringCallScheduleChanges.Paused }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallSchedulePaused);
			new RecurringCallScheduleEvent { Change = RecurringCallScheduleChanges.Resumed }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallScheduleResumed);
			new RecurringCallScheduleEvent { Change = RecurringCallScheduleChanges.Deleted }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallScheduleDeleted);
			new RecurringCallOccurrenceEvent { Change = RecurringCallOccurrenceChanges.Created }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallOccurrenceCreated);
			new RecurringCallOccurrenceEvent { Change = RecurringCallOccurrenceChanges.Skipped }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallOccurrenceSkipped);
			new RecurringCallOccurrenceEvent { Change = RecurringCallOccurrenceChanges.Dispatched }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallOccurrenceDispatched);
			new RecurringCallAlertEvent { Raised = true }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallAlertRaised);
			new RecurringCallAlertEvent { Raised = false }.Trigger.Should().Be(WorkflowTriggerEventType.RecurringCallAlertResolved);
		}

		[Test]
		public void An_alert_detail_round_trips_and_a_broken_one_reads_as_empty()
		{
			var alert = new RecurringCallAlert();
			alert.SetDetail(new RecurringCallAlertDetail { Reason = RecurringCallAlertReasons.NoneAvailable, UnavailableUnitIds = new List<int> { 3 } });

			alert.GetDetail().Reason.Should().Be(RecurringCallAlertReasons.NoneAvailable);
			alert.GetDetail().UnavailableUnitIds.Should().Equal(3);
			alert.DetailJson.Should().NotContain("UnavailableUserIds", "empty parts are left out");

			new RecurringCallAlert { DetailJson = "{not json" }.GetDetail().Reason.Should().BeNull();
		}

		[Test]
		public void The_audit_log_says_who_did_what_to_which_schedule()
		{
			var paused = new Resgrid.Model.Events.AuditEvent
			{
				Type = AuditLogTypes.RecurringCallPaused,
				After = "{\"RecurringCallId\":12,\"Name\":\"Dialysis transfer\",\"PauseReason\":\"Holidays\"}"
			};
			AuditQueueLogic.GetRecurringCallAuditMessage(paused, "Pat Lee").Should().Be("Pat Lee paused recurring call Dialysis transfer (Holidays)");

			var skipped = new Resgrid.Model.Events.AuditEvent
			{
				Type = AuditLogTypes.RecurringCallOccurrenceSkipped,
				After = "{\"RecurringCallId\":12,\"Name\":\"Dialysis transfer\",\"OccurrenceOn\":\"2026-11-18T13:00:00Z\"}"
			};
			AuditQueueLogic.GetRecurringCallAuditMessage(skipped, "Pat Lee").Should().Be("Pat Lee skipped the occurrence due 2026-11-18 13:00 UTC of recurring call Dialysis transfer");

			var deleted = new Resgrid.Model.Events.AuditEvent { Type = AuditLogTypes.RecurringCallDeleted, Before = "{\"Name\":\"Dialysis transfer\"}" };
			AuditQueueLogic.GetRecurringCallAuditMessage(deleted, "System").Should().Be("System deleted recurring call Dialysis transfer");
		}

		[Test]
		public void An_update_audit_names_the_schedule_whose_settings_changed()
		{
			// Two schedules can share a name and the same change: the id says which one it was.
			var updated = new Resgrid.Model.Events.AuditEvent
			{
				Type = AuditLogTypes.RecurringCallUpdated,
				Before = "{\"RecurringCallId\":12,\"Name\":\"Dialysis transfer\",\"Priority\":1}",
				After = "{\"RecurringCallId\":12,\"Name\":\"Dialysis transfer\",\"Priority\":2}"
			};

			var data = AuditQueueLogic.GetRecurringCallUpdatedAuditData(updated);

			data.Should().StartWith("RecurringCallId: 12");
			data.Should().Contain("Priority: 1 -> 2");
		}

		[TestCase(WorkflowTriggerEventType.RecurringCallScheduleCreated, "Created", true)]
		[TestCase(WorkflowTriggerEventType.RecurringCallScheduleUpdated, "Updated", true)]
		[TestCase(WorkflowTriggerEventType.RecurringCallSchedulePaused, "Paused", false)]
		[TestCase(WorkflowTriggerEventType.RecurringCallScheduleResumed, "Resumed", true)]
		[TestCase(WorkflowTriggerEventType.RecurringCallScheduleDeleted, "Deleted", false)]
		public void The_schedule_preview_matches_what_the_live_trigger_sends(WorkflowTriggerEventType trigger, string change, bool hasNext)
		{
			var data = (Scriban.Runtime.ScriptObject)Resgrid.Services.WorkflowSampleDataGenerator.GenerateSampleData(trigger);
			var schedule = (Scriban.Runtime.ScriptObject)data["recurring_call"];

			schedule["change"].Should().Be(change);
			(schedule["next_occurrence_on"] != null).Should().Be(hasNext, "a paused or deleted schedule has no next occurrence");
		}

		[Test]
		public void A_skipped_occurrence_preview_has_no_call_and_nobody_assigned()
		{
			var data = (Scriban.Runtime.ScriptObject)Resgrid.Services.WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.RecurringCallOccurrenceSkipped);
			var occurrence = (Scriban.Runtime.ScriptObject)data["occurrence"];

			occurrence["call_id"].Should().Be(0);
			occurrence["unit_count"].Should().Be(0);
			occurrence["by_dispatcher"].Should().Be(true, "a skip is always a dispatcher's, as the live event says");
		}

		[TestCase(WorkflowTriggerEventType.RecurringCallOccurrenceCreated)]
		[TestCase(WorkflowTriggerEventType.RecurringCallOccurrenceDispatched)]
		public void A_created_or_dispatched_occurrence_preview_is_the_workers(WorkflowTriggerEventType trigger)
		{
			var data = (Scriban.Runtime.ScriptObject)Resgrid.Services.WorkflowSampleDataGenerator.GenerateSampleData(trigger);

			((Scriban.Runtime.ScriptObject)data["occurrence"])["by_dispatcher"].Should().Be(false);
		}

		[Test]
		public void The_schedule_preview_next_occurrence_falls_between_its_start_and_end()
		{
			var data = (Scriban.Runtime.ScriptObject)Resgrid.Services.WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.RecurringCallScheduleUpdated);
			var schedule = (Scriban.Runtime.ScriptObject)data["recurring_call"];

			var startsOn = DateTime.ParseExact((string)schedule["starts_on"], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
			var endsOn = DateTime.ParseExact((string)schedule["ends_on"], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
			var next = (DateTime)schedule["next_occurrence_on"];

			next.Date.Should().BeOnOrAfter(startsOn).And.BeOnOrBefore(endsOn);
		}

		[Test]
		public void The_schedule_preview_next_occurrence_is_one_of_its_times_in_the_sample_departments_zone()
		{
			var data = (Scriban.Runtime.ScriptObject)Resgrid.Services.WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.RecurringCallScheduleUpdated);
			var schedule = (Scriban.Runtime.ScriptObject)data["recurring_call"];
			var times = ((string)schedule["times"]).Split(',');
			var eastern = new Department { TimeZone = "Eastern Standard Time" };

			var next = (DateTime)schedule["next_occurrence_on"];
			var local = RecurringCallRecurrence.ToLocal(next, eastern);

			times.Should().Contain(local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), "the preview shows the occurrence the way the live event does");

			var occurrence = (Scriban.Runtime.ScriptObject)((Scriban.Runtime.ScriptObject)Resgrid.Services.WorkflowSampleDataGenerator
				.GenerateSampleData(WorkflowTriggerEventType.RecurringCallOccurrenceCreated))["occurrence"];
			times.Should().Contain(RecurringCallRecurrence.ToLocal((DateTime)occurrence["occurrence_on"], eastern).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
		}
	}
}
