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
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Calls;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Destination capabilities and availability on the web (Belgian EMS request): map administrators manage the department's
	/// POI capabilities and tick them on each POI; dispatchers (and map administrators) record a destination's availability;
	/// the call form's destination options carry both. Every id posted is checked against the caller's department.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class PoiCapabilitiesMvcTests
	{
		private const int DepartmentId = 12;
		private const int OtherDepartmentId = 99;
		private const string UserId = "dispatcher-1";
		private const int TypeId = 5;
		private const int HospitalId = 31;

		private static readonly Claim MapAdmin = new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);
		private static readonly Claim CallCreator = new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create);

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			Localizer<Resgrid.Localization.Areas.User.Mapping.Mapping>();
			Localizer<Resgrid.Localization.Areas.User.Dispatch.Call>();
			Localizer<Resgrid.Localization.Common>();

			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "ABCD" });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentByUserIdAsync(UserId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "ABCD" });
			M<IMappingService>().Setup(x => x.GetPoiCapabilitiesForDepartmentAsync(DepartmentId)).ReturnsAsync(() => Capabilities());
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

		private Mock<IMappingService> Mapping => M<IMappingService>();

		/// <summary>The department's capabilities (a fresh list each time, as the service returns).</summary>
		private static List<PoiCapability> Capabilities() => new List<PoiCapability>
		{
			new PoiCapability { PoiCapabilityId = "cath", DepartmentId = DepartmentId, Name = "Cath Lab", Sort = 1 },
			new PoiCapability { PoiCapabilityId = "stroke", DepartmentId = DepartmentId, Name = "Stroke Center", Sort = 2 },
			new PoiCapability { PoiCapabilityId = "trauma", DepartmentId = DepartmentId, Name = "Trauma Center", Sort = 3 }
		};

		/// <summary>Builds a controller from loose mocks (the configured ones where a test set them up) and a signed-in member.</summary>
		private T Build<T>(params Claim[] claims) where T : Controller
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
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

			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		/// <summary>A destination POI of type 5 ("Hospitals"), as GetDestinationPOIByIdAsync returns it (department-scoped).</summary>
		private Poi Hospital(PoiAvailability availability = null, params string[] capabilityIds)
		{
			var type = new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId, Name = "Hospitals", IsDestination = true };
			var poi = new Poi { PoiId = HospitalId, PoiTypeId = TypeId, Name = "UZ Gent", Address = "Corneel Heymanslaan 10", Type = type };
			Mapping.Setup(x => x.GetDestinationPOIByIdAsync(DepartmentId, HospitalId)).ReturnsAsync(poi);
			Mapping.Setup(x => x.PopulatePoiDetailsAsync(DepartmentId, It.IsAny<IEnumerable<Poi>>()))
				.Callback((int _, IEnumerable<Poi> pois) =>
				{
					foreach (var p in pois.Where(p => p.PoiId == HospitalId))
					{
						p.CapabilityIds = capabilityIds.ToList();
						p.Capabilities = Capabilities().Where(c => capabilityIds.Contains(c.PoiCapabilityId)).Select(c => c.Name).ToList();
						p.Availability = availability;
					}
				})
				.Returns(Task.CompletedTask);
			return poi;
		}

		#region Anti-forgery

		[TestCase("SavePoiCapability")]
		[TestCase("DeletePoiCapability")]
		[TestCase("SetPoiAvailability")]
		public void State_changing_actions_are_post_with_antiforgery(string action)
		{
			var posts = typeof(MappingController).GetMethods().Where(m => m.Name == action && m.GetCustomAttribute<HttpPostAttribute>() != null).ToList();

			posts.Should().NotBeEmpty();
			posts.Should().OnlyContain(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null);
			if (action != "SetPoiAvailability")
				typeof(MappingController).GetMethods().Where(m => m.Name == action).Should().OnlyContain(m => m.GetCustomAttribute<HttpGetAttribute>() == null, "it changes state");
		}

		#endregion Anti-forgery

		#region Capabilities page

		[Test]
		public async Task The_capabilities_page_is_for_map_administrators_only()
		{
			var dispatcher = Build<MappingController>(CallCreator);

			ShouldBeRefused(await dispatcher.PoiCapabilities());
			ShouldBeRefused(await dispatcher.SavePoiCapability(null, "Cath Lab", null, CancellationToken.None));
			ShouldBeRefused(await dispatcher.DeletePoiCapability("cath", CancellationToken.None));

			Mapping.Verify(x => x.SavePoiCapabilityAsync(It.IsAny<PoiCapability>(), It.IsAny<CancellationToken>()), Times.Never);
			Mapping.Verify(x => x.DeletePoiCapabilityAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_capabilities_page_lists_them_with_how_many_POIs_offer_each()
		{
			var pois = new List<Poi> { new Poi { PoiId = 1 }, new Poi { PoiId = 2 }, new Poi { PoiId = 3 } };
			Mapping.Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(pois);
			Mapping.Setup(x => x.PopulatePoiDetailsAsync(DepartmentId, pois)).Callback(() =>
			{
				pois[0].CapabilityIds = new List<string> { "cath", "trauma" };
				pois[1].CapabilityIds = new List<string> { "cath" };
				pois[2].CapabilityIds = new List<string>();
			}).Returns(Task.CompletedTask);

			var model = (await Build<MappingController>(MapAdmin).PoiCapabilities()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<PoiCapabilitiesView>().Which;

			model.Capabilities.Select(c => c.Name).Should().Equal("Cath Lab", "Stroke Center", "Trauma Center");
			model.PoiCounts["cath"].Should().Be(2);
			model.PoiCounts["trauma"].Should().Be(1);
			model.PoiCounts.ContainsKey("stroke").Should().BeFalse();
		}

		[Test]
		public async Task Adding_a_capability_always_creates_one_of_the_callers_department()
		{
			var controller = Build<MappingController>(MapAdmin);

			var result = await controller.SavePoiCapability(null, "  Burns Unit  ", null, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("PoiCapabilities");
			Mapping.Verify(x => x.SavePoiCapabilityAsync(It.Is<PoiCapability>(c => c.PoiCapabilityId == null && c.DepartmentId == DepartmentId && c.Name == "Burns Unit" && c.Sort == 0),
				It.IsAny<CancellationToken>()), Times.Once);
			controller.TempData["PoiCapabilitiesMessage"].Should().Be("PoiCapabilitySaved");
		}

		[Test]
		public async Task Renaming_and_resorting_changes_only_the_departments_capability()
		{
			await Build<MappingController>(MapAdmin).SavePoiCapability("cath", "Cardiac Cath Lab", 7, CancellationToken.None);

			Mapping.Verify(x => x.SavePoiCapabilityAsync(It.Is<PoiCapability>(c => c.PoiCapabilityId == "cath" && c.DepartmentId == DepartmentId && c.Name == "Cardiac Cath Lab" && c.Sort == 7),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Another_departments_capability_cannot_be_renamed_or_deleted()
		{
			var controller = Build<MappingController>(MapAdmin);

			(await controller.SavePoiCapability("foreign-capability", "Mine now", 1, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			(await controller.DeletePoiCapability("foreign-capability", CancellationToken.None)).Should().BeOfType<NotFoundResult>();

			Mapping.Verify(x => x.SavePoiCapabilityAsync(It.IsAny<PoiCapability>(), It.IsAny<CancellationToken>()), Times.Never);
			Mapping.Verify(x => x.DeletePoiCapabilityAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("   ", "PoiCapabilityNameRequired")]
		[TestCase("cath lab", "PoiCapabilityNameExists")]
		public async Task A_blank_or_duplicate_name_is_refused_with_a_message(string name, string error)
		{
			var controller = Build<MappingController>(MapAdmin);

			(await controller.SavePoiCapability(null, name, null, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			controller.TempData["PoiCapabilitiesError"].Should().Be(error);
			Mapping.Verify(x => x.SavePoiCapabilityAsync(It.IsAny<PoiCapability>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_name_longer_than_the_column_is_refused()
		{
			var controller = Build<MappingController>(MapAdmin);

			await controller.SavePoiCapability(null, new string('x', PoiCapability.MaxNameLength + 1), null, CancellationToken.None);

			controller.TempData["PoiCapabilitiesError"].Should().Be("PoiCapabilityNameRequired");
			Mapping.Verify(x => x.SavePoiCapabilityAsync(It.IsAny<PoiCapability>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Keeping_a_capabilitys_own_name_while_resorting_is_not_a_duplicate()
		{
			var controller = Build<MappingController>(MapAdmin);

			await controller.SavePoiCapability("cath", "Cath Lab", 9, CancellationToken.None);

			Mapping.Verify(x => x.SavePoiCapabilityAsync(It.Is<PoiCapability>(c => c.PoiCapabilityId == "cath" && c.Sort == 9), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_save_the_service_refuses_is_a_page_message()
		{
			Mapping.Setup(x => x.SavePoiCapabilityAsync(It.IsAny<PoiCapability>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("A capability named \"Burns\" already exists."));
			var controller = Build<MappingController>(MapAdmin);

			(await controller.SavePoiCapability(null, "Burns", null, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			controller.TempData["PoiCapabilitiesError"].Should().Be("PoiCapabilitySaveFailed");
		}

		[Test]
		public async Task Deleting_a_capability_is_scoped_to_the_department()
		{
			Mapping.Setup(x => x.GetPoiCapabilityByIdAsync(DepartmentId, "cath")).ReturnsAsync(Capabilities()[0]);
			var controller = Build<MappingController>(MapAdmin);

			(await controller.DeletePoiCapability("cath", CancellationToken.None)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("PoiCapabilities");

			Mapping.Verify(x => x.DeletePoiCapabilityAsync(DepartmentId, "cath", It.IsAny<CancellationToken>()), Times.Once);
			controller.TempData["PoiCapabilitiesMessage"].Should().Be("PoiCapabilityDeleted");
		}

		#endregion Capabilities page

		#region Add / edit POI

		[Test]
		public async Task Add_POI_offers_the_departments_capabilities()
		{
			Mapping.Setup(x => x.GetTypeByIdAsync(TypeId)).ReturnsAsync(new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId });

			var model = (await Build<MappingController>(MapAdmin).AddPOI(TypeId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddPOIView>().Which;

			model.Capabilities.Select(c => c.PoiCapabilityId).Should().Equal("cath", "stroke", "trauma");
			model.SelectedCapabilityIds.Should().BeEmpty();
		}

		[Test]
		public async Task Adding_a_POI_tags_the_saved_POI_with_the_ticked_capabilities()
		{
			Mapping.Setup(x => x.GetTypeByIdAsync(TypeId)).ReturnsAsync(new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId });
			Mapping.Setup(x => x.SavePOIAsync(It.IsAny<Poi>(), It.IsAny<CancellationToken>())).ReturnsAsync((Poi p, CancellationToken _) => { p.PoiId = 501; return p; });

			await Build<MappingController>(MapAdmin).AddPOI(new AddPOIView { TypeId = TypeId, Poi = new Poi { Name = "AZ Sint-Lucas" }, SelectedCapabilityIds = new List<string> { "cath", "trauma" } }, CancellationToken.None);

			Mapping.Verify(x => x.SetPoiCapabilitiesAsync(DepartmentId, 501, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "cath", "trauma" })), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Adding_a_POI_without_ticked_capabilities_writes_none()
		{
			Mapping.Setup(x => x.GetTypeByIdAsync(TypeId)).ReturnsAsync(new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId });
			Mapping.Setup(x => x.SavePOIAsync(It.IsAny<Poi>(), It.IsAny<CancellationToken>())).ReturnsAsync((Poi p, CancellationToken _) => { p.PoiId = 501; return p; });

			await Build<MappingController>(MapAdmin).AddPOI(new AddPOIView { TypeId = TypeId, Poi = new Poi { Name = "Depot" } }, CancellationToken.None);

			Mapping.Verify(x => x.SetPoiCapabilitiesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Edit_POI_ticks_the_capabilities_the_POI_offers()
		{
			Mapping.Setup(x => x.GetPOIByIdAsync(HospitalId)).ReturnsAsync(new Poi { PoiId = HospitalId, PoiTypeId = TypeId, Name = "UZ Gent" });
			Mapping.Setup(x => x.GetTypeByIdAsync(TypeId)).ReturnsAsync(new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId });
			Mapping.Setup(x => x.GetPoiCapabilityIdsForPoiAsync(DepartmentId, HospitalId)).ReturnsAsync(new List<string> { "stroke" });

			var model = (await Build<MappingController>(MapAdmin).EditPOI(HospitalId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddPOIView>().Which;

			model.Capabilities.Should().HaveCount(3);
			model.SelectedCapabilityIds.Should().Equal("stroke");
		}

		[TestCase("cath,stroke")]
		[TestCase("")]
		public async Task Editing_a_POI_replaces_its_capabilities_with_the_ticked_ones(string tickedList)
		{
			var ticked = tickedList.Split(',', StringSplitOptions.RemoveEmptyEntries);
			Mapping.Setup(x => x.GetPOIByIdAsync(HospitalId)).ReturnsAsync(new Poi { PoiId = HospitalId, PoiTypeId = TypeId, Name = "UZ Gent" });
			Mapping.Setup(x => x.GetTypeByIdAsync(TypeId)).ReturnsAsync(new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId });

			var result = await Build<MappingController>(MapAdmin).EditPOI(new AddPOIView { Poi = new Poi { PoiId = HospitalId, Name = "UZ Gent" }, SelectedCapabilityIds = ticked.ToList() }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			Mapping.Verify(x => x.SetPoiCapabilitiesAsync(DepartmentId, HospitalId, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(ticked)), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Editing_another_departments_POI_writes_no_capabilities()
		{
			Mapping.Setup(x => x.GetPOIByIdAsync(HospitalId)).ReturnsAsync(new Poi { PoiId = HospitalId, PoiTypeId = 6 });
			Mapping.Setup(x => x.GetTypeByIdAsync(6)).ReturnsAsync(new PoiType { PoiTypeId = 6, DepartmentId = OtherDepartmentId });

			ShouldBeRefused(await Build<MappingController>(MapAdmin).EditPOI(new AddPOIView { Poi = new Poi { PoiId = HospitalId }, SelectedCapabilityIds = new List<string> { "cath" } }, CancellationToken.None));

			Mapping.Verify(x => x.SetPoiCapabilitiesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Add / edit POI

		#region POI list

		[Test]
		public async Task A_destination_types_list_shows_capabilities_and_current_availability()
		{
			var expires = DateTime.UtcNow.AddHours(2);
			var pois = new List<Poi> { new Poi { PoiId = HospitalId, PoiTypeId = TypeId, Name = "UZ Gent" }, new Poi { PoiId = 32, PoiTypeId = TypeId, Name = "AZ Jan Palfijn" } };
			Mapping.Setup(x => x.GetTypeByIdAsync(TypeId)).ReturnsAsync(new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId, IsDestination = true, Name = "Hospitals", Pois = pois });
			Mapping.Setup(x => x.PopulatePoiDetailsAsync(DepartmentId, It.IsAny<IEnumerable<Poi>>())).Callback(() =>
			{
				pois[0].Capabilities = new List<string> { "Cath Lab", "Trauma Center" };
				pois[0].Availability = new PoiAvailability { PoiId = HospitalId, DepartmentId = DepartmentId, Status = (int)PoiAvailabilityStatuses.Diversion, UnavailableCapabilityIds = "cath", Note = "ED full", SetOn = DateTime.UtcNow, ExpiresOn = expires };
				pois[1].Capabilities = new List<string>();
			}).Returns(Task.CompletedTask);

			var listed = (await Build<MappingController>(MapAdmin).GetPoisForType(TypeId)).Should().BeOfType<JsonResult>().Which.Value.Should().BeOfType<List<PoiJson>>().Which;

			listed[0].Capabilities.Should().Equal("Cath Lab", "Trauma Center");
			listed[0].IsDestination.Should().BeTrue();
			listed[0].AvailabilityStatus.Should().Be((int)PoiAvailabilityStatuses.Diversion);
			listed[0].AvailabilityLabel.Should().Be("PoiAvailabilityDiversion");
			listed[0].UnavailableCapabilities.Should().Equal("Cath Lab");
			listed[0].AvailabilityNote.Should().Be("ED full");
			listed[0].AvailabilityExpires.Should().NotBeNullOrEmpty();
			listed[1].AvailabilityStatus.Should().Be((int)PoiAvailabilityStatuses.Open);
			listed[1].AvailabilityLabel.Should().Be("PoiAvailabilityOpen");
		}

		[Test]
		public async Task Other_types_list_capabilities_without_availability()
		{
			var pois = new List<Poi> { new Poi { PoiId = 40, PoiTypeId = 6, Name = "Shelter" } };
			Mapping.Setup(x => x.GetTypeByIdAsync(6)).ReturnsAsync(new PoiType { PoiTypeId = 6, DepartmentId = DepartmentId, IsDestination = false, Pois = pois });
			Mapping.Setup(x => x.PopulatePoiDetailsAsync(DepartmentId, It.IsAny<IEnumerable<Poi>>())).Callback(() => pois[0].Capabilities = new List<string> { "Pet friendly" }).Returns(Task.CompletedTask);

			var listed = (await Build<MappingController>(MapAdmin).GetPoisForType(6)).Should().BeOfType<JsonResult>().Which.Value.Should().BeOfType<List<PoiJson>>().Which;

			listed.Single().Capabilities.Should().Equal("Pet friendly");
			listed.Single().IsDestination.Should().BeFalse();
			listed.Single().AvailabilityLabel.Should().BeNull();
		}

		#endregion POI list

		#region Destination availability

		[Test]
		public async Task Dispatchers_and_map_administrators_open_the_destinations_page_and_other_members_do_not()
		{
			Mapping.Setup(x => x.GetDestinationPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi>());

			(await Build<MappingController>(CallCreator).Destinations()).Should().BeOfType<ViewResult>();
			(await Build<MappingController>(MapAdmin).Destinations()).Should().BeOfType<ViewResult>();
			ShouldBeRefused(await Build<MappingController>().Destinations());
			ShouldBeRefused(await Build<MappingController>().SetPoiAvailability(HospitalId, null));
			ShouldBeRefused(await Build<MappingController>().SetPoiAvailability(new SetPoiAvailabilityView { PoiId = HospitalId, Status = 2 }, CancellationToken.None));

			Mapping.Verify(x => x.SetPoiAvailabilityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<PoiAvailabilityStatuses>(), It.IsAny<IEnumerable<string>>(),
				It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_destinations_page_lists_what_each_destination_offers_and_its_availability()
		{
			var poi = Hospital(new PoiAvailability { PoiId = HospitalId, DepartmentId = DepartmentId, Status = (int)PoiAvailabilityStatuses.Limited, UnavailableCapabilityIds = "stroke", Note = "CT down", SetOn = DateTime.UtcNow },
				"cath", "stroke");
			Mapping.Setup(x => x.GetDestinationPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi> { poi });

			var model = (await Build<MappingController>(CallCreator).Destinations()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<DestinationAvailabilityView>().Which;

			var row = model.Destinations.Single();
			row.Name.Should().Be("UZ Gent");
			row.TypeName.Should().Be("Hospitals");
			row.Capabilities.Should().Equal("Cath Lab", "Stroke Center");
			row.Status.Should().Be((int)PoiAvailabilityStatuses.Limited);
			row.StatusLabel.Should().Be("PoiAvailabilityLimited");
			row.UnavailableCapabilities.Should().Equal("Stroke Center");
			row.Note.Should().Be("CT down");
			model.CanManagePois.Should().BeFalse();
		}

		[Test]
		public async Task The_availability_form_offers_the_destinations_capabilities_and_keeps_a_current_expiry()
		{
			Hospital(new PoiAvailability { PoiId = HospitalId, DepartmentId = DepartmentId, Status = (int)PoiAvailabilityStatuses.Diversion, UnavailableCapabilityIds = "trauma", SetOn = DateTime.UtcNow, ExpiresOn = DateTime.UtcNow.AddHours(3) },
				"cath");

			var model = (await Build<MappingController>(CallCreator).SetPoiAvailability(HospitalId, null)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<SetPoiAvailabilityView>().Which;

			model.Status.Should().Be((int)PoiAvailabilityStatuses.Diversion);
			model.UnavailableCapabilityIds.Should().Equal("trauma");
			model.ExpiresInHours.Should().Be(SetPoiAvailabilityView.KeepExpiry);
			// What it offers, plus what is reported unavailable but no longer ticked on the POI.
			model.Capabilities.Select(c => c.PoiCapabilityId).Should().Equal("cath", "trauma");
		}

		[Test]
		public async Task A_dispatcher_sets_a_destinations_availability()
		{
			Hospital(null, "cath", "stroke");
			var controller = Build<MappingController>(CallCreator);

			var result = await controller.SetPoiAvailability(new SetPoiAvailabilityView
			{
				PoiId = HospitalId, Status = (int)PoiAvailabilityStatuses.Diversion, UnavailableCapabilityIds = new List<string> { "cath" }, Note = "  ED overloaded  ", ExpiresInHours = 4
			}, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Destinations");
			Mapping.Verify(x => x.SetPoiAvailabilityAsync(DepartmentId, HospitalId, PoiAvailabilityStatuses.Diversion,
				It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "cath" })), "ED overloaded",
				It.Is<DateTime?>(d => d.HasValue && Math.Abs((d.Value - DateTime.UtcNow.AddHours(4)).TotalMinutes) < 1), UserId, It.IsAny<CancellationToken>()), Times.Once);
			controller.TempData["PoiAvailabilityMessage"].Should().Be("PoiAvailabilitySaved");
		}

		[Test]
		public async Task Open_without_an_expiry_clears_the_report()
		{
			Hospital(new PoiAvailability { PoiId = HospitalId, DepartmentId = DepartmentId, Status = (int)PoiAvailabilityStatuses.Closed, SetOn = DateTime.UtcNow });

			await Build<MappingController>(CallCreator).SetPoiAvailability(new SetPoiAvailabilityView { PoiId = HospitalId, Status = (int)PoiAvailabilityStatuses.Open, ExpiresInHours = 0 }, CancellationToken.None);

			Mapping.Verify(x => x.SetPoiAvailabilityAsync(DepartmentId, HospitalId, PoiAvailabilityStatuses.Open, It.IsAny<IEnumerable<string>>(), null, null, UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Keeping_the_expiry_passes_the_current_reports_expiry_through()
		{
			var expires = DateTime.UtcNow.AddHours(5);
			Hospital(new PoiAvailability { PoiId = HospitalId, DepartmentId = DepartmentId, Status = (int)PoiAvailabilityStatuses.Limited, SetOn = DateTime.UtcNow, ExpiresOn = expires });

			await Build<MappingController>(CallCreator).SetPoiAvailability(new SetPoiAvailabilityView { PoiId = HospitalId, Status = (int)PoiAvailabilityStatuses.Closed, ExpiresInHours = SetPoiAvailabilityView.KeepExpiry }, CancellationToken.None);

			Mapping.Verify(x => x.SetPoiAvailabilityAsync(DepartmentId, HospitalId, PoiAvailabilityStatuses.Closed, It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), expires, UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_map_administrator_returns_to_the_types_list()
		{
			Hospital();

			var result = await Build<MappingController>(MapAdmin).SetPoiAvailability(new SetPoiAvailabilityView { PoiId = HospitalId, Status = 1, ReturnTo = "Type" }, CancellationToken.None);

			var redirect = result.Should().BeOfType<RedirectToActionResult>().Which;
			redirect.ActionName.Should().Be("ViewType");
			redirect.RouteValues["poiTypeId"].Should().Be(TypeId);
		}

		[Test]
		public async Task A_dispatcher_asking_for_the_types_list_goes_back_to_the_destinations_page()
		{
			Hospital();

			var result = await Build<MappingController>(CallCreator).SetPoiAvailability(new SetPoiAvailabilityView { PoiId = HospitalId, Status = 1, ReturnTo = "Type" }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Destinations", "the POI type list is for map administrators");
		}

		[TestCase(7, 0, null, "Status")]
		[TestCase(-1, 0, null, "Status")]
		[TestCase(2, 3, null, "ExpiresInHours")]
		[TestCase(2, SetPoiAvailabilityView.KeepExpiry, null, "ExpiresInHours")]
		[TestCase(2, 0, 501, "Note")]
		public async Task An_invalid_post_shows_the_form_again_and_saves_nothing(int status, int expiresInHours, int? noteLength, string field)
		{
			Hospital(null, "cath");
			var controller = Build<MappingController>(CallCreator);

			var result = await controller.SetPoiAvailability(new SetPoiAvailabilityView
			{
				PoiId = HospitalId, Status = status, ExpiresInHours = expiresInHours, Note = noteLength.HasValue ? new string('n', noteLength.Value) : null
			}, CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<SetPoiAvailabilityView>().Which;
			model.Destination.Should().NotBeNull("the form shows the destination again");
			controller.ModelState[field].Errors.Should().NotBeEmpty();
			Mapping.Verify(x => x.SetPoiAvailabilityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<PoiAvailabilityStatuses>(), It.IsAny<IEnumerable<string>>(),
				It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Another_departments_POI_or_one_that_is_not_a_destination_is_not_found()
		{
			// GetDestinationPOIByIdAsync is department-scoped and destinations-only: it finds nothing for either.
			Mapping.Setup(x => x.GetDestinationPOIByIdAsync(DepartmentId, 777)).ReturnsAsync((Poi)null);
			var controller = Build<MappingController>(CallCreator);

			(await controller.SetPoiAvailability(777, null)).Should().BeOfType<NotFoundResult>();
			(await controller.SetPoiAvailability(new SetPoiAvailabilityView { PoiId = 777, Status = 3 }, CancellationToken.None)).Should().BeOfType<NotFoundResult>();

			Mapping.Verify(x => x.SetPoiAvailabilityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<PoiAvailabilityStatuses>(), It.IsAny<IEnumerable<string>>(),
				It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_save_the_service_refuses_shows_the_form_with_a_message()
		{
			Hospital();
			Mapping.Setup(x => x.SetPoiAvailabilityAsync(DepartmentId, HospitalId, It.IsAny<PoiAvailabilityStatuses>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
				It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("The destination does not exist."));
			var controller = Build<MappingController>(CallCreator);

			(await controller.SetPoiAvailability(new SetPoiAvailabilityView { PoiId = HospitalId, Status = 3 }, CancellationToken.None)).Should().BeOfType<ViewResult>();

			controller.ModelState[string.Empty].Errors.Single().ErrorMessage.Should().Be("PoiAvailabilitySaveFailed");
		}

		#endregion Destination availability

		#region Call form destination options

		[Test]
		public async Task The_call_forms_destination_options_name_capabilities_and_a_status_other_than_open()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(true);
			M<IDepartmentGroupsService>().Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			M<IDepartmentGroupsService>().Setup(x => x.GetAllStationGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			M<ICallsService>().Setup(x => x.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>());
			M<ICallsService>().Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartment(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			M<IUnitsService>().Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>());

			var hospitals = new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId, Name = "Hospitals", IsDestination = true };
			var gent = new Poi { PoiId = HospitalId, PoiTypeId = TypeId, Name = "UZ Gent", Type = hospitals };
			var aalst = new Poi { PoiId = 32, PoiTypeId = TypeId, Name = "OLV Aalst", Type = hospitals };
			var eeklo = new Poi { PoiId = 33, PoiTypeId = TypeId, Name = "AZ Alma", Type = hospitals };
			Mapping.Setup(x => x.GetDestinationPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi> { gent, aalst, eeklo });
			Mapping.Setup(x => x.PopulatePoiDetailsAsync(DepartmentId, It.IsAny<IEnumerable<Poi>>())).Callback(() =>
			{
				gent.Capabilities = new List<string> { "Cath Lab", "Trauma Center" };
				gent.Availability = new PoiAvailability { PoiId = HospitalId, Status = (int)PoiAvailabilityStatuses.Diversion, SetOn = DateTime.UtcNow };
				aalst.Capabilities = new List<string> { "Stroke Center" };
				// An expired report reads as open.
				aalst.Availability = new PoiAvailability { PoiId = 32, Status = (int)PoiAvailabilityStatuses.Closed, SetOn = DateTime.UtcNow.AddDays(-1), ExpiresOn = DateTime.UtcNow.AddMinutes(-5) };
				eeklo.Capabilities = new List<string>();
				eeklo.Availability = new PoiAvailability { PoiId = 33, Status = (int)PoiAvailabilityStatuses.Closed, SetOn = DateTime.UtcNow };
			}).Returns(Task.CompletedTask);

			var model = (await Build<DispatchController>(CallCreator).NewCall()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<NewCallView>().Which;

			var options = model.DestinationPois.Where(o => !string.IsNullOrEmpty(o.Value)).ToDictionary(o => o.Value, o => o.Text);
			options[HospitalId.ToString()].Should().Be("UZ Gent — Cath Lab, Trauma Center (DestinationAvailabilityDiversion)");
			options["32"].Should().Be("OLV Aalst — Stroke Center");
			options["33"].Should().Be("AZ Alma (DestinationAvailabilityClosed)");
			model.DestinationPois.First().Value.Should().BeEmpty("the no-destination option stays first");
		}

		#endregion Call form destination options
	}
}
