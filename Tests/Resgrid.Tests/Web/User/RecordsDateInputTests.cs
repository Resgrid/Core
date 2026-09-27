using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;
using RecordsStrings = Resgrid.Localization.Areas.User.Records.Records;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Browsers accept any year in a date input, so a two-digit year posts as 0026. That reached the investigation-note insert
	/// and failed with a SqlDateTime overflow (Sentry RESGRID-WEB-1MY); recorded dates are now refused and filter dates ignored.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class RecordsDateInputTests
	{
		private IHttpContextAccessor _previous;
		private DefaultHttpContext _http;
		private Mock<IRecordsCutoverService> _cutover;
		private Mock<IFeatureToggleService> _toggles;
		private Mock<IStringLocalizer<RecordsStrings>> _localizer;
		private Mock<IRecordsInvestigationsService> _investigations;
		private RecordInvestigationsController _controller;

		private sealed class Probe : RecordsPreventionMvcControllerBase
		{
			public Probe(IRecordsCutoverService cutover, IFeatureToggleService toggles, IStringLocalizer<RecordsStrings> localizer) : base(cutover, toggles, localizer) { }
			public DateTime? Filter(string value) => ParseUtc(value);
			public DateTime? Entered(string value) => ParseEnteredUtc(value);
		}

		[SetUp]
		public void SetUp()
		{
			_previous = ClaimsAuthorizationHelper._httpContextAccessor;
			_http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, "investigator"), new Claim(ClaimTypes.PrimaryGroupSid, "77") }, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_cutover = new Mock<IRecordsCutoverService>();
			_cutover.Setup(c => c.GetModuleStateAsync(77, false)).ReturnsAsync(new RecordsModuleState { FlagEnabled = true });
			_toggles = new Mock<IFeatureToggleService>();
			_toggles.Setup(t => t.IsEnabledAsync(It.IsAny<string>(), 77, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			_localizer = new Mock<IStringLocalizer<RecordsStrings>>();
			_localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			_investigations = new Mock<IRecordsInvestigationsService>();
			_controller = new RecordInvestigationsController(_investigations.Object, Mock.Of<IRecordsOccupancyService>(), Mock.Of<IUserProfileService>(),
				_cutover.Object, _toggles.Object, _localizer.Object, Mock.Of<ICallsService>(), Mock.Of<IRecordsAuthorizationService>(), Mock.Of<IDepartmentsService>())
			{
				ControllerContext = new ControllerContext { HttpContext = _http },
				TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>())
			};
			_controller.ViewData[nameof(DepartmentTime)] = new DepartmentTime(new Department { TimeZone = "America/Los_Angeles" });
		}

		[TearDown] public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previous;

		private Probe NewProbe(string timeZone)
		{
			var probe = new Probe(_cutover.Object, _toggles.Object, _localizer.Object);
			probe.ViewData[nameof(DepartmentTime)] = new DepartmentTime(new Department { TimeZone = timeZone });
			return probe;
		}

		[Test]
		public async Task Note_with_a_two_digit_year_is_refused_with_a_message_instead_of_reaching_the_insert()
		{
			var result = await _controller.SaveNote("case", null, 1, "0026-09-25T19:00", "Interview", "Body", default);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(RecordInvestigationsController.Details));
			_controller.TempData["RecordsError"].Should().Be("InvalidDate");
			_investigations.Verify(i => i.AddNoteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RmsInvestigationNoteKind>(),
				It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Note_with_a_valid_local_time_is_saved_as_department_utc()
		{
			var result = await _controller.SaveNote("case", null, 1, "2026-09-25T19:00", "Interview", "Body", default);

			result.Should().BeOfType<RedirectToActionResult>();
			_controller.TempData["RecordsError"].Should().BeNull();
			_investigations.Verify(i => i.AddNoteAsync(77, "investigator", "case", (RmsInvestigationNoteKind)1,
				new DateTime(2026, 9, 26, 2, 0, 0, DateTimeKind.Utc), "Interview", "Body", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Evidence_with_a_two_digit_year_is_refused()
		{
			await _controller.AddEvidence("case", 1, "Photo", "0026-09-25T19:00", null, null, default);

			_controller.TempData["RecordsError"].Should().Be("InvalidDate");
			_investigations.Verify(i => i.AddEvidenceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RmsInvestigationEvidence>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("0026-09-25")]
		[TestCase("1752-12-31T23:00")]
		[TestCase("9999-12-31T23:00")]
		[TestCase("not a date")]
		public void Unstorable_or_unreadable_dates_are_null_for_filters_and_refused_when_entered(string value)
		{
			var probe = NewProbe("America/Los_Angeles");

			probe.Filter(value).Should().BeNull();
			probe.Invoking(p => p.Entered(value)).Should().Throw<ArgumentException>().WithMessage("InvalidDate");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		public void Blank_dates_are_null_so_the_callers_default_applies(string value)
		{
			var probe = NewProbe("America/Los_Angeles");

			probe.Filter(value).Should().BeNull();
			probe.Entered(value).Should().BeNull();
		}

		[Test]
		public void Year_one_in_a_zone_ahead_of_utc_is_rejected_rather_than_overflowing_the_zone_conversion()
		{
			var probe = NewProbe("Pacific/Kiritimati");

			probe.Filter("0001-01-01T00:00").Should().BeNull();
			probe.Filter("2026-01-01T00:00").Should().Be(new DateTime(2025, 12, 31, 10, 0, 0, DateTimeKind.Utc));
		}
	}
}
