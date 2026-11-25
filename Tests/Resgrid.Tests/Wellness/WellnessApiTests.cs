using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Wellness;
using Resgrid.Web.ServicesCore.Helpers;
using ApiWellnessController = Resgrid.Web.Services.Controllers.v4.WellnessController;
using static Resgrid.Tests.Wellness.WellnessTestWorld;

namespace Resgrid.Tests.Wellness
{
	/// <summary>
	/// The v4 endpoints Omni uses (list mine, log, confirm or dismiss a suggestion) act on the signed-in member's own entries only, even
	/// for a wellness officer, and a suggestion becomes an entry only through an explicit confirm.
	/// </summary>
	[TestFixture]
	public class WellnessApiTests
	{
		private Activity _activity;

		private ApiWellnessController Controller(WellnessTestWorld world, string userId)
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
			return new ApiWellnessController(world.Service()) { ControllerContext = new ControllerContext { HttpContext = httpContext } };
		}

		[SetUp]
		public void SetUp() => _activity = new Activity("WellnessApiTests").Start();

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		[Test]
		public async Task GetMyExposures_returns_only_the_callers_entries_and_suggestions()
		{
			var world = new WellnessTestWorld();
			world.AddExposure(Member, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, DateTime.UtcNow.AddDays(-3));
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Suggested, DateTime.UtcNow.AddDays(-1));
			world.AddExposure(Peer, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, DateTime.UtcNow.AddDays(-1), substance: "PEER-ONLY");

			var result = (await Controller(world, Member).GetMyExposures()).Result as OkObjectResult;

			var data = ((MyExposuresResult)result!.Value).Data;
			data.Entries.Should().ContainSingle();
			data.Suggestions.Should().ContainSingle();
			data.CriticalIncidentCount.Should().Be(1);
			data.Entries.Concat(data.Suggestions).Should().NotContain(e => e.Substance == "PEER-ONLY");
		}

		[Test]
		public async Task A_wellness_officer_cannot_confirm_someone_elses_suggestion_through_the_api()
		{
			var world = new WellnessTestWorld();
			var suggestion = world.AddExposure(Member, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Suggested, DateTime.UtcNow.AddDays(-1));

			var byOfficer = await Controller(world, Officer).ConfirmSuggestion(new ConfirmExposureSuggestionInput { ExposureId = suggestion.RmsResponderExposureId }, default);
			byOfficer.Result.Should().BeOfType<NotFoundResult>();
			suggestion.Status.Should().Be((int)ResponderExposureStatuses.Suggested);

			var byMember = (await Controller(world, Member).ConfirmSuggestion(new ConfirmExposureSuggestionInput { ExposureId = suggestion.RmsResponderExposureId }, default)).Result as OkObjectResult;
			((ExposureEntryResult)byMember!.Value).Data.StatusName.Should().Be(nameof(ResponderExposureStatuses.Confirmed));
		}

		[Test]
		public async Task CreateExposure_logs_an_entry_for_the_caller_and_returns_the_rule_code_on_a_refusal()
		{
			var world = new WellnessTestWorld();

			var created = (await Controller(world, Member).CreateExposure(new CreateExposureInput
			{
				Category = (int)ResponderExposureCategories.HazardousSubstance,
				OccurredOn = DateTime.UtcNow.AddHours(-2),
				Substance = "Chlorine"
			}, default)).Result as OkObjectResult;

			((ExposureEntryResult)created!.Value).Data.Substance.Should().Be("Chlorine");
			world.Exposures.Should().ContainSingle(e => e.UserId == Member && e.Status == (int)ResponderExposureStatuses.Confirmed);

			var refused = (await Controller(world, Member).CreateExposure(new CreateExposureInput { Category = 0, OccurredOn = DateTime.UtcNow }, default)).Result as BadRequestObjectResult;
			((ExposureEntryResult)refused!.Value).Error.Should().Be("category_required");
		}

		[Test]
		public void The_web_exports_take_no_parameter_that_could_name_another_member()
		{
			var controller = typeof(Resgrid.Web.Areas.User.Controllers.WellnessController);
			controller.GetMethod("ExportPdf")!.GetParameters().Should().BeEmpty();
			controller.GetMethod("ExportCsv")!.GetParameters().Should().BeEmpty();
		}

		[Test]
		public void A_sealed_value_never_leaves_the_api_as_an_envelope()
		{
			var data = ApiWellnessController.Map(new RmsResponderExposure { RmsResponderExposureId = "x", Substance = "rgdp:v1:sealed", HealthDetails = ProtectedDataEnvelope.RedactionValue });
			data.Substance.Should().Be(ProtectedDataEnvelope.RedactionValue);
			data.Withheld.Should().BeTrue();
		}
	}
}
