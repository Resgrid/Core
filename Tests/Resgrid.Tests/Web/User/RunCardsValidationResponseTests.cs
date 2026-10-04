using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.RunCards;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[TestFixture, NonParallelizable]
	public class RunCardsValidationResponseTests
	{
		[Test]
		public async Task Invalid_references_return_bad_request_without_exposing_the_service_exception()
		{
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, "user"), new Claim(ClaimTypes.PrimaryGroupSid, "12") }, "test")) };
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			try
			{
				var cards = new Mock<IRunCardsService>();
				cards.Setup(x => x.SaveRunCardAsync(It.IsAny<RunCard>(), It.IsAny<CancellationToken>()))
					.ThrowsAsync(new ArgumentException("Internal reference details for department 99"));
				var auth = new Mock<IAuthorizationService>();
				auth.Setup(x => x.CanUserModifyDepartmentAsync("user", 12)).ReturnsAsync(true);
				var flags = new Mock<IFeatureToggleService>();
				flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, 12, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
				var controller = new RunCardsController(cards.Object, Mock.Of<ICallsService>(), Mock.Of<IUnitsService>(),
					Mock.Of<IPersonnelRolesService>(), Mock.Of<ICustomStateService>(), Mock.Of<IDepartmentGroupsService>(),
					Mock.Of<IDispatchRecommendationService>(), auth.Object, flags.Object)
					{ ControllerContext = new ControllerContext { HttpContext = http } };
				var input = new RunCardEditInput { Name = "Response", Triggers = new List<RunCardTriggerInput> { new RunCardTriggerInput() },
					AlarmLevels = new List<RunCardAlarmLevelInput> { new RunCardAlarmLevelInput { AlarmLevel = 1 } } };

				var result = (await controller.Save(input, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>().Subject;

				result.StatusCode.Should().Be(400);
				var json = JsonSerializer.SerializeToElement(result.Value);
				json.GetProperty("success").GetBoolean().Should().BeFalse();
				json.GetProperty("message").GetString().Should().Contain("Reload the editor").And.NotContain("Internal").And.NotContain("99");
			}
			finally { ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}
}
