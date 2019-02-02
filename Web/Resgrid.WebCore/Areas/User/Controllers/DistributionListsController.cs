using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.DistributionLists;
using Resgrid.Web.Areas.User.Models.Personnel;
using Microsoft.AspNetCore.Authorization;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class DistributionListsController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDistributionListsService _distributionListsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEmailService _emailService;

		public DistributionListsController(IDistributionListsService distributionListsService, IDepartmentsService departmentsService, IEmailService emailService)
		{
			_distributionListsService = distributionListsService;
			_departmentsService = departmentsService;
			_emailService = emailService;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Index()
		{
			IndexView model = new IndexView();
			model.Lists = _distributionListsService.GetDistributionListsByDepartmentId(DepartmentId);

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult NewList()
		{
			var model = new NewListView();
			model.List = new DistributionList();
			model.List.Port = 110;
			model.ListTypes = model.Type.ToSelectList();
			model.List.UseSsl = false;
			model.List.Type = (int)DistributionListTypes.Internal;

			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId, true);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult NewList(NewListView model, IFormCollection collection)
		{
			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId, true);
			model.ListTypes = model.Type.ToSelectList();

			var result = _distributionListsService.GetDistributionListByAddress(model.List.EmailAddress);

			if (result != null)
				ModelState.AddModelError("List.EmailAddress", "Email address already in use, please try another one");

			if (ModelState.IsValid)
			{
				model.List.DepartmentId = DepartmentId;
				model.List.Members = new Collection<DistributionListMember>();
				model.List.Type = (int)DistributionListTypes.Internal;

				if (collection.ContainsKey("listMembers"))
				{
					var userIds = collection["listMembers"].ToString().Split(char.Parse(","));

					foreach (var userId in userIds)
					{
						var member = new DistributionListMember();
						member.UserId = userId;

						model.List.Members.Add(member);
					}
				}

				_distributionListsService.SaveDistributionList(model.List);

				return RedirectToAction("Index", "DistributionLists", new { Area = "User" });
			}

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult EditList(int distributionListId)
		{
			var model = new EditListView();
			model.List = _distributionListsService.GetDistributionListById(distributionListId);
			model.ListTypes = model.Type.ToSelectList();

			if (!model.List.UseSsl.HasValue)
				model.List.UseSsl = false;

			if (model.List.Type.HasValue)
				model.Type = (DistributionListTypes)model.List.Type;
			else
				model.Type = DistributionListTypes.External;

			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId, true);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult EditList(EditListView model, IFormCollection collection)
		{
			var list = _distributionListsService.GetDistributionListById(model.List.DistributionListId);
			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			model.ListTypes = model.Type.ToSelectList();

			if (!model.List.UseSsl.HasValue)
				model.List.UseSsl = false;

			if (model.Type == DistributionListTypes.External && !StringHelpers.IsValidDomainName(model.List.Hostname))
				ModelState.AddModelError("List.Hostname", string.Format("The hostname supplied is not valid, must looks somthing like mail.mydepartment.com."));


			if (ModelState.IsValid)
			{
				model.List.DepartmentId = DepartmentId;
				model.List.Members = new Collection<DistributionListMember>();

				list.Name = model.List.Name;
				list.IsDisabled = model.List.IsDisabled;
				list.Type = (int)DistributionListTypes.Internal;

				list.EmailAddress = model.List.EmailAddress;
				list.Hostname = null;
				list.Port = null;
				list.UseSsl = null;
				list.Username = null;
				list.Password = null;

				list.Members = new Collection<DistributionListMember>();

				if (collection.ContainsKey("listMembers"))
				{
					var userIds = collection["listMembers"].ToString().Split(char.Parse(","));

					foreach (var userId in userIds)
					{
						var member = new DistributionListMember();
						member.UserId = userId;

						list.Members.Add(member);
					}
				}

				_distributionListsService.SaveDistributionList(list);

				return RedirectToAction("Index", "DistributionLists", new { Area = "User" });
			}

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult DeleteList(int distributionListId)
		{
			var list = _distributionListsService.GetDistributionListById(distributionListId);

			if (list.DepartmentId == DepartmentId)
			{
				_distributionListsService.DeleteDistributionListsById(distributionListId);
			}

			return RedirectToAction("Index", "DistributionLists", new { Area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult SetListStatus(int distributionListId, bool disable)
		{
			var list = _distributionListsService.GetDistributionListById(distributionListId);
			list.IsDisabled = disable;

			_distributionListsService.SaveDistributionListOnly(list);

			return RedirectToAction("Index", "DistributionLists", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult ValidateAddress(int id, string emailAddress)
		{
			string returnText = "";

			var result = _distributionListsService.GetDistributionListByAddress(emailAddress);

			if (result != null && result.DistributionListId != id)
				returnText = "Address already in use, please select another";

			return Content(returnText);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult GetMembersForList(int id)
		{
			var personnelJson = new List<PersonnelForJson>();
			var list = _distributionListsService.GetDistributionListById(id);

			if (list.DepartmentId != DepartmentId)
				Unauthorized();

			var members = _distributionListsService.GetAllListMembersByListId(id);

			foreach (var member in members)
			{
				var person = new PersonnelForJson();
				person.UserId = member.UserId;

				personnelJson.Add(person);
			}

			return Json(personnelJson);
		}
	}
}