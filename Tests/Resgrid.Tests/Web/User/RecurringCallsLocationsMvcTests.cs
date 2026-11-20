using System;
using System.Collections.Generic;
using System.Linq;
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
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Dispatch;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Recurring Calls (M0322): a schedule's free destination and route, round-tripped through the form, copied by "Repeat this call"
	/// and checked on save.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class RecurringCallsLocationsMvcTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "dispatcher-1";
		private const string RoutePlanId = "plan-1";

		private static RecurringCallEditView Form(Action<RecurringCallEditView> configure = null)
		{
			var form = new RecurringCallEditView
			{
				Name = "IFT Mercy to Regional",
				Priority = 0,
				Frequency = (int)RecurringCallFrequencies.Weekly,
				RepeatInterval = 1,
				Days = new List<int> { 2 },
				Times = new List<string> { "08:00" },
				StartsOn = "2026-11-17",
				LeadTimeMinutes = 1440,
				IsActive = true,
				Address = "1 Main St",
				DispatchUserIds = new List<string> { "medic-1" }
			};
			configure?.Invoke(form);
			return form;
		}

		#region Form round trip

		[Test]
		public void The_free_destination_and_route_round_trip_through_the_form()
		{
			var series = new RecurringCall();
			var form = Form(f =>
			{
				f.DestinationAddress = "  Riverside Dialysis ";
				f.DestinationCoordinates = " 39.8 , -105.0 ";
				f.RoutePlanId = " " + RoutePlanId + " ";
			});

			RecurringCallsController.ApplyCallDetails(form, series, null);
			RecurringCallsController.ApplyScheduleDetails(form, series);

			series.DestinationAddress.Should().Be("Riverside Dialysis");
			series.DestinationGeoLocationData.Should().Be("39.8,-105");
			series.RoutePlanId.Should().Be(RoutePlanId);

			var edit = RecurringCallsController.ToEditView(series);
			edit.DestinationAddress.Should().Be("Riverside Dialysis");
			edit.DestinationCoordinates.Should().Be("39.8,-105");
			edit.RoutePlanId.Should().Be(RoutePlanId);
		}

		[Test]
		public void A_destination_poi_clears_the_free_destination()
		{
			var series = new RecurringCall();
			var form = Form(f =>
			{
				f.DestinationPoiId = 31;
				f.DestinationAddress = "Riverside Dialysis";
				f.DestinationCoordinates = "39.8,-105";
			});

			RecurringCallsController.ApplyCallDetails(form, series, null);

			series.DestinationPoiId.Should().Be(31);
			series.DestinationAddress.Should().BeNull();
			series.DestinationGeoLocationData.Should().BeNull();
		}

		[Test]
		public void A_hidden_destination_keeps_the_stored_free_destination_on_edit()
		{
			var policy = new NewCallFieldPolicy { Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.DestinationPoi, Visible = false } } };
			var existing = new RecurringCall { RecurringCallId = 5, DestinationAddress = "Stored", DestinationGeoLocationData = "39.8,-105" };

			RecurringCallsController.ApplyCallDetails(Form(), existing, policy);

			existing.DestinationAddress.Should().Be("Stored");
			existing.DestinationGeoLocationData.Should().Be("39.8,-105");
		}

		[Test]
		public void A_free_destination_satisfies_a_required_destination()
		{
			var policy = new NewCallFieldPolicy
			{
				Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.DestinationPoi, Visible = true, Required = true } }
			};
			var series = new RecurringCall();
			RecurringCallsController.ApplySchedule(Form(), series);
			RecurringCallsController.ApplyCallDetails(Form(f => f.DestinationAddress = "Riverside Dialysis"), series, policy);

			NewCallFieldPolicyValidator.Validate(policy, RecurringCallsController.FieldValues(series)).Should().BeEmpty();
		}

		[Test]
		public void Repeat_this_call_copies_the_free_destination_the_route_and_the_location_poi()
		{
			var model = new RecurringCallEditView();
			var call = new Call
			{
				CallId = 42,
				Number = "26-42",
				Name = "Transfer",
				LoggedOn = new DateTime(2026, 11, 18, 13, 0, 0, DateTimeKind.Utc),
				LocationPoiId = 7,
				DestinationAddress = "Riverside Dialysis",
				DestinationGeoLocationData = "39.8,-105",
				RoutePlanId = RoutePlanId
			};

			RecurringCallsController.CopyFromCall(model, call, new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });

			model.DestinationAddress.Should().Be("Riverside Dialysis");
			model.DestinationCoordinates.Should().Be("39.8,-105");
			model.RoutePlanId.Should().Be(RoutePlanId);
			model.FacilityPoiId.Should().Be(7, "the POI the call started from is where every call it creates starts");
		}

		[Test]
		public void Repeat_this_call_with_a_destination_poi_copies_no_free_destination()
		{
			var model = new RecurringCallEditView();
			var call = new Call
			{
				CallId = 42,
				LoggedOn = new DateTime(2026, 11, 18, 13, 0, 0, DateTimeKind.Utc),
				DestinationPoiId = 31,
				DestinationAddress = "Left over",
				DestinationGeoLocationData = "39.8,-105"
			};

			RecurringCallsController.CopyFromCall(model, call, new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });

			model.DestinationPoiId.Should().Be(31);
			model.DestinationAddress.Should().BeNull();
			model.DestinationCoordinates.Should().BeNull();
		}

		#endregion Form round trip

		#region Save

		private Dictionary<Type, Mock> _mocks;
		private RecurringCall _saved;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_saved = null;

			Localizer<Resgrid.Localization.Areas.User.Dispatch.RecurringCalls>();
			Localizer<Resgrid.Localization.Areas.User.Dispatch.Call>();
			Localizer<Resgrid.Localization.Areas.User.Department.Department>();

			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			M<IAuthorizationService>().Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(true);
			M<ICallRoutesService>().Setup(x => x.GetEligibleRoutePlanAsync(DepartmentId, It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync((CallRoutePlan)null);
			M<ICallRoutesService>().Setup(x => x.GetEligibleRoutePlanAsync(DepartmentId, RoutePlanId, It.IsAny<int?>()))
				.ReturnsAsync(new CallRoutePlan { Plan = new RoutePlan { RoutePlanId = RoutePlanId, DepartmentId = DepartmentId, Name = "Dialysis run" }, Endpoints = new RoutePlanEndpoints() });
			M<IRecurringCallsService>().Setup(x => x.SaveAsync(It.IsAny<RecurringCall>(), UserId, It.IsAny<bool>(), It.IsAny<AuditOrigin>(), It.IsAny<CancellationToken>()))
				.Callback<RecurringCall, string, bool, AuditOrigin, CancellationToken>((series, _, __, ___, ____) => _saved = series)
				.ReturnsAsync((RecurringCall series, string _, bool __, AuditOrigin ___, CancellationToken ____) => new RecurringCallSaveResult { Series = series });
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

		private void Localizer<T>()
		{
			M<IStringLocalizer<T>>().Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
		}

		private RecurringCallsController Build()
		{
			var constructor = typeof(RecurringCallsController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
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
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
				new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create)
			}, "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (RecurringCallsController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		[Test]
		public async Task A_schedule_saves_its_free_destination_and_an_eligible_route()
		{
			var result = await Build().Save(Form(f =>
			{
				f.DestinationAddress = "Riverside Dialysis";
				f.DestinationCoordinates = "39.8,-105";
				f.RoutePlanId = RoutePlanId;
			}), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_saved.DestinationAddress.Should().Be("Riverside Dialysis");
			_saved.DestinationGeoLocationData.Should().Be("39.8,-105");
			_saved.RoutePlanId.Should().Be(RoutePlanId);
		}

		[Test]
		public async Task A_route_the_calls_cannot_follow_is_refused()
		{
			var controller = Build();

			var result = await controller.Save(Form(f => f.RoutePlanId = "archived-plan"), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState[nameof(RecurringCallEditView.RoutePlanId)].Errors.Single().ErrorMessage.Should().Be("ErrorRoutePlanInvalid");
			_saved.Should().BeNull();
		}

		[Test]
		public async Task A_destination_point_that_is_not_a_coordinate_is_refused()
		{
			var controller = Build();

			var result = await controller.Save(Form(f => f.DestinationCoordinates = "39,8;-105"), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState[nameof(RecurringCallEditView.DestinationCoordinates)].Errors.Single().ErrorMessage.Should().Be("ErrorDestinationCoordinatesInvalid");
			_saved.Should().BeNull();
		}

		[Test]
		public async Task An_edit_keeps_a_stored_route_that_can_no_longer_be_followed()
		{
			var stored = new RecurringCall
			{
				RecurringCallId = 5,
				DepartmentId = DepartmentId,
				CreatedByUserId = UserId,
				IsActive = true,
				RoutePlanId = "archived-plan"
			};
			M<IRecurringCallsService>().Setup(x => x.GetByIdAsync(DepartmentId, 5)).ReturnsAsync(stored);

			var result = await Build().Save(Form(f =>
			{
				f.RecurringCallId = 5;
				f.RoutePlanId = "archived-plan";
			}), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_saved.RoutePlanId.Should().Be("archived-plan");
			M<ICallRoutesService>().Verify(x => x.GetEligibleRoutePlanAsync(DepartmentId, "archived-plan", It.IsAny<int?>()), Times.Never);
		}

		private static void PostedForm(RecurringCallsController controller, params string[] keys)
		{
			var request = controller.ControllerContext.HttpContext.Request;
			request.ContentType = "application/x-www-form-urlencoded";
			request.Form = new FormCollection(keys.ToDictionary(k => k, k => new Microsoft.Extensions.Primitives.StringValues("")));
		}

		[Test]
		public async Task A_form_posted_without_the_route_select_keeps_the_schedules_route()
		{
			// The route list could not be read, so the edit form had no route select: the save must not clear the route.
			var stored = new RecurringCall { RecurringCallId = 5, DepartmentId = DepartmentId, CreatedByUserId = UserId, IsActive = true, RoutePlanId = RoutePlanId };
			M<IRecurringCallsService>().Setup(x => x.GetByIdAsync(DepartmentId, 5)).ReturnsAsync(stored);
			var controller = Build();
			PostedForm(controller, nameof(RecurringCallEditView.Name));

			var result = await controller.Save(Form(f => f.RecurringCallId = 5), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_saved.RoutePlanId.Should().Be(RoutePlanId);
		}

		[Test]
		public async Task A_blank_route_selection_still_clears_the_schedules_route()
		{
			var stored = new RecurringCall { RecurringCallId = 5, DepartmentId = DepartmentId, CreatedByUserId = UserId, IsActive = true, RoutePlanId = RoutePlanId };
			M<IRecurringCallsService>().Setup(x => x.GetByIdAsync(DepartmentId, 5)).ReturnsAsync(stored);
			var controller = Build();
			PostedForm(controller, nameof(RecurringCallEditView.Name), nameof(RecurringCallEditView.RoutePlanId));

			var result = await controller.Save(Form(f => f.RecurringCallId = 5), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_saved.RoutePlanId.Should().BeNull();
		}

		[Test]
		public async Task The_schedules_route_stays_offered_when_the_route_list_cannot_be_read()
		{
			M<ICallRoutesService>().Setup(x => x.GetCallRoutePlansAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("database down"));
			M<IRouteService>().Setup(x => x.GetRoutePlanByIdAsync(RoutePlanId)).ThrowsAsync(new InvalidOperationException("database down"));

			var method = typeof(RecurringCallsController).GetMethod("RoutePlanItemsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var items = await (Task<List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>>)method.Invoke(Build(), new object[] { RoutePlanId });

			items.Should().HaveCount(2, "No route, and the schedule's own route");
			items.Single(x => x.Selected).Value.Should().Be(RoutePlanId);
		}

		#endregion Save
	}
}
