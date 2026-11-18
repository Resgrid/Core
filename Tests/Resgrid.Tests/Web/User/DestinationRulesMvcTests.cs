using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Mapping → Destination Rules on the web: department admins manage the rules and the suggestion setting; anyone who can
	/// see calls gets the call form's suggestions, which answer at once while the setting is off.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DestinationRulesMvcTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "admin-1";

		private static readonly Claim DepartmentAdmin = new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			M<IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.DestinationRules>>().Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			M<IDestinationRulesService>().Setup(x => x.GetRulesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DestinationRule>());
			M<IDepartmentSettingsService>().Setup(x => x.GetDestinationSuggestionConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DestinationSuggestionConfig());
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		private DestinationRulesController Build(params Claim[] claims)
		{
			var constructor = typeof(DestinationRulesController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}.Concat(claims), "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (DestinationRulesController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		private static JObject JsonOf(IActionResult result) => JObject.FromObject(result.Should().BeOfType<JsonResult>().Which.Value);

		[TestCase("SaveSettings")]
		[TestCase("Edit")]
		[TestCase("Delete")]
		public void State_changing_actions_are_post_with_antiforgery(string action)
		{
			var posts = typeof(DestinationRulesController).GetMethods().Where(m => m.Name == action && m.GetCustomAttribute<HttpPostAttribute>() != null).ToList();

			posts.Should().NotBeEmpty();
			posts.Should().OnlyContain(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null);
		}

		[Test]
		public async Task Only_department_admins_manage_rules_and_the_setting()
		{
			var controller = Build();

			ShouldBeRefused(await controller.Index());
			ShouldBeRefused(await controller.Edit((string)null));
			ShouldBeRefused(await controller.Edit(new DestinationRuleEditView { Name = "x" }, CancellationToken.None));
			ShouldBeRefused(await controller.Delete("r1", CancellationToken.None));
			ShouldBeRefused(await controller.SaveSettings(2, 3, CancellationToken.None));
			ShouldBeRefused(await controller.Preview(null, null, null));

			M<IDestinationRulesService>().Verify(x => x.SaveRuleAsync(It.IsAny<int>(), It.IsAny<DestinationRule>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			M<IDepartmentSettingsService>().Verify(x => x.SetDestinationSuggestionConfigAsync(It.IsAny<int>(), It.IsAny<DestinationSuggestionConfig>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Saving_the_setting_stores_the_mode_and_window()
		{
			var result = await Build(DepartmentAdmin).SaveSettings((int)DestinationSuggestionModes.SuggestAndFill, 5, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			M<IDepartmentSettingsService>().Verify(x => x.SetDestinationSuggestionConfigAsync(DepartmentId,
				It.Is<DestinationSuggestionConfig>(c => c.Mode == 2 && c.PreferredWithinMinutes == 5), It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase(7, 3)]
		[TestCase(1, 31)]
		[TestCase(1, -1)]
		public async Task An_unknown_mode_or_window_is_refused(int mode, int window)
		{
			var controller = Build(DepartmentAdmin);

			await controller.SaveSettings(mode, window, CancellationToken.None);

			controller.TempData["DestinationRulesError"].Should().NotBeNull();
			M<IDepartmentSettingsService>().Verify(x => x.SetDestinationSuggestionConfigAsync(It.IsAny<int>(), It.IsAny<DestinationSuggestionConfig>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Saving_a_rule_passes_the_form_to_the_service_for_the_callers_department()
		{
			M<IDestinationRulesService>().Setup(x => x.SaveRuleAsync(DepartmentId, It.IsAny<DestinationRule>(), UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, DestinationRule rule, string __, CancellationToken ___) => new DestinationRuleSaveResult { Rule = rule });

			var result = await Build(DepartmentAdmin).Edit(new DestinationRuleEditView
			{
				Name = "Helicopter",
				UnitTypeId = 7,
				CallTypeId = 0,
				PoiTypeIds = new List<int> { 10, 11 },
				RequiredCapabilityIds = new List<string> { "helipad" },
				PreferredCapabilityIds = new List<string> { "cath" }
			}, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<IDestinationRulesService>().Verify(x => x.SaveRuleAsync(DepartmentId, It.Is<DestinationRule>(r =>
				r.Name == "Helicopter" && r.UnitTypeId == 7 && r.CallTypeId == null && r.PoiTypeIds == "10,11" && r.RequiredCapabilityIds == "helipad" &&
				r.PreferredCapabilityIds == "cath"), UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_refused_rule_shows_the_form_again_with_the_reason()
		{
			M<IDestinationRulesService>().Setup(x => x.SaveRuleAsync(DepartmentId, It.IsAny<DestinationRule>(), UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DestinationRuleSaveResult { Error = DestinationRuleSaveErrors.NothingRequired });
			var controller = Build(DepartmentAdmin);

			var result = await controller.Edit(new DestinationRuleEditView { Name = "Empty" }, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState.IsValid.Should().BeFalse();
			controller.ModelState[string.Empty].Errors.Single().ErrorMessage.Should().Be("RuleNothingRequired");
		}

		[Test]
		public async Task Another_departments_rule_cannot_be_opened_or_deleted()
		{
			var controller = Build(DepartmentAdmin);

			(await controller.Edit("theirs")).Should().BeOfType<NotFoundResult>();
			(await controller.Delete("theirs", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			M<IDestinationRulesService>().Verify(x => x.DeleteRuleAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Suggestions_answer_off_without_details_while_the_setting_is_off()
		{
			M<INearestDestinationService>().Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DestinationSuggestions { Mode = DestinationSuggestionModes.Off });

			var json = JsonOf(await Build().Suggest(50.8, 3.8, 1, "Burns", CancellationToken.None));

			json["success"].Value<bool>().Should().BeTrue();
			json["mode"].Value<int>().Should().Be(0);
			json["destinations"].Should().BeNull();
		}

		[Test]
		public async Task Suggestions_name_capabilities_and_carry_drive_time_and_distance()
		{
			var type = new PoiType { PoiTypeId = 10, DepartmentId = DepartmentId, Name = "ED", IsDestination = true };
			var poi = new Poi { PoiId = 31, PoiTypeId = 10, Type = type, Name = "UZ Gent", CapabilityIds = new List<string> { "burn" } };
			M<IMappingService>().Setup(x => x.GetPoiCapabilitiesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PoiCapability>
			{
				new PoiCapability { PoiCapabilityId = "burn", DepartmentId = DepartmentId, Name = "Burn Center" },
				new PoiCapability { PoiCapabilityId = "helipad", DepartmentId = DepartmentId, Name = "Helipad" }
			});
			var requirements = new DestinationRequirements { RequiredCapabilityIds = new List<string> { "burn", "helipad" } };
			requirements.AppliedRules.Add(new DestinationRule { Name = "Burns" });
			M<INearestDestinationService>().Setup(x => x.SuggestDestinationsAsync(It.Is<DestinationSuggestionRequest>(r =>
					r.DepartmentId == DepartmentId && r.CallType == "Burns" && r.CallPriority == 1 && r.UnitId == null), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DestinationSuggestions
				{
					Mode = DestinationSuggestionModes.SuggestAndFill,
					Requirements = requirements,
					Destinations = new List<NearestDestination>
					{
						new NearestDestination { Poi = poi, DistanceMeters = 5000, TravelSeconds = 600, TravelDistanceMeters = 7200, MeetsRequirements = false,
							MissingCapabilityIds = new List<string> { "helipad" }, Tier = DestinationTiers.Lacking }
					}
				});

			var json = JsonOf(await Build().Suggest(50.8, 3.8, 1, "Burns", CancellationToken.None));

			json["mode"].Value<int>().Should().Be(2);
			json["suggestedPoiId"].Type.Should().Be(JTokenType.Null);
			json["rules"].Values<string>().Should().Equal("Burns");
			json["required"].Values<string>().Should().Equal("Burn Center", "Helipad");
			var row = json["destinations"][0];
			row["poiId"].Value<int>().Should().Be(31);
			row["travelSeconds"].Value<double>().Should().Be(600);
			row["travelDistanceMeters"].Value<double>().Should().Be(7200);
			row["missing"].Values<string>().Should().Equal("Helipad");
		}
	}
}
