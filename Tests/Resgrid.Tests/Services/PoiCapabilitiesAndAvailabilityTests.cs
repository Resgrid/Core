using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Destination capabilities and availability (Belgian EMS, 2026-11-13): a department tags its POIs with capabilities
	/// ("Cath Lab"), and dispatchers record when a destination is limited, on diversion or closed.
	/// </summary>
	[TestFixture]
	public class PoiCapabilitiesAndAvailabilityTests
	{
		private const int DeptId = 7;
		private const int HospitalType = 3;
		private const int ShelterType = 4;
		private const int Gent = 901;
		private const int Zottegem = 900;
		private const int Shelter = 950;

		private Mock<IPoiTypesRepository> _poiTypes;
		private Mock<IPoisRepository> _pois;
		private Mock<IPoiCapabilitiesRepository> _capabilities;
		private Mock<IPoiAvailabilitiesRepository> _availabilities;
		private MappingService _service;

		private List<PoiCapability> _storedCapabilities;
		private List<PoiCapabilityAssignment> _storedAssignments;
		private List<PoiAvailability> _storedAvailabilities;

		[SetUp]
		public void SetUp()
		{
			_poiTypes = new Mock<IPoiTypesRepository>();
			_pois = new Mock<IPoisRepository>();
			_capabilities = new Mock<IPoiCapabilitiesRepository>();
			_availabilities = new Mock<IPoiAvailabilitiesRepository>();

			_storedCapabilities = new List<PoiCapability>
			{
				new PoiCapability { PoiCapabilityId = "stroke", DepartmentId = DeptId, Name = "Stroke Center", Sort = 2 },
				new PoiCapability { PoiCapabilityId = "cath", DepartmentId = DeptId, Name = "Cath Lab", Sort = 1 },
				new PoiCapability { PoiCapabilityId = "trauma", DepartmentId = DeptId, Name = "Trauma Center", Sort = 3 }
			};
			_storedAssignments = new List<PoiCapabilityAssignment>
			{
				new PoiCapabilityAssignment { DepartmentId = DeptId, PoiId = Gent, PoiCapabilityId = "trauma" },
				new PoiCapabilityAssignment { DepartmentId = DeptId, PoiId = Gent, PoiCapabilityId = "CATH" },
				new PoiCapabilityAssignment { DepartmentId = DeptId, PoiId = Zottegem, PoiCapabilityId = "stroke" },
				new PoiCapabilityAssignment { DepartmentId = DeptId, PoiId = Zottegem, PoiCapabilityId = "deleted-elsewhere" }
			};
			_storedAvailabilities = new List<PoiAvailability>();

			_poiTypes.Setup(x => x.GetPoiTypesByDepartmentIdAsync(DeptId)).ReturnsAsync(() => new List<PoiType>
			{
				new PoiType
				{
					PoiTypeId = HospitalType, DepartmentId = DeptId, Name = "Hospital", IsDestination = true,
					Pois = new List<Poi>
					{
						new Poi { PoiId = Zottegem, PoiTypeId = HospitalType, Name = "AZ Sint-Elisabeth Zottegem", Latitude = 50.87, Longitude = 3.81 },
						new Poi { PoiId = Gent, PoiTypeId = HospitalType, Name = "UZ Gent", Latitude = 51.02, Longitude = 3.73 }
					}
				},
				new PoiType
				{
					PoiTypeId = ShelterType, DepartmentId = DeptId, Name = "Shelter", IsDestination = false,
					Pois = new List<Poi> { new Poi { PoiId = Shelter, PoiTypeId = ShelterType, Name = "Gym" } }
				}
			});
			_capabilities.Setup(x => x.GetAllByDepartmentIdAsync(DeptId)).ReturnsAsync(() => _storedCapabilities.ToList());
			_capabilities.Setup(x => x.GetAssignmentsByDepartmentIdAsync(DeptId)).ReturnsAsync(() => _storedAssignments.ToList());
			_capabilities.Setup(x => x.SaveOrUpdateAsync(It.IsAny<PoiCapability>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((PoiCapability c, CancellationToken _, bool __) =>
				{
					c.PoiCapabilityId ??= Guid.NewGuid().ToString();
					return c;
				});
			_availabilities.Setup(x => x.GetAllByDepartmentIdAsync(DeptId)).ReturnsAsync(() => _storedAvailabilities.ToList());
			_availabilities.Setup(x => x.GetByPoiIdAsync(DeptId, It.IsAny<int>())).ReturnsAsync((int _, int poiId) => _storedAvailabilities.FirstOrDefault(a => a.PoiId == poiId));
			_availabilities.Setup(x => x.SaveOrUpdateAsync(It.IsAny<PoiAvailability>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((PoiAvailability a, CancellationToken _, bool __) => a);

			_service = new MappingService(_poiTypes.Object, _pois.Object, null, null, null, null, _capabilities.Object, _availabilities.Object);
		}

		[Test]
		public async Task capabilities_are_listed_in_the_departments_order()
		{
			var list = await _service.GetPoiCapabilitiesForDepartmentAsync(DeptId);

			list.Select(x => x.Name).Should().Equal("Cath Lab", "Stroke Center", "Trauma Center");
		}

		[Test]
		public async Task a_new_capability_is_trimmed_and_sorted_last()
		{
			var saved = await _service.SavePoiCapabilityAsync(new PoiCapability { DepartmentId = DeptId, Name = "  Burn Center  " });

			saved.Name.Should().Be("Burn Center");
			saved.Sort.Should().Be(4);
		}

		[TestCase("cath lab")]
		[TestCase("   ")]
		public void a_duplicate_or_blank_name_is_refused(string name)
		{
			Func<Task> act = () => _service.SavePoiCapabilityAsync(new PoiCapability { DepartmentId = DeptId, Name = name });

			act.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task renaming_a_capability_to_its_own_name_in_another_case_is_allowed()
		{
			var saved = await _service.SavePoiCapabilityAsync(new PoiCapability { PoiCapabilityId = "cath", DepartmentId = DeptId, Name = "CATH LAB", Sort = 1 });

			saved.Name.Should().Be("CATH LAB");
		}

		[Test]
		public void another_departments_capability_cannot_be_saved()
		{
			Func<Task> act = () => _service.SavePoiCapabilityAsync(new PoiCapability { PoiCapabilityId = "theirs", DepartmentId = DeptId, Name = "Helipad" });

			act.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task import_names_reuse_existing_capabilities_and_add_the_missing_ones()
		{
			var result = await _service.GetOrAddPoiCapabilitiesByNameAsync(DeptId, new[] { "cath lab", "Neurosurgery", " ", "NEUROSURGERY" });

			result.Select(x => x.Name).Should().Equal("Cath Lab", "Neurosurgery");
			_capabilities.Verify(x => x.SaveOrUpdateAsync(It.Is<PoiCapability>(c => c.Name == "Neurosurgery" && c.Sort == 4), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task a_poi_only_gets_the_departments_own_capabilities()
		{
			await _service.SetPoiCapabilitiesAsync(DeptId, Gent, new[] { "CATH", "cath", "theirs", null, "stroke" });

			_capabilities.Verify(x => x.ReplacePoiAssignmentsAsync(DeptId, Gent, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "cath", "stroke" })), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task details_fill_capabilities_in_department_order_and_only_current_availability()
		{
			_storedAvailabilities.Add(new PoiAvailability { PoiAvailabilityId = "a", DepartmentId = DeptId, PoiId = Gent, Status = (int)PoiAvailabilityStatuses.Diversion, SetOn = DateTime.UtcNow });
			_storedAvailabilities.Add(new PoiAvailability { PoiAvailabilityId = "b", DepartmentId = DeptId, PoiId = Zottegem, Status = (int)PoiAvailabilityStatuses.Closed, SetOn = DateTime.UtcNow.AddHours(-3), ExpiresOn = DateTime.UtcNow.AddHours(-1) });
			var pois = await _service.GetDestinationPOIsForDepartmentAsync(DeptId);

			await _service.PopulatePoiDetailsAsync(DeptId, pois);

			var gent = pois.Single(x => x.PoiId == Gent);
			gent.CapabilityIds.Should().Equal("cath", "trauma");
			gent.Capabilities.Should().Equal("Cath Lab", "Trauma Center");
			gent.Availability.Status.Should().Be((int)PoiAvailabilityStatuses.Diversion);

			var zottegem = pois.Single(x => x.PoiId == Zottegem);
			zottegem.Capabilities.Should().Equal("Stroke Center");
			zottegem.Availability.Should().BeNull("an expired report reads as open");
		}

		[Test]
		public async Task a_department_without_capabilities_never_reads_assignments()
		{
			_storedCapabilities.Clear();
			var pois = await _service.GetDestinationPOIsForDepartmentAsync(DeptId);

			await _service.PopulatePoiDetailsAsync(DeptId, pois);

			pois.Should().OnlyContain(x => x.CapabilityIds.Count == 0 && x.Capabilities.Count == 0);
			_capabilities.Verify(x => x.GetAssignmentsByDepartmentIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task setting_a_diversion_keeps_only_known_capabilities_and_caps_the_expiry()
		{
			var report = await _service.SetPoiAvailabilityAsync(DeptId, Gent, PoiAvailabilityStatuses.Limited, new[] { "CATH", "theirs" }, "  Cath lab team out  ",
				DateTime.UtcNow.AddDays(30), "dispatcher");

			report.Status.Should().Be((int)PoiAvailabilityStatuses.Limited);
			report.GetUnavailableCapabilityIds().Should().Equal("cath");
			report.Note.Should().Be("Cath lab team out");
			report.SetByUserId.Should().Be("dispatcher");
			report.ExpiresOn.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
		}

		[Test]
		public async Task reopening_a_destination_deletes_its_report()
		{
			_storedAvailabilities.Add(new PoiAvailability { PoiAvailabilityId = "a", DepartmentId = DeptId, PoiId = Gent, Status = (int)PoiAvailabilityStatuses.Closed, SetOn = DateTime.UtcNow });

			var report = await _service.SetPoiAvailabilityAsync(DeptId, Gent, PoiAvailabilityStatuses.Open, null, null, null, "dispatcher");

			report.Should().BeNull();
			_availabilities.Verify(x => x.DeleteForPoisAsync(DeptId, It.Is<IEnumerable<int>>(ids => ids.Single() == Gent), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void availability_is_only_for_the_departments_destinations()
		{
			Func<Task> act = () => _service.SetPoiAvailabilityAsync(DeptId, Shelter, PoiAvailabilityStatuses.Closed, null, null, null, "dispatcher");

			act.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task deleting_a_capability_takes_it_off_pois_and_reports()
		{
			(await _service.DeletePoiCapabilityAsync(DeptId, "CATH")).Should().BeFalse("the mock delete returns false, but the cleanup still runs");

			_capabilities.Verify(x => x.DeleteAssignmentsForCapabilityAsync(DeptId, "cath", It.IsAny<CancellationToken>()), Times.Once);
			_availabilities.Verify(x => x.RemoveCapabilityAsync(DeptId, "cath", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task deleting_a_poi_removes_its_capabilities_and_report()
		{
			_pois.Setup(x => x.GetByIdAsync(Gent)).ReturnsAsync(new Poi { PoiId = Gent, PoiTypeId = HospitalType });
			_poiTypes.Setup(x => x.GetPoiTypeByTypeIdAsync(HospitalType)).ReturnsAsync(new PoiType { PoiTypeId = HospitalType, DepartmentId = DeptId });
			_pois.Setup(x => x.DeleteAsync(It.IsAny<Poi>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await _service.DeletePOIAsync(Gent)).Should().BeTrue();

			_capabilities.Verify(x => x.DeleteAssignmentsForPoisAsync(DeptId, It.Is<IEnumerable<int>>(ids => ids.Single() == Gent), It.IsAny<CancellationToken>()), Times.Once);
			_availabilities.Verify(x => x.DeleteForPoisAsync(DeptId, It.Is<IEnumerable<int>>(ids => ids.Single() == Gent), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void rules_read_what_a_destination_can_still_take()
		{
			var now = DateTime.UtcNow;
			var limited = new PoiAvailability { PoiId = Gent, Status = (int)PoiAvailabilityStatuses.Limited, UnavailableCapabilityIds = "cath, ,CATH,trauma" };
			var closed = new PoiAvailability { PoiId = Gent, Status = (int)PoiAvailabilityStatuses.Closed };
			var diversion = new PoiAvailability { PoiId = Gent, Status = (int)PoiAvailabilityStatuses.Diversion };

			limited.GetUnavailableCapabilityIds().Should().Equal("cath", "trauma");
			PoiAvailabilityRules.CanProvide(limited, "Cath", now).Should().BeFalse();
			PoiAvailabilityRules.CanProvide(limited, "stroke", now).Should().BeTrue();
			PoiAvailabilityRules.CanProvide(limited, null, now).Should().BeTrue();
			PoiAvailabilityRules.CanProvide(closed, null, now).Should().BeFalse();
			PoiAvailabilityRules.CanProvide(diversion, "cath", now).Should().BeTrue();
			PoiAvailabilityRules.CanProvide(null, "cath", now).Should().BeTrue();
			PoiAvailabilityRules.StatusOf(new PoiAvailability { Status = 99 }, now).Should().Be(PoiAvailabilityStatuses.Open);
			PoiAvailabilityRules.StatusOf(new PoiAvailability { Status = 3, ExpiresOn = now.AddMinutes(-1) }, now).Should().Be(PoiAvailabilityStatuses.Open);
		}
	}

	/// <summary>"We request transport to the nearest cath lab": destinations ranked from a point.</summary>
	[TestFixture]
	public class NearestDestinationServiceTests
	{
		private const int DeptId = 7;

		private Mock<IMappingService> _mapping;
		private Mock<IGeoService> _geo;
		private Mock<IUnitsService> _units;
		private NearestDestinationService _service;
		private List<Poi> _pois;

		// Origin near Zottegem; distances grow with the latitude offset.
		private const double OriginLat = 50.80;
		private const double OriginLon = 3.80;

		[SetUp]
		public void SetUp()
		{
			_mapping = new Mock<IMappingService>();
			_geo = new Mock<IGeoService>();
			_units = new Mock<IUnitsService>();
			var type = new PoiType { PoiTypeId = 3, DepartmentId = DeptId, Name = "Hospital", IsDestination = true };

			_pois = new List<Poi>
			{
				Hospital(1, "Near, stroke only", 0.01, type, "stroke"),
				Hospital(2, "Second, cath", 0.02, type, "cath"),
				Hospital(3, "Third, cath on diversion", 0.03, type, "cath"),
				Hospital(4, "Fourth, cath closed", 0.04, type, "cath"),
				Hospital(5, "Far, cath", 0.50, type, "cath"),
				Hospital(6, "No position", 0, type, "cath")
			};
			_pois[5].Latitude = 0;
			_pois[5].Longitude = 0;

			_mapping.Setup(x => x.GetDestinationPOIsForDepartmentAsync(DeptId)).ReturnsAsync(() => _pois);
			_mapping.Setup(x => x.PopulatePoiDetailsAsync(DeptId, It.IsAny<IEnumerable<Poi>>())).Returns(Task.CompletedTask);
			// Routed seconds = 1000 * latitude offset, except hospital 2, which is slow by road.
			_geo.Setup(x => x.GetTravelEstimateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TravelLookup>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string _, string destination, TravelLookup __, CancellationToken ___) =>
				{
					var lat = double.Parse(destination.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);
					var seconds = Math.Round((lat - OriginLat) * 100000);
					return new TravelEstimate { Seconds = Math.Abs(lat - (OriginLat + 0.02)) < 0.0001 ? 9000 : seconds, Source = EtaSources.Road };
				});

			_service = new NearestDestinationService(_mapping.Object, _geo.Object, _units.Object);
		}

		private static Poi Hospital(int id, string name, double latitudeOffset, PoiType type, params string[] capabilities) => new Poi
		{
			PoiId = id,
			PoiTypeId = type.PoiTypeId,
			Type = type,
			Name = name,
			Latitude = OriginLat + latitudeOffset,
			Longitude = OriginLon,
			CapabilityIds = capabilities.ToList(),
			Capabilities = capabilities.ToList()
		};

		[Test]
		public async Task the_nearest_cath_labs_are_ranked_by_road_with_diversion_and_closed_after_them()
		{
			_pois[2].Availability = new PoiAvailability { PoiId = 3, Status = (int)PoiAvailabilityStatuses.Diversion, SetOn = DateTime.UtcNow };
			_pois[3].Availability = new PoiAvailability { PoiId = 4, Status = (int)PoiAvailabilityStatuses.Closed, SetOn = DateTime.UtcNow };

			var ranked = await _service.GetNearestDestinationsAsync(DeptId, OriginLat, OriginLon, "CATH", null, 10);

			// Open first by road time (2 at 9000 s before 5 at 50000 s), then diversion (3), then closed (4).
			ranked.Select(x => x.Poi.PoiId).Should().Equal(2, 5, 3, 4);
			ranked[0].TravelSeconds.Should().Be(9000);
			ranked.Single(x => x.Poi.PoiId == 3).Status.Should().Be(PoiAvailabilityStatuses.Diversion);
			ranked.Single(x => x.Poi.PoiId == 4).CanProvide.Should().BeFalse();
		}

		[Test]
		public async Task a_capability_reported_down_ranks_with_the_closed_ones()
		{
			_pois[1].Availability = new PoiAvailability { PoiId = 2, Status = (int)PoiAvailabilityStatuses.Limited, UnavailableCapabilityIds = "cath", SetOn = DateTime.UtcNow };

			var ranked = await _service.GetNearestDestinationsAsync(DeptId, OriginLat, OriginLon, "cath", null, 10);

			ranked.Last().Poi.PoiId.Should().Be(2);
			ranked.Last().CanProvide.Should().BeFalse();
		}

		[Test]
		public async Task without_a_capability_every_destination_with_a_position_is_ranked()
		{
			var ranked = await _service.GetNearestDestinationsAsync(DeptId, OriginLat, OriginLon, null, null, 10);

			ranked.Select(x => x.Poi.PoiId).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
			ranked.First().Poi.PoiId.Should().Be(1);
		}

		[Test]
		public async Task only_the_shortlist_is_routed_and_the_rest_rank_by_distance()
		{
			for (var i = 0; i < 10; i++)
				_pois.Add(Hospital(100 + i, "Extra " + i, 1 + i * 0.1, _pois[0].Type, "cath"));

			var ranked = await _service.GetNearestDestinationsAsync(DeptId, OriginLat, OriginLon, "cath", null, 50);

			_geo.Verify(x => x.GetTravelEstimateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TravelLookup>(), It.IsAny<CancellationToken>()),
				Times.Exactly(INearestDestinationService.DefaultShortlistSize));
			ranked.Where(x => x.TravelSeconds < 0).Select(x => x.DistanceMeters).Should().BeInAscendingOrder();
		}

		[Test]
		public async Task a_failed_route_falls_back_to_distance()
		{
			_geo.Setup(x => x.GetTravelEstimateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TravelLookup>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("router down"));

			var ranked = await _service.GetNearestDestinationsAsync(DeptId, OriginLat, OriginLon, "cath", null, 3);

			ranked.Select(x => x.Poi.PoiId).Should().Equal(2, 3, 4);
			ranked.Should().OnlyContain(x => x.TravelSeconds < 0);
		}

		[TestCase(0, 0)]
		[TestCase(91, 3)]
		public async Task an_unusable_origin_returns_nothing(double latitude, double longitude)
		{
			(await _service.GetNearestDestinationsAsync(DeptId, latitude, longitude, null, null, 10)).Should().BeEmpty();
		}

		[Test]
		public async Task the_units_own_travel_is_used_for_routing()
		{
			var unit = new Unit { UnitId = 12, DepartmentId = DeptId };
			var travel = new UnitTravelProfile(UnitTravelModes.Walking, 5);
			_units.Setup(x => x.GetUnitByIdAsync(12)).ReturnsAsync(unit);
			_units.Setup(x => x.GetTravelProfilesForUnitsAsync(DeptId, It.IsAny<IEnumerable<Unit>>())).ReturnsAsync(new Dictionary<int, UnitTravelProfile> { { 12, travel } });

			await _service.GetNearestDestinationsAsync(DeptId, OriginLat, OriginLon, "cath", 12, 3);

			_geo.Verify(x => x.GetTravelEstimateAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<TravelLookup>(l => l.Travel == travel && l.DepartmentId == DeptId), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
			_units.Verify(x => x.GetVehicleProfilesForUnitsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<Unit>>()), Times.Never, "only driving units carry vehicle limits");
		}
	}
}
