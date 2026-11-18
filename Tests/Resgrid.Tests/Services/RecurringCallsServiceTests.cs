using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Recurring calls (M0319): each occurrence becomes one ordinary scheduled call within its lead time, claimed so it is created
	/// once; skipped and already-created occurrences are left alone; schedule edits can replace the calls not yet sent.
	/// </summary>
	[TestFixture]
	public class RecurringCallsServiceTests
	{
		private const int DepartmentId = 7;
		private static readonly Department Eastern = new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" };

		// Monday 16 November 2026, 09:00 EST.
		private static readonly DateTime Now = new DateTime(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);

		private Mock<IRecurringCallsRepository> _repository;
		private Mock<IRecurringCallOccurrencesRepository> _occurrences;
		private Mock<ICallsService> _callsService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentGroupsService> _groupsService;
		private Mock<IUnitsService> _unitsService;
		private Mock<IPersonnelRolesService> _rolesService;
		private Mock<IMappingService> _mappingService;
		private Mock<IGeoLocationProvider> _geoLocationProvider;
		private Mock<IDispatchScopeService> _scopeService;
		private Mock<IEventAggregator> _eventAggregator;
		private Mock<IRecurringCallAlertsRepository> _alertsRepository;
		private Mock<IQueueService> _queueService;
		private Mock<IUserProfileService> _profiles;
		private Mock<ICallDispatchStatusService> _dispatchStatus;
		private Mock<IFeatureToggleService> _featureToggles;
		private Mock<IDispatchRecommendationService> _recommendations;
		private Mock<ICustomStateService> _customStates;
		private Mock<IActionLogsService> _actionLogs;
		private Mock<IUserStateService> _userStates;
		private Mock<ICommunicationService> _communication;
		private Mock<IDepartmentSettingsService> _settings;
		private RecurringCallsService _service;

		private List<RecurringCallOccurrence> _claims;
		private List<RecurringCallAlert> _alerts;
		private List<object> _events;
		private List<UnitState> _unitStates;
		private List<Call> _saved;
		private int _nextCallId;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<IRecurringCallsRepository>();
			_occurrences = new Mock<IRecurringCallOccurrencesRepository>();
			_callsService = new Mock<ICallsService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_groupsService = new Mock<IDepartmentGroupsService>();
			_unitsService = new Mock<IUnitsService>();
			_rolesService = new Mock<IPersonnelRolesService>();
			_mappingService = new Mock<IMappingService>();
			_geoLocationProvider = new Mock<IGeoLocationProvider>();
			_scopeService = new Mock<IDispatchScopeService>();
			_eventAggregator = new Mock<IEventAggregator>();
			_alertsRepository = new Mock<IRecurringCallAlertsRepository>();
			_queueService = new Mock<IQueueService>();
			_profiles = new Mock<IUserProfileService>();
			_dispatchStatus = new Mock<ICallDispatchStatusService>();
			_featureToggles = new Mock<IFeatureToggleService>();
			_recommendations = new Mock<IDispatchRecommendationService>();
			_customStates = new Mock<ICustomStateService>();
			_actionLogs = new Mock<IActionLogsService>();
			_userStates = new Mock<IUserStateService>();
			_communication = new Mock<ICommunicationService>();
			_settings = new Mock<IDepartmentSettingsService>();

			_alerts = new List<RecurringCallAlert>();
			_events = new List<object>();
			_unitStates = new List<UnitState>();
			_claims = new List<RecurringCallOccurrence>();
			_saved = new List<Call>();
			_nextCallId = 1000;

			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(Eastern);
			_departmentsService.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { UserId = "medic-1", DepartmentId = DepartmentId },
				new DepartmentMember { UserId = "left", DepartmentId = DepartmentId, IsDeleted = true }
			});
			_groupsService.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 3 } });
			_unitsService.Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 11 }, new Unit { UnitId = 12, IsDeleted = true } });
			_rolesService.Setup(x => x.GetRolesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 5 } });

			_occurrences.Setup(x => x.TryClaimAsync(It.IsAny<RecurringCallOccurrence>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RecurringCallOccurrence o, CancellationToken _) =>
				{
					if (_claims.Any(c => c.RecurringCallId == o.RecurringCallId && c.OccurrenceOn == o.OccurrenceOn))
						return false;

					_claims.Add(o);
					return true;
				});
			_occurrences.Setup(x => x.GetForScheduleInRangeAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int id, DateTime from, DateTime to) => _claims.Where(c => c.RecurringCallId == id && c.OccurrenceOn >= from && c.OccurrenceOn < to).ToList());
			_occurrences.Setup(x => x.GetForDepartmentInRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int id, DateTime from, DateTime to) => _claims.Where(c => c.OccurrenceOn >= from && c.OccurrenceOn < to).ToList());
			_occurrences.Setup(x => x.GetByOccurrenceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int id, DateTime on) => _claims.FirstOrDefault(c => c.RecurringCallId == id && c.OccurrenceOn == on));
			_occurrences.Setup(x => x.DeleteIfStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string id, int state, CancellationToken _) => _claims.RemoveAll(c => c.RecurringCallOccurrenceId == id && c.State == state) == 1);
			_occurrences.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RecurringCallOccurrence>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RecurringCallOccurrence o, CancellationToken _, bool __) => o);
			_occurrences.Setup(x => x.GetStaleClaimsAsync(It.IsAny<DateTime>())).ReturnsAsync(new List<RecurringCallOccurrence>());

			_callsService.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call c, CancellationToken _) =>
				{
					if (c.CallId == 0)
						c.CallId = ++_nextCallId;
					_saved.Add(c);
					return c;
				});

			_repository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RecurringCall>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RecurringCall r, CancellationToken _, bool __) =>
				{
					if (r.RecurringCallId == 0)
						r.RecurringCallId = 77;
					return r;
				});

			_scopeService.Setup(x => x.FilterCallsForUserAsync(DepartmentId, It.IsAny<string>(), It.IsAny<List<Call>>()))
				.ReturnsAsync((int d, string u, List<Call> calls) => calls);

			_occurrences.Setup(x => x.TryTransitionStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string id, int from, int to, string user, DateTime on, CancellationToken _) =>
				{
					var row = _claims.FirstOrDefault(c => c.RecurringCallOccurrenceId == id && c.State == from);
					if (row == null)
						return false;

					row.State = to;
					row.UserId = user;
					return true;
				});
			_occurrences.Setup(x => x.GetByCallAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int id, int callId) => _claims.FirstOrDefault(c => c.RecurringCallId == id && c.CallId == callId));

			// Alerts: one per occurrence and kind, as the unique index makes it.
			_alertsRepository.Setup(x => x.TryRaiseAsync(It.IsAny<RecurringCallAlert>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RecurringCallAlert a, CancellationToken _) =>
				{
					if (_alerts.Any(x => x.RecurringCallId == a.RecurringCallId && x.OccurrenceOn == a.OccurrenceOn && x.Kind == a.Kind))
						return false;

					_alerts.Add(a);
					return true;
				});
			_alertsRepository.Setup(x => x.GetOpenForOccurrenceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int id, DateTime on) => _alerts.Where(a => a.RecurringCallId == id && a.OccurrenceOn == on && a.IsOpen).ToList());
			_alertsRepository.Setup(x => x.GetOpenForScheduleAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => _alerts.Where(a => a.RecurringCallId == id && a.IsOpen).ToList());
			_alertsRepository.Setup(x => x.GetAllOpenAsync()).ReturnsAsync(() => _alerts.Where(a => a.IsOpen).ToList());
			_alertsRepository.Setup(x => x.GetOpenForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _alerts.Where(a => a.IsOpen).ToList());
			_alertsRepository.Setup(x => x.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _alerts.FirstOrDefault(a => a.RecurringCallAlertId == (string)id));
			_alertsRepository.Setup(x => x.TryResolveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string id, int resolution, string user, string note, DateTime on, CancellationToken _) =>
				{
					var alert = _alerts.FirstOrDefault(a => a.RecurringCallAlertId == id && a.IsOpen);
					if (alert == null)
						return false;

					alert.ResolvedOn = on;
					alert.Resolution = resolution;
					alert.ResolvedByUserId = user;
					return true;
				});
			_alertsRepository.Setup(x => x.TryAcknowledgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string id, string user, DateTime on, CancellationToken _) =>
				{
					var alert = _alerts.FirstOrDefault(a => a.RecurringCallAlertId == id && a.IsOpen && !a.AcknowledgedOn.HasValue);
					if (alert == null)
						return false;

					alert.AcknowledgedOn = on;
					alert.AcknowledgedByUserId = user;
					return true;
				});

			_eventAggregator.Setup(x => x.SendMessage(It.IsAny<RecurringCallScheduleEvent>())).Callback((RecurringCallScheduleEvent e) => _events.Add(e));
			_eventAggregator.Setup(x => x.SendMessage(It.IsAny<RecurringCallOccurrenceEvent>())).Callback((RecurringCallOccurrenceEvent e) => _events.Add(e));
			_eventAggregator.Setup(x => x.SendMessage(It.IsAny<RecurringCallAlertEvent>())).Callback((RecurringCallAlertEvent e) => _events.Add(e));
			_eventAggregator.Setup(x => x.SendMessage(It.IsAny<RecurringCallAlertsUpdatedEvent>())).Callback((RecurringCallAlertsUpdatedEvent e) => _events.Add(e));
			_eventAggregator.Setup(x => x.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => _events.Add(e));

			// Dispatch: the claim is free, the broadcast queues, nothing is available unless a test says so.
			_callsService.Setup(x => x.TryClaimCallForDispatchAsync(It.IsAny<int>(), DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(Now);
			_callsService.Setup(x => x.PopulateCallData(It.IsAny<Call>(), true, false, false, true, true, true, true, false, false, It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool a, bool b, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k) => c);
			_queueService.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(() => _unitStates);
			_customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomState>());
			_actionLogs.Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>());
			_userStates.Setup(x => x.GetLatestStatesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<UserState>());
			_profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Dictionary<string, UserProfile>());
			_communication.Setup(x => x.SendNotificationAsync(It.IsAny<string>(), DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>())).ReturnsAsync(true);

			_service = new RecurringCallsService(_repository.Object, _occurrences.Object, _alertsRepository.Object, _callsService.Object, _departmentsService.Object,
				_groupsService.Object, _unitsService.Object, _rolesService.Object, _mappingService.Object, _geoLocationProvider.Object,
				_scopeService.Object, _eventAggregator.Object, _queueService.Object, _profiles.Object, _dispatchStatus.Object, _featureToggles.Object,
				_recommendations.Object, _customStates.Object, _actionLogs.Object, _userStates.Object, _communication.Object, _settings.Object);
		}

		private List<T> Events<T>() => _events.OfType<T>().ToList();

		/// <summary>Daily at 08:00 and 13:00 Eastern from 1 November, created a day ahead, set up last week.</summary>
		private static RecurringCall Series(int leadMinutes = 24 * 60)
		{
			return new RecurringCall
			{
				RecurringCallId = 77,
				DepartmentId = DepartmentId,
				Name = "IFT Mercy to Regional",
				NatureOfCall = "Scheduled transfer",
				Address = "1 Main St",
				GeoLocationData = "40.1,-75.2",
				Priority = 1,
				Type = "Transfer",
				Frequency = (int)RecurringCallFrequencies.Daily,
				RepeatInterval = 1,
				Times = "08:00,13:00",
				StartsOn = new DateTime(2026, 11, 1),
				LeadTimeMinutes = leadMinutes,
				IsActive = true,
				CreatedByUserId = "dispatcher-1",
				// Set up just before "now": nothing earlier is the worker's to have created (or to report missed).
				CreatedOn = Now.AddMinutes(-5),
				DispatchUserIds = "medic-1,left,stranger",
				DispatchGroupIds = "3,99",
				DispatchUnitIds = "11,12",
				DispatchRoleIds = "5"
			};
		}

		private void Schedules(params RecurringCall[] schedules)
		{
			_repository.Setup(x => x.GetActiveAsync()).ReturnsAsync(schedules.ToList());
		}

		[Test]
		public async Task Each_occurrence_within_the_lead_time_becomes_one_scheduled_call()
		{
			Schedules(Series());

			var created = await _service.GenerateDueCallsAsync(Now);

			// Within the next 24 hours of Monday 09:00 EST: Monday 13:00, Tuesday 08:00 (Tuesday 13:00 is 28 hours away).
			created.Should().Be(2);
			_saved.Select(x => x.DispatchOn).Should().Equal(new DateTime(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc));

			var call = _saved[0];
			call.Name.Should().Be("IFT Mercy to Regional");
			call.State.Should().Be((int)CallStates.Active);
			call.HasBeenDispatched.Should().BeFalse("the scheduled-calls worker sends it at its time");
			call.RecurringCallId.Should().Be(77);
			call.ReportingUserId.Should().Be("dispatcher-1");
			call.LoggedOn.Should().Be(Now);

			_claims.Should().HaveCount(2).And.OnlyContain(x => x.State == (int)RecurringCallOccurrenceStates.Created && x.CallId.HasValue);
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CallAddedEvent>()), Times.Exactly(2));
		}

		[Test]
		public async Task Recipients_who_left_or_were_deleted_are_not_dispatched()
		{
			Schedules(Series());

			await _service.GenerateDueCallsAsync(Now);

			var call = _saved[0];
			call.Dispatches.Select(x => x.UserId).Should().Equal("medic-1");
			call.GroupDispatches.Select(x => x.DepartmentGroupId).Should().Equal(3);
			call.UnitDispatches.Select(x => x.UnitId).Should().Equal(11);
			call.RoleDispatches.Select(x => x.RoleId).Should().Equal(5);
		}

		[Test]
		public async Task A_second_pass_creates_nothing_new()
		{
			Schedules(Series());

			await _service.GenerateDueCallsAsync(Now);
			var second = await _service.GenerateDueCallsAsync(Now.AddMinutes(5));

			second.Should().Be(0);
			_saved.Should().HaveCount(2);
		}

		[Test]
		public async Task A_skipped_occurrence_is_not_created()
		{
			Schedules(Series());
			_claims.Add(new RecurringCallOccurrence
			{
				RecurringCallOccurrenceId = "skip", RecurringCallId = 77, DepartmentId = DepartmentId,
				OccurrenceOn = new DateTime(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc), State = (int)RecurringCallOccurrenceStates.Skipped
			});

			await _service.GenerateDueCallsAsync(Now);

			_saved.Select(x => x.DispatchOn).Should().Equal(new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc));
		}

		[Test]
		public async Task An_occurrence_someone_else_claimed_is_not_created_twice()
		{
			Schedules(Series());
			_occurrences.Setup(x => x.TryClaimAsync(It.IsAny<RecurringCallOccurrence>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var created = await _service.GenerateDueCallsAsync(Now);

			created.Should().Be(0);
			_callsService.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_call_that_could_not_be_saved_gives_its_claim_back()
		{
			Schedules(Series());
			_callsService.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			var created = await _service.GenerateDueCallsAsync(Now);

			created.Should().Be(0);
			_claims.Should().BeEmpty("the next pass retries the occurrence");
			_alerts.Should().HaveCount(2).And.OnlyContain(a => a.Kind == (int)RecurringCallAlertKinds.CreateFailed && a.GetDetail().Reason == RecurringCallAlertReasons.SaveFailed);
		}

		[Test]
		public async Task An_occurrence_missed_by_more_than_the_grace_is_left_alone()
		{
			// Monday 13:00 EST was 15 minutes ago: too late for the dispatch worker to send, so it is not created.
			Schedules(Series(leadMinutes: 60));

			var created = await _service.GenerateDueCallsAsync(new DateTime(2026, 11, 16, 18, 15, 0, DateTimeKind.Utc));

			created.Should().Be(0);

			// Not silently dropped: recorded as missed and the dispatcher is told.
			var missed = new DateTime(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc);
			_claims.Should().ContainSingle(c => c.OccurrenceOn == missed && c.State == (int)RecurringCallOccurrenceStates.Missed);
			_alerts.Should().ContainSingle(a => a.OccurrenceOn == missed && a.Kind == (int)RecurringCallAlertKinds.Missed);
		}

		[Test]
		public async Task Nothing_due_before_the_schedule_was_last_saved_is_created()
		{
			var series = Series(leadMinutes: 60);
			series.UpdatedOn = new DateTime(2026, 11, 16, 18, 2, 0, DateTimeKind.Utc);
			Schedules(series);

			// 13:00 EST was 3 minutes ago (inside the grace), but the schedule was saved after it.
			var created = await _service.GenerateDueCallsAsync(new DateTime(2026, 11, 16, 18, 3, 0, DateTimeKind.Utc));

			created.Should().Be(0);
		}

		[Test]
		public async Task A_dead_claim_whose_call_was_saved_is_marked_created()
		{
			Schedules();
			var stale = new RecurringCallOccurrence
			{
				RecurringCallOccurrenceId = "stale", RecurringCallId = 77, DepartmentId = DepartmentId,
				OccurrenceOn = Now.AddHours(4), State = (int)RecurringCallOccurrenceStates.Creating, UpdatedOn = Now.AddMinutes(-30)
			};
			_claims.Add(stale);
			_occurrences.Setup(x => x.GetStaleClaimsAsync(It.IsAny<DateTime>())).ReturnsAsync(new List<RecurringCallOccurrence> { stale });
			_repository.Setup(x => x.GetCallForOccurrenceAsync(77, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new Call { CallId = 555 });

			await _service.GenerateDueCallsAsync(Now);

			stale.State.Should().Be((int)RecurringCallOccurrenceStates.Created);
			stale.CallId.Should().Be(555);
		}

		[Test]
		public async Task A_dead_claim_with_no_call_is_dropped_so_the_occurrence_is_created_again()
		{
			Schedules();
			var stale = new RecurringCallOccurrence
			{
				RecurringCallOccurrenceId = "stale", RecurringCallId = 77, DepartmentId = DepartmentId,
				OccurrenceOn = Now.AddHours(4), State = (int)RecurringCallOccurrenceStates.Creating, UpdatedOn = Now.AddMinutes(-30)
			};
			_claims.Add(stale);
			_occurrences.Setup(x => x.GetStaleClaimsAsync(It.IsAny<DateTime>())).ReturnsAsync(new List<RecurringCallOccurrence> { stale });

			await _service.GenerateDueCallsAsync(Now);

			_claims.Should().BeEmpty();
		}

		[Test]
		public async Task Saving_with_replace_deletes_the_calls_not_yet_sent_and_forgets_their_occurrences()
		{
			var series = Series(leadMinutes: 60);
			// Ended, so the save itself creates nothing new whatever the time of day the test runs.
			series.EndsOn = new DateTime(2026, 11, 2);
			var future = DateTime.UtcNow.AddDays(2);
			var waiting = new Call { CallId = 900, DepartmentId = DepartmentId, RecurringCallId = 77, State = (int)CallStates.Active, HasBeenDispatched = false, DispatchOn = future };
			var sent = new Call { CallId = 901, DepartmentId = DepartmentId, RecurringCallId = 77, State = (int)CallStates.Active, HasBeenDispatched = true, DispatchOn = future.AddHours(1) };
			_claims.Add(new RecurringCallOccurrence { RecurringCallOccurrenceId = "o1", RecurringCallId = 77, OccurrenceOn = future, State = (int)RecurringCallOccurrenceStates.Created, CallId = 900 });
			_claims.Add(new RecurringCallOccurrence { RecurringCallOccurrenceId = "o2", RecurringCallId = 77, OccurrenceOn = future.AddHours(1), State = (int)RecurringCallOccurrenceStates.Created, CallId = 901 });

			_repository.Setup(x => x.GetCallsForScheduleAsync(DepartmentId, 77, It.IsAny<DateTime>())).ReturnsAsync(new List<Call> { waiting, sent });
			_callsService.Setup(x => x.GetCallByIdAsync(900, It.IsAny<bool>())).ReturnsAsync(waiting);
			_callsService.Setup(x => x.GetCallByIdAsync(901, It.IsAny<bool>())).ReturnsAsync(sent);

			var result = await _service.SaveAsync(series, "dispatcher-2", true);

			result.ReplacedCalls.Should().Be(1);
			waiting.IsDeleted.Should().BeTrue();
			waiting.DeletedByUserId.Should().Be("dispatcher-2");
			sent.IsDeleted.Should().BeFalse("a call that has gone out is never touched");
			_claims.Select(x => x.RecurringCallOccurrenceId).Should().Equal("o2");
			series.UpdatedByUserId.Should().Be("dispatcher-2");
			series.OwnerUserId.Should().Be("dispatcher-2");
		}

		[Test]
		public async Task A_new_schedule_with_only_an_address_is_located_once()
		{
			var series = Series();
			series.RecurringCallId = 0;
			series.GeoLocationData = null;
			series.StartsOn = new DateTime(2030, 1, 1);
			_geoLocationProvider.Setup(x => x.GetLatLonFromAddress("1 Main St")).ReturnsAsync("40.5,-75.5");

			var result = await _service.SaveAsync(series, "dispatcher-1", false);

			result.Series.GeoLocationData.Should().Be("40.5,-75.5");
			result.Series.CreatedByUserId.Should().Be("dispatcher-1");
			result.CreatedCalls.Should().Be(0, "nothing is due before 2030");
		}

		[Test]
		public async Task Only_a_real_occurrence_can_be_skipped()
		{
			var series = Series();
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);
			var nextWeek = DateTime.UtcNow.Date.AddDays(7);
			// 08:00 EST/EDT is 12:00 or 13:00 UTC; 13:37 never is.
			var notAnOccurrence = nextWeek.AddHours(13).AddMinutes(37);

			(await _service.SkipOccurrenceAsync(DepartmentId, 77, notAnOccurrence, "dispatcher-1")).Should().BeFalse();
			_claims.Should().BeEmpty();
		}

		[Test]
		public async Task Another_departments_schedule_is_not_found()
		{
			var series = Series();
			series.DepartmentId = 8;
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);

			(await _service.GetByIdAsync(DepartmentId, 77)).Should().BeNull();
			(await _service.SkipOccurrenceAsync(DepartmentId, 77, Now, "dispatcher-1")).Should().BeFalse();
		}

		[Test]
		public async Task The_calendar_shows_calls_projected_and_skipped_occurrences()
		{
			var series = Series();
			series.StartsOn = DateTime.UtcNow.Date.AddDays(2);
			series.EndsOn = series.StartsOn;
			series.CreatedOn = DateTime.UtcNow.AddDays(-1);
			_repository.Setup(x => x.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<RecurringCall> { series });

			var from = DateTime.UtcNow.Date;
			var to = from.AddDays(5);
			var occurrences = RecurringCallRecurrence.UtcOccurrences(series, Eastern, from, to);
			occurrences.Should().HaveCount(2);

			// The morning run was skipped; a one-off scheduled call and a dispatched recurring call are already on the books.
			_claims.Add(new RecurringCallOccurrence { RecurringCallOccurrenceId = "s", RecurringCallId = 77, DepartmentId = DepartmentId, OccurrenceOn = occurrences[0], State = (int)RecurringCallOccurrenceStates.Skipped });
			_repository.Setup(x => x.GetScheduledCallsInRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new List<Call>
			{
				new Call { CallId = 1, DepartmentId = DepartmentId, Name = "One-off", State = (int)CallStates.Active, HasBeenDispatched = false, DispatchOn = from.AddDays(1) },
				new Call { CallId = 2, DepartmentId = DepartmentId, Name = "Sent", RecurringCallId = 77, State = (int)CallStates.Active, HasBeenDispatched = true, DispatchOn = from.AddHours(1) }
			});

			var entries = await _service.GetCalendarAsync(DepartmentId, "dispatcher-1", from, to);

			entries.Select(x => x.Kind).Should().Equal(
				ScheduledCallCalendarEntryKinds.Dispatched, ScheduledCallCalendarEntryKinds.Scheduled,
				ScheduledCallCalendarEntryKinds.Skipped, ScheduledCallCalendarEntryKinds.Projected);
			entries[3].DispatchOnUtc.Should().Be(occurrences[1]);
			entries[3].RecurringCallId.Should().Be(77);
		}

		[Test]
		public async Task A_paused_schedule_projects_nothing()
		{
			var series = Series();
			series.IsActive = false;
			series.StartsOn = DateTime.UtcNow.Date.AddDays(1);
			_repository.Setup(x => x.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<RecurringCall> { series });
			_repository.Setup(x => x.GetScheduledCallsInRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new List<Call>());

			var entries = await _service.GetCalendarAsync(DepartmentId, "dispatcher-1", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7));

			entries.Should().BeEmpty();
		}

		[Test]
		public async Task A_dispatcher_mode_schedule_creates_nothing_and_alerts_the_dispatcher_when_due()
		{
			var series = Series();
			series.CreationMode = (int)RecurringCallCreationModes.Dispatcher;
			series.ManualAlertMinutes = 15;
			series.AlertUserIds = "medic-1";
			series.CreatedOn = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
			Schedules(series);

			// 12:50 UTC: Monday 08:00 EST (13:00 UTC) is 10 minutes away, inside the 15-minute alert window.
			var created = await _service.GenerateDueCallsAsync(new DateTime(2026, 11, 16, 12, 50, 0, DateTimeKind.Utc));

			created.Should().Be(0);
			_callsService.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			var due = new DateTime(2026, 11, 16, 13, 0, 0, DateTimeKind.Utc);
			_claims.Should().ContainSingle(c => c.OccurrenceOn == due && c.State == (int)RecurringCallOccurrenceStates.AwaitingDispatcher);
			_alerts.Should().ContainSingle(a => a.OccurrenceOn == due && a.Kind == (int)RecurringCallAlertKinds.DispatchDue);

			var alertId = _alerts.Single().RecurringCallAlertId;
			_communication.Verify(x => x.SendNotificationAsync("medic-1", DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				It.Is<string>(t => t.Contains("IFT Mercy to Regional")), It.IsAny<UserProfile>(), false, "RCA:" + alertId), Times.Once);
			_alertsRepository.Verify(x => x.MarkNotifiedAsync(alertId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			Events<RecurringCallAlertEvent>().Should().ContainSingle(e => e.Raised && e.Trigger == WorkflowTriggerEventType.RecurringCallAlertRaised && e.KindName == "DispatchDue");
			Events<RecurringCallAlertsUpdatedEvent>().Should().NotBeEmpty("dispatch clients refresh their alert list");

			// A second pass raises nothing more.
			await _service.GenerateDueCallsAsync(new DateTime(2026, 11, 16, 12, 55, 0, DateTimeKind.Utc));
			_alerts.Should().HaveCount(1);
		}

		[Test]
		public async Task Sending_a_due_occurrence_creates_its_call_dispatches_it_and_closes_the_alert()
		{
			var series = Series();
			series.CreationMode = (int)RecurringCallCreationModes.Dispatcher;
			series.DispatchUserIds = null;
			series.DispatchGroupIds = null;
			series.DispatchRoleIds = null;
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);
			var due = RecurringCallRecurrence.NextOccurrenceUtc(series, Eastern, DateTime.UtcNow).Value;
			_claims.Add(new RecurringCallOccurrence { RecurringCallOccurrenceId = "o", RecurringCallId = 77, DepartmentId = DepartmentId, OccurrenceOn = due, State = (int)RecurringCallOccurrenceStates.AwaitingDispatcher });
			_alerts.Add(new RecurringCallAlert { RecurringCallAlertId = "a", RecurringCallId = 77, DepartmentId = DepartmentId, OccurrenceOn = due, Kind = (int)RecurringCallAlertKinds.DispatchDue, CreatedOn = DateTime.UtcNow });

			var result = await _service.DispatchOccurrenceNowAsync(DepartmentId, 77, due, "dispatcher-2");

			result.Outcome.Should().Be(RecurringCallDispatchOutcomes.Dispatched);
			result.Call.UnitDispatches.Select(x => x.UnitId).Should().Equal(11);
			result.Call.HasBeenDispatched.Should().BeTrue();
			result.Call.ReportingUserId.Should().Be("dispatcher-2");
			_claims.Single().State.Should().Be((int)RecurringCallOccurrenceStates.Created);
			_alerts.Single().IsOpen.Should().BeFalse();
			_alerts.Single().Resolution.Should().Be((int)RecurringCallAlertResolutions.CallCreated);
			_queueService.Verify(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Once);
			_callsService.Verify(x => x.CompleteCallDispatchClaimAsync(result.Call.CallId, DepartmentId, Now, It.IsAny<CancellationToken>()), Times.Once);
			Events<RecurringCallOccurrenceEvent>().Select(e => e.Trigger).Should().Equal(
				WorkflowTriggerEventType.RecurringCallOccurrenceCreated, WorkflowTriggerEventType.RecurringCallOccurrenceDispatched);
			Events<AuditEvent>().Should().ContainSingle(e => e.Type == AuditLogTypes.RecurringCallOccurrenceCreated && e.UserId == "dispatcher-2");
		}

		[Test]
		public async Task Unavailable_units_are_left_off_and_the_dispatcher_is_told()
		{
			var series = Series();
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);
			var call = RecurringCallsService.BuildCall(series, Now, Now, "dispatcher-1", RecurringCallDispatchTargets.Everything(series), false);
			call.CallId = 500;
			call.UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallDispatchUnitId = 1, UnitId = 11 }, new CallDispatchUnit { CallDispatchUnitId = 2, UnitId = 14 } };
			_claims.Add(new RecurringCallOccurrence { RecurringCallOccurrenceId = "o", RecurringCallId = 77, OccurrenceOn = Now, State = (int)RecurringCallOccurrenceStates.Created, CallId = 500 });
			// Unit 14 is on another call.
			_unitStates.Add(new UnitState { UnitId = 14, State = (int)UnitStateTypes.Responding, Timestamp = Now.AddMinutes(-20) });

			var result = await _service.DispatchCallAsync(call, null);

			result.Outcome.Should().Be(RecurringCallDispatchOutcomes.Dispatched);
			result.UnavailableUnitIds.Should().Equal(14);
			call.UnitDispatches.Select(x => x.UnitId).Should().Equal(11);
			_callsService.Verify(x => x.DeleteUnitDispatchesAsync(It.Is<List<CallDispatchUnit>>(l => l.Single().UnitId == 14), It.IsAny<CancellationToken>()), Times.Once);
			_alerts.Should().ContainSingle(a => a.Kind == (int)RecurringCallAlertKinds.ResourcesUnavailable && a.CallId == 500 && a.GetDetail().UnavailableUnitIds.Single() == 14);
			Events<RecurringCallOccurrenceEvent>().Should().ContainSingle(e => e.Change == RecurringCallOccurrenceChanges.Dispatched && e.UnavailableCount == 1 && !e.ByDispatcher);
		}

		[Test]
		public async Task Nobody_available_holds_the_call_as_pending_and_alerts()
		{
			var series = Series();
			series.DispatchUserIds = null;
			series.DispatchGroupIds = null;
			series.DispatchRoleIds = null;
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);
			var call = RecurringCallsService.BuildCall(series, Now, Now, "dispatcher-1", RecurringCallDispatchTargets.Everything(series), false);
			call.CallId = 501;
			call.UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallDispatchUnitId = 3, UnitId = 11 } };
			_unitStates.Add(new UnitState { UnitId = 11, State = (int)UnitStateTypes.OutOfService, Timestamp = Now.AddHours(-2) });

			var result = await _service.DispatchCallAsync(call, null);

			result.Outcome.Should().Be(RecurringCallDispatchOutcomes.Held);
			_queueService.Verify(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(501, DepartmentId, Now, (int)CallStates.Pending, Now, null, It.IsAny<CancellationToken>()), Times.Once);
			_callsService.Verify(x => x.ReleaseCallDispatchClaimAsync(501, DepartmentId, Now, (int)CallStates.Active, It.IsAny<DateTime?>(), false, It.IsAny<CancellationToken>()), Times.Never,
				"the claim is handed back once, as pending");
			_alerts.Should().ContainSingle(a => a.Kind == (int)RecurringCallAlertKinds.NoResources && a.CallId == 501 && a.GetDetail().Reason == RecurringCallAlertReasons.NoneAvailable);
		}

		[Test]
		public async Task A_schedule_naming_nobody_is_held_with_the_none_chosen_reason()
		{
			var series = Series();
			series.DispatchUserIds = series.DispatchGroupIds = series.DispatchUnitIds = series.DispatchRoleIds = null;
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);
			var call = RecurringCallsService.BuildCall(series, Now, Now, "dispatcher-1", RecurringCallDispatchTargets.Everything(series), false);
			call.CallId = 502;

			var result = await _service.DispatchCallAsync(call, null);

			result.Outcome.Should().Be(RecurringCallDispatchOutcomes.Held);
			_alerts.Single().GetDetail().Reason.Should().Be(RecurringCallAlertReasons.NoneChosen);
		}

		[Test]
		public async Task The_run_card_option_applies_the_matching_card_even_without_auto_dispatch()
		{
			var series = Series();
			series.UseRunCard = true;
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);
			_featureToggles.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			var recommendation = new DispatchRecommendationResult { MatchedRunCardId = 9, AutoDispatch = false };
			recommendation.Units.Add(new UnitRecommendation { UnitId = 20 });
			_recommendations.Setup(x => x.EnrichCallForDispatchAsync(It.IsAny<Call>(), 1, false, It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call c, int level, bool only, CancellationToken _) =>
				{
					c.UnitDispatches.Add(new CallDispatchUnit { UnitId = 20 });
					return recommendation;
				});
			var call = RecurringCallsService.BuildCall(series, Now, Now, "dispatcher-1", RecurringCallDispatchTargets.Everything(series), false);
			call.CallId = 503;

			var result = await _service.DispatchCallAsync(call, null);

			result.RunCardApplied.Should().BeTrue();
			call.UnitDispatches.Select(x => x.UnitId).Should().Contain(20);
			_recommendations.Verify(x => x.EnrichCallForDispatchAsync(It.IsAny<Call>(), 1, false, It.IsAny<CancellationToken>()), Times.Once);
			_recommendations.Verify(x => x.RecordActivationAsync(call, recommendation, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Pausing_keeps_the_schedule_with_its_reason_and_the_worker_resumes_it_on_its_date()
		{
			var series = Series();
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);

			var paused = await _service.PauseAsync(DepartmentId, 77, "dispatcher-1", "  Clinic closed for the holidays ", new DateTime(2026, 11, 20), false,
				new AuditOrigin { IpAddress = "10.0.0.1", UserAgent = "test" });

			paused.IsActive.Should().BeFalse();
			paused.IsDeleted.Should().BeFalse();
			paused.PauseReason.Should().Be("Clinic closed for the holidays");
			paused.PausedByUserId.Should().Be("dispatcher-1");
			paused.ResumeOn.Should().Be(new DateTime(2026, 11, 20));
			Events<AuditEvent>().Should().ContainSingle(e => e.Type == AuditLogTypes.RecurringCallPaused && e.IpAddress == "10.0.0.1" && e.After.Contains("Clinic closed"));
			Events<RecurringCallScheduleEvent>().Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.RecurringCallSchedulePaused && !e.IsActive);

			// The worker leaves it alone before the date, and resumes it on the date (department time).
			_repository.Setup(x => x.GetPausedWithResumeDateAsync()).ReturnsAsync(new List<RecurringCall> { series });
			Schedules();
			await _service.GenerateDueCallsAsync(new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc));
			series.IsActive.Should().BeFalse();

			await _service.GenerateDueCallsAsync(new DateTime(2026, 11, 20, 5, 30, 0, DateTimeKind.Utc));
			series.IsActive.Should().BeTrue();
			series.PauseReason.Should().BeNull();
			series.ResumeOn.Should().BeNull();
			series.UpdatedOn.Should().Be(new DateTime(2026, 11, 20, 5, 30, 0, DateTimeKind.Utc), "nothing due while it was paused is created");
			Events<AuditEvent>().Should().ContainSingle(e => e.Type == AuditLogTypes.RecurringCallResumed && e.UserId == "system");
		}

		[Test]
		public async Task Saving_audits_and_raises_the_created_trigger()
		{
			var series = Series();
			series.RecurringCallId = 0;
			series.StartsOn = new DateTime(2030, 1, 1);

			await _service.SaveAsync(series, "dispatcher-1", false, new AuditOrigin { IpAddress = "10.0.0.2" });

			Events<AuditEvent>().Should().ContainSingle(e => e.Type == AuditLogTypes.RecurringCallCreated && e.UserId == "dispatcher-1" && e.Before == null && e.IpAddress == "10.0.0.2");
			Events<RecurringCallScheduleEvent>().Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.RecurringCallScheduleCreated && e.RecurringCallId == 77);
		}

		[Test]
		public async Task A_held_call_that_a_dispatcher_sent_closes_its_alert_on_the_next_pass()
		{
			Schedules();
			var series = Series();
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);
			_alerts.Add(new RecurringCallAlert { RecurringCallAlertId = "held", RecurringCallId = 77, DepartmentId = DepartmentId, OccurrenceOn = Now, Kind = (int)RecurringCallAlertKinds.NoResources, CallId = 600, CreatedOn = Now });
			_callsService.Setup(x => x.GetCallByIdAsync(600, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 600, DepartmentId = DepartmentId, State = (int)CallStates.Active, HasBeenDispatched = true });

			await _service.GenerateDueCallsAsync(Now);

			_alerts.Single().Resolution.Should().Be((int)RecurringCallAlertResolutions.Dispatched);
			Events<RecurringCallAlertEvent>().Should().ContainSingle(e => !e.Raised && e.Trigger == WorkflowTriggerEventType.RecurringCallAlertResolved && e.Resolution == "Dispatched");
			Events<RecurringCallOccurrenceEvent>().Should().ContainSingle(e => e.Change == RecurringCallOccurrenceChanges.Dispatched && e.ByDispatcher);
		}

		[Test]
		public async Task Skipping_an_occurrence_awaiting_a_dispatcher_closes_its_alert()
		{
			var series = Series();
			series.CreationMode = (int)RecurringCallCreationModes.Dispatcher;
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(series);
			var due = RecurringCallRecurrence.NextOccurrenceUtc(series, Eastern, DateTime.UtcNow).Value;
			_claims.Add(new RecurringCallOccurrence { RecurringCallOccurrenceId = "o", RecurringCallId = 77, DepartmentId = DepartmentId, OccurrenceOn = due, State = (int)RecurringCallOccurrenceStates.AwaitingDispatcher });
			_alerts.Add(new RecurringCallAlert { RecurringCallAlertId = "a", RecurringCallId = 77, DepartmentId = DepartmentId, OccurrenceOn = due, Kind = (int)RecurringCallAlertKinds.DispatchDue, CreatedOn = DateTime.UtcNow });

			(await _service.SkipOccurrenceAsync(DepartmentId, 77, due, "dispatcher-1")).Should().BeTrue();

			_claims.Single().State.Should().Be((int)RecurringCallOccurrenceStates.Skipped);
			_alerts.Single().Resolution.Should().Be((int)RecurringCallAlertResolutions.Skipped);
			Events<RecurringCallOccurrenceEvent>().Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.RecurringCallOccurrenceSkipped);
			Events<AuditEvent>().Should().ContainSingle(e => e.Type == AuditLogTypes.RecurringCallOccurrenceSkipped);
		}

		[Test]
		public async Task Acknowledging_and_dismissing_an_alert_are_audited()
		{
			_repository.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(Series());
			_alerts.Add(new RecurringCallAlert { RecurringCallAlertId = "a", RecurringCallId = 77, DepartmentId = DepartmentId, OccurrenceOn = Now, Kind = (int)RecurringCallAlertKinds.Missed, CreatedOn = Now });

			(await _service.AcknowledgeAlertAsync(DepartmentId, "a", "dispatcher-1")).Should().BeTrue();
			(await _service.AcknowledgeAlertAsync(DepartmentId, "a", "dispatcher-2")).Should().BeFalse("already acknowledged");
			(await _service.DismissAlertAsync(8, "a", "dispatcher-1", null)).Should().BeFalse("another department's alert");
			(await _service.DismissAlertAsync(DepartmentId, "a", "dispatcher-1", "Crew took it by phone")).Should().BeTrue();

			_alerts.Single().Resolution.Should().Be((int)RecurringCallAlertResolutions.Dismissed);
			Events<AuditEvent>().Select(e => e.Type).Should().Equal(AuditLogTypes.RecurringCallAlertAcknowledged, AuditLogTypes.RecurringCallAlertDismissed);
		}

		[Test]
		public void The_call_notes_carry_the_pickup_and_destination()
		{
			var series = Series();
			series.Notes = "Bariatric stretcher";
			series.FacilityName = "Mercy General";
			series.FacilityDetails = "Dialysis unit, 2nd floor";
			series.DestinationName = "Riverside Dialysis";

			var call = RecurringCallsService.BuildCall(series, Now, Now, "dispatcher-1", RecurringCallDispatchTargets.Everything(series), false);

			call.Notes.Should().Be("Bariatric stretcher" + Environment.NewLine + "Pickup: Mercy General - Dialysis unit, 2nd floor" + Environment.NewLine + "Destination: Riverside Dialysis");
		}

		[TestCase(RecurringCallAlertKinds.DispatchDue, null, "It is time to send the call for Mon Nov 16 08:00.")]
		[TestCase(RecurringCallAlertKinds.Missed, null, "The call for Mon Nov 16 08:00 was not created in time. Send it now or skip it.")]
		[TestCase(RecurringCallAlertKinds.NoResources, RecurringCallAlertReasons.NoneChosen, "Nobody could be sent for Mon Nov 16 08:00: no resources are chosen and no run card matched. The call is held as pending; choose resources and dispatch it.")]
		public void The_alert_text_says_what_to_do(RecurringCallAlertKinds kind, string reason, string expected)
		{
			RecurringCallsService.AlertMessage(kind, "Mon Nov 16 08:00", new RecurringCallAlertDetail { Reason = reason }, null, null).Should().Be(expected);
		}

		[Test]
		public void The_alert_text_names_the_unavailable_resources()
		{
			var text = RecurringCallsService.AlertMessage(RecurringCallAlertKinds.ResourcesUnavailable, "Mon Nov 16 08:00",
				new RecurringCallAlertDetail { UnavailableUnitIds = new List<int> { 14 } }, new Dictionary<int, string> { [14] = "Medic 14" }, null);

			text.Should().Be("The call for Mon Nov 16 08:00 went out without Medic 14 (not available). Add or replace resources if needed.");
		}

		[Test]
		public void A_stand_in_call_carries_the_schedules_recipients_for_the_scope_check()
		{
			var call = RecurringCallsService.BuildCall(Series(), Now, Now, "dispatcher-1", RecurringCallDispatchTargets.Everything(Series()), true);

			call.Dispatches.Select(x => x.UserId).Should().BeEquivalentTo("medic-1", "left", "stranger");
			call.GroupDispatches.Should().NotBeNull();
			call.UnitDispatches.Should().NotBeNull();
			call.DestinationPoiId.Should().BeNull("the schedule has none");
		}
	}
}
