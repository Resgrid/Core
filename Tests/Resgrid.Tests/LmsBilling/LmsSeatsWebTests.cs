using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;
using Resgrid.Web.ViewComponents;

namespace Resgrid.Tests.LmsBilling
{
	/// <summary>
	/// The seat switch on the personnel pages (§17.4 "where the switch lives"): department admins only, NotFound while the LMS is off,
	/// a refusal beyond the purchased seats offers a pack, and the personnel page asks the gate once per request however many LMS
	/// panels it draws (R-L9).
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LmsSeatsWebTests
	{
		private const int DepartmentId = 44;

		private Mock<ILmsSeatService> _seats;
		private Mock<ILmsAccessService> _access;
		private Mock<IDepartmentGroupsService> _groups;
		private DefaultHttpContext _http;

		[SetUp]
		public void SetUp()
		{
			_seats = new Mock<ILmsSeatService>();
			_access = new Mock<ILmsAccessService>();
			_access.Setup(a => a.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(true);
			_seats.Setup(s => s.GetSummaryAsync(DepartmentId)).ReturnsAsync(new LmsSeatSummary
			{
				State = LmsSubscriptionStates.Active, PurchasedPacks = 1, PackSize = 25, EnabledSeats = 25,
				EnabledUserIds = new HashSet<string>(new[] { "pat" }, StringComparer.OrdinalIgnoreCase),
				LicensedUserIds = new HashSet<string>(new[] { "pat" }, StringComparer.OrdinalIgnoreCase)
			});
			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(g => g.GetAllGroupsForDepartmentUnlimitedThinAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 3, DepartmentId = DepartmentId, Name = "Career" } });
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private void SignIn(bool admin)
		{
			var claims = new List<Claim> { new(ClaimTypes.PrimarySid, "admin"), new(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) };
			if (admin) claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
			_http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
		}

		private LmsSeatsController Controller()
		{
			var strings = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons>>();
			strings.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return new LmsSeatsController(_seats.Object, _access.Object, _groups.Object, strings.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = _http },
				TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>())
			};
		}

		private T Component<T>(T component) where T : ViewComponent
		{
			component.ViewComponentContext = new ViewComponentContext { ViewContext = new ViewContext { HttpContext = _http, TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>()) } };
			component.ViewEngine = Mock.Of<ICompositeViewEngine>();
			return component;
		}

		[Test]
		public async Task Only_department_admins_switch_seats()
		{
			SignIn(admin: false);

			(await Controller().SetSeat("pat", true, CancellationToken.None)).Should().BeOfType<ForbidResult>();
			(await Controller().SetGroup(3, true, CancellationToken.None)).Should().BeOfType<ForbidResult>();
			_seats.Verify(s => s.SetSeatAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_switch_is_not_found_while_the_lms_is_off()
		{
			SignIn(admin: true);
			_access.Setup(a => a.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(false);

			(await Controller().SetSeat("pat", true, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task A_seat_refused_for_lack_of_seats_offers_a_pack_and_returns_to_the_member()
		{
			SignIn(admin: true);
			_seats.Setup(s => s.SetSeatAsync(DepartmentId, "sam", true, "admin", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.SeatsFull, PacksNeeded = 1, Summary = new LmsSeatSummary { PurchasedPacks = 1, PackSize = 25 } });
			var controller = Controller();

			var result = await controller.SetSeat("sam", true, CancellationToken.None);

			var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
			redirect.ActionName.Should().Be("ViewPerson");
			redirect.RouteValues["userId"].Should().Be("sam");
			controller.TempData[LmsSeatsController.OfferPackKey].Should().Be(true);
			((string)controller.TempData[LmsSeatsController.MessageKey]).Should().StartWith("LmsSeatsFull");
		}

		[Test]
		public async Task A_group_of_another_department_is_not_found()
		{
			SignIn(admin: true);
			_groups.Setup(g => g.GetGroupByIdAsync(9, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 9, DepartmentId = 999 });

			(await Controller().SetGroup(9, true, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task The_personnel_panels_are_for_admins_and_ask_the_gate_once_per_request()
		{
			SignIn(admin: false);
			var hidden = await Component(new LmsSeatSwitchViewComponent(_access.Object, _seats.Object)).InvokeAsync("pat");
			hidden.Should().BeOfType<ContentViewComponentResult>().Which.Content.Should().BeEmpty();
			_seats.Verify(s => s.GetSummaryAsync(It.IsAny<int>()), Times.Never);

			SignIn(admin: true);
			var seatSwitch = await Component(new LmsSeatSwitchViewComponent(_access.Object, _seats.Object)).InvokeAsync("pat");
			var counter = await Component(new LmsSeatSummaryViewComponent(_access.Object, _seats.Object, _groups.Object)).InvokeAsync();

			seatSwitch.Should().BeOfType<ViewViewComponentResult>().Which.ViewData.Model.Should().BeOfType<LmsSeatPanelView>().Which.MemberEnabled.Should().BeTrue();
			counter.Should().BeOfType<ViewViewComponentResult>().Which.ViewData.Model.Should().BeOfType<LmsSeatPanelView>().Which.Groups.Should().ContainSingle();
			_seats.Verify(s => s.GetSummaryAsync(DepartmentId), Times.Once);
			_access.Verify(a => a.IsFlagEnabledAsync(DepartmentId), Times.Once);
		}

		[Test]
		public async Task The_personnel_panels_stay_away_while_the_lms_is_off()
		{
			SignIn(admin: true);
			_access.Setup(a => a.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(false);

			var counter = await Component(new LmsSeatSummaryViewComponent(_access.Object, _seats.Object, _groups.Object)).InvokeAsync();

			counter.Should().BeOfType<ContentViewComponentResult>().Which.Content.Should().BeEmpty();
			_seats.Verify(s => s.GetSummaryAsync(It.IsAny<int>()), Times.Never);
		}
	}
}
