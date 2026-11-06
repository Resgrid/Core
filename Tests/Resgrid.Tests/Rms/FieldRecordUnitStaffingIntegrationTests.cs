using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Uses the real UnitsService between Records and the repository boundaries. A mocked pre-hydrated
	/// IUnitsService would conceal the missing Dapper crew collection that prompted these regressions.
	/// No database, credentials, session claims, field requests or department mutations are used.
	/// </summary>
	[TestFixture]
	public class FieldRecordUnitStaffingIntegrationTests
	{
		private const int DepartmentId = 9;
		private const int UnitId = 7;
		private const int LatestStateId = 901;
		private const string CrewUser = "training-crew";
		private Mock<IUnitsRepository> _unitRepository;
		private Mock<IUnitStatesRepository> _stateRepository;
		private Mock<IUnitStateRoleRepository> _roleRepository;
		private Mock<IUnitActiveRolesRepository> _activeRepository;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<ICallsService> _calls;
		private Unit _unit;
		private UnitState _latest;
		private List<UnitStateRole> _persistedRoles;
		private UnitsService _units;
		private FieldRecordsService _field;
		private RecordWorkAssignmentsService _assignments;

		[SetUp]
		public void SetUp()
		{
			_unit = new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Training apparatus" };
			// Dapper's production projection supplies no Roles navigation collection.
			_latest = new UnitState { UnitStateId = LatestStateId, UnitId = UnitId, State = (int)UnitStateTypes.Available };
			_persistedRoles = new List<UnitStateRole>();
			_unitRepository = new Mock<IUnitsRepository>();
			_unitRepository.Setup(r => r.GetByIdAsync(UnitId)).ReturnsAsync(() => _unit);
			_stateRepository = new Mock<IUnitStatesRepository>();
			_stateRepository.Setup(r => r.GetLastUnitStateByUnitIdAsync(UnitId)).ReturnsAsync(() => _latest);
			_roleRepository = new Mock<IUnitStateRoleRepository>();
			_roleRepository.Setup(r => r.GetRolesForUnitStatesAsync(It.IsAny<IReadOnlyCollection<int>>()))
				.ReturnsAsync(() => _persistedRoles);
			_activeRepository = new Mock<IUnitActiveRolesRepository>();
			_units = new UnitsService(_unitRepository.Object, _stateRepository.Object, Mock.Of<IUnitLogsRepository>(),
				Mock.Of<IUnitTypesRepository>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(),
				_roleRepository.Object, Mock.Of<IUserStateService>(), Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(),
				new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
				Mock.Of<IUnitLocationsDocRepository>(), new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
				_activeRepository.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(), Mock.Of<IPersonnelRolesService>(),
				new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()),
				new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()), Mock.Of<ICallStatusAttributionService>());

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), DepartmentId, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_authorization.Setup(a => a.GetReadScopeStampAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync("training-scope");
			var cutover = new Mock<IRecordsCutoverService>();
			cutover.Setup(c => c.GetModuleStateAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState
				{ DepartmentId = DepartmentId, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active });
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			var definitions = new Mock<IRecordDefinitionsService>();
			definitions.Setup(d => d.GetPublishedAsync(DepartmentId)).ReturnsAsync(new List<RmsRecordDefinitionVersion>());
			definitions.Setup(d => d.ListAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<RecordDefinitionSummary>());
			_calls = new Mock<ICallsService>();
			_field = new FieldRecordsService(cutover.Object, _authorization.Object, flags.Object, definitions.Object,
				Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IRecordsService>(), Mock.Of<IRecordWorkAssignmentsService>(),
				_units, Mock.Of<IDepartmentGroupsService>(), _calls.Object, Mock.Of<IIncidentCommandService>(),
				Mock.Of<IRecordsFieldRolloutService>(), Mock.Of<ICallSourceDataService>(), Mock.Of<IDepartmentSettingsService>());
			_assignments = new RecordWorkAssignmentsService(Mock.Of<IRmsRecordWorkAssignmentsRepository>(), Mock.Of<IRmsOperationalRecordsRepository>(),
				_authorization.Object, Mock.Of<IRmsAccessAuditsRepository>(), _units, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IIncidentCommandService>());
		}

		private FieldRecordCatalogRequest Request() => new FieldRecordCatalogRequest
		{
			Origin = RmsOriginClient.Unit, AppVersion = "5.2.0", ClientCapability = RecordsClientCapabilities.Packs,
			Context = new FieldRecordContext { UnitId = UnitId }
		};

		private async Task AssertRefused()
		{
			var catalog = await _field.GetCatalogAsync(DepartmentId, CrewUser, Request());
			catalog.Ok.Should().BeFalse();
			catalog.ContextVerified.Should().BeFalse();
			catalog.Definitions.Should().BeEmpty();
			catalog.Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
			(await _assignments.IsStaffedOnUnitAsync(DepartmentId, CrewUser, UnitId)).Should().BeFalse();
		}

		[Test]
		public async Task Exact_latest_persisted_seat_enables_Unit_catalog_and_assignment_addressee()
		{
			_persistedRoles.Add(new UnitStateRole { UnitStateId = LatestStateId, UserId = CrewUser.ToUpperInvariant(), Role = "Driver" });
			var catalog = await _field.GetCatalogAsync(DepartmentId, CrewUser, Request());
			catalog.Ok.Should().BeTrue();
			catalog.ContextVerified.Should().BeTrue();
			catalog.Definitions.Select(d => d.DefinitionKey).Should().Contain(RmsDefinitionKeys.UnitActivity);
			(await _assignments.IsStaffedOnUnitAsync(DepartmentId, CrewUser, UnitId)).Should().BeTrue();
			_roleRepository.Verify(r => r.GetRolesForUnitStatesAsync(It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 1 && ids.Single() == LatestStateId)), Times.Exactly(2));
			_roleRepository.Verify(r => r.GetCurrentRolesForUnitAsync(It.IsAny<int>()), Times.Never);
			_activeRepository.Verify(r => r.GetActiveRolesByUnitIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public Task Selecting_an_apparatus_with_no_persisted_crew_cannot_author() => AssertRefused();

		[Test]
		public async Task A_different_members_latest_seat_does_not_authorize_the_caller()
		{
			_persistedRoles.Add(new UnitStateRole { UnitStateId = LatestStateId, UserId = "other-member", Role = "Driver" });
			await AssertRefused();
		}

		[Test]
		public async Task Older_matching_seats_cannot_replace_the_exact_latest_state()
		{
			_persistedRoles.Add(new UnitStateRole { UnitStateId = LatestStateId - 1, UserId = CrewUser, Role = "Driver" });
			_persistedRoles.Add(new UnitStateRole { UnitStateId = LatestStateId, UserId = "replacement-member", Role = "Driver" });
			await AssertRefused();
			_latest.Roles.Should().OnlyContain(r => r.UnitStateId == LatestStateId);
		}

		[Test]
		public async Task Prepopulated_navigation_values_cannot_replace_persisted_latest_seats()
		{
			_latest.Roles = new List<UnitStateRole> { new UnitStateRole { UnitStateId = LatestStateId, UserId = CrewUser } };
			await AssertRefused();
		}

		[Test]
		public async Task Active_picks_alone_do_not_change_the_documented_latest_saved_state_authority()
		{
			_activeRepository.Setup(r => r.GetActiveRolesByUnitIdAsync(UnitId)).ReturnsAsync(new[]
				{ new UnitActiveRole { UnitId = UnitId, DepartmentId = DepartmentId, UserId = CrewUser, Role = "Driver" } });
			await AssertRefused();
			_activeRepository.Verify(r => r.GetActiveRolesByUnitIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Other_department_unit_cannot_borrow_crew_evidence()
		{
			_unit.DepartmentId = DepartmentId + 1;
			_persistedRoles.Add(new UnitStateRole { UnitStateId = LatestStateId, UserId = CrewUser });
			await AssertRefused();
			_stateRepository.Verify(r => r.GetLastUnitStateByUnitIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Missing_stale_unit_cannot_author()
		{
			_unit = null;
			await AssertRefused();
			_stateRepository.Verify(r => r.GetLastUnitStateByUnitIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Soft_deleted_unit_cannot_author_from_its_historical_crew()
		{
			_unit.IsDeleted = true;
			_persistedRoles.Add(new UnitStateRole { UnitStateId = LatestStateId, UserId = CrewUser });
			await AssertRefused();
			_stateRepository.Verify(r => r.GetLastUnitStateByUnitIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_synthetic_default_status_with_no_saved_state_proves_no_staffing()
		{
			_latest = null;
			await AssertRefused();
			_roleRepository.Verify(r => r.GetRolesForUnitStatesAsync(It.IsAny<IReadOnlyCollection<int>>()), Times.Never);
		}

		[Test]
		public async Task Ordinary_status_read_does_not_query_the_crew_repository()
		{
			await _units.GetLastUnitStateByUnitIdAsync(UnitId);
			_roleRepository.Verify(r => r.GetRolesForUnitStatesAsync(It.IsAny<IReadOnlyCollection<int>>()), Times.Never);
		}

		[Test]
		public async Task Null_role_rows_cannot_authorize_a_member()
		{
			_persistedRoles.Add(null);
			await AssertRefused();
		}

		[Test]
		public async Task A_null_role_result_is_no_staffing_evidence()
		{
			_persistedRoles = null;
			await AssertRefused();
		}

		[Test]
		public void Role_read_failure_cannot_degrade_into_an_authorized_context()
		{
			_roleRepository.Setup(r => r.GetRolesForUnitStatesAsync(It.IsAny<IReadOnlyCollection<int>>())).ThrowsAsync(new InvalidOperationException("training read failure"));
			Assert.ThrowsAsync<InvalidOperationException>(async () => await _field.GetCatalogAsync(DepartmentId, CrewUser, Request()));
			Assert.ThrowsAsync<InvalidOperationException>(async () => await _assignments.IsStaffedOnUnitAsync(DepartmentId, CrewUser, UnitId));
		}

		[Test]
		public async Task Valid_crew_does_not_bypass_source_call_authorization()
		{
			_persistedRoles.Add(new UnitStateRole { UnitStateId = LatestStateId, UserId = CrewUser });
			_calls.Setup(c => c.GetCallByIdAsync(501, true)).ReturnsAsync(new Call { CallId = 501, DepartmentId = DepartmentId });
			_authorization.Setup(a => a.CanReadSourceCallAsync(CrewUser, DepartmentId, It.IsAny<Call>())).ReturnsAsync(false);
			var request = Request();
			request.Context.CallId = 501;
			var catalog = await _field.GetCatalogAsync(DepartmentId, CrewUser, request);
			catalog.Ok.Should().BeFalse();
			catalog.Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
			_stateRepository.Verify(r => r.GetLastUnitStateByUnitIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Other_app_visible_unit_reference_does_not_claim_Unit_crew_membership()
		{
			var verified = await _field.VerifyContextAsync(DepartmentId, CrewUser, RmsOriginClient.Responder, new FieldRecordContext { UnitId = UnitId });
			verified.Ok.Should().BeTrue();
			verified.StaffedOnUnit.Should().BeFalse();
		}
	}
}
