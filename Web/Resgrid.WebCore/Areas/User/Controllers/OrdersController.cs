using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Orders;
using Resgrid.WebCore.Areas.User.Models.Orders;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class OrdersController : SecureBaseController
	{
		#region Private Members and Constructors

		private readonly IResourceOrdersService _resourceOrdersService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IGeoLocationProvider _geoLocationProvider;

		public OrdersController(IResourceOrdersService resourceOrdersService, ICustomStateService customStateService, IDepartmentsService departmentsService, 
			IDepartmentSettingsService departmentSettingsService, IGeoLocationProvider geoLocationProvider)
		{
			_resourceOrdersService = resourceOrdersService;
			_customStateService = customStateService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_geoLocationProvider = geoLocationProvider;
		}

		#endregion Private Members and Constructors

		public async Task<IActionResult> Index()
		{
			var model = new OrdersIndexView();

			model.YourOrders = new List<ResourceOrder>();
			model.YourOrders.AddRange((await _resourceOrdersService.GetAllOrdersByDepartmentIdAsync(DepartmentId)).OrderByDescending(x => x.OpenDate));

			model.OthersOrders = new List<ResourceOrder>();
			model.OthersOrders.AddRange(await _resourceOrdersService.GetOpenAvailableOrdersAsync(DepartmentId));

			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Coordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(model.Department);

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Settings()
		{
			var model = new OrderSetttingsView();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var settings = await _resourceOrdersService.GetSettingsByDepartmentIdAsync(DepartmentId);

			if (settings != null)
				model.Settings = settings;
			else
			{
				model.Settings = new ResourceOrderSetting();
				model.Settings.DefaultResourceOrderManagerUserId = department.ManagingUserId;
				model.Settings.Range = 500;
			}

			model.OrderVisibilities = model.Visibility.ToSelectListInt();

			var staffingLevels = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingLevels == null)
				model.StaffingLevels = model.UserStateTypes.ToSelectListInt();
			else
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");

			model.SetUsers(await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, false, true), await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId));
			ViewBag.Users = new SelectList(model.Users, "Key", "Value");

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> Settings(OrderSetttingsView model, IFormCollection form, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var settings = await _resourceOrdersService.GetSettingsByDepartmentIdAsync(DepartmentId);

			if (ModelState.IsValid)
			{
				if (settings == null)
				{
					settings = new ResourceOrderSetting();
					settings.DepartmentId = DepartmentId;
					settings.ImportEmailCode = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);
				}

				settings.AllowedStaffingLevelToReceiveNotifications = model.Settings.AllowedStaffingLevelToReceiveNotifications;
				settings.AutomaticFillAcceptance = model.Settings.AutomaticFillAcceptance;
				settings.BoundryGeofence = model.Settings.BoundryGeofence;
				settings.DefaultResourceOrderManagerUserId = model.Settings.DefaultResourceOrderManagerUserId;
				settings.DoNotReceiveOrders = model.Settings.DoNotReceiveOrders;
				settings.LimitStaffingLevelToReceiveNotifications = model.Settings.LimitStaffingLevelToReceiveNotifications;
				settings.NotifyUsers = model.Settings.NotifyUsers;
				settings.Range = model.Settings.Range;
				settings.RoleAllowedToFulfilOrdersRoleId = model.Settings.RoleAllowedToFulfilOrdersRoleId;
				settings.Visibility = model.Settings.Visibility;

				if (form.ContainsKey("departmentTypes"))
				{
					settings.TargetDepartmentType = form["departmentTypes"].ToString();
				}

				if (form.ContainsKey("notifyRoles"))
				{
					settings.UserIdsToNotifyOnOrders = form["notifyRoles"].ToString();
				}

				await _resourceOrdersService.SaveSettingsAsync(settings, cancellationToken);

				return RedirectToAction("Index");
			}

			model.OrderVisibilities = model.Visibility.ToSelectListInt();

			var staffingLevels = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingLevels == null)
				model.StaffingLevels = model.UserStateTypes.ToSelectListInt();
			else
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");

			model.SetUsers(await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, false, true), await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId));


			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> New()
		{
			var model = new NewOrderView();

			model.Order = new ResourceOrder();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Order.NeededBy = DateTime.UtcNow.AddDays(30);
			model.Order.NeededBy = new DateTime(model.Order.NeededBy.Year, model.Order.NeededBy.Month, model.Order.NeededBy.Day, model.Order.NeededBy.Hour, 0, 0, model.Order.NeededBy.Kind);

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> New(NewOrderView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var settings = await _resourceOrdersService.GetSettingsByDepartmentIdAsync(DepartmentId);

				List<int> questions = (from object key in form.Keys where key.ToString().StartsWith("itemResource_") select int.Parse(key.ToString().Replace("itemResource_", ""))).ToList();

				if (questions.Count > 0)
					model.Order.Items = new Collection<ResourceOrderItem>();

				foreach (var i in questions)
				{
					if (form.ContainsKey("itemResource_" + i))
					{
						var resourceText = form["itemResource_" + i];
						var resourceMin = form["itemMin_" + i];
						var resourceMax = form["itemMax_" + i];
						var resourceCode = form["itemFinancial_" + i];
						var resourceNeeds = form["itemNeeds_" + i];
						var resourceRequirements = form["itemRequirements_" + i];

						var item = new ResourceOrderItem();
						item.Resource = resourceText;

						int minimum = 0;
						if (int.TryParse(resourceMin, out minimum))
						{
							item.Min = minimum;
						}

						if (minimum == 0)
							item.Min = 1;

						int maxiumum = 0;
						if (int.TryParse(resourceMax, out maxiumum))
						{
							item.Max = maxiumum;
						}

						if (minimum > maxiumum)
							item.Max = item.Min;

						if (item.Max == 0)
							item.Max = item.Min;

						item.FinancialCode = resourceCode;
						item.SpecialNeeds = resourceNeeds;
						item.Requirements = resourceRequirements;

						model.Order.Items.Add(item);
					}
				}

				model.Order.DepartmentId = DepartmentId;
				model.Order.OpenDate = DateTime.UtcNow;

				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
				var mapCenterLocation = await _departmentSettingsService.GetMapCenterCoordinatesAsync(department);

				model.Order.OriginLatitude = mapCenterLocation.Latitude;
				model.Order.OriginLongitude = mapCenterLocation.Longitude;

				if (settings != null)
				{
					model.Order.Visibility = settings.Visibility;
					model.Order.Range = settings.Range;
				}
				else
				{
					model.Order.Visibility = 0;
					model.Order.Range = 500;
				}
				await _resourceOrdersService.CreateOrderAsync(model.Order, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		public async Task<IActionResult> View(int orderId)
		{
			var model = new ViewOrderView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Order = await _resourceOrdersService.GetOrderByIdAsync(orderId);

			return View(model);
		}

		public async Task<IActionResult> AcceptFill(int fillId)
		{
			var model = new ViewOrderView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			var fill = await _resourceOrdersService.GetOrderFillByIdAsync(fillId);
			await _resourceOrdersService.SetFillStatusAsync(fillId, UserId, true);

			return RedirectToAction("View", new {orderId = fill.OrderItem.Order.ResourceOrderId});
		}

		public async Task<IActionResult> Fill(int orderId)
		{
			var model = new FillOrderView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Order = await _resourceOrdersService.GetOrderByIdAsync(orderId);
			model.Fill = new ResourceOrderFill();

			model.SetUsers(await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, false, true), await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId));
			ViewBag.Users = new SelectList(model.Users, "Key", "Value");

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> FillItem(int id, bool error, string errorMessage)
		{
			var model = new FillItemView();
			model.Error = error;
			model.ErrorMessage = HttpUtility.UrlDecode(errorMessage);
			model.Fill = await _resourceOrdersService.GetOrderFillByIdAsync(id);

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> FillItem(FillItemInput data, CancellationToken cancellationToken)
		{
			var model = new FillItemView();

			if (data == null || data.Id == 0 || data.Units == null || data.Units.Count() <= 0)
			{
				model.Error = true;
				model.ErrorMessage = "There was an issue trying to process you fill. Please open the order and try the fill again.";

				return View(model);
			}

			var item = await _resourceOrdersService.GetOrderItemByIdAsync(data.Id);
			if (item == null)
			{
				model.Error = true;
				model.ErrorMessage = "Unable to find this order. The order may be been closed or removed.";

				return View(model);
			}

			var order = await _resourceOrdersService.GetOrderByIdAsync(item.ResourceOrderId);
			var settings = await _resourceOrdersService.GetSettingsByDepartmentIdAsync(order.DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(order.DepartmentId);

			var fill = new ResourceOrderFill();
			fill.ResourceOrderItemId = item.ResourceOrderItemId;
			fill.DepartmentId = DepartmentId;
			fill.FillingUserId = UserId;
			fill.ContactName = data.Name;
			fill.ContactNumber = data.Number;
			fill.Note = data.Note;
			fill.LeadUserId = data.LeadUserId;
			fill.FilledOn = DateTime.UtcNow;

			if (order.AutomaticFillAcceptance)
			{
				fill.Accepted = true;
				fill.AcceptedOn = DateTime.UtcNow;

				if (settings != null && settings.DefaultResourceOrderManagerUserId != null)
					fill.AcceptedUserId = settings.DefaultResourceOrderManagerUserId;
				else
					fill.AcceptedUserId = department.ManagingUserId;
			}

			fill.Units = new List<ResourceOrderFillUnit>();
			foreach (var unit in data.Units)
			{
				var itemUnit = new ResourceOrderFillUnit();
				itemUnit.UnitId = unit;

				fill.Units.Add(itemUnit);
			}

			var savedFill = await _resourceOrdersService.CreateFillAsync(fill, cancellationToken);

			return Json(new { id = savedFill.ResourceOrderFillId, error = model.Error, errorMessage = HttpUtility.UrlEncode(model.ErrorMessage) });
		}

		[HttpGet]
		public async Task<IActionResult> GetYourOrders()
		{
			List<ResourceOrderJson> ordersJson = new List<ResourceOrderJson>();

			var yourOrders = await _resourceOrdersService.GetOpenOrdersByDepartmentIdAsync(DepartmentId);

			foreach (var order in yourOrders)
			{
				var orderJson = new ResourceOrderJson();
				orderJson.Id = order.ResourceOrderId;
				orderJson.DepartmentId = order.ResourceOrderId;
				orderJson.Type = order.Type;
				orderJson.AllowPartialFills = order.AllowPartialFills;
				orderJson.Title = order.Title;
				orderJson.IncidentNumber = order.IncidentNumber;
				orderJson.IncidentName = order.IncidentName;
				orderJson.IncidentAddress = order.IncidentAddress;
				orderJson.IncidentLatitude = order.IncidentLatitude;
				orderJson.IncidentLongitude = order.IncidentLongitude;
				orderJson.Summary = order.Summary;
				orderJson.OpenDate = order.OpenDate;
				orderJson.NeededBy = order.NeededBy;
				orderJson.MeetupDate = order.MeetupDate;
				orderJson.CloseDate = order.CloseDate;
				orderJson.ContactName = order.ContactName;
				orderJson.ContactNumber = order.ContactNumber;
				orderJson.SpecialInstructions = order.SpecialInstructions;
				orderJson.MeetupLocation = order.MeetupLocation;
				orderJson.FinancialCode = order.FinancialCode;
				orderJson.AutomaticFillAcceptance = order.AutomaticFillAcceptance;
				orderJson.Visibility = order.Visibility;
				orderJson.Range = order.Range;
				orderJson.OriginLatitude = order.OriginLatitude;
				orderJson.OriginLongitude = order.OriginLongitude;

				if (order.CloseDate.HasValue)
				{
					orderJson.Status = "Open";
				}
				else
				{
					orderJson.Status = "Closed";
				}

				if (order.Items != null)
				{
					orderJson.ResourceOrderCount = order.Items.Count;
					orderJson.TotalUnitsOrdered = order.Items.Sum(x => x.Min);
					orderJson.TotalUntisFilled = order.Items.Sum(x => x.Fills.Count);
				}

				switch (order.Visibility)
				{
					case 0:
						orderJson.VisibilityName = "Range";
						break;
					case 1:
						orderJson.VisibilityName = "Geofence";
						break;
					case 2:
						orderJson.VisibilityName = "Linked";
						break;
					case 3:
						orderJson.VisibilityName = "Unrestricted";
						break;
				}

				ordersJson.Add(orderJson);
			}

			return Json(ordersJson);
		}

		[HttpGet]
		public async Task<IActionResult> GetAvailableOrders()
		{
			List<ResourceOrderJson> ordersJson = new List<ResourceOrderJson>();

			var yourOrders = await _resourceOrdersService.GetOpenAvailableOrdersAsync(DepartmentId);

			foreach (var order in yourOrders)
			{
				var orderJson = new ResourceOrderJson();
				orderJson.Id = order.ResourceOrderId;
				orderJson.DepartmentId = order.ResourceOrderId;
				orderJson.Type = order.Type;
				orderJson.AllowPartialFills = order.AllowPartialFills;
				orderJson.Title = order.Title;
				orderJson.IncidentNumber = order.IncidentNumber;
				orderJson.IncidentName = order.IncidentName;
				orderJson.IncidentAddress = order.IncidentAddress;
				orderJson.IncidentLatitude = order.IncidentLatitude;
				orderJson.IncidentLongitude = order.IncidentLongitude;
				orderJson.Summary = order.Summary;
				orderJson.OpenDate = order.OpenDate;
				orderJson.NeededBy = order.NeededBy;
				orderJson.MeetupDate = order.MeetupDate;
				orderJson.CloseDate = order.CloseDate;
				orderJson.ContactName = order.ContactName;
				orderJson.ContactNumber = order.ContactNumber;
				orderJson.SpecialInstructions = order.SpecialInstructions;
				orderJson.MeetupLocation = order.MeetupLocation;
				orderJson.FinancialCode = order.FinancialCode;
				orderJson.AutomaticFillAcceptance = order.AutomaticFillAcceptance;
				orderJson.Visibility = order.Visibility;
				orderJson.Range = order.Range;
				orderJson.OriginLatitude = order.OriginLatitude;
				orderJson.OriginLongitude = order.OriginLongitude;

				if (order.CloseDate.HasValue)
				{
					orderJson.Status = "Open";
				}
				else
				{
					orderJson.Status = "Closed";
				}

				if (order.Items != null)
				{
					orderJson.ResourceOrderCount = order.Items.Count;
					orderJson.TotalUnitsOrdered = order.Items.Sum(x => x.Min);
					orderJson.TotalUntisFilled = order.Items.Sum(x => x.Fills.Count);
				}

				switch (order.Visibility)
				{
					case 0:
						orderJson.VisibilityName = "Range";
						break;
					case 1:
						orderJson.VisibilityName = "Geofence";
						break;
					case 2:
						orderJson.VisibilityName = "Linked";
						break;
					case 3:
						orderJson.VisibilityName = "Unrestricted";
						break;
				}

				ordersJson.Add(orderJson);
			}

			return Json(ordersJson);
		}
	}
}
