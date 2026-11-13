using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Areas.User.Models.Units;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Mapping → Road Closures (M0314): call viewers see closures; department admins and members who can create calls
	/// manage them; closure times are typed in department time and stored in UTC. Also the travel mode form fields.
	/// </summary>
	[TestFixture]
	public class RoadClosuresControllerTests
	{
		private const int DepartmentId = 42;
		private const string UserId = "dispatcher-1";
		private const string Shape = "{\"type\":\"LineString\",\"coordinates\":[[3.715,51.05],[3.725,51.05]]}";

		private Mock<IRoadClosureService> _closures;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.RoadClosures>> _localizer;
		private RoadClosure _saved;

		[SetUp]
		public void SetUp()
		{
			_saved = null;
			_closures = new Mock<IRoadClosureService>();
			_closures.Setup(c => c.SaveClosureAsync(It.IsAny<RoadClosure>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((RoadClosure c, string _, CancellationToken __) => _saved = c)
				.ReturnsAsync((RoadClosure c, string _, CancellationToken __) => c);

			_departments = new Mock<IDepartmentsService>();
			// Brussels: UTC+1 in November.
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Romance Standard Time" });

			_settings = new Mock<IDepartmentSettingsService>();

			_localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.RoadClosures>>();
			_localizer.Setup(l => l[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, name));
			_localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns((string name, object[] _) => new LocalizedString(name, name));
		}

		private RoadClosuresController Build(params Claim[] claims)
		{
			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}.Concat(claims), "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			return new RoadClosuresController(_closures.Object, _departments.Object, _settings.Object, _localizer.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		private static Claim CallView => new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View);
		private static Claim CallCreate => new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create);
		private static Claim DepartmentAdmin => new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);

		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		private static RoadClosureEditView Posted(params RoadClosureWindowInput[] windows) => new RoadClosureEditView
		{
			Name = "Marathon - Main Street",
			GeometryJson = Shape,
			AppliesToDriving = true,
			Windows = windows.ToList()
		};

		[Test]
		public async Task a_member_who_can_only_view_calls_sees_closures_but_cannot_change_them()
		{
			_closures.Setup(c => c.GetClosuresForDepartmentAsync(DepartmentId, false)).ReturnsAsync(new List<RoadClosure>());

			var index = await Build(CallView).Index();
			index.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<RoadClosuresIndexView>().Which.CanManage.Should().BeFalse();

			ShouldBeRefused(await Build(CallView).New());
			ShouldBeRefused(await Build(CallView).Edit("c1"));
			ShouldBeRefused(await Build(CallView).Save(Posted(), CancellationToken.None));
			ShouldBeRefused(await Build(CallView).SetEnabled("c1", false, CancellationToken.None));
			ShouldBeRefused(await Build(CallView).Delete("c1", CancellationToken.None));
			_closures.Verify(c => c.SaveClosureAsync(It.IsAny<RoadClosure>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_closures.Verify(c => c.DeleteClosureAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task dispatchers_and_department_admins_can_add_closures()
		{
			(await Build(CallView, CallCreate).New()).Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("Edit");
			(await Build(DepartmentAdmin).New()).Should().BeOfType<ViewResult>();
		}

		[Test]
		public async Task closure_times_are_typed_in_department_time_and_saved_in_utc()
		{
			var result = await Build(CallCreate).Save(Posted(
				new RoadClosureWindowInput { StartsOn = "2026-11-20T22:00", EndsOn = "2026-11-21T06:00" },
				new RoadClosureWindowInput { StartsOn = "2026-11-22T09:30" }), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			_saved.DepartmentId.Should().Be(DepartmentId);
			_saved.AppliesToModes.Should().Be(RoadClosureTravelModes.Driving);
			_saved.Windows.Should().HaveCount(2);
			_saved.Windows[0].StartsOn.Should().Be(new DateTime(2026, 11, 20, 21, 0, 0, DateTimeKind.Utc));
			_saved.Windows[0].EndsOn.Should().Be(new DateTime(2026, 11, 21, 5, 0, 0, DateTimeKind.Utc));
			_saved.Windows[1].EndsOn.Should().BeNull("a blank end is until further notice");
		}

		[Test]
		public async Task a_time_that_ends_before_it_starts_is_sent_back_unsaved()
		{
			var result = await Build(CallCreate).Save(Posted(new RoadClosureWindowInput { StartsOn = "2026-11-20T22:00", EndsOn = "2026-11-20T21:00" }), CancellationToken.None);

			result.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("Edit");
			_saved.Should().BeNull();
		}

		[Test]
		public async Task a_closure_the_service_refuses_comes_back_with_the_reason_on_its_field()
		{
			_closures.Setup(c => c.SaveClosureAsync(It.IsAny<RoadClosure>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new RoadClosureValidationException(RoadClosureValidationErrors.ShapeRequired, nameof(RoadClosure.GeometryJson), "Draw it."));
			var controller = Build(CallCreate);

			var result = await controller.Save(Posted(), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState[nameof(RoadClosure.GeometryJson)].Errors.Should().ContainSingle(e => e.ErrorMessage == "ErrorShapeRequired");
		}

		[Test]
		public async Task another_departments_closure_cannot_be_opened_or_saved()
		{
			_closures.Setup(c => c.GetClosureByIdAsync(DepartmentId, "elsewhere")).ReturnsAsync((RoadClosure)null);

			(await Build(DepartmentAdmin).Edit("elsewhere")).Should().BeOfType<NotFoundResult>();

			var post = Posted();
			post.RoadClosureId = "elsewhere";
			(await Build(DepartmentAdmin).Save(post, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			_saved.Should().BeNull();
		}

		#region Travel mode fields

		[Test]
		public void a_unit_type_saves_its_mode_and_a_speed_entered_in_mph_as_kmh()
		{
			var type = new UnitType();

			new TravelModeInput { Metric = false, Mode = "1", Speed = 10 }.ApplyTo(type);

			type.TravelMode.Should().Be((int)UnitTravelModes.Cycling);
			type.TravelSpeedKmh.Should().BeApproximately(16.09, 0.01);
		}

		[Test]
		public void a_unit_left_blank_inherits_from_its_type()
		{
			var unit = new Unit { TravelMode = 2, TravelSpeedKmh = 5 };

			new TravelModeInput { Metric = true, Mode = "", Speed = null }.ApplyTo(unit);

			unit.TravelMode.Should().BeNull();
			unit.TravelSpeedKmh.Should().BeNull();
		}

		[Test]
		public void travel_inputs_out_of_range_are_refused()
		{
			new TravelModeInput { Metric = true, Mode = "9" }.Validate().Should().ContainSingle(e => e.Field == "Mode");
			new TravelModeInput { Metric = true, Mode = "3", Speed = 5000 }.Validate().Should().ContainSingle(e => e.Field == "Speed");
			new TravelModeInput { Metric = true, Mode = "3", Speed = 200 }.Validate().Should().BeEmpty();
		}

		[TestCase(-10d)]
		[TestCase(0d)]
		[TestCase(double.NaN)]
		[TestCase(double.PositiveInfinity)]
		public void a_speed_that_is_not_a_positive_number_is_refused_not_read_as_blank(double speed)
		{
			// PR 585: read as blank it passed validation and cleared the speed already saved.
			new TravelModeInput { Metric = true, Mode = "2", Speed = speed }.Validate().Should().ContainSingle(e => e.Field == "Speed");
			new TravelModeInput { Metric = false, Mode = "2", Speed = speed }.Validate().Should().ContainSingle(e => e.Field == "Speed");
			new TravelModeInput { Metric = true, Mode = "2", Speed = null }.Validate().Should().BeEmpty("blank is the only way to use the default");
		}

		[TestCase(null, "current")]
		[TestCase("", "current")]
		[TestCase("ALL", "all")]
		[TestCase(" all ", "all")]
		[TestCase("ended", "current")]
		[TestCase("scheduled", "current")]
		[TestCase("disabled", "current")]
		public async Task the_list_takes_only_the_filters_it_offers(string filter, string expected)
		{
			// PR 585: the page offers current and all; other names were accepted and then shown as current.
			_closures.Setup(c => c.GetClosuresForDepartmentAsync(DepartmentId, false)).ReturnsAsync(new List<RoadClosure>());

			var index = await Build(CallView).Index(filter);

			index.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<RoadClosuresIndexView>().Which.Filter.Should().Be(expected);
		}

		[Test]
		public void the_form_shows_stored_values_in_the_chosen_units()
		{
			var input = TravelModeInput.From(new UnitType { TravelMode = 3, TravelSpeedKmh = 180 }, metric: false);

			input.Mode.Should().Be("3");
			input.Speed.Should().BeApproximately(111.8, 0.1);
			TravelModeInput.From(new Unit(), metric: true).Mode.Should().BeEmpty();
		}

		#endregion
	}
}
