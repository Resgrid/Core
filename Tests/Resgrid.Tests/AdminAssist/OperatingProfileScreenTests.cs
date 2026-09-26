using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>Season start and end are picked as a month and a day and stored as MM-DD.</summary>
	[TestFixture]
	public class SeasonMonthDayTests
	{
		private static bool Valid(string start, string end)
		{
			var profile = new DepartmentOperatingProfile { SeasonStartMonthDay = start, SeasonEndMonthDay = end };
			return Validator.TryValidateObject(profile, new ValidationContext(profile), new List<ValidationResult>(), true);
		}

		[TestCase("03", "15", "03-15")]
		[TestCase("3", "5", "03-05")]
		[TestCase("", "", null)]
		[TestCase(null, null, null)]
		public void Compose_builds_MM_DD_or_no_season(string month, string day, string expected) =>
			Assert.That(SeasonMonthDay.Compose(month, day), Is.EqualTo(expected));

		[TestCase("03", "")]
		[TestCase("", "15")]
		[TestCase("02", "30")]
		[TestCase("13", "01")]
		[TestCase("x", "1")]
		[TestCase("-1", "5")]
		public void A_partial_or_impossible_pick_fails_profile_validation_instead_of_clearing_the_season(string month, string day) =>
			Assert.That(Valid(SeasonMonthDay.Compose(month, day), "11-15"), Is.False);

		[Test]
		public void Every_day_a_picker_offers_is_a_valid_season_boundary_and_the_next_one_is_not()
		{
			for (var month = 1; month <= 12; month++)
			{
				var last = SeasonMonthDay.DaysIn(month);
				Assert.That(Valid(SeasonMonthDay.Compose(month.ToString(), last.ToString()), "01-01"), Is.True, "month " + month);
				if (last < 31) Assert.That(Valid(SeasonMonthDay.Compose(month.ToString(), (last + 1).ToString()), "01-01"), Is.False, "month " + month);
			}
			Assert.That(SeasonMonthDay.DaysIn(2), Is.EqualTo(29), "A season boundary may fall on the leap day.");
		}

		[TestCase("03-15", 3, 15)]
		[TestCase("02-29", 2, 29)]
		[TestCase(null, 0, 0)]
		[TestCase("3-15", 0, 0)]
		[TestCase("ab-cd", 0, 0)]
		public void Split_reads_a_stored_value(string value, int month, int day) =>
			Assert.That(SeasonMonthDay.Split(value), Is.EqualTo((month, day)));

		private static string[] Locales => Resgrid.Localization.SupportedLocales.SupportedLanguagesMap.Keys.ToArray();

		[TestCaseSource(nameof(Locales))]
		public void Month_names_are_the_twelve_Gregorian_months_in_the_UI_language(string locale)
		{
			var culture = CultureInfo.GetCultureInfo(locale);
			var names = SeasonMonthDay.MonthNames(culture);
			var gregorian = (CultureInfo)culture.Clone();
			if (gregorian.DateTimeFormat.Calendar is not GregorianCalendar)
				gregorian.DateTimeFormat.Calendar = culture.OptionalCalendars.OfType<GregorianCalendar>().First();

			Assert.That(names, Has.Length.EqualTo(12));
			for (var month = 1; month <= 12; month++)
			{
				var expected = new DateTime(2001, month, 1).ToString("MMMM", gregorian);
				Assert.That(names[month - 1], Is.EqualTo(culture.TextInfo.ToUpper(expected[0]) + expected[1..]), locale + " month " + month);
			}
		}
	}

	/// <summary>The operating profile screen composes picked seasons and offers reference pickers only when it redisplays.</summary>
	[TestFixture, NonParallelizable]
	public class OperatingProfileControllerTests
	{
		private readonly Dictionary<Type, Mock> _mocks = new();
		private IHttpContextAccessor _previousAccessor;
		private DepartmentController _controller;
		private DepartmentOperatingProfile _saved;

		private Mock<T> Of<T>() where T : class => (Mock<T>)_mocks[typeof(T)];

		[SetUp]
		public void SetUp()
		{
			var constructor = typeof(DepartmentController).GetConstructors().Single();
			var arguments = constructor.GetParameters().Select(parameter =>
			{
				var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(parameter.ParameterType));
				_mocks[parameter.ParameterType] = mock;
				return mock.Object;
			}).ToArray();
			_controller = (DepartmentController)constructor.Invoke(arguments);

			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{ new Claim(ClaimTypes.PrimarySid, "admin"), new Claim(ClaimTypes.PrimaryGroupSid, "7") }, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			_controller.ControllerContext = new ControllerContext { HttpContext = http };
			_controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			_controller.ObjectValidator = Mock.Of<IObjectModelValidator>();

			Of<IFeatureToggleService>().Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminSetup, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
			Of<IDepartmentsService>().Setup(d => d.GetDepartmentMemberAsync("admin", 7, true)).ReturnsAsync(new DepartmentMember { DepartmentId = 7, UserId = "admin", IsAdmin = true });
			Of<IDepartmentsService>().Setup(d => d.GetDepartmentByIdAsync(7, true)).ReturnsAsync(new Department { DepartmentId = 7, TimeZone = "Pacific Standard Time" });
			Of<IDepartmentGroupsService>().Setup(g => g.GetAllGroupsForDepartmentUnlimitedThinAsync(7))
				.ReturnsAsync(new List<DepartmentGroup> { new() { DepartmentGroupId = 13, Name = "Station 2" }, new() { DepartmentGroupId = 12, Name = "Station 1" } });
			Of<IDepartmentSettingsService>().Setup(s => s.GetOperatingProfileDocumentOptionsAsync(7, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<Document> { new() { DocumentId = 40, Name = "Staffing policy", Category = "Policies" }, new() { DocumentId = 39, Name = "Loose note" } });
			Of<IDepartmentSettingsService>().Setup(s => s.SetOperatingProfileAsync(7, It.IsAny<DepartmentOperatingProfile>(), "admin", It.IsAny<CancellationToken>()))
				.Callback<int, DepartmentOperatingProfile, string, CancellationToken>((_, profile, _, _) => _saved = profile).ReturnsAsync(new DepartmentSetting());
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		[Test]
		public async Task Saving_stores_the_picked_season_as_MM_DD_and_confirms()
		{
			var result = await _controller.OperatingProfile(new DepartmentOperatingProfile { Revision = 3, SiteGroupReferences = new() { "12", "" } }, "11", "15", "02", "29", default);

			Assert.That(result, Is.InstanceOf<RedirectToActionResult>());
			Assert.That(_saved.SeasonStartMonthDay, Is.EqualTo("11-15"));
			Assert.That(_saved.SeasonEndMonthDay, Is.EqualTo("02-29"));
			Assert.That(_saved.SiteGroupReferences, Is.EqualTo(new[] { "12" }));
			Assert.That(_controller.TempData["OperatingProfileSaved"], Is.EqualTo(true));
			// The pickers are only needed when the form is shown again.
			Of<IDepartmentSettingsService>().Verify(s => s.GetOperatingProfileDocumentOptionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Blank_pickers_save_no_season()
		{
			await _controller.OperatingProfile(new DepartmentOperatingProfile { SeasonStartMonthDay = "03-01", SeasonEndMonthDay = "09-30" }, "", "", "", "", default);

			Assert.That(_saved.SeasonStartMonthDay, Is.Null);
			Assert.That(_saved.SeasonEndMonthDay, Is.Null);
		}

		[Test]
		public async Task A_redisplay_offers_department_groups_and_current_documents_without_reading_file_contents()
		{
			Of<IDepartmentSettingsService>().Setup(s => s.SetOperatingProfileAsync(7, It.IsAny<DepartmentOperatingProfile>(), "admin", It.IsAny<CancellationToken>()))
				.ThrowsAsync(new AdminAssistConcurrencyException());

			var result = await _controller.OperatingProfile(new DepartmentOperatingProfile(), "", "", "", "", default);

			Assert.That(result, Is.InstanceOf<ViewResult>());
			Assert.That(_controller.Response.StatusCode, Is.EqualTo(409));
			var groups = (List<SelectListItem>)_controller.ViewData["ProfileGroups"];
			Assert.That(groups.Select(g => (g.Text, g.Value)), Is.EqualTo(new[] { ("Station 1", "12"), ("Station 2", "13") }));
			var documents = (List<SelectListItem>)_controller.ViewData["ProfileDocuments"];
			Assert.That(documents.Select(d => (d.Text, d.Value, d.Group?.Name)), Is.EqualTo(new[] { ("Loose note", "39", (string)null), ("Staffing policy", "40", "Policies") }));
			Of<IProtectedReadService>().Verify(p => p.ResolveDocumentsForReadAsync(7, It.Is<IReadOnlyList<Document>>(d => d.Count == 2), null, "admin", false, It.IsAny<CancellationToken>()), Times.Once);
			Of<IDocumentsService>().VerifyNoOtherCalls();
		}
	}
}
