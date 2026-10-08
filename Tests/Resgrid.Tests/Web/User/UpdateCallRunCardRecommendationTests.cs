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
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The web Update Call page offers the run card recommendation for what the call still needs (Belgian EMS, 2026-10-07):
	/// what is already on the call counts toward the run card, and the edit form's unsaved priority, type and location win.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class UpdateCallRunCardRecommendationTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "dispatcher-1";
		private const int CallId = 42;

		private Dictionary<Type, Mock> _mocks;
		private DispatchRecommendationRequest _request;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_request = null;

			M<IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.Call>>().Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			M<IStringLocalizer<Resgrid.Localization.Common>>().Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			M<IFeatureToggleService>().Setup(x => x.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(true);
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				Priority = 2,
				Type = "Medical",
				AlarmLevel = 1,
				GeoLocationData = "50.87,3.81"
			});
			M<ICallsService>().Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool _, bool __, bool ___, bool ____, bool _____, bool ______, bool _______, bool ________, bool _________, bool __________) =>
				{
					c.UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 5 }, new CallDispatchUnit { UnitId = 5 } };
					c.Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "crew-1" } };
					return c;
				});
			M<IDispatchRecommendationService>().Setup(x => x.GetRecommendationAsync(It.IsAny<DispatchRecommendationRequest>(), It.IsAny<CancellationToken>()))
				.Callback<DispatchRecommendationRequest, CancellationToken>((r, _) => _request = r)
				.ReturnsAsync(new DispatchRecommendationResult { MatchedRunCardId = 3, MatchedRunCardName = "Cardiac arrest" });
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

		private DispatchController Build()
		{
			var constructor = typeof(DispatchController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
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
			}, "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (DispatchController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private static bool Success(IActionResult result) =>
			(bool)((JsonResult)result).Value.GetType().GetProperty("success")!.GetValue(((JsonResult)result).Value)!;

		[Test]
		public async Task counts_what_the_call_already_has_toward_its_run_card()
		{
			var result = await Build().GetCallDispatchRecommendation(CallId, null, null, null, null);

			Success(result).Should().BeTrue();
			_request.Should().NotBeNull();
			_request.DepartmentId.Should().Be(DepartmentId);
			_request.Priority.Should().Be(2);
			_request.CallTypeName.Should().Be("Medical");
			_request.Latitude.Should().Be(50.87);
			_request.Longitude.Should().Be(3.81);
			_request.TargetAlarmLevel.Should().Be(1);
			_request.CountDispatchedTowardRequirements.Should().BeTrue();
			_request.AlreadyDispatchedUnitIds.Should().Equal(5);
			_request.AlreadyDispatchedUserIds.Should().Equal("crew-1");
		}

		[Test]
		public async Task the_unsaved_form_values_win_over_the_stored_call()
		{
			await Build().GetCallDispatchRecommendation(CallId, 3, "Structure Fire", 51.05, 3.72);

			_request.Priority.Should().Be(3);
			_request.CallTypeName.Should().Be("Structure Fire");
			_request.Latitude.Should().Be(51.05);
			_request.Longitude.Should().Be(3.72);
		}

		[Test]
		public async Task an_impossible_form_location_falls_back_to_the_stored_one()
		{
			await Build().GetCallDispatchRecommendation(CallId, null, " ", 123.0, 3.72);

			_request.CallTypeName.Should().Be("Medical");
			_request.Latitude.Should().Be(50.87);
			_request.Longitude.Should().Be(3.81);
		}

		[Test]
		public async Task nothing_is_looked_up_when_the_department_does_not_use_run_cards()
		{
			M<IFeatureToggleService>().Setup(x => x.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

			var result = await Build().GetCallDispatchRecommendation(CallId, null, null, null, null);

			Success(result).Should().BeFalse();
			_request.Should().BeNull();
		}

		[Test]
		public async Task a_member_who_cannot_edit_the_call_is_refused()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(false);

			var result = await Build().GetCallDispatchRecommendation(CallId, null, null, null, null);

			// The MVC base controller's Unauthorized() redirects to the not-authorized page.
			result.Should().NotBeOfType<JsonResult>();
			_request.Should().BeNull();
		}

		[Test]
		public async Task another_departments_call_is_not_found()
		{
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = 99, Type = "Medical" });

			var result = await Build().GetCallDispatchRecommendation(CallId, null, null, null, null);

			Success(result).Should().BeFalse();
			_request.Should().BeNull();
		}

		[Test]
		public void the_action_needs_call_update_and_is_a_get()
		{
			var method = typeof(DispatchController).GetMethod(nameof(DispatchController.GetCallDispatchRecommendation))!;

			method.GetCustomAttributes(typeof(HttpGetAttribute), false).Should().NotBeEmpty();
			method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
				.Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
				.Select(a => a.Policy).Should().Contain(Resgrid.Providers.Claims.ResgridResources.Call_Update);
		}
	}

	[TestFixture]
	public class DispatchRecommendationRequestForCallInProgressTests
	{
		private static Call CallAt(int alarmLevel) => new Call
		{
			CallId = 1,
			DepartmentId = 4,
			Priority = 1,
			Type = "Fire",
			AlarmLevel = alarmLevel,
			GeoLocationData = "not a point",
			UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 8 } },
			Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "a" }, new CallDispatch { UserId = "" } }
		};

		[TestCase(0, null, 1, true)]
		[TestCase(2, null, 2, true)]
		[TestCase(2, 1, 1, true)]
		[TestCase(2, 3, 3, false)]
		public void fills_the_current_level_after_what_the_call_has_and_previews_higher_ones_in_full(int callLevel, int? requested, int expectedLevel, bool counts)
		{
			var request = DispatchRecommendationRequest.ForCallInProgress(CallAt(callLevel), requested);

			request.TargetAlarmLevel.Should().Be(expectedLevel);
			request.CountDispatchedTowardRequirements.Should().Be(counts);
		}

		[Test]
		public void takes_the_call_and_its_direct_dispatches()
		{
			var request = DispatchRecommendationRequest.ForCallInProgress(CallAt(1));

			request.DepartmentId.Should().Be(4);
			request.CallTypeName.Should().Be("Fire");
			request.Latitude.Should().BeNull("an unparseable location is left out");
			request.AlreadyDispatchedUnitIds.Should().Equal(8);
			request.AlreadyDispatchedUserIds.Should().Equal("a");
		}

		[Test]
		public void needs_a_call() =>
			FluentActions.Invoking(() => DispatchRecommendationRequest.ForCallInProgress(null)).Should().Throw<ArgumentNullException>();
	}
}
