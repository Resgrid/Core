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
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>Street address text matches calls and occupancies the way occupancies match addresses, beside the word-by-word index.</summary>
	public partial class UnifiedSearchServiceTests
	{
		private Mock<ICallLocationKeysRepository> _callKeys;
		private Mock<IRmsOccupanciesRepository> _occupancyRows;

		private UnifiedSearchService ServiceWithAddressMatching(bool occupancies = false)
		{
			_callKeys = new Mock<ICallLocationKeysRepository>();
			_callKeys.Setup(k => k.GetByAddressKeysAsync(7, It.IsAny<IEnumerable<string>>(), It.IsAny<int>())).ReturnsAsync(new List<CallLocationCandidate>());
			_projections.Setup(p => p.GetByEntityIdsAsync(7, It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int _, string type, IEnumerable<string> ids) => ids.Select(id => Projection(type, id)).ToList());

			Lazy<RecordsPreventionGate> gate = null;
			if (occupancies)
			{
				_occupancyRows = new Mock<IRmsOccupanciesRepository>();
				_occupancyRows.Setup(o => o.GetByIdForDepartmentAsync(7, It.IsAny<string>()))
					.ReturnsAsync((int _, string id) => new RmsOccupancy { RmsOccupancyId = id, DepartmentId = 7, Status = (int)RmsOccupancyStatus.Active });
				_cutover.Setup(c => c.GetModuleStateAsync(7, It.IsAny<bool>()))
					.ReturnsAsync(new RecordsModuleState { DepartmentId = 7, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active });
				_flags.Setup(f => f.IsEnabledAsync(RecordsPreventionModules.FlagKey(RecordsPreventionModule.Occupancy), 7, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
				_recordsAuth.Setup(a => a.IsActiveMemberAsync("u1", 7)).ReturnsAsync(true);
				gate = new Lazy<RecordsPreventionGate>(() => new RecordsPreventionGate(_cutover.Object, _flags.Object, _recordsAuth.Object,
					Mock.Of<IRmsPreventionSequencesRepository>(), Mock.Of<IRmsAccessAuditsRepository>(), Mock.Of<IDocumentNumberingService>()));
			}

			var permissions = new Resgrid.Services.PermissionsService(_permissions.Object, Mock.Of<IUsersService>(), Mock.Of<IDepartmentGroupsService>());
			return new UnifiedSearchService(_global.Object, _actions.Object, _flags.Object, _auth.Object, _states.Object, _recordsSearch.Object,
				_recordsAuth.Object, _records.Object, _cutover.Object, _departments.Object, permissions, _groups.Object,
				_roles.Object, _calls.Object, _units.Object, _messages.Object, _documents.Object, _notes.Object,
				_contacts.Object, _protection.Object, _settings.Object, _projections.Object,
				preventionGate: gate, occupancies: _occupancyRows?.Object, callLocationKeys: _callKeys.Object);
		}

		/// <summary>The call location index rows for "110|MAIN": each call's address as it was logged.</summary>
		private void CallsAt(params (int CallId, string Address)[] calls)
		{
			_callKeys.Setup(k => k.GetByAddressKeysAsync(7, It.Is<IEnumerable<string>>(keys => keys.Contains("110|MAIN")), It.IsAny<int>()))
				.ReturnsAsync(calls.Select(c => new CallLocationCandidate
				{
					CallId = c.CallId, LoggedOn = DateTime.UtcNow, AddressKey = "110|MAIN", Indexed = true,
					AddressCanonical = StreetAddressParser.Parse(c.Address).ToCanonical()
				}).ToList());
		}

		[TestCase("110 Main Street")]
		[TestCase("110 Main St")]
		[TestCase("110 main st.")]
		public async Task A_street_address_finds_the_calls_logged_under_other_spellings(string text)
		{
			var service = ServiceWithAddressMatching();
			Answer();
			CallsAt((10, "110 Main St"), (11, "110 Main"), (12, "110 Main Street, Springfield"), (13, "110 Main Ave"));

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = text }, Principal("Call:View"));

			result.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "10", "11", "12" }, "Main Ave is a different street");
			result.Hits.Should().OnlyContain(h => h.EntityType == SearchEntityTypes.Call);
			result.Total.Should().Be(3);
		}

		[Test]
		public async Task A_street_address_without_a_suffix_finds_the_street_under_any_suffix()
		{
			var service = ServiceWithAddressMatching();
			Answer();
			CallsAt((10, "110 Main St"), (11, "110 Main Street"), (12, "110 Main"), (13, "112 Main St"));

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main" }, Principal("Call:View"));

			result.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "10", "11", "12" }, "a different house number is a different place");
		}

		[Test]
		public async Task An_address_hit_the_index_also_found_leads_and_is_shown_once()
		{
			var service = ServiceWithAddressMatching();
			Answer(Hit(SearchEntityTypes.Call, "1"), Hit(SearchEntityTypes.Call, "2"));
			CallsAt((2, "110 Main St"));

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main Street" }, Principal("Call:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal("2", "1");
			result.Total.Should().Be(2);
		}

		[Test]
		public async Task Same_address_ranks_ahead_of_similar()
		{
			var service = ServiceWithAddressMatching();
			Answer();
			CallsAt((20, "110 S Main St"), (21, "110 Main St"));

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main Street" }, Principal("Call:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal(new[] { "21", "20" }, "a directional on one side only is a similar address");
		}

		[Test]
		public async Task An_address_call_the_caller_cannot_view_is_dropped_and_the_total_withheld()
		{
			var service = ServiceWithAddressMatching();
			Answer();
			CallsAt((10, "110 Main St"), (11, "110 Main"));
			_auth.Setup(a => a.CanUserViewCallAsync("u1", 11)).ReturnsAsync(false);

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main Street" }, Principal("Call:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal("10");
			result.Total.Should().BeNull();
		}

		[Test]
		public async Task An_address_call_outside_the_date_range_is_left_out()
		{
			var service = ServiceWithAddressMatching();
			Answer();
			CallsAt((10, "110 Main St"));

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main Street", FromUtc = DateTime.UtcNow.AddDays(1) }, Principal("Call:View"));

			result.Hits.Should().BeEmpty();
			result.Total.Should().Be(0);
		}

		[Test]
		public async Task Address_matching_needs_the_call_family_and_text_in_the_clear()
		{
			var service = ServiceWithAddressMatching();
			Answer();
			CallsAt((10, "110 Main St"));

			(await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main Street" }, Principal())).Hits.Should().BeEmpty();

			_protection.Setup(p => p.GetPolicyByDepartmentIdAsync(7, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = 7, State = (int)DepartmentDataProtectionState.Encrypting });
			(await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main Street" }, Principal("Call:View"))).Hits.Should().BeEmpty();

			_callKeys.Verify(k => k.GetByAddressKeysAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Text_that_is_not_a_street_address_or_a_typeahead_prefix_skips_the_address_index()
		{
			var service = ServiceWithAddressMatching();
			Answer();

			await service.SearchAsync(new UnifiedSearchRequest { Text = "structure fire" }, Principal("Call:View"));
			await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main Street", Prefix = true }, Principal("Call:View"));

			_callKeys.Verify(k => k.GetByAddressKeysAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task The_palette_stops_once_its_page_is_filled()
		{
			var service = ServiceWithAddressMatching();
			Answer();
			CallsAt((10, "110 Main St"), (11, "110 Main"), (12, "110 Main Street"));

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "110 Main Street", Take = 2, CountTotal = false }, Principal("Call:View"));

			result.Hits.Should().HaveCount(2);
			result.Total.Should().BeNull();
			_auth.Verify(a => a.CanUserViewCallAsync("u1", It.IsAny<int>()), Times.Exactly(2));
		}

		[Test]
		public async Task A_street_address_finds_the_occupancy_under_another_spelling()
		{
			var service = ServiceWithAddressMatching(occupancies: true);
			Answer();
			_occupancyRows.Setup(o => o.QueryAsync(7, It.Is<RmsOccupancyQuery>(q => q.Search == "110")))
				.ReturnsAsync(new List<RmsOccupancy>
				{
					new RmsOccupancy { RmsOccupancyId = "o1", DepartmentId = 7, AddressText = "110 S Main St", City = "Springfield" },
					new RmsOccupancy { RmsOccupancyId = "o2", DepartmentId = 7, AddressText = "1100 Main St" }
				});

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "110 South Main Street" }, Principal("Record:View"));

			result.Hits.Select(h => (h.EntityType, h.EntityId)).Should().Equal((SearchEntityTypes.Occupancy, "o1"));
			_callKeys.Verify(k => k.GetByAddressKeysAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<int>()), Times.Never,
				"calls need the call family");
		}
	}
}
