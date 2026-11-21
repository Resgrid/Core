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
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Calls;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The web call forms' start location, destination and route (M0322): a location picked from any of the department's POIs, a
	/// free destination for one that is not a POI, and a pre-defined route, checked as the v4 SaveCall/EditCall check them.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallLocationsMvcTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "dispatcher-1";
		private const int CallId = 42;
		private const int LocationPoiId = 7;
		private const int DestinationPoiId = 31;
		private const string RoutePlanId = "plan-1";

		private Dictionary<Type, Mock> _mocks;
		private Call _saved;
		private Call _stored;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_saved = null;
			_stored = null;

			Localizer<Resgrid.Localization.Areas.User.Dispatch.Call>();
			Localizer<Resgrid.Localization.Common>();
			Localizer<Resgrid.Localization.Areas.User.Department.Department>();

			var department = new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" };
			M<IDepartmentsService>().Setup(x => x.GetDepartmentByUserIdAsync(UserId, It.IsAny<bool>())).ReturnsAsync(department);
			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			M<IDepartmentGroupsService>().Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			M<IUnitsService>().Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>());
			M<ICallsService>().Setup(x => x.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>());
			M<ICallsService>().Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new List<DepartmentCallPriority> { new DepartmentCallPriority { DepartmentCallPriorityId = 0, Name = "Low", IsDefault = true } });
			M<ICallsService>().Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.Callback<Call, CancellationToken>((c, _) => _saved = c)
				.ReturnsAsync((Call c, CancellationToken _) =>
				{
					if (c.CallId == 0)
						c.CallId = CallId;
					return c;
				});
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _stored);
			M<ICallsService>().Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool _, bool __, bool ___, bool ____, bool _____, bool ______, bool _______, bool ________, bool _________, bool __________) => c);
			M<IAuthorizationService>().Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(true);
			M<IProtectedReadService>().Setup(x => x.ResolveForReadAsync(DepartmentId, It.IsAny<Call>(), It.IsAny<string>(), UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, Call c, string __, string ___, CancellationToken ____) => new ProtectedReadResult { Call = c });

			// The location POI (any type) and the route a call may follow; anything else is not the department's.
			M<ICallRoutesService>().Setup(x => x.GetLocationPoiAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync((Poi)null);
			M<ICallRoutesService>().Setup(x => x.GetLocationPoiAsync(DepartmentId, LocationPoiId))
				.ReturnsAsync(new Poi { PoiId = LocationPoiId, Name = "Mercy General", Address = "1 Mercy Way", Latitude = 39.74, Longitude = -104.99 });
			M<ICallRoutesService>().Setup(x => x.GetEligibleRoutePlanAsync(DepartmentId, It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync((CallRoutePlan)null);
			M<ICallRoutesService>().Setup(x => x.GetEligibleRoutePlanAsync(DepartmentId, RoutePlanId, It.IsAny<int?>())).ReturnsAsync(EligiblePlan());

			M<IMappingService>().Setup(x => x.GetDestinationPOIByIdAsync(DepartmentId, DestinationPoiId))
				.ReturnsAsync(new Poi { PoiId = DestinationPoiId, Name = "UZ Gent", Latitude = 51.02, Longitude = 3.73 });
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

		private static CallRoutePlan EligiblePlan() => new CallRoutePlan
		{
			Plan = new RoutePlan { RoutePlanId = RoutePlanId, DepartmentId = DepartmentId, Name = "Dialysis run", RouteStatus = (int)RouteStatus.Active, RouteColor = "#ff0000" },
			Endpoints = new RoutePlanEndpoints
			{
				Start = new RoutePlanEndpoint { Latitude = 39.70, Longitude = -104.90, Name = "Station 1" },
				End = new RoutePlanEndpoint { Latitude = 39.80, Longitude = -105.00, Name = "Riverside Dialysis" },
				StopsCount = 2
			}
		};

		private DispatchController Build(params Claim[] claims)
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
			}.Concat(claims), "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (DispatchController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private static FormCollection Form(params (string Key, string Value)[] values) =>
			new FormCollection(values.ToDictionary(x => x.Key, x => new StringValues(x.Value)));

		private static NewCallView NewCallForm(Action<Call> call = null)
		{
			var model = new NewCallView
			{
				Call = new Call { Name = "Transfer", NatureOfCall = "Dialysis transfer", Address = "1 Main St" },
				Latitude = "39.7392",
				Longitude = "-104.9903"
			};
			call?.Invoke(model.Call);
			return model;
		}

		private void PolicyRequires(string key)
		{
			M<IDepartmentSettingsService>().Setup(x => x.GetNewCallFieldPolicyAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new NewCallFieldPolicy
			{
				Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = key, Visible = true, Required = true } }
			});
		}

		private void PolicyHides(string key)
		{
			M<IDepartmentSettingsService>().Setup(x => x.GetNewCallFieldPolicyAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new NewCallFieldPolicy
			{
				Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = key, Visible = false } }
			});
		}

		#region New call

		[Test]
		public async Task A_new_call_keeps_its_location_poi_route_and_free_destination()
		{
			var model = NewCallForm(c =>
			{
				c.LocationPoiId = LocationPoiId;
				c.RoutePlanId = " " + RoutePlanId + " ";
				c.DestinationAddress = "  Riverside Dialysis ";
				c.DestinationW3W = " index.home.raft ";
				// Model-bound but never posted by the form: only the typed pair counts.
				c.DestinationGeoLocationData = "1,1";
			});
			model.DestinationLatitude = " 39.8 ";
			model.DestinationLongitude = "-105.0";

			var result = await Build().NewCall(model, Form(), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_saved.Should().NotBeNull();
			_saved.LocationPoiId.Should().Be(LocationPoiId);
			_saved.RoutePlanId.Should().Be(RoutePlanId);
			_saved.DestinationPoiId.Should().BeNull();
			_saved.DestinationAddress.Should().Be("Riverside Dialysis");
			_saved.DestinationGeoLocationData.Should().Be("39.8,-105");
			_saved.DestinationW3W.Should().Be("index.home.raft");
		}

		[Test]
		public async Task A_destination_poi_clears_the_free_destination()
		{
			var model = NewCallForm(c =>
			{
				c.DestinationPoiId = DestinationPoiId;
				c.DestinationAddress = "Somewhere else";
				c.DestinationW3W = "a.b.c";
			});
			model.DestinationLatitude = "39.8";
			model.DestinationLongitude = "-105.0";

			(await Build().NewCall(model, Form(), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			_saved.DestinationPoiId.Should().Be(DestinationPoiId);
			_saved.DestinationAddress.Should().BeNull();
			_saved.DestinationGeoLocationData.Should().BeNull();
			_saved.DestinationW3W.Should().BeNull();
		}

		[Test]
		public async Task A_location_poi_of_another_department_is_refused()
		{
			var controller = Build();

			var result = await controller.NewCall(NewCallForm(c => c.LocationPoiId = 999), Form(), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState["Call.LocationPoiId"].Errors.Single().ErrorMessage.Should().Be("InvalidLocationPoi");
			_saved.Should().BeNull();
		}

		[Test]
		public async Task A_route_the_call_cannot_follow_is_refused()
		{
			var controller = Build();

			var result = await controller.NewCall(NewCallForm(c => c.RoutePlanId = "archived-plan"), Form(), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState["Call.RoutePlanId"].Errors.Single().ErrorMessage.Should().Be("InvalidCallRoute");
			_saved.Should().BeNull();
		}

		[TestCase("50,85", "4,35")]
		[TestCase("91", "4.35")]
		[TestCase("50.85", "")]
		public async Task A_destination_point_that_is_not_an_invariant_coordinate_is_refused(string latitude, string longitude)
		{
			var controller = Build();
			var model = NewCallForm();
			model.DestinationLatitude = latitude;
			model.DestinationLongitude = longitude;

			var result = await controller.NewCall(model, Form(), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState[nameof(NewCallView.DestinationLatitude)].Errors.Single().ErrorMessage.Should().Be("InvalidDestinationCoordinates");
			_saved.Should().BeNull();
		}

		[Test]
		public async Task A_free_destination_satisfies_a_required_destination()
		{
			PolicyRequires(NewCallFieldKeys.DestinationPoi);
			var model = NewCallForm(c => c.DestinationAddress = "Riverside Dialysis");

			(await Build().NewCall(model, Form(), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			_saved.DestinationAddress.Should().Be("Riverside Dialysis");
		}

		[Test]
		public async Task A_required_destination_is_still_required_without_a_poi_or_a_free_destination()
		{
			PolicyRequires(NewCallFieldKeys.DestinationPoi);
			var controller = Build();

			(await controller.NewCall(NewCallForm(), Form(), CancellationToken.None)).Should().BeOfType<ViewResult>();

			controller.ModelState.Keys.Should().Contain("NewCallField_" + NewCallFieldKeys.DestinationPoi);
		}

		[Test]
		public async Task A_hidden_destination_saves_no_free_destination()
		{
			PolicyHides(NewCallFieldKeys.DestinationPoi);
			var model = NewCallForm(c => c.DestinationAddress = "Crafted");
			model.DestinationLatitude = "39.8";
			model.DestinationLongitude = "-105";

			(await Build().NewCall(model, Form(), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			_saved.DestinationAddress.Should().BeNull();
			_saved.DestinationGeoLocationData.Should().BeNull();
		}

		[Test]
		public async Task A_pending_call_keeps_its_location_poi_route_and_free_destination()
		{
			var model = NewCallForm(c =>
			{
				c.LocationPoiId = LocationPoiId;
				c.RoutePlanId = RoutePlanId;
				c.DestinationAddress = "Riverside Dialysis";
			});
			model.SaveAsPending = true;

			var result = await Build().NewCall(model, Form(), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("PendingCalls");
			_saved.State.Should().Be((int)CallStates.Pending);
			_saved.LocationPoiId.Should().Be(LocationPoiId);
			_saved.RoutePlanId.Should().Be(RoutePlanId);
			_saved.DestinationAddress.Should().Be("Riverside Dialysis");
		}

		[Test]
		public async Task The_new_call_form_offers_the_departments_pois_and_the_routes_a_call_can_follow()
		{
			M<IMappingService>().Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi>
			{
				new Poi { PoiId = LocationPoiId, Name = "Mercy General", Address = "1 Mercy Way", Latitude = 39.74, Longitude = -104.99, Type = new PoiType { Name = "Hospitals" } },
				new Poi { PoiId = 8, Name = "Hydrant 12", Latitude = 39.75, Longitude = -104.98, Type = new PoiType { Name = "Hydrants" } }
			});
			M<ICallRoutesService>().Setup(x => x.GetCallRoutePlansAsync(DepartmentId)).ReturnsAsync(new List<CallRoutePlan> { EligiblePlan() });

			var model = (await Build(new Claim(Resgrid.Providers.Claims.ResgridClaimTypes.Resources.Route, Resgrid.Providers.Claims.ResgridClaimTypes.Actions.View)).NewCall())
				.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<NewCallView>().Which;

			model.LocationPois.Select(x => x.PoiId).Should().BeEquivalentTo(new[] { LocationPoiId, 8 }, "any POI type can be the call location");
			model.LocationPois.Single(x => x.PoiId == LocationPoiId).Address.Should().Be("1 Mercy Way");
			model.RoutePlans.Should().ContainSingle(x => x.RoutePlanId == RoutePlanId && x.StartName == "Station 1" && x.EndName == "Riverside Dialysis" && x.StopsCount == 2);
			model.CanViewRoutes.Should().BeTrue();
			model.ShowsLocationPoi().Should().BeTrue();
			model.ShowsRoute().Should().BeTrue();
		}

		#endregion New call

		#region Update call

		private void Stored(Action<Call> configure = null)
		{
			_stored = new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				State = (int)CallStates.Active,
				Name = "Transfer",
				Address = "1 Main St",
				GeoLocationData = "39.7392,-104.9903",
				W3W = "filled.count.soap",
				DestinationAddress = "Stored destination",
				DestinationGeoLocationData = "39.8,-105",
				DestinationW3W = "stored.dest.words",
				LocationPoiId = LocationPoiId,
				RoutePlanId = "archived-plan",
				Dispatches = new List<CallDispatch>(),
				GroupDispatches = new List<CallDispatchGroup>(),
				UnitDispatches = new List<CallDispatchUnit>(),
				RoleDispatches = new List<CallDispatchRole>(),
				References = new List<CallReference>(),
				Protocols = new List<CallProtocol>(),
				Contacts = new List<CallContact>()
			};
			configure?.Invoke(_stored);
		}

		private static UpdateCallView UpdateCallForm(Action<Call> call = null)
		{
			var model = new UpdateCallView
			{
				Call = new Call { CallId = CallId, Name = "Transfer", NatureOfCall = "Dialysis transfer", Address = "1 Main St" },
				Latitude = "39.7392",
				Longitude = "-104.9903"
			};
			call?.Invoke(model.Call);
			return model;
		}

		[Test]
		public async Task Opening_an_edit_fills_the_what3words_and_the_free_destination_point()
		{
			Stored();

			var model = (await Build().UpdateCall(CallId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<UpdateCallView>().Which;

			model.What3Word.Should().Be("filled.count.soap", "an edit with the field shown used to clear the call's words");
			model.DestinationLatitude.Should().Be("39.8");
			model.DestinationLongitude.Should().Be("-105");
		}

		[Test]
		public async Task A_hidden_destination_keeps_the_stored_poi_and_free_destination()
		{
			PolicyHides(NewCallFieldKeys.DestinationPoi);
			Stored();
			var model = UpdateCallForm(c => c.DestinationAddress = null);

			var result = await Build().UpdateCall(model, Form(), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_saved.DestinationAddress.Should().Be("Stored destination");
			_saved.DestinationGeoLocationData.Should().Be("39.8,-105");
			_saved.DestinationW3W.Should().Be("stored.dest.words");
		}

		[Test]
		public async Task Inputs_left_off_the_form_keep_the_stored_location_poi_and_route()
		{
			Stored();

			// No Call.LocationPoiId / Call.RoutePlanId keys: the pickers were not on the form.
			(await Build().UpdateCall(UpdateCallForm(), Form(), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			_saved.LocationPoiId.Should().Be(LocationPoiId);
			_saved.RoutePlanId.Should().Be("archived-plan");
		}

		[Test]
		public async Task An_edit_writes_the_shown_destination_location_poi_and_a_changed_route()
		{
			Stored(c => c.RoutePlanId = null);
			var model = UpdateCallForm(c =>
			{
				c.LocationPoiId = null;
				c.RoutePlanId = RoutePlanId;
				c.DestinationAddress = "New destination";
				c.DestinationW3W = "";
			});
			model.DestinationLatitude = "51.05";
			model.DestinationLongitude = "3.72";

			var result = await Build().UpdateCall(model, Form(("Call.LocationPoiId", ""), ("Call.RoutePlanId", RoutePlanId)), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_saved.LocationPoiId.Should().BeNull("the picker was on the form and posted none");
			_saved.RoutePlanId.Should().Be(RoutePlanId);
			_saved.DestinationAddress.Should().Be("New destination");
			_saved.DestinationGeoLocationData.Should().Be("51.05,3.72");
			_saved.DestinationW3W.Should().BeNull();
		}

		[Test]
		public async Task An_unchanged_route_that_can_no_longer_be_followed_is_kept()
		{
			Stored();
			var controller = Build();

			var result = await controller.UpdateCall(UpdateCallForm(c => c.RoutePlanId = "archived-plan"), Form(("Call.RoutePlanId", "archived-plan")), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_saved.RoutePlanId.Should().Be("archived-plan");
			M<ICallRoutesService>().Verify(x => x.GetEligibleRoutePlanAsync(DepartmentId, "archived-plan", It.IsAny<int?>()), Times.Never);
		}

		[Test]
		public async Task A_changed_route_the_call_cannot_follow_is_refused()
		{
			Stored(c => c.RoutePlanId = null);
			var controller = Build();

			var result = await controller.UpdateCall(UpdateCallForm(c => c.RoutePlanId = "other-department-plan"), Form(("Call.RoutePlanId", "other-department-plan")),
				CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState["Call.RoutePlanId"].Errors.Single().ErrorMessage.Should().Be("InvalidCallRoute");
			_saved.Should().BeNull();
		}

		[Test]
		public async Task A_cleared_route_is_cleared()
		{
			Stored(c => c.RoutePlanId = RoutePlanId);

			(await Build().UpdateCall(UpdateCallForm(c => c.RoutePlanId = ""), Form(("Call.RoutePlanId", "")), CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>();

			_saved.RoutePlanId.Should().BeNull();
		}

		[Test]
		public async Task An_edit_refuses_a_location_poi_of_another_department()
		{
			Stored();
			var controller = Build();

			var result = await controller.UpdateCall(UpdateCallForm(c => c.LocationPoiId = 999), Form(("Call.LocationPoiId", "999")), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState["Call.LocationPoiId"].Errors.Single().ErrorMessage.Should().Be("InvalidLocationPoi");
			_saved.Should().BeNull();
		}

		[Test]
		public async Task A_re_rendered_edit_keeps_the_stored_calls_department_for_its_panels()
		{
			Stored();
			var controller = Build();

			// The form does not post the department; the intake conversation and destination advice panels look the call up by it.
			var result = await controller.UpdateCall(UpdateCallForm(c => c.LocationPoiId = 999), Form(("Call.LocationPoiId", "999")), CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<UpdateCallView>().Which;
			model.Call.DepartmentId.Should().Be(DepartmentId);
			model.Call.LocationPoiId.Should().Be(999, "the posted values stay for the dispatcher to correct");
		}

		[Test]
		public async Task Opening_an_edit_records_what_the_pickers_opened_with()
		{
			Stored();
			M<IMappingService>().Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<Poi> { new Poi { PoiId = LocationPoiId, Name = "Mercy General", Latitude = 39.74, Longitude = -104.99 } });

			var model = (await Build().UpdateCall(CallId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<UpdateCallView>().Which;

			model.OriginalLocationPoiId.Should().Be(LocationPoiId.ToString());
			model.OriginalRoutePlanId.Should().Be("archived-plan", "the call's own route stays offered and selected");
		}

		[Test]
		public async Task The_calls_route_stays_selected_when_its_name_cannot_be_read()
		{
			Stored();
			M<IRouteService>().Setup(x => x.GetRoutePlanByIdAsync("archived-plan")).ThrowsAsync(new InvalidOperationException("database down"));

			var model = (await Build().UpdateCall(CallId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<UpdateCallView>().Which;

			var option = model.RoutePlans.Should().ContainSingle(x => x.Selected).Which;
			option.RoutePlanId.Should().Be("archived-plan");
			option.IsAvailable.Should().BeFalse();
			model.OriginalRoutePlanId.Should().Be("archived-plan");
		}

		[Test]
		public async Task A_route_picker_left_as_it_opened_keeps_the_route_another_editor_set()
		{
			// Opened on "archived-plan"; another editor saved plan-1 meanwhile; this editor only changed another field.
			Stored(c => c.RoutePlanId = RoutePlanId);

			(await Build().UpdateCall(UpdateCallForm(c => c.RoutePlanId = "archived-plan"),
					Form(("Call.RoutePlanId", "archived-plan"), ("OriginalRoutePlanId", "archived-plan")), CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>();

			_saved.RoutePlanId.Should().Be(RoutePlanId);
		}

		[Test]
		public async Task The_save_reads_the_call_again_so_a_route_saved_while_the_form_was_checked_is_kept()
		{
			// PR #588 round 2: the POST read the call once, before the form lookups, and saved that copy. Another editor's route saved during
			// those lookups was then written back over by this editor's unchanged picker.
			Stored(c => c.RoutePlanId = RoutePlanId);
			var reads = 0;
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>()))
				.ReturnsAsync(() => ++reads == 1 ? new Call { CallId = CallId, DepartmentId = DepartmentId, RoutePlanId = "archived-plan" } : _stored);

			(await Build().UpdateCall(UpdateCallForm(c => c.RoutePlanId = "archived-plan"),
					Form(("Call.RoutePlanId", "archived-plan"), ("OriginalRoutePlanId", "archived-plan")), CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>();

			reads.Should().BeGreaterThan(1);
			_saved.Should().BeSameAs(_stored, "the save writes the call as stored at the point of use");
			_saved.RoutePlanId.Should().Be(RoutePlanId);
		}

		[Test]
		public async Task A_route_this_editor_changed_or_removed_is_still_written()
		{
			Stored(c => c.RoutePlanId = "archived-plan");
			(await Build().UpdateCall(UpdateCallForm(c => c.RoutePlanId = RoutePlanId),
					Form(("Call.RoutePlanId", RoutePlanId), ("OriginalRoutePlanId", "archived-plan")), CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>();
			_saved.RoutePlanId.Should().Be(RoutePlanId);

			Stored(c => c.RoutePlanId = RoutePlanId);
			(await Build().UpdateCall(UpdateCallForm(c => c.RoutePlanId = ""),
					Form(("Call.RoutePlanId", ""), ("OriginalRoutePlanId", RoutePlanId)), CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>();
			_saved.RoutePlanId.Should().BeNull();
		}

		[Test]
		public async Task A_location_poi_the_picker_could_not_show_is_kept_by_an_unrelated_save()
		{
			// The stored POI was not among the picker's options, so it opened (and posted) blank.
			Stored();

			(await Build().UpdateCall(UpdateCallForm(c => c.LocationPoiId = null),
					Form(("Call.LocationPoiId", ""), ("OriginalLocationPoiId", "")), CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>();

			_saved.LocationPoiId.Should().Be(LocationPoiId);
		}

		[Test]
		public async Task A_location_poi_the_picker_showed_can_still_be_removed()
		{
			Stored();

			(await Build().UpdateCall(UpdateCallForm(c => c.LocationPoiId = null),
					Form(("Call.LocationPoiId", ""), ("OriginalLocationPoiId", LocationPoiId.ToString())), CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>();

			_saved.LocationPoiId.Should().BeNull();
		}

		[TestCase(null, "a", "a", false)]
		[TestCase("a", "a", "b", false)]
		[TestCase("b", "a", "b", true)]
		[TestCase("", "a", "a", true)]
		[TestCase(" A ", "a", "b", false)]
		public void A_picker_is_changed_only_when_it_posts_something_else_than_it_opened_with(string posted, string original, string stored, bool changed)
		{
			var values = new Dictionary<string, StringValues> { ["Original"] = original };
			if (posted != null)
				values["Field"] = posted;

			CallLocationsEdit.IsChanged(new FormCollection(values), "Field", "Original", stored).Should().Be(changed);
		}

		[Test]
		public void An_older_form_without_the_original_compares_with_the_stored_value()
		{
			CallLocationsEdit.IsChanged(Form(("Field", "a")), "Field", "Original", "a").Should().BeFalse();
			CallLocationsEdit.IsChanged(Form(("Field", "b")), "Field", "Original", "a").Should().BeTrue();
		}

		#endregion Update call

		#region Call page

		private async Task<CallLocationDetails> CallLocationDetailsAsync(Call call)
		{
			var method = typeof(DispatchController).GetMethod("BuildCallLocationDetailsAsync",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var task = (Task<CallLocationDetails>)method.Invoke(Build(), new object[] { call, new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" }, new List<Unit>() });
			return await task;
		}

		[Test]
		public async Task The_route_runs_of_a_call_show_after_its_route_was_cleared()
		{
			M<IRouteService>().Setup(x => x.GetInstancesForCallAsync(DepartmentId, CallId)).ReturnsAsync(new List<RouteInstance>
			{
				new RouteInstance { RouteInstanceId = "run-1", RoutePlanId = RoutePlanId, DepartmentId = DepartmentId, UnitId = 3, Status = (int)RouteInstanceStatus.Completed }
			});

			var details = await CallLocationDetailsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId, RoutePlanId = null });

			details.HasRoute.Should().BeFalse();
			details.RouteRuns.Should().ContainSingle(x => x.RouteInstanceId == "run-1");
			details.ShowsRouteRuns.Should().BeTrue();
			new CallLocationDetails().ShowsRouteRuns.Should().BeFalse();
		}

		[Test]
		public async Task A_failed_location_poi_lookup_leaves_the_route_and_its_runs()
		{
			M<ICallRoutesService>().Setup(x => x.GetLocationPoiAsync(DepartmentId, LocationPoiId)).ThrowsAsync(new InvalidOperationException("database down"));
			M<IRouteService>().Setup(x => x.GetRoutePlanByIdAsync(RoutePlanId)).ReturnsAsync(new RoutePlan { RoutePlanId = RoutePlanId, DepartmentId = DepartmentId, Name = "Dialysis run" });
			M<IRouteService>().Setup(x => x.GetInstancesForCallAsync(DepartmentId, CallId)).ReturnsAsync(new List<RouteInstance>
			{
				new RouteInstance { RouteInstanceId = "run-1", RoutePlanId = RoutePlanId, DepartmentId = DepartmentId, UnitId = 3 }
			});

			var details = await CallLocationDetailsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId, LocationPoiId = LocationPoiId, RoutePlanId = RoutePlanId });

			details.LocationPoiName.Should().BeNull();
			details.RoutePlanName.Should().Be("Dialysis run");
			details.RouteRuns.Should().ContainSingle();
		}

		#endregion Call page

		#region Route preview

		[Test]
		public async Task The_route_preview_draws_an_eligible_plan_with_numbered_stops()
		{
			M<IRouteService>().Setup(x => x.GetRouteStopsForPlanAsync(RoutePlanId)).ReturnsAsync(new List<RouteStop>
			{
				new RouteStop { RouteStopId = "b", RoutePlanId = RoutePlanId, StopOrder = 2, Name = "Second", Latitude = 39.76m, Longitude = -104.95m },
				new RouteStop { RouteStopId = "a", RoutePlanId = RoutePlanId, StopOrder = 1, Name = "First", Latitude = 39.75m, Longitude = -104.94m },
				new RouteStop { RouteStopId = "c", RoutePlanId = RoutePlanId, StopOrder = 3, Name = "Gone", Latitude = 39.77m, Longitude = -104.96m, IsDeleted = true }
			});

			var json = (await Build().GetCallRoutePreview(RoutePlanId)).Should().BeOfType<JsonResult>().Which.Value;

			((bool)json.GetType().GetProperty("success").GetValue(json)).Should().BeTrue();
			var stops = ((System.Collections.IEnumerable)json.GetType().GetProperty("stops").GetValue(json)).Cast<object>().ToList();
			stops.Select(x => (string)x.GetType().GetProperty("name").GetValue(x)).Should().Equal("First", "Second");
			stops.Select(x => (int)x.GetType().GetProperty("order").GetValue(x)).Should().Equal(1, 2);
			json.GetType().GetProperty("start").GetValue(json).Should().NotBeNull();
		}

		[Test]
		public async Task The_route_preview_refuses_a_plan_the_call_cannot_follow()
		{
			var json = (await Build().GetCallRoutePreview("archived-plan")).Should().BeOfType<JsonResult>().Which.Value;

			((bool)json.GetType().GetProperty("success").GetValue(json)).Should().BeFalse();
			M<IRouteService>().Verify(x => x.GetRouteStopsForPlanAsync(It.IsAny<string>()), Times.Never);
		}

		#endregion Route preview
	}
}
