using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
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
	[TestFixture]
	public class OutstandingRequirementsServiceTests
	{
		private const int DepartmentId = 3;
		private const int CallId = 90;
		private const int MugTypeId = 11;
		private const int PitTypeId = 12;

		private Mock<ICallOutstandingRequirementsRepository> _repository;
		private Mock<ICallsService> _calls;
		private Mock<IUnitsService> _units;
		private Mock<ICustomStateService> _customStates;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IRunCardsService> _runCards;
		private Mock<ICallDispatchStatusService> _dispatchStatuses;
		private Mock<IQueueService> _queue;
		private Mock<IEventAggregator> _events;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IGeoService> _geo;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IFeatureToggleService> _flags;
		private Mock<ICacheProvider> _cache;
		private OutstandingRequirementsService _service;
		private List<CallOutstandingRequirement> _stored;
		private Call _call;
		private DispatchRecommendationConfig _config;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<ICallOutstandingRequirementsRepository>();
			_calls = new Mock<ICallsService>();
			_units = new Mock<IUnitsService>();
			_customStates = new Mock<ICustomStateService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_runCards = new Mock<IRunCardsService>();
			_dispatchStatuses = new Mock<ICallDispatchStatusService>();
			_queue = new Mock<IQueueService>();
			_events = new Mock<IEventAggregator>();
			_groups = new Mock<IDepartmentGroupsService>();
			_geo = new Mock<IGeoService>();
			_roles = new Mock<IPersonnelRolesService>();
			_flags = new Mock<IFeatureToggleService>();
			_cache = new Mock<ICacheProvider>();
			_stored = new List<CallOutstandingRequirement>();
			_config = new DispatchRecommendationConfig();

			_repository.Setup(x => x.GetByCallIdAsync(CallId)).ReturnsAsync(() => _stored.ToList());
			_repository.Setup(x => x.GetOpenByDepartmentIdAsync(DepartmentId)).ReturnsAsync(() => _stored.Where(r => r.IsOpen).ToList());
			_repository.Setup(x => x.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _stored.FirstOrDefault(r => r.CallOutstandingRequirementId == (int)id));
			_repository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallOutstandingRequirement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CallOutstandingRequirement r, CancellationToken c, bool f) =>
				{
					if (r.CallOutstandingRequirementId == 0)
					{
						r.CallOutstandingRequirementId = _stored.Count + 1;
						_stored.Add(r);
					}
					return r;
				});
			_repository.Setup(x => x.TryClaimAsync(It.IsAny<int>(), DepartmentId, It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_call = new Call { CallId = CallId, DepartmentId = DepartmentId, State = (int)CallStates.Active, Number = "123", Name = "Cardiac", Priority = 1, LoggedOn = DateTime.UtcNow.AddMinutes(-10),
				UnitDispatches = new List<CallDispatchUnit>(), Dispatches = new List<CallDispatch>() };
			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool _, bool __, bool ___, bool ____, bool _____, bool ______, bool _______, bool ________, bool _________, bool __________) => c);
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => c);

			_units.Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<UnitType>
			{
				new UnitType { UnitTypeId = MugTypeId, DepartmentId = DepartmentId, Type = "MUG", BaseType = (int)UnitTypeClasses.MedicalResponse },
				new UnitType { UnitTypeId = PitTypeId, DepartmentId = DepartmentId, Type = "PIT", BaseType = (int)UnitTypeClasses.MedicalResponse }
			});
			_units.Setup(x => x.GetUnitsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, DepartmentId = DepartmentId, Name = "M-1", Type = "MUG" },
				new Unit { UnitId = 2, DepartmentId = DepartmentId, Name = "P-21", Type = "PIT" }
			});
			_units.Setup(x => x.GetUnitByIdAsync(1)).ReturnsAsync(new Unit { UnitId = 1, DepartmentId = DepartmentId, Name = "M-1", Type = "MUG" });
			_units.Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId)).ReturnsAsync(new List<UnitsLocation>());
			_customStates.Setup(x => x.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomState>());
			_settings.Setup(x => x.GetDispatchResponseCodesAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DispatchResponseCodesConfig());
			_settings.Setup(x => x.GetDispatchRecommendationConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _config);
			_runCards.Setup(x => x.GetLastUnitDispatchTimesAsync(DepartmentId)).ReturnsAsync(new Dictionary<int, DateTime>());
			_flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			_service = new OutstandingRequirementsService(_repository.Object, _calls.Object, _units.Object, _customStates.Object, _settings.Object, _runCards.Object,
				_dispatchStatuses.Object, _queue.Object, _events.Object, _groups.Object, _geo.Object, _roles.Object, _flags.Object, _cache.Object);
		}

		private static DispatchRecommendationResult ResultWithOutstandingMug(RequirementFallbackSteps step = RequirementFallbackSteps.Substitute, int requiredCount = 1)
		{
			return ResultWithOutstandingRows(step, requiredCount, Tuple.Create(500, "MUG"));
		}

		private static DispatchRecommendationResult ResultWithOutstandingRows(RequirementFallbackSteps step, int requiredCount, params Tuple<int, string>[] rows)
		{
			return new DispatchRecommendationResult
			{
				MatchedRunCardId = 8,
				AlarmLevel = 1,
				Decision = new RunCardDecisionExplanation
				{
					ResolvedRows = rows.Select(r => new ResolvedRequirementRow
					{
						Scope = RequirementScopes.Package, RequirementId = r.Item1, Label = r.Item2, RequiredCount = requiredCount, KeepOutstanding = true,
						Targets = new List<RunCardTarget> { new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitType, TargetRefId = MugTypeId } }
					}).ToList(),
					Fallbacks = rows.Select(r => new RunCardFallbackExplanation
					{
						RowLabel = r.Item2, RequirementScope = RequirementScopes.Package, RequirementId = r.Item1, Step = step, Outstanding = true,
						RequiredCount = requiredCount, FilledCount = 0, SentInstead = "P-21"
					}).ToList()
				}
			};
		}

		[Test]
		public async Task create_records_a_requirement_for_a_substituted_outstanding_row_once()
		{
			var created = await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");

			var requirement = created.Should().ContainSingle().Subject;
			requirement.Label.Should().Be("MUG");
			requirement.RequiredCount.Should().Be(1);
			requirement.SentInstead.Should().Be("P-21");
			requirement.ResponsePackageRowId.Should().Be(500);
			JsonConvert.DeserializeObject<List<RunCardTarget>>(requirement.TargetsJson).Should().ContainSingle(t => t.TargetRefId == MugTypeId);
			_events.Verify(x => x.SendMessageAsync(It.Is<OutstandingRequirementsUpdatedEvent>(e => e.CallId == CallId)), Times.Once);

			// Re-evaluating or adding resources must not stack a second copy.
			(await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1")).Should().BeEmpty();
			_stored.Should().HaveCount(1);
		}

		[Test]
		public async Task create_ignores_relaxed_rows_and_results_without_a_decision()
		{
			(await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(RequirementFallbackSteps.Relaxed), "user-1")).Should().BeEmpty();
			(await _service.CreateFromRecommendationAsync(_call, new DispatchRecommendationResult { MatchedRunCardId = 8 }, "user-1")).Should().BeEmpty();
		}

		[Test]
		public async Task a_matching_unit_dispatched_after_the_requirement_resolves_it_on_read()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");
			var createdOn = _stored.Single().CreatedOn;

			// The PIT sent instead at save time does not count; a MUG sent later does.
			_call.UnitDispatches.Add(new CallDispatchUnit { CallId = CallId, UnitId = 2, DispatchedOn = createdOn.AddSeconds(-1) });
			(await _service.GetForCallAsync(DepartmentId, CallId)).Should().ContainSingle();

			_call.UnitDispatches.Add(new CallDispatchUnit { CallId = CallId, UnitId = 1, DispatchedOn = createdOn.AddMinutes(3) });
			(await _service.GetForCallAsync(DepartmentId, CallId)).Should().BeEmpty();

			var resolved = _stored.Single();
			resolved.Resolution.Should().Be((int)OutstandingRequirementResolutions.Dispatched);
			resolved.ResolvedByUnitId.Should().Be(1);
			(await _service.HasOpenRequirementsAsync(DepartmentId, CallId)).Should().BeFalse();
		}

		[Test]
		public async Task closing_the_call_resolves_the_requirement_as_call_closed()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");
			_call.State = (int)CallStates.Closed;
			_call.ClosedByUserId = "closer";

			(await _service.GetForCallAsync(DepartmentId, CallId)).Should().BeEmpty();
			_stored.Single().Resolution.Should().Be((int)OutstandingRequirementResolutions.CallClosed);
			_stored.Single().ResolvedByUserId.Should().Be("closer");
		}

		[Test]
		public async Task dismiss_records_who_and_why()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");

			(await _service.DismissAsync(DepartmentId, 1, "dispatcher", "Patient transported by family")).Should().BeTrue();

			var requirement = _stored.Single();
			requirement.Resolution.Should().Be((int)OutstandingRequirementResolutions.Dismissed);
			requirement.ResolvedByUserId.Should().Be("dispatcher");
			requirement.ResolutionNote.Should().Be("Patient transported by family");
			(await _service.DismissAsync(DepartmentId, 1, "dispatcher", null)).Should().BeFalse();
			(await _service.DismissAsync(DepartmentId + 1, 1, "dispatcher", null)).Should().BeFalse();
		}

		[Test]
		public async Task a_freed_matching_unit_alerts_dispatchers_when_auto_dispatch_is_off()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");
			_events.Invocations.Clear();

			await _service.OnUnitStateSavedAsync(DepartmentId,
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = DateTime.UtcNow },
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Committed, Timestamp = DateTime.UtcNow.AddMinutes(-30) });

			_events.Verify(x => x.SendMessageAsync(It.Is<OutstandingRequirementsUpdatedEvent>(e =>
				e.CallId == CallId && JsonConvert.DeserializeObject<OutstandingRequirementsAlert>(e.Payload).Kind == OutstandingRequirementsAlertKinds.UnitAvailable)), Times.Once);
			_repository.Verify(x => x.TryClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_stored.Single().IsOpen.Should().BeTrue();
		}

		[Test]
		public async Task a_unit_reporting_available_again_does_not_alert_twice()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");
			_events.Invocations.Clear();

			await _service.OnUnitStateSavedAsync(DepartmentId,
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = DateTime.UtcNow },
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = DateTime.UtcNow.AddMinutes(-30) });

			_events.Verify(x => x.SendMessageAsync(It.IsAny<OutstandingRequirementsUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async Task auto_dispatch_claims_the_requirement_and_sends_the_unit()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");
			_config.WaitingCallAutoDispatch = true;
			_units.Setup(x => x.GetLastUnitStateByUnitIdAsync(1)).ReturnsAsync(new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, SetByUserId = "crew" });

			await _service.OnUnitStateSavedAsync(DepartmentId,
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = DateTime.UtcNow },
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Committed, Timestamp = DateTime.UtcNow.AddMinutes(-30) });

			_repository.Verify(x => x.TryClaimAsync(1, DepartmentId, 1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_call.UnitDispatches.Should().ContainSingle(d => d.UnitId == 1);
			_dispatchStatuses.Verify(x => x.ApplyDispatchStatusesAsync(It.IsAny<Call>(), null, It.Is<IEnumerable<int>>(ids => ids.Single() == 1), It.IsAny<CancellationToken>()), Times.Once);
			_queue.Verify(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Once);
			_stored.Single().Resolution.Should().Be((int)OutstandingRequirementResolutions.AutoDispatched);
			_calls.Verify(x => x.SaveCallNoteAsync(It.Is<CallNote>(n => n.Note.Contains("Auto-dispatched M-1")), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task auto_dispatch_only_alerts_when_the_unit_is_too_far()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");
			_config.WaitingCallAutoDispatch = true;
			_config.WaitingCallMaxResponseSeconds = 600;
			// The call has no location, so the unit's response cannot be measured against the limit.

			await _service.OnUnitStateSavedAsync(DepartmentId,
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = DateTime.UtcNow },
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Committed, Timestamp = DateTime.UtcNow.AddMinutes(-30) });

			_repository.Verify(x => x.TryClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_stored.Single().IsOpen.Should().BeTrue();
		}

		[Test]
		public async Task status_changes_are_ignored_while_the_feature_is_off()
		{
			_flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

			await _service.OnUnitStateSavedAsync(DepartmentId, new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available }, null);

			_repository.Verify(x => x.GetOpenByDepartmentIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task one_step_dispatch_adds_the_unit_and_resolves_the_requirement()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");

			var result = await _service.DispatchUnitToCallAsync(DepartmentId, CallId, 1, "dispatcher");

			result.Success.Should().BeTrue();
			result.ResolvedRequirements.Should().Be(1);
			_call.UnitDispatches.Should().ContainSingle(d => d.UnitId == 1);
			_stored.Single().Resolution.Should().Be((int)OutstandingRequirementResolutions.Dispatched);
			_stored.Single().ResolvedByUserId.Should().Be("dispatcher");
		}

		[Test]
		public async Task repeated_reads_count_a_dispatch_once()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(requiredCount: 2), "user-1");
			var createdOn = _stored.Single().CreatedOn;
			_call.UnitDispatches.Add(new CallDispatchUnit { CallId = CallId, UnitId = 1, DispatchedOn = createdOn.AddMinutes(3) });

			// Badge polls, refreshes and status changes all reconcile; one MUG of the two must stay one.
			for (var i = 0; i < 3; i++)
				(await _service.GetForCallAsync(DepartmentId, CallId)).Should().ContainSingle();

			_stored.Single().ResolvedCount.Should().Be(1);
			_stored.Single().IsOpen.Should().BeTrue();
			(await _service.GetAwaitingCallCountAsync(DepartmentId)).Should().Be(1);

			_units.Setup(x => x.GetUnitsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, DepartmentId = DepartmentId, Name = "M-1", Type = "MUG" },
				new Unit { UnitId = 3, DepartmentId = DepartmentId, Name = "M-3", Type = "MUG" }
			});
			_call.UnitDispatches.Add(new CallDispatchUnit { CallId = CallId, UnitId = 3, DispatchedOn = createdOn.AddMinutes(5) });

			(await _service.GetForCallAsync(DepartmentId, CallId)).Should().BeEmpty();
			_stored.Single().ResolvedCount.Should().Be(2);
			_stored.Single().Resolution.Should().Be((int)OutstandingRequirementResolutions.Dispatched);
		}

		[Test]
		public async Task a_unit_that_fulfilled_one_requirement_does_not_count_toward_the_next()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingRows(RequirementFallbackSteps.Substitute, 1,
				Tuple.Create(500, "MUG"), Tuple.Create(501, "Second MUG")), "user-1");
			_stored.Should().HaveCount(2);
			_call.UnitDispatches.Add(new CallDispatchUnit { CallId = CallId, UnitId = 1, DispatchedOn = _stored.Max(r => r.CreatedOn).AddMinutes(3) });

			// The awaiting list passes only the open requirements; the fulfilled one must still hold on to M-1.
			for (var i = 0; i < 3; i++)
				(await _service.GetAwaitingCallCountAsync(DepartmentId)).Should().Be(1);

			_stored.Count(r => r.IsOpen).Should().Be(1);
			_stored.Single(r => r.IsOpen).ResolvedCount.Should().Be(0);
		}

		[Test]
		public async Task the_awaiting_list_and_count_keep_to_the_calls_the_reader_may_see()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");

			(await _service.GetAwaitingCallCountAsync(DepartmentId)).Should().Be(1);
			(await _service.GetAwaitingCallCountAsync(DepartmentId, new HashSet<int> { CallId })).Should().Be(1);
			(await _service.GetAwaitingCallCountAsync(DepartmentId, new HashSet<int> { CallId + 1 })).Should().Be(0);
			(await _service.GetAwaitingCallsAsync(DepartmentId, new HashSet<int> { CallId })).Should().ContainSingle(c => c.CallId == CallId);
			(await _service.GetAwaitingCallsAsync(DepartmentId, new HashSet<int>())).Should().BeEmpty();
		}

		[Test]
		public async Task auto_dispatch_stands_down_while_another_pass_holds_the_unit()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");
			_config.WaitingCallAutoDispatch = true;
			_units.Setup(x => x.GetLastUnitStateByUnitIdAsync(1)).ReturnsAsync(new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, SetByUserId = "crew" });
			_cache.Setup(x => x.IncrementAsync("OutstandingAutoDispatch_1", It.IsAny<TimeSpan>())).ReturnsAsync(2);
			_events.Invocations.Clear();

			await _service.OnUnitStateSavedAsync(DepartmentId,
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = DateTime.UtcNow },
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Committed, Timestamp = DateTime.UtcNow.AddMinutes(-30) });

			// The other pass owns this unit: no second claim, no second dispatch, no "unit available" alert, and its claim stays.
			_repository.Verify(x => x.TryClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_call.UnitDispatches.Should().BeEmpty();
			_events.Verify(x => x.SendMessageAsync(It.IsAny<OutstandingRequirementsUpdatedEvent>()), Times.Never);
			_cache.Verify(x => x.RemoveAsync(It.IsAny<string>()), Times.Never);
			_stored.Single().IsOpen.Should().BeTrue();
		}

		[Test]
		public async Task auto_dispatch_releases_its_unit_claim_when_done()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(), "user-1");
			_config.WaitingCallAutoDispatch = true;
			_units.Setup(x => x.GetLastUnitStateByUnitIdAsync(1)).ReturnsAsync(new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, SetByUserId = "crew" });
			_cache.Setup(x => x.IncrementAsync("OutstandingAutoDispatch_1", It.IsAny<TimeSpan>())).ReturnsAsync(1);

			await _service.OnUnitStateSavedAsync(DepartmentId,
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = DateTime.UtcNow },
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Committed, Timestamp = DateTime.UtcNow.AddMinutes(-30) });

			_call.UnitDispatches.Should().ContainSingle(d => d.UnitId == 1);
			_cache.Verify(x => x.RemoveAsync("OutstandingAutoDispatch_1"), Times.Once);
		}

		[Test]
		public async Task one_step_dispatch_of_a_unit_already_on_the_call_does_not_count_it_again()
		{
			await _service.CreateFromRecommendationAsync(_call, ResultWithOutstandingMug(requiredCount: 2), "user-1");
			_call.UnitDispatches.Add(new CallDispatchUnit { CallId = CallId, UnitId = 1, DispatchedOn = _stored.Single().CreatedOn.AddMinutes(3) });
			(await _service.GetForCallAsync(DepartmentId, CallId)).Single().ResolvedCount.Should().Be(1);

			var result = await _service.DispatchUnitToCallAsync(DepartmentId, CallId, 1, "dispatcher");

			result.ResolvedRequirements.Should().Be(0);
			_stored.Single().ResolvedCount.Should().Be(1);
			_stored.Single().IsOpen.Should().BeTrue();
		}

		[Test]
		public async Task sent_instead_is_cut_to_the_column_length()
		{
			var result = ResultWithOutstandingMug();
			result.Decision.Fallbacks.Single().SentInstead = string.Join(", ", Enumerable.Range(1, 80).Select(i => $"Unit {i}"));

			var created = await _service.CreateFromRecommendationAsync(_call, result, "user-1");

			created.Single().SentInstead.Length.Should().Be(200);
		}
	}
}
