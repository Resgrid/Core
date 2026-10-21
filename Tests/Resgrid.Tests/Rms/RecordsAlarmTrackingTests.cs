using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Smoke / CO alarm tracking (M0284): per-type counts on CRR activities, the call link, the alarm request queue
	/// (Open, then Completed into an activity or Declined), and alarm history following an occupancy merge.
	/// </summary>
	[TestFixture]
	public class RecordsAlarmTrackingTests
	{
		private const int OwnCall = 501;
		private const int ForeignCall = 502;
		private const string OwnContact = "contact-own";
		private const string ForeignContact = "contact-foreign";

		private RmsPreventionHarness _h;

		[SetUp]
		public void SetUp()
		{
			_h = new RmsPreventionHarness();
			var calls = new Dictionary<int, Call>
			{
				[OwnCall] = new Call { CallId = OwnCall, DepartmentId = Dept, Number = "26-41", Address = "4 Elm St" },
				[ForeignCall] = new Call { CallId = ForeignCall, DepartmentId = Dept + 1, Number = "26-99" }
			};
			_h.Calls.Setup(c => c.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => calls.TryGetValue(Convert.ToInt32(id), out var call) ? call : null);
			_h.Authorization.Setup(a => a.CanReadSourceCallAsync(It.IsAny<string>(), Dept, It.IsAny<Call>())).ReturnsAsync((string u, int d, Call call) => call != null && call.DepartmentId == d);
			var contacts = new Dictionary<string, Contact>
			{
				[OwnContact] = new Contact { ContactId = OwnContact, DepartmentId = Dept },
				[ForeignContact] = new Contact { ContactId = ForeignContact, DepartmentId = Dept + 1 }
			};
			_h.Contacts.Setup(c => c.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => contacts.TryGetValue(id?.ToString() ?? "", out var contact) ? contact : null);
		}

		private Task<RmsCrrAlarmRequest> OpenRequestAsync(string location = "4 Elm St", int smoke = 1, int co = 0, int combo = 0, RmsAlarmReason reason = RmsAlarmReason.Faulty)
			=> _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { LocationText = location, SmokeAlarmsRequested = smoke, CoAlarmsRequested = co, ComboAlarmsRequested = combo, Reason = (int)reason });

		// ---- CRR activity counts and call link -------------------------------------------------------------------------

		[Test]
		public async Task Activity_keeps_smoke_co_and_combination_counts_apart()
		{
			var saved = await _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity
			{
				Kind = (int)RmsCrrActivityKind.SmokeAlarmInstallation, Title = "Elm St visit", OccurredOn = DateTime.UtcNow,
				SmokeAlarmsInstalled = 2, CoAlarmsInstalled = 1, ComboAlarmsInstalled = 3, AlarmReason = (int)RmsAlarmReason.Expired
			});

			saved.SmokeAlarmsInstalled.Should().Be(2); saved.CoAlarmsInstalled.Should().Be(1); saved.ComboAlarmsInstalled.Should().Be(3);
			saved.AlarmsInstalledTotal().Should().Be(6);
			saved.AlarmReason.Should().Be((int)RmsAlarmReason.Expired);
			saved.RmsCrrAlarmRequestId.Should().BeNull("only completing a request links one");
		}

		[Test]
		public async Task Activity_without_alarms_carries_no_alarm_reason_and_negative_counts_are_zero()
		{
			var saved = await _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { Title = "School talk", SmokeAlarmsInstalled = -3, AlarmReason = (int)RmsAlarmReason.Faulty });
			saved.SmokeAlarmsInstalled.Should().Be(0);
			saved.AlarmReason.Should().Be((int)RmsAlarmReason.Unspecified);
		}

		[Test]
		public async Task Activity_refuses_an_implausible_unit_count()
		{
			Func<Task> save = () => _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { Title = "Typo", CoAlarmsInstalled = 100000 });
			await save.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task Activity_links_a_readable_call_of_the_department_and_refuses_another_departments_call()
		{
			var saved = await _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { Title = "Left on call", CallId = OwnCall, SmokeAlarmsInstalled = 1 });
			saved.CallId.Should().Be(OwnCall);

			Func<Task> foreign = () => _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { Title = "Left on call", CallId = ForeignCall, SmokeAlarmsInstalled = 1 });
			await foreign.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task An_unchanged_call_link_is_not_checked_again_on_update()
		{
			var saved = await _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { Title = "Left on call", CallId = OwnCall, SmokeAlarmsInstalled = 1 });
			_h.Authorization.Setup(a => a.CanReadSourceCallAsync(It.IsAny<string>(), Dept, It.IsAny<Call>())).ReturnsAsync(false);

			var updated = await _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { RmsCrrActivityId = saved.RmsCrrActivityId, Title = "Left on call (2)", CallId = OwnCall, SmokeAlarmsInstalled = 2 });
			updated.SmokeAlarmsInstalled.Should().Be(2);
		}

		[Test]
		public async Task Summary_totals_every_alarm_type_reason_and_open_requests()
		{
			var now = DateTime.UtcNow;
			_h.Crr.Rows.Add(new RmsCrrActivity { RmsCrrActivityId = "a1", DepartmentId = Dept, Kind = (int)RmsCrrActivityKind.SmokeAlarmInstallation, OccurredOn = now.AddDays(-3), SmokeAlarmsInstalled = 2, ComboAlarmsInstalled = 1, AlarmReason = (int)RmsAlarmReason.Faulty });
			_h.Crr.Rows.Add(new RmsCrrActivity { RmsCrrActivityId = "a2", DepartmentId = Dept, Kind = (int)RmsCrrActivityKind.SmokeAlarmInstallation, OccurredOn = now.AddDays(-2), CoAlarmsInstalled = 1, AlarmReason = (int)RmsAlarmReason.Missing });
			_h.Crr.Rows.Add(new RmsCrrActivity { RmsCrrActivityId = "a3", DepartmentId = Dept, Kind = (int)RmsCrrActivityKind.SchoolProgram, OccurredOn = now.AddDays(-1), AudienceCount = 40 });
			await OpenRequestAsync();
			await OpenRequestAsync("9 Oak Ave");

			var summary = await _h.CrrService.GetSummaryAsync(Dept, Member, now.AddDays(-30), now.AddDays(1));

			summary.SmokeAlarmsInstalled.Should().Be(2); summary.CoAlarmsInstalled.Should().Be(1); summary.ComboAlarmsInstalled.Should().Be(1);
			summary.AlarmsInstalled.Should().Be(4);
			summary.AlarmVisits.Should().Be(2, "the school program left no alarm");
			summary.AlarmsByReason.Should().Contain(new KeyValuePair<int, int>((int)RmsAlarmReason.Faulty, 3)).And.Contain(new KeyValuePair<int, int>((int)RmsAlarmReason.Missing, 1));
			summary.OpenAlarmRequests.Should().Be(2);
			summary.AlarmRequestsByStatus.Should().Contain(new KeyValuePair<int, int>((int)RmsAlarmRequestStatus.Open, 2));
		}

		// ---- Alarm requests ---------------------------------------------------------------------------------------------

		[Test]
		public async Task New_request_is_numbered_open_and_audited()
		{
			var request = await OpenRequestAsync();

			request.RequestNumber.Should().Be($"ALM-{DateTime.UtcNow.Year}-0001");
			request.Status.Should().Be((int)RmsAlarmRequestStatus.Open);
			request.RowVersion.Should().Be(1);
			_h.Audits.Rows.Should().ContainSingle(a => a.Purpose == "Alarm request recorded" && a.CorrelationId == request.RmsCrrAlarmRequestId);
			(await OpenRequestAsync("9 Oak Ave")).RequestNumber.Should().Be($"ALM-{DateTime.UtcNow.Year}-0002");
		}

		[Test]
		public async Task Request_needs_somewhere_to_go()
		{
			Func<Task> save = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { SmokeAlarmsRequested = 1 });
			await save.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task Request_at_an_occupancy_takes_its_address_and_coordinates_when_none_is_given()
		{
			var occupancy = _h.SeedOccupancy(address: "12 River Rd", lat: 45.5m, lon: -122.6m);
			var request = await _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsOccupancyId = occupancy.RmsOccupancyId, CoAlarmsRequested = 1 });

			request.LocationText.Should().Be("12 River Rd");
			request.Latitude.Should().Be(45.5m); request.Longitude.Should().Be(-122.6m);
		}

		[Test]
		public async Task Request_refuses_a_contact_call_or_assignee_outside_the_department()
		{
			Func<Task> contact = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { LocationText = "4 Elm St", RequesterContactId = ForeignContact });
			Func<Task> call = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { LocationText = "4 Elm St", CallId = ForeignCall });
			Func<Task> assignee = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { LocationText = "4 Elm St", AssignedToUserId = Outsider });
			await contact.Should().ThrowAsync<ArgumentException>();
			await call.Should().ThrowAsync<ArgumentException>();
			await assignee.Should().ThrowAsync<ArgumentException>();

			var ok = await _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RequesterContactId = OwnContact, CallId = OwnCall, AssignedToUserId = Member, Source = (int)RmsAlarmRequestSource.DuringCall });
			ok.RequesterContactId.Should().Be(OwnContact); ok.CallId.Should().Be(OwnCall); ok.AssignedToUserId.Should().Be(Member);
		}

		[Test]
		public async Task Members_without_the_prevention_permission_can_read_but_not_write()
		{
			var request = await OpenRequestAsync();
			(await _h.AlarmRequestsService.GetAsync(Dept, Member, request.RmsCrrAlarmRequestId)).Should().NotBeNull();

			Func<Task> save = () => _h.AlarmRequestsService.SaveAsync(Dept, Member, new RmsCrrAlarmRequest { LocationText = "4 Elm St" });
			Func<Task> complete = () => _h.AlarmRequestsService.CompleteAsync(Dept, Member, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId, SmokeAlarmsInstalled = 1 });
			await save.Should().ThrowAsync<UnauthorizedAccessException>();
			await complete.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Requests_are_off_with_the_crr_module()
		{
			_h.DisabledFlags.Add(FeatureFlagKeys.RecordsPreventionCrr);
			Func<Task> list = () => _h.AlarmRequestsService.ListAsync(Dept, Admin, null);
			await list.Should().ThrowAsync<RecordsModuleDisabledException>();
		}

		[Test]
		public async Task Completing_records_one_activity_linked_both_ways_and_closes_the_request()
		{
			var occupancy = _h.SeedOccupancy();
			var request = await _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsOccupancyId = occupancy.RmsOccupancyId, CallId = OwnCall, SmokeAlarmsRequested = 1, Reason = (int)RmsAlarmReason.Faulty });

			var done = await _h.AlarmRequestsService.CompleteAsync(Dept, Admin, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId, SmokeAlarmsInstalled = 1, ComboAlarmsInstalled = 1, HoursSpent = 0.5m });

			done.Request.Status.Should().Be((int)RmsAlarmRequestStatus.Completed);
			done.Request.ClosedByUserId.Should().Be(Admin);
			done.Request.RmsCrrActivityId.Should().Be(done.Activity.RmsCrrActivityId);
			done.Activity.RmsCrrAlarmRequestId.Should().Be(request.RmsCrrAlarmRequestId);
			done.Activity.Kind.Should().Be((int)RmsCrrActivityKind.SmokeAlarmInstallation);
			done.Activity.AlarmsInstalledTotal().Should().Be(2);
			done.Activity.AlarmReason.Should().Be((int)RmsAlarmReason.Faulty, "the request's reason is used when the crew gives none");
			done.Activity.RmsOccupancyId.Should().Be(occupancy.RmsOccupancyId);
			done.Activity.CallId.Should().Be(OwnCall);
			_h.Crr.Rows.Should().ContainSingle();
		}

		[Test]
		public async Task Completing_with_no_alarms_left_is_refused()
		{
			var request = await OpenRequestAsync();
			Func<Task> complete = () => _h.AlarmRequestsService.CompleteAsync(Dept, Admin, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId });
			await complete.Should().ThrowAsync<ArgumentException>();
			request.Status.Should().Be((int)RmsAlarmRequestStatus.Open);
		}

		[Test]
		public async Task A_second_completion_records_nothing_more()
		{
			var request = await OpenRequestAsync();
			await _h.AlarmRequestsService.CompleteAsync(Dept, Admin, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId, SmokeAlarmsInstalled = 1 });

			Func<Task> again = () => _h.AlarmRequestsService.CompleteAsync(Dept, Admin2, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId, SmokeAlarmsInstalled = 1 });
			await again.Should().ThrowAsync<InvalidOperationException>();
			_h.Crr.Rows.Should().ContainSingle();
		}

		[Test]
		public async Task A_completion_that_loses_the_row_claim_inserts_no_activity()
		{
			var request = await OpenRequestAsync();
			_h.AlarmRequests.RefuseNextClaim = true;

			Func<Task> complete = () => _h.AlarmRequestsService.CompleteAsync(Dept, Admin, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId, SmokeAlarmsInstalled = 1 });
			await complete.Should().ThrowAsync<InvalidOperationException>();
			_h.Crr.Rows.Should().BeEmpty();
			_h.UnitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
		}

		[Test]
		public async Task Declining_needs_a_reason_and_a_declined_request_can_be_reopened()
		{
			var request = await OpenRequestAsync();
			Func<Task> noReason = () => _h.AlarmRequestsService.DeclineAsync(Dept, Admin, request.RmsCrrAlarmRequestId, RmsAlarmRequestDeclineReason.None, null);
			await noReason.Should().ThrowAsync<ArgumentException>();

			var declined = await _h.AlarmRequestsService.DeclineAsync(Dept, Admin, request.RmsCrrAlarmRequestId, RmsAlarmRequestDeclineReason.UnableToContact, "Three attempts");
			declined.Status.Should().Be((int)RmsAlarmRequestStatus.Declined);
			declined.DeclineReason.Should().Be((int)RmsAlarmRequestDeclineReason.UnableToContact);
			declined.ClosedOn.Should().NotBeNull();

			var reopened = await _h.AlarmRequestsService.ReopenAsync(Dept, Admin, request.RmsCrrAlarmRequestId);
			reopened.Status.Should().Be((int)RmsAlarmRequestStatus.Open);
			reopened.DeclineReason.Should().Be((int)RmsAlarmRequestDeclineReason.None);
			reopened.ClosedOn.Should().BeNull();
		}

		[Test]
		public async Task Closed_requests_cannot_be_edited_and_a_completed_one_cannot_be_reopened_or_deleted()
		{
			var request = await OpenRequestAsync();
			await _h.AlarmRequestsService.CompleteAsync(Dept, Admin, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId, SmokeAlarmsInstalled = 1 });

			Func<Task> edit = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsCrrAlarmRequestId = request.RmsCrrAlarmRequestId, LocationText = "Somewhere else" });
			Func<Task> reopen = () => _h.AlarmRequestsService.ReopenAsync(Dept, Admin, request.RmsCrrAlarmRequestId);
			Func<Task> delete = () => _h.AlarmRequestsService.DeleteAsync(Dept, Admin, request.RmsCrrAlarmRequestId);
			await edit.Should().ThrowAsync<InvalidOperationException>();
			await reopen.Should().ThrowAsync<InvalidOperationException>();
			await delete.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task An_edit_from_a_stale_form_is_refused()
		{
			var request = await OpenRequestAsync();
			await _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsCrrAlarmRequestId = request.RmsCrrAlarmRequestId, RowVersion = 1, LocationText = "4 Elm St, rear" });
			request.RowVersion.Should().Be(2);

			Func<Task> stale = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin2, new RmsCrrAlarmRequest { RmsCrrAlarmRequestId = request.RmsCrrAlarmRequestId, RowVersion = 1, LocationText = "4 Elm St, front" });
			await stale.Should().ThrowAsync<InvalidOperationException>();
			request.LocationText.Should().Be("4 Elm St, rear");
		}

		[Test]
		public async Task Deleting_the_activity_reopens_the_request_it_completed()
		{
			var request = await OpenRequestAsync();
			var done = await _h.AlarmRequestsService.CompleteAsync(Dept, Admin, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId, CoAlarmsInstalled = 1 });

			await _h.CrrService.DeleteAsync(Dept, Admin, done.Activity.RmsCrrActivityId);

			done.Activity.DeletedOn.Should().NotBeNull();
			request.Status.Should().Be((int)RmsAlarmRequestStatus.Open);
			request.RmsCrrActivityId.Should().BeNull();
			request.ClosedOn.Should().BeNull();
		}

		[Test]
		public async Task An_activity_that_completed_a_request_keeps_an_alarm_and_the_request_occupancy()
		{
			var occupancy = _h.SeedOccupancy();
			var other = _h.SeedOccupancy("Next door", "6 Elm St");
			var request = await _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsOccupancyId = occupancy.RmsOccupancyId, SmokeAlarmsRequested = 1 });
			var done = await _h.AlarmRequestsService.CompleteAsync(Dept, Admin, new AlarmRequestCompletionInput { RequestId = request.RmsCrrAlarmRequestId, SmokeAlarmsInstalled = 1 });
			var id = done.Activity.RmsCrrActivityId;

			Func<Task> none = () => _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { RmsCrrActivityId = id, Title = "Alarm visit", RmsOccupancyId = occupancy.RmsOccupancyId });
			Func<Task> moved = () => _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { RmsCrrActivityId = id, Title = "Alarm visit", RmsOccupancyId = other.RmsOccupancyId, SmokeAlarmsInstalled = 1 });
			await none.Should().ThrowAsync<ArgumentException>();
			await moved.Should().ThrowAsync<InvalidOperationException>();
			done.Activity.SmokeAlarmsInstalled.Should().Be(1);
			done.Activity.RmsOccupancyId.Should().Be(occupancy.RmsOccupancyId);

			var corrected = await _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { RmsCrrActivityId = id, Title = "Alarm visit", RmsOccupancyId = occupancy.RmsOccupancyId, ComboAlarmsInstalled = 2 });
			corrected.AlarmsInstalledTotal().Should().Be(2);
			request.Status.Should().Be((int)RmsAlarmRequestStatus.Completed);
		}

		[Test]
		public async Task Saving_a_request_id_that_does_not_load_is_refused_instead_of_creating_one()
		{
			var request = await OpenRequestAsync();
			await _h.AlarmRequestsService.DeleteAsync(Dept, Admin, request.RmsCrrAlarmRequestId);

			Func<Task> deleted = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsCrrAlarmRequestId = request.RmsCrrAlarmRequestId, LocationText = "4 Elm St" });
			Func<Task> unknown = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsCrrAlarmRequestId = Guid.NewGuid().ToString(), LocationText = "4 Elm St" });
			await deleted.Should().ThrowAsync<ArgumentException>();
			await unknown.Should().ThrowAsync<ArgumentException>();
			_h.AlarmRequests.Rows.Should().ContainSingle();
		}

		[Test]
		public async Task Open_and_declined_requests_can_be_deleted()
		{
			var request = await OpenRequestAsync();
			await _h.AlarmRequestsService.DeleteAsync(Dept, Admin, request.RmsCrrAlarmRequestId);

			request.DeletedOn.Should().NotBeNull();
			(await _h.AlarmRequestsService.GetAsync(Dept, Admin, request.RmsCrrAlarmRequestId)).Should().BeNull();
			(await _h.AlarmRequestsService.CountAsync(Dept, Admin, null)).Should().Be(0);
		}

		[Test]
		public async Task Open_queue_lists_oldest_first_and_filters_by_assignee()
		{
			var older = await _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { LocationText = "1 First St", RequestedOn = DateTime.UtcNow.AddDays(-5), AssignedToUserId = Member });
			var newer = await _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { LocationText = "2 Second St", RequestedOn = DateTime.UtcNow.AddDays(-1) });

			var queue = await _h.AlarmRequestsService.ListAsync(Dept, Member, new Resgrid.Model.Repositories.RmsAlarmRequestQuery { Statuses = new List<int> { (int)RmsAlarmRequestStatus.Open }, OldestFirst = true });
			queue.Select(r => r.RmsCrrAlarmRequestId).Should().Equal(older.RmsCrrAlarmRequestId, newer.RmsCrrAlarmRequestId);

			var mine = await _h.AlarmRequestsService.ListAsync(Dept, Member, new Resgrid.Model.Repositories.RmsAlarmRequestQuery { AssignedToUserId = Member });
			mine.Should().ContainSingle(r => r.RmsCrrAlarmRequestId == older.RmsCrrAlarmRequestId);
		}

		// ---- Occupancy merge --------------------------------------------------------------------------------------------

		[Test]
		public async Task Merging_an_occupancy_moves_its_alarm_activity_and_requests_to_the_survivor()
		{
			var source = _h.SeedOccupancy("Old record", "5 Mill Ln");
			var survivor = _h.SeedOccupancy("Mill Lane house", "5 Mill Lane");
			var activity = await _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { Title = "Alarm visit", RmsOccupancyId = source.RmsOccupancyId, SmokeAlarmsInstalled = 2 });
			var request = await _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsOccupancyId = source.RmsOccupancyId, CoAlarmsRequested = 1 });

			await _h.OccupancyService.MergeAsync(Dept, Admin, source.RmsOccupancyId, survivor.RmsOccupancyId);

			activity.RmsOccupancyId.Should().Be(survivor.RmsOccupancyId);
			request.RmsOccupancyId.Should().Be(survivor.RmsOccupancyId);
			(await _h.CrrService.GetForOccupancyAsync(Dept, Member, survivor.RmsOccupancyId)).Should().ContainSingle();
			(await _h.AlarmRequestsService.GetForOccupancyAsync(Dept, Member, survivor.RmsOccupancyId)).Should().ContainSingle();
		}

		[Test]
		public async Task A_merged_occupancy_takes_no_new_alarm_activity_or_request()
		{
			var source = _h.SeedOccupancy("Old record", "5 Mill Ln");
			var survivor = _h.SeedOccupancy("Mill Lane house", "5 Mill Lane");
			await _h.OccupancyService.MergeAsync(Dept, Admin, source.RmsOccupancyId, survivor.RmsOccupancyId);

			Func<Task> activity = () => _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { Title = "Alarm visit", RmsOccupancyId = source.RmsOccupancyId, SmokeAlarmsInstalled = 1 });
			Func<Task> request = () => _h.AlarmRequestsService.SaveAsync(Dept, Admin, new RmsCrrAlarmRequest { RmsOccupancyId = source.RmsOccupancyId, CoAlarmsRequested = 1 });
			await activity.Should().ThrowAsync<ArgumentException>();
			await request.Should().ThrowAsync<ArgumentException>();
			_h.Crr.Rows.Should().BeEmpty();
			_h.AlarmRequests.Rows.Should().BeEmpty();
		}
	}
}
