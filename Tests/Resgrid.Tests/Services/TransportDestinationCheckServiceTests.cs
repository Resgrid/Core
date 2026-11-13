using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Belgian EMS, 2026-11-13: a call's assigned destination (the hospital) is pre-selected for transport statuses, and a
	/// unit heading somewhere else is recorded on the call when the department turned transport destination checks on
	/// (DepartmentSettingTypes.TransportDestinationChecks).
	/// </summary>
	[TestFixture]
	public class TransportDestinationCheckServiceTests
	{
		private const int DeptId = 7;
		private const int UnitId = 5;
		private const int CallId = 40;
		private const int AssignedPoi = 900;
		private const int OtherPoi = 901;

		private const int Departing = 100; // Transporting
		private const int AtHospital = 101; // At Hospital
		private const int OnScene = 102; // On Scene
		private const int NoMeaning = 103; // no base type

		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ICallStatusAttributionService> _attribution;
		private Mock<ICallsService> _calls;
		private Mock<IMappingService> _mapping;
		private Mock<IUnitsService> _units;
		private Mock<ICustomStateService> _customStates;
		private TransportDestinationCheckService _service;

		private Call _call;
		private CallNote _savedNote;

		[SetUp]
		public void SetUp()
		{
			_settings = new Mock<IDepartmentSettingsService>();
			_attribution = new Mock<ICallStatusAttributionService>();
			_calls = new Mock<ICallsService>();
			_mapping = new Mock<IMappingService>();
			_units = new Mock<IUnitsService>();
			_customStates = new Mock<ICustomStateService>();
			_savedNote = null;

			_call = new Call { CallId = CallId, DepartmentId = DeptId, State = (int)CallStates.Active, DestinationPoiId = AssignedPoi, ReportingUserId = "reporter" };

			_settings.Setup(x => x.GetTransportDestinationChecksAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(true);
			_customStates.Setup(x => x.GetDefaultUnitStatuses()).Returns(new CustomStateService(null, null, null, null).GetDefaultUnitStatuses());
			_customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(DeptId)).ReturnsAsync(new List<CustomState>
			{
				new CustomState
				{
					CustomStateId = 1,
					DepartmentId = DeptId,
					Type = (int)CustomStateTypes.Unit,
					Details = new List<CustomStateDetail>
					{
						new CustomStateDetail { CustomStateDetailId = Departing, ButtonText = "Vertrek ZH", BaseType = (int)ActionBaseTypes.Transporting, DetailType = (int)CustomStateDetailTypes.CallsAndPois },
						new CustomStateDetail { CustomStateDetailId = AtHospital, ButtonText = "Aankomst ZH", BaseType = (int)ActionBaseTypes.AtHospital, DetailType = (int)CustomStateDetailTypes.Pois },
						new CustomStateDetail { CustomStateDetailId = OnScene, ButtonText = "Ter plaatse", BaseType = (int)ActionBaseTypes.OnScene, DetailType = (int)CustomStateDetailTypes.CallsAndPois },
						new CustomStateDetail { CustomStateDetailId = NoMeaning, ButtonText = "Elders", BaseType = (int)ActionBaseTypes.None, DetailType = (int)CustomStateDetailTypes.Pois }
					}
				}
			});
			_attribution.Setup(x => x.GetWorkingCallIdForUnitAsync(DeptId, UnitId, It.IsAny<UnitState>())).ReturnsAsync(CallId);
			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.Callback((CallNote note, CancellationToken _) => _savedNote = note)
				.ReturnsAsync((CallNote note, CancellationToken _) => note);
			_mapping.Setup(x => x.GetDestinationPOIByIdAsync(DeptId, AssignedPoi)).ReturnsAsync(new Poi { PoiId = AssignedPoi, Name = "AZ Sint-Elisabeth Zottegem" });
			_mapping.Setup(x => x.GetDestinationPOIByIdAsync(DeptId, OtherPoi)).ReturnsAsync(new Poi { PoiId = OtherPoi, Name = "UZ Gent" });
			_units.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DeptId, Name = "ZW Herzele" });

			_service = new TransportDestinationCheckService(_settings.Object, _attribution.Object, _calls.Object, _mapping.Object, _units.Object, _customStates.Object);
		}

		private static UnitState State(int status, int? poiId, string note = "STEMI, cath lab", int? destinationType = (int)DestinationEntityTypes.Poi) => new UnitState
		{
			UnitStateId = 300,
			UnitId = UnitId,
			State = status,
			DestinationId = poiId,
			DestinationType = poiId.HasValue ? destinationType : null,
			Note = note,
			SetByUserId = "dispatcher",
			Timestamp = DateTime.UtcNow
		};

		private static UnitState OnSceneAtCall() => new UnitState
		{
			UnitStateId = 299,
			UnitId = UnitId,
			State = OnScene,
			DestinationId = CallId,
			DestinationType = (int)DestinationEntityTypes.Call
		};

		[Test]
		public async Task a_transport_status_to_another_hospital_is_noted_on_the_call_with_both_destinations_and_the_reason()
		{
			var note = await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi), OnSceneAtCall());

			note.Should().NotBeNull();
			_savedNote.CallId.Should().Be(CallId);
			_savedNote.UserId.Should().Be("dispatcher");
			_savedNote.Source.Should().Be((int)CallNoteSources.System);
			_savedNote.Note.Should().Be("ZW Herzele set Vertrek ZH to UZ Gent, not the call's assigned destination AZ Sint-Elisabeth Zottegem. Reason: STEMI, cath lab");
			_attribution.Verify(x => x.GetWorkingCallIdForUnitAsync(DeptId, UnitId, It.Is<UnitState>(s => s.UnitStateId == 299)), Times.Once);
		}

		[Test]
		public async Task at_hospital_counts_as_a_transport_status()
		{
			await _service.OnUnitStateSavedAsync(DeptId, State(AtHospital, OtherPoi), OnSceneAtCall());

			_savedNote.Should().NotBeNull();
			_savedNote.Note.Should().StartWith("ZW Herzele set Aankomst ZH to UZ Gent");
		}

		[Test]
		public async Task a_deviation_without_a_reason_says_so()
		{
			await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi, note: "  "), OnSceneAtCall());

			_savedNote.Note.Should().EndWith("No reason given.");
		}

		[Test]
		public async Task a_protected_status_note_is_not_copied_to_the_call()
		{
			var state = State(Departing, OtherPoi);
			state.IsProtected = true;
			state.Note = "rgdp:envelope";

			await _service.OnUnitStateSavedAsync(DeptId, state, OnSceneAtCall());

			_savedNote.Note.Should().NotContain("rgdp");
			_savedNote.Note.Should().Contain("recorded on the unit's status");
		}

		[Test]
		public async Task the_reporting_user_signs_the_note_when_the_status_has_no_submitter()
		{
			var state = State(Departing, OtherPoi);
			state.SetByUserId = null;

			await _service.OnUnitStateSavedAsync(DeptId, state, OnSceneAtCall());

			_savedNote.UserId.Should().Be("reporter");
		}

		[Test]
		public async Task the_assigned_hospital_is_not_a_deviation()
		{
			(await _service.OnUnitStateSavedAsync(DeptId, State(Departing, AssignedPoi), OnSceneAtCall())).Should().BeNull();

			_calls.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task nothing_is_recorded_when_the_department_has_the_checks_off()
		{
			_settings.Setup(x => x.GetTransportDestinationChecksAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(false);

			(await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi), OnSceneAtCall())).Should().BeNull();

			_calls.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_call_without_an_assigned_destination_has_nothing_to_deviate_from()
		{
			_call.DestinationPoiId = null;

			(await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi), OnSceneAtCall())).Should().BeNull();
		}

		[TestCase(OnScene)]
		[TestCase(NoMeaning)]
		[TestCase((int)UnitStateTypes.Responding)]
		public async Task only_transporting_and_at_hospital_base_types_are_checked(int status)
		{
			(await _service.OnUnitStateSavedAsync(DeptId, State(status, OtherPoi), OnSceneAtCall())).Should().BeNull();

			_calls.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase((int)DestinationEntityTypes.Station)]
		[TestCase((int)DestinationEntityTypes.Call)]
		public async Task a_station_or_call_destination_is_never_checked(int destinationType)
		{
			(await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi, destinationType: destinationType), OnSceneAtCall())).Should().BeNull();

			_settings.Verify(x => x.GetTransportDestinationChecksAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task arriving_at_the_hospital_the_unit_already_headed_to_is_not_noted_twice()
		{
			var departing = State(Departing, OtherPoi);
			departing.UnitStateId = 299;

			(await _service.OnUnitStateSavedAsync(DeptId, State(AtHospital, OtherPoi), departing)).Should().BeNull();

			_settings.Verify(x => x.GetTransportDestinationChecksAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task changing_hospital_mid_transport_is_noted_again()
		{
			var departing = State(Departing, OtherPoi);
			departing.UnitStateId = 299;
			_mapping.Setup(x => x.GetDestinationPOIByIdAsync(DeptId, 902)).ReturnsAsync(new Poi { PoiId = 902, Name = "UZ Brussel" });

			await _service.OnUnitStateSavedAsync(DeptId, State(Departing, 902, note: "Burn centre"), departing);

			_savedNote.Note.Should().Contain("to UZ Brussel").And.EndWith("Reason: Burn centre");
		}

		[Test]
		public async Task a_second_hospital_change_walks_back_to_the_status_that_names_the_call()
		{
			// A unit that took the call itself (no dispatch): its first transport status points at a POI, not at the call.
			var departing = State(Departing, OtherPoi);
			departing.UnitStateId = 299;
			var onScene = OnSceneAtCall();
			onScene.UnitStateId = 298;
			_units.Setup(x => x.GetLastUnitStateBeforeIdAsync(UnitId, 299)).ReturnsAsync(onScene);
			_mapping.Setup(x => x.GetDestinationPOIByIdAsync(DeptId, 902)).ReturnsAsync(new Poi { PoiId = 902, Name = "UZ Brussel" });

			await _service.OnUnitStateSavedAsync(DeptId, State(Departing, 902, note: "Burn centre"), departing);

			_attribution.Verify(x => x.GetWorkingCallIdForUnitAsync(DeptId, UnitId, It.Is<UnitState>(s => s.UnitStateId == 298)), Times.Once);
			_savedNote.Note.Should().Contain("to UZ Brussel");
		}

		[Test]
		public async Task the_walk_back_stops_at_a_status_that_is_not_a_transport_to_a_poi()
		{
			var departing = State(Departing, OtherPoi);
			departing.UnitStateId = 299;
			var earlier = State(NoMeaning, AssignedPoi);
			earlier.UnitStateId = 298;
			_units.Setup(x => x.GetLastUnitStateBeforeIdAsync(UnitId, 299)).ReturnsAsync(earlier);

			await _service.OnUnitStateSavedAsync(DeptId, State(AtHospital, 902), departing);

			_attribution.Verify(x => x.GetWorkingCallIdForUnitAsync(DeptId, UnitId, It.Is<UnitState>(s => s.UnitStateId == 298)), Times.Once);
			_units.Verify(x => x.GetLastUnitStateBeforeIdAsync(UnitId, 298), Times.Never);
		}

		[Test]
		public async Task an_offline_status_replayed_after_a_newer_one_is_not_noted()
		{
			var replayed = State(Departing, OtherPoi);
			replayed.Timestamp = DateTime.UtcNow.AddMinutes(-20);
			var newer = OnSceneAtCall();
			newer.Timestamp = DateTime.UtcNow.AddMinutes(-2);

			(await _service.OnUnitStateSavedAsync(DeptId, replayed, newer)).Should().BeNull();

			_attribution.Verify(x => x.GetWorkingCallIdForUnitAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UnitState>()), Times.Never);
		}

		[Test]
		public async Task an_offline_status_replayed_in_order_is_still_noted()
		{
			var replayed = State(Departing, OtherPoi);
			replayed.Timestamp = DateTime.UtcNow.AddMinutes(-20);
			var before = OnSceneAtCall();
			before.Timestamp = DateTime.UtcNow.AddMinutes(-30);

			(await _service.OnUnitStateSavedAsync(DeptId, replayed, before)).Should().NotBeNull();
		}

		[Test]
		public async Task a_unit_with_no_working_call_is_not_checked()
		{
			_attribution.Setup(x => x.GetWorkingCallIdForUnitAsync(DeptId, UnitId, It.IsAny<UnitState>())).ReturnsAsync((int?)null);

			(await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi), null)).Should().BeNull();
		}

		[Test]
		public async Task without_a_previous_status_the_working_call_is_resolved_from_the_new_one()
		{
			await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi), null);

			_attribution.Verify(x => x.GetWorkingCallIdForUnitAsync(DeptId, UnitId, It.Is<UnitState>(s => s.UnitStateId == 300)), Times.Once);
			_savedNote.Should().NotBeNull();
		}

		[TestCase(CallStates.Closed, DeptId)]
		[TestCase(CallStates.Active, 99)]
		public async Task a_closed_call_or_another_departments_call_is_not_noted(CallStates state, int departmentId)
		{
			_call.State = (int)state;
			_call.DepartmentId = departmentId;

			(await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi), OnSceneAtCall())).Should().BeNull();
		}

		[Test]
		public async Task a_destination_no_longer_listed_is_named_by_its_id()
		{
			_mapping.Setup(x => x.GetDestinationPOIByIdAsync(DeptId, OtherPoi)).ReturnsAsync((Poi)null);

			await _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi), OnSceneAtCall());

			_savedNote.Note.Should().Contain($"to POI {OtherPoi},");
		}

		[Test]
		public async Task a_failure_never_reaches_the_status_save()
		{
			_calls.Setup(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			Func<Task> act = () => _service.OnUnitStateSavedAsync(DeptId, State(Departing, OtherPoi), OnSceneAtCall());

			await act.Should().NotThrowAsync();
		}

		[Test]
		public void note_text_falls_back_for_a_missing_unit_or_status_name()
		{
			TransportDestinationCheckService.BuildNoteText(null, " ", "UZ Gent", "AZ Zottegem", null)
				.Should().Be("A unit set a transport status to UZ Gent, not the call's assigned destination AZ Zottegem. No reason given.");
		}
	}

	/// <summary>The rules every app shares (<see cref="TransportDestinationRules"/>).</summary>
	[TestFixture]
	public class TransportDestinationRulesTests
	{
		[TestCase((int)ActionBaseTypes.Transporting, true)]
		[TestCase((int)ActionBaseTypes.AtHospital, true)]
		[TestCase((int)ActionBaseTypes.Delivering, false)]
		[TestCase((int)ActionBaseTypes.OnScene, false)]
		[TestCase((int)ActionBaseTypes.None, false)]
		[TestCase(null, false)]
		public void transport_base_types(int? baseType, bool expected)
		{
			TransportDestinationRules.IsTransportBaseType(baseType).Should().Be(expected);
		}

		[TestCase(901, (int)DestinationEntityTypes.Poi, 900, true)]
		[TestCase(900, (int)DestinationEntityTypes.Poi, 900, false)]
		[TestCase(901, (int)DestinationEntityTypes.Station, 900, false)]
		[TestCase(901, (int)DestinationEntityTypes.Call, 900, false)]
		[TestCase(901, (int)DestinationEntityTypes.Poi, null, false)]
		[TestCase(901, (int)DestinationEntityTypes.Poi, 0, false)]
		[TestCase(null, (int)DestinationEntityTypes.Poi, 900, false)]
		[TestCase(901, null, 900, false)]
		public void deviation(int? destinationId, int? destinationType, int? assigned, bool expected)
		{
			TransportDestinationRules.IsDeviation(destinationId, destinationType, assigned).Should().Be(expected);
		}

		[Test]
		public void assigned_destination_ignores_zero_and_missing()
		{
			TransportDestinationRules.AssignedDestinationPoiId(new Call { DestinationPoiId = 0 }).Should().BeNull();
			TransportDestinationRules.AssignedDestinationPoiId(new Call()).Should().BeNull();
			TransportDestinationRules.AssignedDestinationPoiId(null).Should().BeNull();
			TransportDestinationRules.AssignedDestinationPoiId(new Call { DestinationPoiId = 12 }).Should().Be(12);
		}
	}

	/// <summary>UnitsService runs the transport destination check after a status a unit reported, never after Resgrid's own.</summary>
	[TestFixture]
	public class UnitsServiceTransportDestinationTests
	{
		private const int DeptId = 10;

		private Mock<IUnitStatesRepository> _unitStatesRepo;
		private Mock<ITransportDestinationCheckService> _transport;
		private UnitsService _service;

		[SetUp]
		public void SetUp()
		{
			_unitStatesRepo = new Mock<IUnitStatesRepository>();
			_unitStatesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<UnitState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UnitState s, CancellationToken _, bool __) => s);

			var protectedWrite = new Mock<IProtectedWriteService>();
			protectedWrite.Setup(x => x.PrepareUnitStateWriteAsync(It.IsAny<int>(), It.IsAny<UnitState>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			_transport = new Mock<ITransportDestinationCheckService>();

			_service = new UnitsService(
				Mock.Of<IUnitsRepository>(), _unitStatesRepo.Object, Mock.Of<IUnitLogsRepository>(),
				Mock.Of<IUnitTypesRepository>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(),
				Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<ICustomStateService>(),
				new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
				Mock.Of<IUnitLocationsDocRepository>(),
				new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
				Mock.Of<IUnitActiveRolesRepository>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(),
				Mock.Of<IPersonnelRolesService>(),
				new Lazy<IProtectedWriteService>(() => protectedWrite.Object),
				new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()),
				Mock.Of<ICallStatusAttributionService>(),
				transportDestinationCheckService: new Lazy<ITransportDestinationCheckService>(() => _transport.Object));
		}

		private static UnitState PoiState() => new UnitState
		{
			UnitStateId = 55,
			UnitId = 7,
			State = 100,
			DestinationId = 901,
			DestinationType = (int)DestinationEntityTypes.Poi,
			Timestamp = DateTime.UtcNow
		};

		[Test]
		public async Task a_reported_poi_status_is_checked()
		{
			var state = PoiState();

			await _service.SetUnitStateAsync(state, DeptId);

			_transport.Verify(x => x.OnUnitStateSavedAsync(DeptId, state, It.IsAny<UnitState>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task an_automatic_status_is_never_checked()
		{
			await _service.SetUnitStateAsync(PoiState(), DeptId, autoGenerated: true);

			_transport.Verify(x => x.OnUnitStateSavedAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<UnitState>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_status_without_a_poi_skips_the_check()
		{
			var state = PoiState();
			state.DestinationType = (int)DestinationEntityTypes.Call;

			await _service.SetUnitStateAsync(state, DeptId);

			_transport.Verify(x => x.OnUnitStateSavedAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<UnitState>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_failing_check_never_fails_the_save()
		{
			_transport.Setup(x => x.OnUnitStateSavedAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<UnitState>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("boom"));

			var saved = await _service.SetUnitStateAsync(PoiState(), DeptId);

			saved.Should().NotBeNull();
		}
	}
}
