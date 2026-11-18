using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Mocks;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The page owed to resources an edit adds to a live call (PR #587 F8/F10). An edit pages only what it changed, so when the queue
	/// refuses the page the same edit retried sees nothing new: the page has to outlive the request, be retried, and still go only to
	/// whoever is owed it when it finally goes out.
	/// </summary>
	[TestFixture]
	public class CallBroadcastOutboxServiceTests
	{
		private const int DeptId = 7;
		private const int CallId = 40;
		private const string Medic = "medic-user";
		private const string Driver = "driver-user";
		private const int Engine = 11;
		private const int Ladder = 12;
		private const int Station = 21;
		private const int Paramedics = 31;

		private InMemoryCallBroadcastOutboxRepository _outbox;
		private Mock<ICallsService> _calls;
		private Mock<IUserProfileService> _profiles;
		private Mock<IQueueService> _queue;
		private List<CallQueueItem> _queued;
		private Func<bool> _accept;
		private CallBroadcastOutboxService _service;
		private Call _call;

		[SetUp]
		public void SetUp()
		{
			_outbox = new InMemoryCallBroadcastOutboxRepository();
			_calls = new Mock<ICallsService>();
			_profiles = new Mock<IUserProfileService>();
			_queue = new Mock<IQueueService>();
			_queued = new List<CallQueueItem>();
			_accept = () => true;

			_queue.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.Callback<CallQueueItem, CancellationToken>((cqi, _) => _queued.Add(cqi))
				.ReturnsAsync(() => _accept());

			_profiles.Setup(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>()))
				.ReturnsAsync((List<string> ids) => ids.Select(id => new UserProfile { UserId = id }).ToList());
			_profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new Dictionary<string, UserProfile> { [Medic] = new UserProfile { UserId = Medic }, [Driver] = new UserProfile { UserId = Driver } });

			_call = LiveCall();
			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync((Call c, bool a, bool b, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k) => c);

			_service = new CallBroadcastOutboxService(_outbox, _calls.Object, _profiles.Object, _queue.Object);
		}

		private static Call LiveCall()
		{
			return new Call
			{
				CallId = CallId,
				DepartmentId = DeptId,
				Name = "Structure fire",
				State = (int)CallStates.Active,
				HasBeenDispatched = true,
				Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = Medic }, new CallDispatch { CallId = CallId, UserId = Driver } },
				GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { CallId = CallId, DepartmentGroupId = Station } },
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = CallId, UnitId = Engine }, new CallDispatchUnit { CallId = CallId, UnitId = Ladder } },
				RoleDispatches = new List<CallDispatchRole> { new CallDispatchRole { CallId = CallId, RoleId = Paramedics } },
				PersonnelReleases = new List<CallPersonnelRelease>()
			};
		}

		private Task<CallBroadcastOutboxEntry> RecordMedicAsync() =>
			_service.RecordAsync(_call, new[] { Medic }, null, null, null, "dispatcher");

		[Test]
		public async Task A_redispatched_member_is_paged_and_the_row_ends_sent()
		{
			var recorded = await RecordMedicAsync();

			recorded.Persisted.Should().BeTrue();
			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Pending);

			(await _service.SendPendingForCallAsync(_call, recorded)).Should().Be(1);

			_queued.Should().ContainSingle();
			_queued[0].BroadcastOnlySelectedDispatches.Should().BeTrue();
			_queued[0].BroadcastUserIds.Should().Equal(new[] { Medic });
			_queued[0].Call.Dispatches.Select(x => x.UserId).Should().Equal(new[] { Medic });
			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Sent);
			_outbox.Single().Attempts.Should().Be(1);
		}

		[Test]
		public async Task A_refused_page_stays_pending_and_the_next_save_of_the_call_sends_it()
		{
			_accept = () => false;
			var recorded = await RecordMedicAsync();

			(await _service.SendPendingForCallAsync(_call, recorded)).Should().Be(0);

			var row = _outbox.Single();
			row.State.Should().Be((int)CallBroadcastOutboxStates.Pending);
			row.Attempts.Should().Be(1);
			row.LastError.Should().NotBeNullOrEmpty();
			row.NextAttemptOn.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(30), TimeSpan.FromSeconds(10));

			// The dispatcher saves the same edit again: nothing is new on the call, but the owed page goes now.
			_accept = () => true;
			(await _service.SendPendingForCallAsync(_call)).Should().Be(1);

			_queued.Should().HaveCount(2);
			_queued.Last().BroadcastUserIds.Should().Equal(new[] { Medic });
			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Sent);
		}

		[Test]
		public async Task A_queue_that_throws_is_treated_as_a_refusal()
		{
			_queue.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("broker down"));

			var recorded = await RecordMedicAsync();
			(await _service.SendPendingForCallAsync(_call, recorded)).Should().Be(0);

			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Pending);
		}

		[Test]
		public async Task The_worker_sends_a_due_row_from_the_call_as_stored()
		{
			_accept = () => false;
			var recorded = await RecordMedicAsync();
			await _service.SendPendingForCallAsync(_call, recorded);
			_outbox.Single().NextAttemptOn = DateTime.UtcNow.AddSeconds(-1);

			_accept = () => true;
			(await _service.SendDueAsync(100, CancellationToken.None)).Should().Be(1);

			_calls.Verify(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>()), Times.Once);
			_queued.Last().BroadcastUserIds.Should().Equal(new[] { Medic });
			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Sent);
			_outbox.Single().Attempts.Should().Be(2);
		}

		[Test]
		public async Task The_worker_leaves_a_row_its_edit_may_still_be_sending()
		{
			await RecordMedicAsync();

			(await _service.SendDueAsync(100, CancellationToken.None)).Should().Be(0);

			_queued.Should().BeEmpty("a new row waits out its grace for the edit's own send");
			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Pending);
		}

		[Test]
		public async Task A_row_whose_edit_never_sent_it_goes_out_after_the_grace()
		{
			// The request failed between recording and sending (a cancel notice threw, the process stopped).
			await RecordMedicAsync();
			_outbox.Single().NextAttemptOn = DateTime.UtcNow.AddSeconds(-1);

			(await _service.SendDueAsync(100, CancellationToken.None)).Should().Be(1);

			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Sent);
		}

		[Test]
		public async Task A_member_released_again_before_the_retry_is_not_paged()
		{
			_accept = () => false;
			var recorded = await RecordMedicAsync();
			await _service.SendPendingForCallAsync(_call, recorded);

			_call.PersonnelReleases.Add(new CallPersonnelRelease { CallId = CallId, UserId = Medic.ToUpperInvariant() });
			_accept = () => true;
			(await _service.SendPendingForCallAsync(_call)).Should().Be(0);

			_queued.Should().HaveCount(1, "only the refused attempt reached the queue");
			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Skipped);
		}

		[Test]
		public async Task Only_the_resources_still_on_the_call_are_paged()
		{
			_accept = () => false;
			var recorded = await _service.RecordAsync(_call, new[] { Medic, "someone-removed" }, new[] { Station, 99 }, new[] { Engine, Ladder }, new[] { Paramedics }, "dispatcher");
			await _service.SendPendingForCallAsync(_call, recorded);

			// The ladder is stood down and the role is taken off the call before the retry.
			_call.UnitDispatches.Single(x => x.UnitId == Ladder).ReleasedOn = DateTime.UtcNow;
			_call.RoleDispatches.Clear();
			_accept = () => true;
			await _service.SendPendingForCallAsync(_call);

			var sent = _queued.Last();
			sent.BroadcastUserIds.Should().Equal(new[] { Medic });
			sent.BroadcastGroupIds.Should().Equal(new[] { Station });
			sent.BroadcastUnitIds.Should().Equal(new[] { Engine });
			sent.BroadcastRoleIds.Should().BeEmpty();
			sent.Profiles.Should().HaveCount(2, "a group or unit page carries every profile, as the edit always did");
		}

		[Test]
		public async Task A_member_is_paged_under_the_spelling_of_the_dispatch_row()
		{
			var recorded = await _service.RecordAsync(_call, new[] { Medic.ToUpperInvariant() }, null, null, null, "dispatcher");

			await _service.SendPendingForCallAsync(_call, recorded);

			_queued.Single().BroadcastUserIds.Should().Equal(new[] { Medic });
			_queued.Single().Call.Dispatches.Should().ContainSingle(x => x.UserId == Medic);
		}

		[Test]
		public async Task A_closed_call_pages_nobody()
		{
			_accept = () => false;
			var recorded = await RecordMedicAsync();
			await _service.SendPendingForCallAsync(_call, recorded);

			_call.State = (int)CallStates.Closed;
			_outbox.Single().NextAttemptOn = DateTime.UtcNow.AddSeconds(-1);
			_accept = () => true;
			(await _service.SendDueAsync(100, CancellationToken.None)).Should().Be(0);

			_queued.Should().HaveCount(1);
			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Skipped);
		}

		[Test]
		public async Task A_page_refused_past_its_useful_age_is_dropped()
		{
			_accept = () => false;
			await RecordMedicAsync();
			var row = _outbox.Single();
			row.CreatedOn = DateTime.UtcNow.Subtract(CallBroadcastOutboxService.MaximumAge).AddMinutes(-1);
			row.NextAttemptOn = DateTime.UtcNow.AddSeconds(-1);

			await _service.SendDueAsync(100, CancellationToken.None);

			row.State.Should().Be((int)CallBroadcastOutboxStates.Expired);
			row.CompletedOn.Should().NotBeNull();
		}

		[Test]
		public async Task A_sender_that_stopped_mid_send_is_taken_over_after_its_lease()
		{
			await RecordMedicAsync();
			var row = _outbox.Single();
			row.State = (int)CallBroadcastOutboxStates.Sending;
			row.ClaimToken = "stopped";
			row.Attempts = 1;
			row.LeaseExpiresOn = DateTime.UtcNow.AddMinutes(1);

			(await _service.SendDueAsync(100, CancellationToken.None)).Should().Be(0, "its lease is still running");
			(await _service.SendPendingForCallAsync(_call)).Should().Be(0, "the edit's flush respects a live claim too");

			row.LeaseExpiresOn = DateTime.UtcNow.AddSeconds(-1);
			(await _service.SendDueAsync(100, CancellationToken.None)).Should().Be(1);

			row.State.Should().Be((int)CallBroadcastOutboxStates.Sent);
			row.Attempts.Should().Be(2);
		}

		[Test]
		public async Task A_full_rebroadcast_supersedes_the_pending_pages()
		{
			_accept = () => false;
			var recorded = await RecordMedicAsync();
			await _service.SendPendingForCallAsync(_call, recorded);

			await _service.SupersedeForCallAsync(CallId);
			_outbox.Single().NextAttemptOn = DateTime.UtcNow.AddSeconds(-1);
			_accept = () => true;

			(await _service.SendDueAsync(100, CancellationToken.None)).Should().Be(0);
			_outbox.Single().State.Should().Be((int)CallBroadcastOutboxStates.Superseded);
		}

		[Test]
		public async Task A_row_that_cannot_be_written_is_still_sent_once_from_the_request()
		{
			_outbox.FailInserts = true;

			var recorded = await RecordMedicAsync();

			recorded.Should().NotBeNull();
			recorded.Persisted.Should().BeFalse();
			(await _service.SendPendingForCallAsync(_call, recorded)).Should().Be(1);
			_queued.Single().BroadcastUserIds.Should().Equal(new[] { Medic });
			_outbox.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Nothing_added_records_nothing()
		{
			(await _service.RecordAsync(_call, new string[0], new int[0], null, null, "dispatcher")).Should().BeNull();
			(await _service.RecordAsync(null, new[] { Medic }, null, null, null, "dispatcher")).Should().BeNull();

			_outbox.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Finished_rows_are_purged_after_the_retention()
		{
			var recorded = await RecordMedicAsync();
			await _service.SendPendingForCallAsync(_call, recorded);
			_outbox.Single().CompletedOn = DateTime.UtcNow.Subtract(CallBroadcastOutboxService.RetainFinishedFor).AddMinutes(-1);

			await _service.SendDueAsync(100, CancellationToken.None);

			_outbox.Rows.Should().BeEmpty();
		}

		[TestCase(1, 30)]
		[TestCase(2, 60)]
		[TestCase(3, 120)]
		[TestCase(4, 240)]
		[TestCase(5, 300)]
		[TestCase(12, 300)]
		public void Backoff_doubles_from_thirty_seconds_up_to_five_minutes(int attempts, int seconds)
		{
			CallBroadcastOutboxService.BackoffFor(attempts).Should().Be(TimeSpan.FromSeconds(seconds));
		}

		[Test]
		public void Recipients_round_trip_through_the_row()
		{
			var entry = new CallBroadcastOutboxEntry();
			entry.SetRecipients(new[] { Medic, Medic.ToUpperInvariant(), " ", Driver }, new[] { 3, 3, 4 }, new[] { 5 }, null);

			entry.GetUserIds().Should().Equal(new[] { Medic, Driver });
			entry.GetGroupIds().Should().Equal(new[] { 3, 4 });
			entry.GetUnitIds().Should().Equal(new[] { 5 });
			entry.GetRoleIds().Should().BeEmpty();
			entry.RoleIds.Should().BeNull();
			entry.HasRecipients.Should().BeTrue();
		}
	}
}
