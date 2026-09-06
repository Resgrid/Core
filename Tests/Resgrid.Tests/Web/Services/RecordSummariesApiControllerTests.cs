using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 RecordSummaries (RMS plan sections 5.1 and 4.7): flag gate, member visibility per row, rows the caller
	/// cannot see are omitted from the feed, a bad cursor is a 400, a checksum failure is a 409, and the same
	/// endpoints serve a system principal only through its configured grant.
	/// </summary>
	[TestFixture]
	public class RecordSummariesApiControllerTests
	{
		private const int Dept = 42;
		private const string Me = "author";

		private Mock<IRecordOperationalSummaryService> _summaries;
		private Mock<IRecordsCutoverService> _cutover;
		private Mock<IRecordsAuthorizationService> _authorization;
		private RecordsModuleState _moduleState;
		private RecordSummariesController _controller;
		private DefaultHttpContext _http;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_summaries = new Mock<IRecordOperationalSummaryService>();
			_cutover = new Mock<IRecordsCutoverService>();
			_moduleState = new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true };
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _moduleState);
			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync(Me, Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUserViewRecordAsync(Me, It.IsAny<string>(), Dept)).ReturnsAsync(true);

			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()),
					new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View)
				}, "test"))
			};
			_http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_activity = new Activity("RecordSummariesApiControllerTests").Start();
			_controller = new RecordSummariesController(_summaries.Object, _cutover.Object, _authorization.Object) { ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown]
		public void TearDown()
		{
			_activity?.Stop();
		}

		private static RecordOperationalSummaryV1 Summary(string id) => new RecordOperationalSummaryV1 { DepartmentId = Dept, RecordId = id, RecordKind = RmsRecordKind.Operational, RevisionId = id + "-r1", RevisionChecksum = "abc", CorrectionStatus = RecordOperationalSummaryCorrectionStatus.Current };

		[Test]
		public async Task Flag_off_hides_both_endpoints()
		{
			_moduleState.FlagEnabled = false;
			(await _controller.Get("rec")).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.List()).Result.Should().BeOfType<NotFoundResult>();
			_summaries.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Get_returns_the_summary_only_for_a_visible_record()
		{
			_summaries.Setup(s => s.BuildAsync(Dept, "rec", RmsRecordKind.Operational, null)).ReturnsAsync(Summary("rec"));
			var ok = (await _controller.Get("rec")).Result.Should().BeOfType<OkObjectResult>().Which;
			((RecordOperationalSummaryResult)ok.Value).Data.RecordId.Should().Be("rec");

			_authorization.Setup(a => a.CanUserViewRecordAsync(Me, "hidden", Dept)).ReturnsAsync(false);
			(await _controller.Get("hidden")).Result.Should().BeOfType<NotFoundResult>("a record the member cannot see is indistinguishable from a missing one");
			_summaries.Verify(s => s.BuildAsync(Dept, "hidden", It.IsAny<RmsRecordKind>(), It.IsAny<string>()), Times.Never);

			(await _controller.Get(" ")).Result.Should().BeOfType<BadRequestResult>();
		}

		[Test]
		public async Task Get_reports_a_checksum_failure_as_a_conflict()
		{
			_summaries.Setup(s => s.BuildAsync(Dept, "rec", RmsRecordKind.Operational, "r9")).ThrowsAsync(new InvalidOperationException("The revision checksum does not match; the summary cannot be trusted."));
			var problem = (await _controller.Get("rec", RmsRecordKind.Operational, "r9")).Result.Should().BeOfType<ObjectResult>().Which;
			problem.StatusCode.Should().Be(409);
		}

		[Test]
		public async Task List_omits_rows_the_member_cannot_see_and_passes_paging_through()
		{
			_summaries.Setup(s => s.QueryAsync(Dept, It.Is<RecordOperationalSummaryQuery>(q => q.Take == 2 && q.Cursor == "ros1:x" && q.RecordKind == RmsRecordKind.IncidentReport && q.ChangedSince.HasValue)))
				.ReturnsAsync(new RecordOperationalSummaryPage { Items = new List<RecordOperationalSummaryV1> { Summary("a"), Summary("b") }, HasMore = true, NextCursor = "ros1:next" });
			_authorization.Setup(a => a.CanUserViewRecordAsync(Me, "b", Dept)).ReturnsAsync(false);

			var ok = (await _controller.List(1000, 2, "ros1:x", RmsRecordKind.IncidentReport)).Result.Should().BeOfType<OkObjectResult>().Which;
			var result = (RecordOperationalSummariesResult)ok.Value;
			result.Data.Select(d => d.RecordId).Should().Equal("a");
			result.HasMore.Should().BeTrue();
			result.NextCursor.Should().Be("ros1:next");
			result.ContractVersion.Should().Be(1);
			result.PageSize.Should().Be(1);
		}

		[Test]
		public async Task List_rejects_a_bad_cursor_and_a_bad_since()
		{
			_summaries.Setup(s => s.QueryAsync(Dept, It.IsAny<RecordOperationalSummaryQuery>())).ThrowsAsync(new ArgumentException("bad cursor"));
			(await _controller.List(0, 10, "garbage")).Result.Should().BeOfType<BadRequestResult>();
			(await _controller.List(-1)).Result.Should().BeOfType<BadRequestResult>();
		}

		[Test]
		public async Task List_before_activation_is_empty_rather_than_an_error()
		{
			_moduleState.Activated = false;
			var ok = (await _controller.List()).Result.Should().BeOfType<OkObjectResult>().Which;
			((RecordOperationalSummariesResult)ok.Value).Data.Should().BeEmpty();
			_summaries.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_former_member_sees_nothing()
		{
			_authorization.Setup(a => a.IsActiveMemberAsync(Me, Dept)).ReturnsAsync(false);
			_summaries.Setup(s => s.BuildAsync(Dept, "rec", RmsRecordKind.Operational, null)).ReturnsAsync(Summary("rec"));
			(await _controller.Get("rec")).Result.Should().BeOfType<NotFoundResult>();
		}
	}
}
