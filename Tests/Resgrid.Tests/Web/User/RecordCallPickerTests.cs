using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Repositories.DataRepository.Queries.Calls;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[TestFixture, NonParallelizable]
	public class RecordCallPickerTests
	{
		private IHttpContextAccessor _previous;
		private DefaultHttpContext _http;
		private Mock<ICallsService> _calls;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IRecordsCutoverService> _cutover;
		private Mock<IRecordsProtectionService> _protection;
		private RecordCallsController _controller;
		private static Call Call(int id) => new Call { CallId = id, DepartmentId = 77, Number = "26-" + id, Name = "Warehouse fire", Address = "Main Street", LoggedOn = new DateTime(2026, 9, 22), State = 1 };

		[SetUp]
		public void SetUp()
		{
			_previous = ClaimsAuthorizationHelper._httpContextAccessor;
			_http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, "author"), new Claim(ClaimTypes.PrimaryGroupSid, "77"),
				new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View),
				new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create)
			}, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_calls = new Mock<ICallsService>(MockBehavior.Strict);
			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync("author", 77)).ReturnsAsync(true);
			_authorization.Setup(a => a.CanReadSourceCallAsync("author", 77, It.IsAny<Call>())).ReturnsAsync(true);
			_cutover = new Mock<IRecordsCutoverService>();
			_cutover.Setup(c => c.GetModuleStateAsync(77, false)).ReturnsAsync(new RecordsModuleState { FlagEnabled = true });
			_protection = new Mock<IRecordsProtectionService>();
			_controller = new RecordCallsController(_calls.Object, _authorization.Object, _cutover.Object, _protection.Object) {
				ControllerContext = new ControllerContext { HttpContext = _http }
			};
			_controller.ViewData[nameof(DepartmentTime)] = new DepartmentTime(new Department { TimeZone = "America/Los_Angeles" });
		}
		[TearDown] public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previous;
		private static JObject Json(IActionResult result) => JObject.FromObject(((JsonResult)result).Value);

		[Test]
		public async Task Search_pages_all_history_and_converts_inclusive_department_dates_across_DST()
		{
			CallSearchQuery query = null;
			_calls.Setup(c => c.SearchCallCandidatesAsync(77, It.IsAny<CallSearchQuery>())).Callback<int, CallSearchQuery>((_, q) => query = q)
				.ReturnsAsync(Enumerable.Range(1, 26).Select(Call).ToList());
			var result = Json(await _controller.Search("  Main  ", "closed", "2026-03-08", "2026-03-08", 25));
			query.Term.Should().Be("Main"); query.Closed.Should().BeTrue(); query.Offset.Should().Be(25); query.Take.Should().Be(26);
			query.FromUtc.Should().Be(new DateTime(2026, 3, 8, 8, 0, 0, DateTimeKind.Utc));
			query.UntilUtc.Should().Be(new DateTime(2026, 3, 9, 7, 0, 0, DateTimeKind.Utc));
			result["items"].Count().Should().Be(25); result.Value<int>("nextOffset").Should().Be(50);
			result["items"][0].Value<string>("text").Should().Contain("Warehouse fire").And.Contain("Main Street");
			_http.Response.Headers.CacheControl.ToString().Should().Be("no-store");
		}

		[Test]
		public async Task Search_filters_deleted_foreign_and_unauthorized_results_and_keeps_paging_past_hidden_rows()
		{
			var rows = Enumerable.Range(1, 26).Select(Call).ToList();
			rows[0].IsDeleted = true; rows[1].DepartmentId = 88;
			_authorization.Setup(a => a.CanReadSourceCallAsync("author", 77, It.IsAny<Call>())).ReturnsAsync(false);
			_calls.Setup(c => c.SearchCallCandidatesAsync(77, It.IsAny<CallSearchQuery>())).ReturnsAsync(rows);
			var result = Json(await _controller.Search());
			result["items"].Should().BeEmpty(); result.Value<int>("nextOffset").Should().Be(25);
		}

		[TestCase(false)] [TestCase(true)]
		public async Task Protected_search_and_selected_label_never_expose_names_or_addresses(bool selected)
		{
			_protection.Setup(p => p.IsEnforcedAsync(77)).ReturnsAsync(true);
			_calls.Setup(c => c.GetCallByIdAsync(9, true)).ReturnsAsync(Call(9));
			_calls.Setup(c => c.SearchCallCandidatesAsync(77, It.Is<CallSearchQuery>(q => !q.IncludeText))).ReturnsAsync(new List<Call> { Call(9) });
			var result = Json(await _controller.Search(selectedId: selected ? 9 : (int?)null));
			result.ToString().Should().Contain("26-9").And.NotContain("Warehouse").And.NotContain("Main Street");
		}

		[Test]
		public async Task Ciphertext_never_becomes_a_result_label_even_without_enforcement()
		{
			var call = Call(9); call.Name = "rgdp:secret"; call.Address = "rgdp:address";
			_calls.Setup(c => c.GetCallByIdAsync(9, true)).ReturnsAsync(call);
			Json(await _controller.Search(selectedId: 9)).ToString().Should().NotContain("rgdp:").And.NotContain("secret");
		}

		[TestCase("call")] [TestCase("record")] [TestCase("membership")] [TestCase("module")]
		public async Task Search_requires_call_and_record_access_current_membership_and_module(string missing)
		{
			if (missing == "module") _cutover.Setup(c => c.GetModuleStateAsync(77, false)).ReturnsAsync(new RecordsModuleState());
			else if (missing == "membership") _authorization.Setup(a => a.IsActiveMemberAsync("author", 77)).ReturnsAsync(false);
			else { var identity = (ClaimsIdentity)_http.User.Identity; identity.RemoveClaim(identity.Claims.First(c => c.Type == (missing == "call" ? ResgridClaimTypes.Resources.Call : ResgridClaimTypes.Resources.Record))); }
			var result = await _controller.Search();
			if (missing == "module") result.Should().BeOfType<NotFoundResult>(); else result.Should().BeOfType<ForbidResult>();
			_calls.VerifyNoOtherCalls();
		}

		[TestCase("bad", null, null, 0)] [TestCase(null, "bad", null, 0)]
		[TestCase(null, "2026-09-22", "2026-09-21", 0)] [TestCase(null, null, "9999-12-31", 0)]
		[TestCase(null, null, null, -1)] [TestCase(null, null, null, int.MaxValue)]
		public async Task Invalid_filters_are_rejected_before_querying(string status, string from, string to, int offset)
		{
			(await _controller.Search(status: status, from: from, to: to, offset: offset)).Should().BeOfType<BadRequestResult>();
			_calls.VerifyNoOtherCalls();
		}

		[TestCase("missing")] [TestCase("foreign")] [TestCase("deleted")] [TestCase("denied")]
		public async Task Selected_lookup_and_open_case_reject_unavailable_calls(string reason)
		{
			var call = reason == "missing" ? null : Call(9);
			if (reason == "foreign") call.DepartmentId = 88;
			if (reason == "deleted") call.IsDeleted = true;
			if (reason == "denied") _authorization.Setup(a => a.CanReadSourceCallAsync("author", 77, call)).ReturnsAsync(false);
			_calls.Setup(c => c.GetCallByIdAsync(9, true)).ReturnsAsync(call);
			(await _controller.Search(selectedId: 9)).Should().BeOfType<NotFoundResult>();
			var investigations = new Mock<IRecordsInvestigationsService>(MockBehavior.Strict);
			var controller = InvestigationController(investigations.Object);
			var model = new RecordInvestigationOpenView { Title = "Case", CallId = 9, IncidentSummary = "Keep this text" };
			((ViewResult)await controller.Open(model, default)).Model.Should().BeSameAs(model);
			controller.ModelState.IsValid.Should().BeFalse();
			investigations.VerifyNoOtherCalls();
		}

		[TestCase(null)] [TestCase(9)]
		public async Task Opening_case_passes_selected_call_and_keeps_optional_call(int? callId)
		{
			_calls.Setup(c => c.GetCallByIdAsync(9, true)).ReturnsAsync(Call(9));
			var investigations = new Mock<IRecordsInvestigationsService>(MockBehavior.Strict);
			investigations.Setup(i => i.OpenAsync(77, "author", "Case", null, callId, "Summary", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RmsInvestigationCase { RmsInvestigationCaseId = "new", CaseNumber = "INV-1" });
			(await InvestigationController(investigations.Object).Open(new RecordInvestigationOpenView { Title = "Case", CallId = callId, IncidentSummary = "Summary" }, default))
				.Should().BeOfType<RedirectToActionResult>();
			investigations.VerifyAll();
		}

		private RecordInvestigationsController InvestigationController(IRecordsInvestigationsService investigations)
		{
			var flags = new Mock<IFeatureToggleService>(); flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsInvestigations, 77, false, null)).ReturnsAsync(true);
			var occupancies = new Mock<IRecordsOccupancyService>();
			occupancies.Setup(o => o.ListAsync(77, "author", It.IsAny<RmsOccupancyQuery>())).ReturnsAsync(new List<RmsOccupancy>());
			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return new RecordInvestigationsController(investigations, occupancies.Object, Mock.Of<IUserProfileService>(), _cutover.Object,
				flags.Object, localizer.Object, _calls.Object, _authorization.Object, Mock.Of<IDepartmentsService>()) { ControllerContext = new ControllerContext { HttpContext = _http }, TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>()) };
		}

		[TestCase(false)] [TestCase(true)]
		public void Database_search_is_bounded_deterministic_and_omits_protected_text(bool postgres)
		{
			var config = postgres ? (Resgrid.Repositories.DataRepository.Configs.SqlConfiguration)new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var query = new CallSearchQuery { Term = "' OR 1=1 --", FromUtc = DateTime.UtcNow, UntilUtc = DateTime.UtcNow, Closed = true };
			var sql = SearchCallsQuery.Build(config, query);
			sql.ToLowerInvariant().Should().Contain("departmentid").And.Contain("isdeleted").And.Contain("@departmentid").And.Contain("@term")
				.And.Contain("@take").And.Contain("@offset").And.Contain("desc,").And.NotContain("name").And.NotContain("address").And.NotContain(query.Term);
			query.IncludeText = true;
			SearchCallsQuery.Build(config, query).ToLowerInvariant().Should().Contain("name").And.Contain("address");
			SearchCallsQuery.EscapeLike("%_![").Should().Be("!%!_!!![");
		}
	}
}
