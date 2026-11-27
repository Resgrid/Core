using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Wellness;
using Resgrid.Web.ServicesCore.Helpers;
using ApiMemberHealthController = Resgrid.Web.Services.Controllers.v4.MemberHealthController;
using static Resgrid.Tests.Wellness.MemberHealthTestWorld;

namespace Resgrid.Tests.Wellness
{
	/// <summary>The v4 endpoints Omni uses (my health, my duty status, my injuries, report an exposure or an injury, the duty board).</summary>
	[TestFixture]
	public class MemberHealthApiTests
	{
		private Activity _activity;

		private ApiMemberHealthController Controller(MemberHealthTestWorld world, string userId)
		{
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, userId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			return new ApiMemberHealthController(world.Service(), world.WorkforcePro) { ControllerContext = new ControllerContext { HttpContext = httpContext } };
		}

		[SetUp]
		public void SetUp() => _activity = new Activity("MemberHealthApiTests").Start();

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		[Test]
		public async Task GetMyHealth_returns_only_the_callers_records_with_their_duty_status()
		{
			var world = new MemberHealthTestWorld();
			world.AddInjury(Member, DateTime.UtcNow.AddDays(-2), MemberDutyStatuses.RestrictedDuty);
			world.AddInjury(Other, DateTime.UtcNow.AddDays(-2)).Description = "OTHER-ONLY";
			world.AddIncident(Other, ExposureIncidentStages.Reported, DateTime.UtcNow.AddDays(-2));

			var result = (await Controller(world, Member).GetMyHealth()).Result as OkObjectResult;

			var data = ((MyHealthResult)result!.Value).Data;
			data.DutyStatus.DutyStatus.Should().Be((int)MemberDutyStatuses.RestrictedDuty);
			data.Injuries.Should().ContainSingle().Which.Description.Should().Be("Fell from the tailboard");
			data.Cases.Should().BeEmpty();
		}

		[Test]
		public async Task ReportExposure_is_always_for_the_caller_and_a_rule_refusal_is_a_400_with_its_code()
		{
			var world = new MemberHealthTestWorld();
			var controller = Controller(world, Member);

			var bad = (await controller.ReportExposure(new ReportExposureInput { Route = 0, OccurredOn = DateTime.UtcNow.AddHours(-1) }, CancellationToken.None)).Result as BadRequestObjectResult;
			((ExposureCaseResult)bad!.Value).Error.Should().Be("route_required");

			var ok = (await controller.ReportExposure(new ReportExposureInput { Route = (int)ExposureIncidentRoutes.MucousMembrane, OccurredOn = DateTime.UtcNow.AddHours(-1) }, CancellationToken.None)).Result as OkObjectResult;
			((ExposureCaseResult)ok!.Value).Data.Stage.Should().Be((int)ExposureIncidentStages.Reported);
			world.Incidents.Single().UserId.Should().Be(Member);
		}

		[Test]
		public async Task The_duty_board_is_operational_and_scoped_to_who_the_caller_supervises()
		{
			var world = new MemberHealthTestWorld();
			world.AddInjury(Member, DateTime.UtcNow.AddDays(-2), MemberDutyStatuses.OffDuty, restrictions: "SECRET-RESTRICTION");
			world.AddInjury(OutsideGroup, DateTime.UtcNow.AddDays(-2), MemberDutyStatuses.OffDuty);

			var board = ((DutyBoardResult)((await Controller(world, GroupAdmin).GetDutyBoard()).Result as OkObjectResult)!.Value).Data;
			board.Select(b => b.UserId).Should().BeEquivalentTo(new[] { Member });
			Newtonsoft.Json.JsonConvert.SerializeObject(board).Should().NotContain("SECRET-RESTRICTION");

			((DutyBoardResult)((await Controller(world, Other).GetDutyBoard()).Result as OkObjectResult)!.Value).Data.Should().BeEmpty();
		}
	}
}
