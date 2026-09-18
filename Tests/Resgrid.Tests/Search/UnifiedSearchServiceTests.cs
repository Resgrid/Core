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
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>The unified orchestrator: flag gate, claim-based family filter, per-hit authorization with total suppression, lazy state-row activation.</summary>
	[TestFixture]
	public class UnifiedSearchServiceTests
	{
		private Mock<IGlobalSearchService> _global;
		private Mock<ISystemActionsService> _actions;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IAuthorizationService> _auth;
		private Mock<ISearchIndexStatesRepository> _states;
		private Mock<IRecordsSearchService> _recordsSearch;
		private UnifiedSearchService _service;
		private bool _flagOn;
		private GlobalSearchQuery _lastQuery;

		[SetUp]
		public void SetUp()
		{
			_flagOn = true;
			_global = new Mock<IGlobalSearchService>();
			_global.SetupGet(g => g.IsAvailable).Returns(true);
			_global.Setup(g => g.SearchAsync(It.IsAny<int>(), It.IsAny<GlobalSearchQuery>(), It.IsAny<CancellationToken>()))
				.Callback((int d, GlobalSearchQuery q, CancellationToken _) => _lastQuery = q)
				.ReturnsAsync(() => new GlobalSearchResult
				{
					Total = 2,
					Hits = new List<GlobalSearchHit>
					{
						new GlobalSearchHit { EntityType = SearchEntityTypes.Call, EntityId = "1", Title = "One", Score = 2f },
						new GlobalSearchHit { EntityType = SearchEntityTypes.Call, EntityId = "2", Title = "Two", Score = 1f }
					}
				});
			_actions = new Mock<ISystemActionsService>();
			_actions.Setup(a => a.SearchAsync(It.IsAny<string>(), It.IsAny<SearchPrincipal>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SystemActionHit> { new SystemActionHit { Key = "calls" } });
			_actions.Setup(a => a.ListAsync(It.IsAny<SearchPrincipal>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SystemActionHit>());
			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.SearchUnified, 7, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(() => _flagOn);
			_auth = new Mock<IAuthorizationService>();
			_auth.Setup(a => a.CanUserViewCallAsync("u1", It.IsAny<int>())).ReturnsAsync(true);
			_states = new Mock<ISearchIndexStatesRepository>();
			_recordsSearch = new Mock<IRecordsSearchService>();
			_recordsSearch.SetupGet(r => r.IsAvailable).Returns(false);

			_service = new UnifiedSearchService(_global.Object, _actions.Object, _flags.Object, _auth.Object, _states.Object, _recordsSearch.Object,
				new Mock<IRecordsAuthorizationService>().Object, new Mock<IRecordsService>().Object, new Mock<IRecordsCutoverService>().Object);
		}

		private static SearchPrincipal Principal(params string[] claims) => new SearchPrincipal
		{
			UserId = "u1",
			DepartmentId = 7,
			HasClaim = (r, a) => claims.Contains(r + ":" + a)
		};

		[Test]
		public async Task Flag_off_is_unavailable_and_returns_nothing()
		{
			_flagOn = false;
			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"));
			result.Available.Should().BeFalse();
			result.Hits.Should().BeEmpty();
			result.Actions.Should().BeEmpty();
		}

		[Test]
		public async Task All_hits_authorized_keeps_the_total()
		{
			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"));
			result.Available.Should().BeTrue();
			result.Hits.Should().HaveCount(2);
			result.Total.Should().Be(2);
			result.Actions.Should().ContainSingle(a => a.Key == "calls");
		}

		[Test]
		public async Task A_dropped_hit_suppresses_the_total()
		{
			_auth.Setup(a => a.CanUserViewCallAsync("u1", 2)).ReturnsAsync(false);
			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"));
			result.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "1" });
			result.Total.Should().BeNull();
		}

		[Test]
		public async Task Claims_decide_which_families_reach_the_index()
		{
			await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View", "Notes:View"));
			_lastQuery.EntityTypes.Should().BeEquivalentTo(new[] { SearchEntityTypes.Call, SearchEntityTypes.Note });
			_lastQuery.ViewerUserId.Should().Be("u1");
			_lastQuery.IncludeAdminOnly.Should().BeFalse();

			_lastQuery = null;
			var none = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal());
			_lastQuery.Should().BeNull("no family is searchable without a view claim");
			none.Hits.Should().BeEmpty();
		}

		[Test]
		public async Task Index_unavailable_degrades_and_activates_the_department_lazily()
		{
			_global.SetupGet(g => g.IsAvailable).Returns(false);
			_states.Setup(s => s.GetAsync(SearchIndexNames.Global, 7)).ReturnsAsync((SearchIndexState)null);
			SearchIndexState saved = null;
			_states.Setup(s => s.SaveOrUpdateAsync(It.IsAny<SearchIndexState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((SearchIndexState s, CancellationToken _, bool __) => saved = s).ReturnsAsync((SearchIndexState s, CancellationToken _, bool __) => s);

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"));

			result.Available.Should().BeTrue();
			result.Degraded.Should().BeTrue();
			result.Actions.Should().NotBeEmpty("system functionality still resolves while the index is building");
			saved.Should().NotBeNull();
			saved.State.Should().Be((int)SearchIndexBuildState.RebuildRequested);
			saved.IndexName.Should().Be(SearchIndexNames.Global);
		}
	}
}
