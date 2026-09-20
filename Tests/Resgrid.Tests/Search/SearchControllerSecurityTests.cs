using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Search;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Search
{
	[TestFixture, NonParallelizable]
	public class SearchControllerSecurityTests
	{
		private IHttpContextAccessor _previous;
		private SearchController _controller;
		private Mock<IUnifiedSearchService> _search;
		private Mock<IGlobalSearchService> _global;
		private Mock<IRecordsSearchService> _records;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<ISearchIndexMaintenanceService> _maintenance;
		private Mock<ISearchIndexStatesRepository> _states;

		[SetUp]
		public void SetUp()
		{
			_previous = ClaimsAuthorizationHelper._httpContextAccessor;
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, "viewer"), new Claim(ClaimTypes.PrimaryGroupSid, "7"),
					new Claim("Department", "View"), new Claim("Department", "Update"), new Claim("Unit", "View")
				}, "Test"))
			};
			http.Request.QueryString = new QueryString("?departmentId=8&userId=foreign&isDepartmentAdmin=true");
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			_search = new Mock<IUnifiedSearchService>();
			_search.Setup(s => s.SearchAsync(It.IsAny<UnifiedSearchRequest>(), It.IsAny<SearchPrincipal>(), It.IsAny<CancellationToken>())).ReturnsAsync(new UnifiedSearchResult());
			_global = new Mock<IGlobalSearchService>();
			_records = new Mock<IRecordsSearchService>();
			_authorization = new Mock<IRecordsAuthorizationService>();
			_maintenance = new Mock<ISearchIndexMaintenanceService>();
			_states = new Mock<ISearchIndexStatesRepository>();
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.SearchUnified, 7, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentModuleSettingsAsync(7, true)).ReturnsAsync(new DepartmentModuleSettings());
			_controller = new SearchController(_search.Object, _global.Object, _records.Object, _maintenance.Object, _states.Object, settings.Object, flags.Object, _authorization.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previous;

		[TestCase(false)]
		[TestCase(true)]
		public async Task Search_identity_comes_only_from_authenticated_claims(bool prefix)
		{
			if (prefix) await _controller.Typeahead("engine");
			else await _controller.Search("engine");
			_search.Verify(s => s.SearchAsync(It.Is<UnifiedSearchRequest>(r => r.Prefix == prefix),
				It.Is<SearchPrincipal>(p => p.DepartmentId == 7 && p.UserId == "viewer" && p.HasResourceClaim("Unit", "View")), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Revoked_admin_cannot_read_health_or_request_rebuild()
		{
			(await _controller.Health()).Result.Should().BeOfType<ForbidResult>();
			(await _controller.Rebuild(default)).Result.Should().BeOfType<ForbidResult>();
			_global.Verify(g => g.GetHealthAsync(), Times.Never);
			_maintenance.Verify(m => m.RequestRebuildAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Health_discloses_only_department_counts_and_no_shared_revision()
		{
			_authorization.Setup(a => a.IsDepartmentAdminAsync("viewer", 7)).ReturnsAsync(true);
			_global.Setup(g => g.GetHealthAsync()).ReturnsAsync(new SearchIndexHealth { DocumentCount = 90001, LastSyncedRevision = "other-department-write", LastSyncedOnUtc = DateTime.UtcNow });
			_records.Setup(r => r.GetHealthAsync()).ReturnsAsync(new RecordsSearchHealth { DocumentCount = 80001 });
			_states.Setup(s => s.GetAsync(SearchIndexNames.Global, 7)).ReturnsAsync(new SearchIndexState { DepartmentId = 7, DocumentCount = 5 });
			var response = (SearchHealthResult)((OkObjectResult)(await _controller.Health()).Result).Value;
			response.Data.DepartmentDocumentCount.Should().Be(5);
			response.Data.GlobalDocumentCount.Should().BeNull();
			response.Data.RecordsDocumentCount.Should().BeNull();
			response.Data.LastSyncedRevision.Should().BeNull();
			response.Data.LastSyncedOnUtc.Should().BeNull();
			_states.Setup(s => s.GetAsync(SearchIndexNames.Global, 7)).ReturnsAsync(new SearchIndexState { DepartmentId = 8, DocumentCount = 991 });
			response = (SearchHealthResult)((OkObjectResult)(await _controller.Health()).Result).Value;
			response.Data.DepartmentDocumentCount.Should().Be(0);
		}

		[Test]
		public async Task Anonymous_health_does_not_disclose_shared_index_counts()
		{
			var enabled = Resgrid.Config.SearchConfig.Enabled;
			try
			{
				Resgrid.Config.SearchConfig.Enabled = true;
				_global.Setup(g => g.GetHealthAsync()).ReturnsAsync(new SearchIndexHealth { DocumentCount = 90001, Online = true });
				var health = new Mock<IHealthService>();
				health.Setup(h => h.GetDatabaseTimestamp()).ReturnsAsync("now");
				var controller = new HealthController(health.Object, _global.Object, Mock.Of<IInvoicePaymentsService>());
				var result = await controller.GetCurrent();
				result.Status.Should().Be("success");
				result.Data.SearchOnline.Should().BeTrue();
				result.Data.SearchIndexDocCount.Should().BeNull();
			}
			finally { Resgrid.Config.SearchConfig.Enabled = enabled; }
		}

		[Test]
		public void Endpoints_require_authentication_and_forbid_response_caching()
		{
			foreach (var type in new[] { typeof(SearchController), typeof(Resgrid.Web.Areas.User.Controllers.SearchController) })
			{
				foreach (var action in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
					type.GetCustomAttributes<AuthorizeAttribute>(true).Concat(action.GetCustomAttributes<AuthorizeAttribute>())
						.Should().NotBeEmpty("each search action must require authorization");
				type.GetCustomAttributes<AllowAnonymousAttribute>(true).Should().BeEmpty();
				var cache = type.GetCustomAttribute<ResponseCacheAttribute>();
				cache.NoStore.Should().BeTrue();
				cache.Location.Should().Be(ResponseCacheLocation.None);
			}
			foreach (var name in new[] { nameof(SearchController.Search), nameof(SearchController.Typeahead) })
				typeof(SearchController).GetMethod(name).GetCustomAttributes<AuthorizeAttribute>()
					.Should().Contain(a => a.Policy == ResgridResources.Department_View);
		}

		[Test]
		public void Missing_or_failed_module_checks_deny_module_access()
		{
			new SearchPrincipal().ModuleEnabled(SystemActionModules.Documents).Should().BeFalse();
			new SearchPrincipal { IsModuleEnabled = _ => throw new InvalidOperationException() }.ModuleEnabled(SystemActionModules.Documents).Should().BeFalse();
		}
	}
}
