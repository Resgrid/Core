using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class LocationVisibilityServiceTests
	{
		private const int DepartmentId = 7;
		private const string UnitMatrixKey = "ViewUnitLocationsSecurityMaxtix_7";
		private const string PersonnelMatrixKey = "ViewUserLocationsSecurityMaxtix_7";

		private const string Admin = "a0000000-0000-0000-0000-000000000001";
		private const string StationOneMember = "b0000000-0000-0000-0000-000000000001";
		private const string StationTwoMember = "c0000000-0000-0000-0000-000000000001";

		private Mock<ICacheProvider> _cacheProvider;
		private Mock<IEventAggregator> _eventAggregator;
		private ManualTimeProvider _clock;
		private LocationVisibilityService _service;

		[SetUp]
		public void SetUp()
		{
			_cacheProvider = new Mock<ICacheProvider>();
			_eventAggregator = new Mock<IEventAggregator>();
			_clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
			_service = new LocationVisibilityService(_cacheProvider.Object, _eventAggregator.Object, _clock);
		}

		[Test]
		public async Task Unit_location_goes_to_the_department_when_no_matrix_is_cached()
		{
			var audience = await _service.GetUnitLocationAudienceAsync(DepartmentId, 12);

			audience.IsEntireDepartment.Should().BeTrue();
			(await _service.CanViewUnitLocationAsync(DepartmentId, 12, StationTwoMember)).Should().BeTrue();
		}

		[Test]
		public async Task Unit_location_goes_to_the_department_when_everyone_may_see_unit_locations()
		{
			SetUnitMatrix(new VisibilityPayloadUnits { EveryoneNoGroupLock = true });

			(await _service.GetUnitLocationAudienceAsync(DepartmentId, 12)).IsEntireDepartment.Should().BeTrue();
			(await _service.GetVisibilitySetKeysForViewerAsync(DepartmentId, StationOneMember)).Should().BeEmpty();
		}

		[Test]
		public async Task Units_with_the_same_viewers_share_one_visibility_set_whatever_the_order_or_casing()
		{
			SetUnitMatrix(Restricted(new Dictionary<int, List<string>>
			{
				[12] = new List<string> { Admin, StationOneMember },
				[13] = new List<string> { StationOneMember.ToUpperInvariant(), Admin, Admin },
				[14] = new List<string> { Admin, StationTwoMember }
			}));

			var unit12 = await _service.GetUnitLocationAudienceAsync(DepartmentId, 12);
			var unit13 = await _service.GetUnitLocationAudienceAsync(DepartmentId, 13);
			var unit14 = await _service.GetUnitLocationAudienceAsync(DepartmentId, 14);

			unit12.IsEntireDepartment.Should().BeFalse();
			unit13.VisibilitySetKey.Should().Be(unit12.VisibilitySetKey);
			unit14.VisibilitySetKey.Should().NotBe(unit12.VisibilitySetKey);
		}

		[Test]
		public async Task Only_listed_viewers_may_see_a_restricted_unit()
		{
			SetUnitMatrix(Restricted(new Dictionary<int, List<string>>
			{
				[12] = new List<string> { Admin, StationOneMember }
			}));

			(await _service.CanViewUnitLocationAsync(DepartmentId, 12, StationOneMember.ToUpperInvariant())).Should().BeTrue();
			(await _service.CanViewUnitLocationAsync(DepartmentId, 12, StationTwoMember)).Should().BeFalse();
			(await _service.CanViewUnitLocationAsync(DepartmentId, 12, null)).Should().BeFalse();
		}

		[Test]
		public async Task Unit_missing_from_the_matrix_fails_open_and_requests_one_rebuild()
		{
			SetUnitMatrix(Restricted(new Dictionary<int, List<string>>
			{
				[12] = new List<string> { Admin }
			}));

			(await _service.GetUnitLocationAudienceAsync(DepartmentId, 99)).IsEntireDepartment.Should().BeTrue();
			(await _service.GetUnitLocationAudienceAsync(DepartmentId, 99)).IsEntireDepartment.Should().BeTrue();

			_eventAggregator.Verify(x => x.SendMessage(It.Is<SecurityRefreshEvent>(e =>
				e.DepartmentId == DepartmentId && e.Type == SecurityCacheTypes.WhoCanViewUnitLocations)), Times.Once);
		}

		[Test]
		public async Task A_person_may_always_see_their_own_location()
		{
			SetPersonnelMatrix(Restricted(new Dictionary<string, List<string>>
			{
				[StationOneMember] = new List<string> { Admin }
			}));

			(await _service.CanViewPersonnelLocationAsync(DepartmentId, StationOneMember, StationOneMember.ToUpperInvariant())).Should().BeTrue();
			(await _service.CanViewPersonnelLocationAsync(DepartmentId, StationOneMember, Admin)).Should().BeTrue();
			(await _service.CanViewPersonnelLocationAsync(DepartmentId, StationOneMember, StationTwoMember)).Should().BeFalse();
			(await _service.GetPersonnelLocationAudienceAsync(DepartmentId, StationOneMember.ToUpperInvariant())).IsEntireDepartment.Should().BeFalse();
		}

		[Test]
		public async Task Viewer_is_given_every_unit_and_personnel_set_that_lists_them()
		{
			SetUnitMatrix(Restricted(new Dictionary<int, List<string>>
			{
				[12] = new List<string> { Admin, StationOneMember },
				[14] = new List<string> { Admin, StationTwoMember }
			}));
			SetPersonnelMatrix(Restricted(new Dictionary<string, List<string>>
			{
				[StationOneMember] = new List<string> { Admin }
			}));

			var unit12 = (await _service.GetUnitLocationAudienceAsync(DepartmentId, 12)).VisibilitySetKey;
			var unit14 = (await _service.GetUnitLocationAudienceAsync(DepartmentId, 14)).VisibilitySetKey;
			var person = (await _service.GetPersonnelLocationAudienceAsync(DepartmentId, StationOneMember)).VisibilitySetKey;

			(await _service.GetVisibilitySetKeysForViewerAsync(DepartmentId, Admin)).Should().BeEquivalentTo(new[] { unit12, unit14, person });
			(await _service.GetVisibilitySetKeysForViewerAsync(DepartmentId, StationOneMember)).Should().BeEquivalentTo(new[] { unit12 });
			(await _service.GetVisibilitySetKeysForViewerAsync(DepartmentId, "d0000000-0000-0000-0000-000000000001")).Should().BeEmpty();
		}

		[Test]
		public async Task Matrix_is_read_once_per_snapshot_lifetime()
		{
			SetUnitMatrix(Restricted(new Dictionary<int, List<string>> { [12] = new List<string> { Admin } }));

			await _service.GetUnitLocationAudienceAsync(DepartmentId, 12);
			await _service.GetUnitLocationAudienceAsync(DepartmentId, 12);
			_cacheProvider.Verify(x => x.GetAsync<VisibilityPayloadUnits>(UnitMatrixKey), Times.Once);

			// A rebuilt matrix is picked up once the snapshot expires.
			SetUnitMatrix(new VisibilityPayloadUnits { EveryoneNoGroupLock = true });
			_clock.Advance(LocationVisibilityService.SnapshotLifetime);

			(await _service.GetUnitLocationAudienceAsync(DepartmentId, 12)).IsEntireDepartment.Should().BeTrue();
			_cacheProvider.Verify(x => x.GetAsync<VisibilityPayloadUnits>(UnitMatrixKey), Times.Exactly(2));
		}

		[Test]
		public async Task Unreadable_matrix_fails_open_like_the_rest_checks()
		{
			_cacheProvider
				.Setup(x => x.GetAsync<VisibilityPayloadUnits>(UnitMatrixKey))
				.ThrowsAsync(new TimeoutException("redis"));

			(await _service.GetUnitLocationAudienceAsync(DepartmentId, 12)).IsEntireDepartment.Should().BeTrue();
		}

		[Test]
		public async Task Agrees_with_the_AuthorizationService_matrix_checks()
		{
			var viewers = new[] { Admin, StationOneMember, StationTwoMember, "d0000000-0000-0000-0000-000000000001" };
			SetUnitMatrix(Restricted(new Dictionary<int, List<string>>
			{
				[12] = new List<string> { Admin, StationOneMember },
				[14] = new List<string> { Admin, StationTwoMember },
				[15] = new List<string>()
			}));
			SetPersonnelMatrix(Restricted(new Dictionary<string, List<string>>
			{
				[StationOneMember] = new List<string> { Admin, StationOneMember },
				[StationTwoMember] = new List<string> { Admin }
			}));
			var authorizationService = CreateAuthorizationService(_cacheProvider.Object);

			foreach (var viewer in viewers)
			{
				// 99 is not in the matrix: both fail open.
				foreach (var unitId in new[] { 12, 14, 15, 99 })
				{
					var expected = await authorizationService.CanUserViewUnitLocationViaMatrixAsync(unitId, viewer, DepartmentId);
					(await _service.CanViewUnitLocationAsync(DepartmentId, unitId, viewer))
						.Should().Be(expected, $"viewer {viewer} and unit {unitId}");
				}

				foreach (var person in viewers)
				{
					var expected = await authorizationService.CanUserViewPersonLocationViaMatrixAsync(person, viewer, DepartmentId);
					(await _service.CanViewPersonnelLocationAsync(DepartmentId, person, viewer))
						.Should().Be(expected, $"viewer {viewer} and person {person}");
				}
			}
		}

		private void SetUnitMatrix(VisibilityPayloadUnits matrix)
		{
			_cacheProvider.Setup(x => x.GetAsync<VisibilityPayloadUnits>(UnitMatrixKey)).ReturnsAsync(matrix);
		}

		private void SetPersonnelMatrix(VisibilityPayloadUsers matrix)
		{
			_cacheProvider.Setup(x => x.GetAsync<VisibilityPayloadUsers>(PersonnelMatrixKey)).ReturnsAsync(matrix);
		}

		private static VisibilityPayloadUnits Restricted(Dictionary<int, List<string>> units) =>
			new VisibilityPayloadUnits { EveryoneNoGroupLock = false, Units = units, GeneratedOn = DateTime.UtcNow };

		private static VisibilityPayloadUsers Restricted(Dictionary<string, List<string>> users) =>
			new VisibilityPayloadUsers { EveryoneNoGroupLock = false, Users = users, GeneratedOn = DateTime.UtcNow };

		private static AuthorizationService CreateAuthorizationService(ICacheProvider cacheProvider)
		{
			return new AuthorizationService(Mock.Of<IDepartmentsService>(), Mock.Of<IInvitesService>(), Mock.Of<ICallsService>(),
				Mock.Of<IMessageService>(), Mock.Of<IWorkLogsService>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IUnitsService>(), Mock.Of<IPermissionsService>(), Mock.Of<ICalendarService>(),
				Mock.Of<IProtocolsService>(), Mock.Of<IShiftsService>(), Mock.Of<ICustomStateService>(), Mock.Of<ICertificationService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(), cacheProvider, Mock.Of<IContactsService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<IDispatchScopeService>());
		}

		private sealed class ManualTimeProvider : TimeProvider
		{
			private DateTimeOffset _now;

			public ManualTimeProvider(DateTimeOffset now)
			{
				_now = now;
			}

			public override DateTimeOffset GetUtcNow() => _now;

			public void Advance(TimeSpan by) => _now = _now.Add(by);
		}
	}
}
