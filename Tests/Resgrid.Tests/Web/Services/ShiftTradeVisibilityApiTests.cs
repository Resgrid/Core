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
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.ServicesCore.Helpers;
using ShiftsApiController = Resgrid.Web.Services.Controllers.v4.ShiftsController;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// What members who can't decide a shift trade see of it through the v4 API: an open request (on the board for
	/// everyone) or one they were asked to take, without other applicants' notes, and nothing of a request offered to
	/// other people.
	/// </summary>
	[TestFixture]
	public class ShiftTradeVisibilityApiTests
	{
		private const int DepartmentId = 10;
		private const string Me = "me-user";
		private const string Requester = "requester-user";
		private const string Applicant = "applicant-user";

		private Mock<IShiftsService> _shifts;
		private ShiftsApiController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_shifts = new Mock<IShiftsService>();
			_shifts.Setup(x => x.GetAllShiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Shift> { Shift() });

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId)).ReturnsAsync(ShiftManagementScope.None());

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("ShiftTradeVisibilityApiTests").Start();

			_controller = new ShiftsApiController(_shifts.Object, Mock.Of<IDepartmentsService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IDepartmentGroupsService>(), authorization.Object, Mock.Of<IShiftStaffingService>(), Mock.Of<IWorkShiftsService>(),
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<IUnitsService>(), Mock.Of<ICertificationService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private static Shift Shift() => new Shift { ShiftId = 5, DepartmentId = DepartmentId, Name = "A", Groups = new List<ShiftGroup>(), Days = new List<ShiftDay>() };

		private ShiftSignupTrade Trade(int id, bool isOpen, params ShiftSignupTradeUser[] users)
		{
			var trade = new ShiftSignupTrade
			{
				ShiftSignupTradeId = id,
				IsOpen = isOpen,
				SourceShiftSignupId = 500,
				SourceShiftSignup = new ShiftSignup { ShiftSignupId = 500, ShiftId = 5, UserId = Requester, ShiftDay = new DateTime(2030, 1, 10), Shift = Shift() },
				ReviewNote = "Not this one",
				Users = users.ToList()
			};

			_shifts.Setup(x => x.GetShiftTradeByIdAsync(id)).ReturnsAsync(trade);

			return trade;
		}

		private static ShiftSignupTradeUser User(string userId, string reason) => new ShiftSignupTradeUser { UserId = userId, Offered = true, Reason = reason };

		[Test]
		public async Task A_request_offered_to_other_people_is_not_found()
		{
			Trade(60, false, User(Applicant, "Happy to"));

			var result = (await _controller.GetShiftTrade(60)).Value;

			result.Status.Should().Be(ResponseHelper.NotFound);
			result.Data.Should().BeNull();
		}

		[Test]
		public async Task An_applicant_sees_their_own_note_and_the_review_but_not_the_others_notes()
		{
			Trade(61, true, User(Applicant, "Happy to"), User(Me, "I can cover"));

			var data = (await _controller.GetShiftTrade(61)).Value.Data;

			data.ReviewNote.Should().Be("Not this one");
			data.Users.Single(x => x.UserId == Me).Reason.Should().Be("I can cover");
			data.Users.Single(x => x.UserId == Applicant).Reason.Should().BeEmpty();
		}

		[Test]
		public async Task A_bystander_sees_an_open_request_without_notes()
		{
			Trade(62, true, User(Applicant, "Happy to"));

			var data = (await _controller.GetShiftTrade(62)).Value.Data;

			data.ReviewNote.Should().BeEmpty();
			data.Users.Single().Reason.Should().BeEmpty();
		}

		[Test]
		public async Task The_open_board_hides_notes_from_members_but_not_from_the_requester()
		{
			var others = Trade(63, true, User(Applicant, "Happy to"));
			var mine = Trade(64, true, User(Applicant, "Glad to help"));
			mine.SourceShiftSignup.UserId = Me;
			_shifts.Setup(x => x.GetOpenTradesAsync(DepartmentId)).ReturnsAsync(new List<ShiftSignupTrade> { others, mine });

			var data = (await _controller.GetOpenShiftTrades()).Value.Data;

			var other = data.Single(x => x.ShiftSignupTradeId == "63");
			other.ReviewNote.Should().BeEmpty();
			other.Users.Single().Reason.Should().BeEmpty();

			var own = data.Single(x => x.ShiftSignupTradeId == "64");
			own.ReviewNote.Should().Be("Not this one");
			own.Users.Single().Reason.Should().Be("Glad to help");
		}
	}
}
