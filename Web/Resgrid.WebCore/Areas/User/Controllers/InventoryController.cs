using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Inventory;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model.Helpers;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class InventoryController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IInventoryService _inventoryService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;

		public InventoryController(IInventoryService inventoryService, IDepartmentGroupsService departmentGroupsService, IUnitsService unitsService, IDepartmentsService departmentsService, IUserProfileService userProfileService)
		{
			_inventoryService = inventoryService;
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Inventory_View)]
		public async Task<IActionResult> Index()
		{
			return View();
		}

		[Authorize(Policy = ResgridResources.Inventory_Update)]
		public async Task<IActionResult> ManageTypes()
		{
			return View();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Inventory_Update)]
		public async Task<IActionResult> AddType()
		{
			var model = new AddTypeView();
			model.Type = new InventoryType();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Inventory_Create)]
		public async Task<IActionResult> Adjust()
		{
			var model = new AdjustView();
			model.Inventory = new Inventory();
			model.Types = await _inventoryService.GetAllTypesForDepartmentAsync(DepartmentId);
			model.Stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Inventory_Create)]
		public async Task<IActionResult> Adjust(AdjustView model)
		{
			if (model.Inventory.Amount == 0)
				ModelState.AddModelError("Inventory.Amount", "You must supply a non-zero inventory adjustment (count/amount).");

			if (ModelState.IsValid)
			{
				model.Inventory.DepartmentId = DepartmentId;
				model.Inventory.TimeStamp = DateTime.UtcNow;
				model.Inventory.AddedByUserId = UserId;

				if (model.UnitId > 0)
					model.Inventory.UnitId = model.UnitId;

				await _inventoryService.SaveInventoryAsync(model.Inventory);

				return RedirectToAction("Index");
			}

			model.Types = await _inventoryService.GetAllTypesForDepartmentAsync(DepartmentId);
			model.Stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Inventory_View)]
		public async Task<IActionResult> History()
		{
			return View();
		}

		[Authorize(Policy = ResgridResources.Inventory_View)]
		public async Task<IActionResult> ViewEntry(int inventoryId)
		{
			var model = new ViewEntryView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Inventory = await _inventoryService.GetInventoryByIdAsync(inventoryId);
			
			if (model.Inventory == null || model.Inventory.DepartmentId != DepartmentId)
				Unauthorized();

			var profile = await _userProfileService.GetProfileByUserIdAsync(model.Inventory.AddedByUserId);

			if (profile != null)
				model.Name = profile.FullName.AsFirstNameLastName;
			else
				model.Name = "Unknown";

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Inventory_View)]
		public async Task<IActionResult> DeleteType(int typeId)
		{
			var type = await _inventoryService.GetTypeByIdAsync(typeId);

			if (type == null)
				return RedirectToAction("ManageTypes");

			if (type.DepartmentId != DepartmentId)
				Unauthorized();

			await _inventoryService.DeleteTypeAsync(typeId);

			return RedirectToAction("ManageTypes");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Inventory_Update)]
		public async Task<IActionResult> EditType(int typeId)
		{
			var type = await _inventoryService.GetTypeByIdAsync(typeId);

			if (type == null)
				return RedirectToAction("ManageTypes");

			if (type.DepartmentId != DepartmentId)
				Unauthorized();

			var model = new EditTypeView();
			model.Type = type;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Inventory_Update)]
		public async Task<IActionResult> EditType(EditTypeView model)
		{
			var type = await _inventoryService.GetTypeByIdAsync(model.Type.InventoryTypeId);

			if (type == null)
				return RedirectToAction("ManageTypes");

			type.Type = model.Type.Type;
			type.Description = model.Type.Description;
			type.ExpiresDays = model.Type.ExpiresDays;
			type.UnitOfMesasure = model.Type.UnitOfMesasure;

			await _inventoryService.SaveTypeAsync(type);

			return RedirectToAction("ManageTypes");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Inventory_Update)]
		public async Task<IActionResult> AddType(AddTypeView model)
		{
			if (ModelState.IsValid)
			{
				model.Type.DepartmentId = DepartmentId;
				await _inventoryService.SaveTypeAsync(model.Type);

				return RedirectToAction("ManageTypes");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Inventory_View)]
		public async Task<IActionResult> GetTypesList()
		{
			List<InventoryTypeJson> inventoryJson = new List<InventoryTypeJson>();

			var types = await _inventoryService.GetAllTypesForDepartmentAsync(DepartmentId);

			foreach (var type in types)
			{
				var typeJson = new InventoryTypeJson();
				typeJson.TypeId = type.InventoryTypeId;
				typeJson.Name = type.Type;
				
				if (type.ExpiresDays > 0)
					typeJson.ExpiresDays = $"{type.ExpiresDays} Days";
				else
					typeJson.ExpiresDays = "No Expiry";

				inventoryJson.Add(typeJson);
			}

			return Json(inventoryJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Inventory_View)]
		public async Task<IActionResult> GetCombinedInventoryList()
		{
			List<InventorySummaryJson> inventoryJson = new List<InventorySummaryJson>();

			var items = await _inventoryService.GetConsolidatedInventoryForDepartment(DepartmentId);

			foreach (var item in items)
			{
				var inventory = new InventorySummaryJson();
				inventory.Name = item.Type.Type;
				inventory.Group = item.Group.Name;

				if (item.Unit != null)
					inventory.Unit = item.Unit.Name;
				else
					inventory.Unit = "No Unit";

				inventory.Count = item.Amount;

				inventoryJson.Add(inventory);
			}

			return Json(inventoryJson);
		}

		[HttpGet]

		[Authorize(Policy = ResgridResources.Inventory_View)]
		public async Task<IActionResult> GetInventoryList()
		{
			List<InventoryJson> inventoryJson = new List<InventoryJson>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var items = await _inventoryService.GetAllTransactionsForDepartmentAsync(DepartmentId);
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			//var groups = await _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			
			foreach (var item in items)
			{
				var inventory = new InventoryJson();
				inventory.InventoryId = item.InventoryId;
				inventory.Type = item.Type.Type;
				inventory.Amount = item.Amount;
				inventory.Batch = item.Batch;
				inventory.Timestamp = item.TimeStamp.FormatForDepartment(department);

				if (item.Unit != null)
					inventory.Unit = item.Unit.Name;
				else
					inventory.Unit = "No Unit";

				if (item.Group != null)
					inventory.Group = item.Group.Name;
				else
					inventory.Group = "No Group";

				var name = names.FirstOrDefault(x => x.UserId == item.AddedByUserId);

				if (name != null)
					inventory.UserName = name.Name;
				else
					inventory.UserName = "Unknown";


				inventoryJson.Add(inventory);
			}

			return Json(inventoryJson);
		}
	}
}
