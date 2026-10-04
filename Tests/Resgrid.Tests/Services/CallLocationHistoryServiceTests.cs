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
	[TestFixture]
	public class CallLocationHistoryServiceTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "user-1";

		private Mock<ICallLocationKeysRepository> _keys;
		private Mock<ICallContactsRepository> _callContacts;
		private Mock<IDepartmentDataProtectionService> _dataProtection;
		private Mock<IDispatchScopeService> _dispatchScope;
		private Mock<IDepartmentsService> _departments;
		private Mock<IOccupancyLocationLookup> _occupancies;
		private CallLocationHistoryService _service;
		private DepartmentDataProtectionPolicy _policy;
		private Dictionary<int, Call> _calls;
		private List<CallLocationKey> _upserted;
		private Dictionary<string, List<CallLocationCandidate>> _addressCandidates;

		[SetUp]
		public void SetUp()
		{
			_keys = new Mock<ICallLocationKeysRepository>();
			_callContacts = new Mock<ICallContactsRepository>();
			_dataProtection = new Mock<IDepartmentDataProtectionService>();
			_dispatchScope = new Mock<IDispatchScopeService>();
			_departments = new Mock<IDepartmentsService>();
			_occupancies = new Mock<IOccupancyLocationLookup>();
			_policy = null;
			_calls = new Dictionary<int, Call>();
			_upserted = new List<CallLocationKey>();
			_addressCandidates = new Dictionary<string, List<CallLocationCandidate>>();

			_dataProtection.Setup(x => x.GetPolicyByDepartmentIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _policy);
			_departments.Setup(x => x.IsMemberOfDepartmentAsync(DepartmentId, UserId)).ReturnsAsync(true);
			_dispatchScope.Setup(x => x.CanUserAccessCallAsync(DepartmentId, UserId, It.IsAny<Call>())).ReturnsAsync(true);
			_dispatchScope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, It.IsAny<List<Call>>())).ReturnsAsync((int _, string __, List<Call> calls) => calls);
			_dispatchScope.Setup(x => x.GetScopeForUserAsync(DepartmentId, UserId)).ReturnsAsync(new DispatchScope { DepartmentId = DepartmentId, UserId = UserId, IsDepartmentWide = true });
			_callContacts.Setup(x => x.GetCallContactsByCallIdAsync(It.IsAny<int>())).ReturnsAsync(new List<CallContact>());
			_keys.Setup(x => x.GetCallsAsync(DepartmentId, It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((int _, IEnumerable<int> ids) => ids.Where(_calls.ContainsKey).Select(id => _calls[id]).ToList());
			_keys.Setup(x => x.GetByAddressKeysAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
				.ReturnsAsync((int _, IEnumerable<string> keys, int take) => keys.Distinct().SelectMany(k => _addressCandidates.TryGetValue(k, out var rows) ? rows.Take(take) : Enumerable.Empty<CallLocationCandidate>()).ToList());
			_keys.Setup(x => x.GetWithinBoundsAsync(DepartmentId, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>())).ReturnsAsync(new List<CallLocationCandidate>());
			_keys.Setup(x => x.GetContactCallCandidatesAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>())).ReturnsAsync(new List<CallLocationCandidate>());
			_keys.Setup(x => x.GetNotesForCallsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<CallNote>());
			_keys.Setup(x => x.UpsertAsync(It.IsAny<IEnumerable<CallLocationKey>>(), It.IsAny<CancellationToken>()))
				.Callback((IEnumerable<CallLocationKey> keys, CancellationToken _) => _upserted.AddRange(keys)).Returns(Task.CompletedTask);

			_service = new CallLocationHistoryService(_keys.Object, _callContacts.Object, _dataProtection.Object, _dispatchScope.Object, _departments.Object,
				new Lazy<IOccupancyLocationLookup>(() => _occupancies.Object));
		}

		private Call AddCall(int id, string address, string geo = null, int daysAgo = 1)
		{
			var call = new Call { CallId = id, DepartmentId = DepartmentId, Number = "C-" + id, Name = "Call " + id, Address = address, GeoLocationData = geo, LoggedOn = DateTime.UtcNow.AddDays(-daysAgo) };
			_calls[id] = call;
			return call;
		}

		private static CallLocationCandidate Candidate(Call call)
		{
			var key = CallLocationHistoryService.BuildKey(call.CallId, call.DepartmentId, call.Address, call.GeoLocationData, call.LoggedOn, DateTime.UtcNow);
			return new CallLocationCandidate { CallId = call.CallId, LoggedOn = call.LoggedOn, AddressKey = key?.AddressKey, AddressCanonical = key?.AddressCanonical, Latitude = key?.Latitude, Longitude = key?.Longitude, Indexed = key != null };
		}

		private void IndexByAddress(string addressKey, params Call[] calls)
			=> _addressCandidates[addressKey] = calls.Select(Candidate).ToList();

		[Test]
		public async Task Saving_a_call_indexes_its_parsed_address_and_coordinates()
		{
			await _service.IndexCallAsync(new Call { CallId = 5, DepartmentId = DepartmentId, Address = "110 South Main Street, Springfield", GeoLocationData = "39.7817,-89.6501", LoggedOn = DateTime.UtcNow });

			var key = _upserted.Should().ContainSingle().Subject;
			key.AddressKey.Should().Be("110|MAIN");
			key.Latitude.Should().Be(39.7817m);
			key.KeyVersion.Should().Be(ParsedStreetAddress.Version);
			ParsedStreetAddress.FromCanonical(key.AddressCanonical).PreDirectional.Should().Be("S");
		}

		[Test]
		public async Task Nothing_is_indexed_while_data_protection_is_on()
		{
			_policy = new DepartmentDataProtectionPolicy { DepartmentId = DepartmentId, State = (int)DepartmentDataProtectionState.Enabled };

			await _service.IndexCallAsync(new Call { CallId = 5, DepartmentId = DepartmentId, Address = "110 S Main St", LoggedOn = DateTime.UtcNow });

			_keys.Verify(x => x.UpsertAsync(It.IsAny<IEnumerable<CallLocationKey>>(), It.IsAny<CancellationToken>()), Times.Never);
			_keys.Verify(x => x.DeleteForCallAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_call_without_a_usable_location_drops_its_key()
		{
			await _service.IndexCallAsync(new Call { CallId = 5, DepartmentId = DepartmentId, Address = "Walmart", GeoLocationData = ",", LoggedOn = DateTime.UtcNow });

			_keys.Verify(x => x.DeleteForCallAsync(5, It.IsAny<CancellationToken>()), Times.Once);
			_upserted.Should().BeEmpty();
		}

		[Test]
		public async Task Previous_calls_match_however_the_address_was_typed()
		{
			var current = AddCall(1, "110 S Main St, Springfield, IL 62701", daysAgo: 0);
			var typed = AddCall(2, "110 South Main", daysAgo: 3);
			var spelled = AddCall(3, "110 south main street", daysAgo: 9);
			var noDirection = AddCall(4, "110 Main St", daysAgo: 20);
			var otherSide = AddCall(5, "110 N Main St", daysAgo: 30);
			var otherZip = AddCall(6, "110 S Main St, 62702", daysAgo: 40);
			IndexByAddress("110|MAIN", current, typed, spelled, noDirection, otherSide, otherZip);

			var history = await _service.GetHistoryForCallAsync(DepartmentId, UserId, 1);

			history.AddressMatchingAvailable.Should().BeTrue();
			history.InterpretedAddress.Should().Be("110 S MAIN ST");
			history.Entries.Select(e => e.Call.CallId).Should().Equal(2, 3, 4);
			history.Entries.Single(e => e.Call.CallId == 2).Match.Should().Be(CallLocationMatch.SameAddress);
			history.Entries.Single(e => e.Call.CallId == 4).Match.Should().Be(CallLocationMatch.SimilarAddress, "the directional is missing on one side");
		}

		[Test]
		public async Task The_same_street_address_in_another_town_is_dropped_by_its_coordinates()
		{
			AddCall(1, "110 Main St", "39.7817,-89.6501", daysAgo: 0);
			var here = AddCall(2, "110 Main Street", "39.7818,-89.6502");
			var elsewhere = AddCall(3, "110 Main St", "39.8500,-89.6501");
			IndexByAddress("110|MAIN", here, elsewhere);

			var history = await _service.GetHistoryForCallAsync(DepartmentId, UserId, 1);

			history.Entries.Select(e => e.Call.CallId).Should().Equal(2);
		}

		[Test]
		public async Task Nearby_calls_count_only_when_one_side_has_no_street_address()
		{
			AddCall(1, "110 S Main St", "39.7817,-89.6501", daysAgo: 0);
			var pinned = AddCall(2, null, "39.7818,-89.6502");
			var named = AddCall(3, "Joe's Diner parking lot", "39.7816,-89.6500");
			var nextDoor = AddCall(4, "112 S Main St", "39.7818,-89.6501");
			_keys.Setup(x => x.GetWithinBoundsAsync(DepartmentId, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>()))
				.ReturnsAsync(new[] { pinned, named, nextDoor }.Select(Candidate).ToList());

			var history = await _service.GetHistoryForCallAsync(DepartmentId, UserId, 1);

			history.Entries.Select(e => e.Call.CallId).Should().BeEquivalentTo(new[] { 2, 3 });
			history.Entries.Should().OnlyContain(e => e.Match == CallLocationMatch.Nearby && e.DistanceMeters < CallLocationQuery.DefaultNearbyMeters);
		}

		[Test]
		public async Task Calls_with_the_same_contact_are_included_and_flagged()
		{
			AddCall(1, "110 S Main St", daysAgo: 0);
			var both = AddCall(2, "110 South Main");
			var contactOnly = AddCall(3, "500 Elm St", daysAgo: 5);
			IndexByAddress("110|MAIN", both);
			_callContacts.Setup(x => x.GetCallContactsByCallIdAsync(1)).ReturnsAsync(new List<CallContact> { new CallContact { CallId = 1, DepartmentId = DepartmentId, ContactId = "k1" } });
			_keys.Setup(x => x.GetContactCallCandidatesAsync(DepartmentId, It.Is<IEnumerable<string>>(ids => ids.Contains("k1")), It.IsAny<int>()))
				.ReturnsAsync(new[] { both, contactOnly }.Select(Candidate).ToList());

			var history = await _service.GetHistoryForCallAsync(DepartmentId, UserId, 1);

			history.Entries.Single(e => e.Call.CallId == 2).Match.Should().Be(CallLocationMatch.SameAddress | CallLocationMatch.SameContact);
			history.Entries.Single(e => e.Call.CallId == 3).Match.Should().Be(CallLocationMatch.SameContact);
		}

		[Test]
		public async Task An_occupancy_does_not_take_a_multi_site_contacts_calls_at_its_other_sites()
		{
			_occupancies.Setup(x => x.GetOccupancyLocationAsync(DepartmentId, "occ-1")).ReturnsAsync(new OccupancyLocationSummary
			{
				OccupancyId = "occ-1", AddressText = "110 S Main St", City = "Springfield", ContactIds = new List<string> { "acme" }
			});
			var here = AddCall(2, "110 South Main");
			var otherSite = AddCall(3, "900 Industrial Pkwy");
			var noLocation = AddCall(4, null);
			IndexByAddress("110|MAIN", here);
			_keys.Setup(x => x.GetContactCallCandidatesAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
				.ReturnsAsync(new[] { here, otherSite, noLocation }.Select(Candidate).ToList());

			var history = await _service.GetHistoryForOccupancyAsync(DepartmentId, UserId, "occ-1");

			history.Entries.Select(e => e.Call.CallId).Should().BeEquivalentTo(new[] { 2, 4 });
			history.Entries.Single(e => e.Call.CallId == 2).Match.Should().HaveFlag(CallLocationMatch.SameContact);
		}

		[Test]
		public async Task A_contact_sees_its_linked_calls_and_calls_at_its_occupancies()
		{
			_occupancies.Setup(x => x.GetOccupanciesForContactAsync(DepartmentId, "acme")).ReturnsAsync(new List<OccupancyLocationSummary>
			{
				new OccupancyLocationSummary { OccupancyId = "occ-1", AddressText = "110 S Main St" },
				new OccupancyLocationSummary { OccupancyId = "occ-2", AddressText = "900 Industrial Pkwy" }
			});
			var atFirst = AddCall(2, "110 South Main");
			var atSecond = AddCall(3, "900 Industrial Parkway");
			var linked = AddCall(4, "1 Somewhere Else Rd");
			IndexByAddress("110|MAIN", atFirst);
			IndexByAddress("900|INDUSTRIAL", atSecond);
			_keys.Setup(x => x.GetContactCallCandidatesAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>())).ReturnsAsync(new[] { linked }.Select(Candidate).ToList());

			var history = await _service.GetHistoryForContactAsync(DepartmentId, UserId, "acme");

			history.Entries.Select(e => e.Call.CallId).Should().BeEquivalentTo(new[] { 2, 3, 4 });
		}

		[Test]
		public async Task With_data_protection_on_only_contact_links_are_searched()
		{
			_policy = new DepartmentDataProtectionPolicy { DepartmentId = DepartmentId, State = (int)DepartmentDataProtectionState.Encrypting };
			AddCall(1, "rgenc1:abc", daysAgo: 0);
			var linked = AddCall(2, "rgenc1:def");
			_callContacts.Setup(x => x.GetCallContactsByCallIdAsync(1)).ReturnsAsync(new List<CallContact> { new CallContact { CallId = 1, DepartmentId = DepartmentId, ContactId = "k1" } });
			_keys.Setup(x => x.GetContactCallCandidatesAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
				.ReturnsAsync(new List<CallLocationCandidate> { new CallLocationCandidate { CallId = 2, LoggedOn = linked.LoggedOn } });

			var history = await _service.GetHistoryForCallAsync(DepartmentId, UserId, 1);

			history.AddressMatchingAvailable.Should().BeFalse();
			history.Entries.Should().ContainSingle(e => e.Call.CallId == 2 && e.Match == CallLocationMatch.SameContact);
			_keys.Verify(x => x.GetByAddressKeysAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<int>()), Times.Never);
			_keys.Verify(x => x.GetWithinBoundsAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Calls_outside_the_users_dispatch_scope_are_left_out()
		{
			AddCall(1, "110 S Main St", daysAgo: 0);
			var visible = AddCall(2, "110 South Main");
			var hidden = AddCall(3, "110 S Main Street");
			IndexByAddress("110|MAIN", visible, hidden);
			_dispatchScope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, It.IsAny<List<Call>>()))
				.ReturnsAsync((int _, string __, List<Call> calls) => calls.Where(c => c.CallId != 3).ToList());

			var history = await _service.GetHistoryForCallAsync(DepartmentId, UserId, 1);

			history.Entries.Select(e => e.Call.CallId).Should().Equal(2);
		}

		[Test]
		public async Task A_call_the_user_cannot_see_has_no_history_and_non_members_get_nothing()
		{
			AddCall(1, "110 S Main St");
			_dispatchScope.Setup(x => x.CanUserAccessCallAsync(DepartmentId, UserId, It.IsAny<Call>())).ReturnsAsync(false);
			(await _service.GetHistoryForCallAsync(DepartmentId, UserId, 1)).Entries.Should().BeEmpty();

			(await _service.GetHistoryForContactAsync(DepartmentId, "outsider", "acme")).Entries.Should().BeEmpty();
			_occupancies.Verify(x => x.GetOccupanciesForContactAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Counts_are_withheld_from_users_without_department_wide_scope()
		{
			_dispatchScope.Setup(x => x.GetScopeForUserAsync(DepartmentId, UserId)).ReturnsAsync(new DispatchScope { DepartmentId = DepartmentId, UserId = UserId, IsDepartmentWide = false });

			(await _service.GetCallCountsForContactsAsync(DepartmentId, UserId)).Should().BeNull();
			(await _service.GetCallCountsForOccupanciesAsync(DepartmentId, UserId, new[] { "occ-1" })).Should().BeNull();
			_keys.Verify(x => x.GetCallCountsByContactAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task The_sweep_backfills_a_department_and_marks_it_complete()
		{
			_keys.SetupSequence(x => x.GetDepartmentsNeedingIndexAsync(ParsedStreetAddress.Version, It.IsAny<int>()))
				.ReturnsAsync(new List<int> { DepartmentId })
				.ReturnsAsync(new List<int>());
			_keys.Setup(x => x.GetDepartmentsToSuppressAsync(It.IsAny<int>())).ReturnsAsync(new List<int>());
			_keys.Setup(x => x.GetDepartmentsToResumeAsync(It.IsAny<int>())).ReturnsAsync(new List<int>());
			_keys.Setup(x => x.GetSourcesAsync(DepartmentId, null, CallLocationHistoryService.BackfillBatchSize)).ReturnsAsync(new List<CallLocationSource>
			{
				new CallLocationSource { CallId = 9, DepartmentId = DepartmentId, Address = "110 S Main St", LoggedOn = DateTime.UtcNow },
				new CallLocationSource { CallId = 8, DepartmentId = DepartmentId, Address = "Walmart", LoggedOn = DateTime.UtcNow },
				new CallLocationSource { CallId = 7, DepartmentId = DepartmentId, GeoLocationData = "39.78,-89.65", LoggedOn = DateTime.UtcNow }
			});
			CallLocationIndexState saved = null;
			_keys.Setup(x => x.SaveStateAsync(It.IsAny<CallLocationIndexState>(), It.IsAny<CancellationToken>())).Callback((CallLocationIndexState s, CancellationToken _) => saved = s).Returns(Task.CompletedTask);

			var result = await _service.RunIndexSweepAsync(TimeSpan.FromSeconds(30));

			_upserted.Select(k => k.CallId).Should().BeEquivalentTo(new[] { 9, 7 }, "a place name with no coordinates has nothing to index");
			result.CallsIndexed.Should().Be(2);
			result.DepartmentsCompleted.Should().Be(1);
			saved.CompletedOn.Should().NotBeNull();
			saved.NextCallId.Should().Be(7);
			saved.KeyVersion.Should().Be(ParsedStreetAddress.Version);
		}

		[Test]
		public async Task The_sweep_purges_departments_that_turned_on_data_protection()
		{
			_keys.Setup(x => x.GetDepartmentsToSuppressAsync(It.IsAny<int>())).ReturnsAsync(new List<int> { DepartmentId });
			_keys.Setup(x => x.GetDepartmentsToResumeAsync(It.IsAny<int>())).ReturnsAsync(new List<int>());
			_keys.Setup(x => x.GetDepartmentsNeedingIndexAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<int>());
			CallLocationIndexState saved = null;
			_keys.Setup(x => x.SaveStateAsync(It.IsAny<CallLocationIndexState>(), It.IsAny<CancellationToken>())).Callback((CallLocationIndexState s, CancellationToken _) => saved = s).Returns(Task.CompletedTask);

			var result = await _service.RunIndexSweepAsync(TimeSpan.FromSeconds(30));

			_keys.Verify(x => x.DeleteForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
			saved.IsSuppressed.Should().BeTrue();
			result.DepartmentsSuppressed.Should().Be(1);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task A_call_write_that_races_with_suppression_is_removed(bool policyChanged)
		{
			_keys.Setup(x => x.UpsertAsync(It.IsAny<IEnumerable<CallLocationKey>>(), It.IsAny<CancellationToken>()))
				.Callback(() =>
				{
					if (policyChanged) _policy = new DepartmentDataProtectionPolicy { State = (int)DepartmentDataProtectionState.Enabled };
					else _keys.Setup(x => x.GetStateAsync(DepartmentId)).ReturnsAsync(new CallLocationIndexState { IsSuppressed = true });
				}).Returns(Task.CompletedTask);

			await _service.IndexCallAsync(AddCall(5, "110 S Main St"));

			_keys.Verify(x => x.DeleteForCallAsync(5, It.IsAny<CancellationToken>()), Times.Once);
			_dataProtection.Verify(x => x.GetPolicyByDepartmentIdAsync(DepartmentId, true), Times.Exactly(2));
		}

		[Test]
		public async Task A_resume_failure_does_not_block_other_resumes_or_backfill()
		{
			_keys.Setup(x => x.GetDepartmentsToSuppressAsync(It.IsAny<int>())).ReturnsAsync(new List<int>());
			_keys.Setup(x => x.GetDepartmentsToResumeAsync(It.IsAny<int>())).ReturnsAsync(new List<int> { 13, 14 });
			_keys.Setup(x => x.SaveStateAsync(It.Is<CallLocationIndexState>(s => s.DepartmentId == 13), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("resume failed"));
			_keys.SetupSequence(x => x.GetDepartmentsNeedingIndexAsync(ParsedStreetAddress.Version, It.IsAny<int>()))
				.ReturnsAsync(new List<int> { DepartmentId }).ReturnsAsync(new List<int>());
			_keys.Setup(x => x.GetSourcesAsync(DepartmentId, null, CallLocationHistoryService.BackfillBatchSize)).ReturnsAsync(new List<CallLocationSource>());

			var result = await _service.RunIndexSweepAsync(TimeSpan.FromSeconds(30));

			result.Errors.Should().Be(1);
			result.DepartmentsReset.Should().Be(1);
			result.DepartmentsCompleted.Should().Be(1);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Occupancy_counts_include_more_than_the_history_candidate_limit(bool contactOnly)
		{
			var occupancy = new OccupancyLocationSummary { OccupancyId = "occ-1", AddressText = contactOnly ? null : "110 S Main St", ContactIds = new List<string> { "acme" } };
			_occupancies.Setup(x => x.GetOccupancyLocationsAsync(DepartmentId, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync(new Dictionary<string, OccupancyLocationSummary> { ["occ-1"] = occupancy });
			var calls = Enumerable.Range(1, 1205).Select(id => AddCall(id, contactOnly ? null : "110 S Main St")).ToArray();
			IndexByAddress("110|MAIN", calls);
			_keys.Setup(x => x.GetContactCallCandidatesAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
				.ReturnsAsync((int _, IEnumerable<string> __, int take) => calls.Select(Candidate).Take(take).ToList());

			var counts = await _service.GetCallCountsForOccupanciesAsync(DepartmentId, UserId, new[] { "occ-1" });

			counts["occ-1"].Count.Should().Be(1205, "matching by both address and contact must not duplicate calls");
			counts["occ-1"].IsLowerBound.Should().BeFalse();
		}

		[TestCase("address", false)]
		[TestCase("address", true)]
		[TestCase("contact", false)]
		[TestCase("contact", true)]
		[TestCase("nearby", false)]
		[TestCase("nearby", true)]
		public async Task Occupancy_counts_bound_each_candidate_source_and_identify_lower_bounds(string source, bool exceedsCap)
		{
			var cap = CallLocationHistoryService.MaxOccupancyCountCandidates;
			var occupancy = new OccupancyLocationSummary { OccupancyId = "occ-1",
				AddressText = source == "address" ? "110 S Main St" : null,
				Latitude = source == "nearby" ? 39.78m : null, Longitude = source == "nearby" ? -89.65m : null,
				ContactIds = source == "contact" ? new List<string> { "acme" } : new List<string>() };
			_occupancies.Setup(x => x.GetOccupancyLocationsAsync(DepartmentId, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync(new Dictionary<string, OccupancyLocationSummary> { ["occ-1"] = occupancy });
			var rows = Enumerable.Range(1, cap + (exceedsCap ? 1 : 0))
				.Select(id => Candidate(AddCall(id, occupancy.AddressText, source == "nearby" ? "39.78,-89.65" : null))).ToList();
			var requested = 0;
			if (source == "address")
				_keys.Setup(x => x.GetByAddressKeysAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
					.ReturnsAsync((int _, IEnumerable<string> __, int take) => { requested = take; return rows.Take(take).ToList(); });
			else if (source == "contact")
				_keys.Setup(x => x.GetContactCallCandidatesAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
					.ReturnsAsync((int _, IEnumerable<string> __, int take) => { requested = take; return rows.Take(take).ToList(); });
			else
				_keys.Setup(x => x.GetWithinBoundsAsync(DepartmentId, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>()))
					.ReturnsAsync((int _, decimal __, decimal ___, decimal ____, decimal _____, int take) => { requested = take; return rows.Take(take).ToList(); });

			var counts = await _service.GetCallCountsForOccupanciesAsync(DepartmentId, UserId, new[] { "occ-1" });

			requested.Should().Be(cap + 1);
			counts["occ-1"].Count.Should().Be(cap);
			counts["occ-1"].IsLowerBound.Should().Be(exceedsCap);
		}

		[Test]
		public async Task A_full_candidate_window_is_a_lower_bound_even_when_directional_matching_rejects_rows()
		{
			_occupancies.Setup(x => x.GetOccupancyLocationsAsync(DepartmentId, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync(new Dictionary<string, OccupancyLocationSummary> {
					["occ-1"] = new OccupancyLocationSummary { OccupancyId = "occ-1", AddressText = "110 S Main St" } });
			IndexByAddress("110|MAIN", Enumerable.Range(1, CallLocationHistoryService.MaxOccupancyCountCandidates + 1)
				.Select(id => AddCall(id, "110 N Main St")).ToArray());

			var counts = await _service.GetCallCountsForOccupanciesAsync(DepartmentId, UserId, new[] { "occ-1" });

			counts["occ-1"].Count.Should().Be(0);
			counts["occ-1"].IsLowerBound.Should().BeTrue("later candidates could match the occupancy");
		}

		[Test]
		public async Task Contact_history_batches_addresses_and_compares_each_locations_directional()
		{
			_occupancies.Setup(x => x.GetOccupanciesForContactAsync(DepartmentId, "acme")).ReturnsAsync(new List<OccupancyLocationSummary>
			{
				new OccupancyLocationSummary { AddressText = "110 N Main St" },
				new OccupancyLocationSummary { AddressText = "110 S Main St" },
				new OccupancyLocationSummary { AddressText = "900 Industrial Pkwy" }
			});
			IndexByAddress("110|MAIN", AddCall(1, "110 N Main St"), AddCall(2, "110 S Main St"));
			IndexByAddress("900|INDUSTRIAL", AddCall(3, "900 Industrial Pkwy"));

			var result = await _service.GetHistoryForContactAsync(DepartmentId, UserId, "acme");

			result.Entries.Should().HaveCount(3).And.OnlyContain(e => e.Match == CallLocationMatch.SameAddress);
			_keys.Verify(x => x.GetByAddressKeysAsync(DepartmentId, It.Is<IEnumerable<string>>(keys => keys.Count() == 2), It.IsAny<int>()), Times.Once);
		}

		[Test]
		public async Task Enrollment_during_backfill_purges_the_batch_and_keeps_the_state_suppressed()
		{
			_keys.Setup(x => x.GetDepartmentsToSuppressAsync(It.IsAny<int>())).ReturnsAsync(new List<int>());
			_keys.Setup(x => x.GetDepartmentsToResumeAsync(It.IsAny<int>())).ReturnsAsync(new List<int>());
			_keys.SetupSequence(x => x.GetDepartmentsNeedingIndexAsync(ParsedStreetAddress.Version, It.IsAny<int>()))
				.ReturnsAsync(new List<int> { DepartmentId }).ReturnsAsync(new List<int>());
			_keys.Setup(x => x.GetSourcesAsync(DepartmentId, null, CallLocationHistoryService.BackfillBatchSize)).ReturnsAsync(new List<CallLocationSource>
			{
				new CallLocationSource { CallId = 5, DepartmentId = DepartmentId, Address = "110 Main St", LoggedOn = DateTime.UtcNow }
			});
			_keys.Setup(x => x.UpsertAsync(It.IsAny<IEnumerable<CallLocationKey>>(), It.IsAny<CancellationToken>()))
				.Callback(() => _policy = new DepartmentDataProtectionPolicy { State = (int)DepartmentDataProtectionState.Encrypting }).Returns(Task.CompletedTask);

			var result = await _service.RunIndexSweepAsync(TimeSpan.FromSeconds(30));

			result.DepartmentsCompleted.Should().Be(0);
			result.DepartmentsSuppressed.Should().Be(1);
			_keys.Verify(x => x.DeleteForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
			_keys.Verify(x => x.SaveStateAsync(It.Is<CallLocationIndexState>(s => s.IsSuppressed && s.CompletedOn == null), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
