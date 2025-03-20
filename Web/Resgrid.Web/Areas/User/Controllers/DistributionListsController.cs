using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
		public async Task<IActionResult> Index()
		{
			IndexView model = new IndexView();
			model.Lists = await _distributionListsService.GetDistributionListsByDepartmentIdAsync(DepartmentId);

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewList()
		{
			var model = new NewListView();
			model.List = new DistributionList();
			model.List.Port = 110;
			model.ListTypes = model.Type.ToSelectList();
			model.List.UseSsl = false;
			model.List.Type = (int)DistributionListTypes.Internal;

			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, true);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewList(NewListView model, IFormCollection collection, CancellationToken cancellationToken)
		{
			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, true);
			model.ListTypes = model.Type.ToSelectList();

			var result = await _distributionListsService.GetDistributionListByAddressAsync(model.List.EmailAddress);

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

				await _distributionListsService.SaveDistributionListAsync(model.List, cancellationToken);

				return RedirectToAction("Index", "DistributionLists", new { Area = "User" });
			}

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditList(int distributionListId)
		{
			var model = new EditListView();
			model.List = await _distributionListsService.GetDistributionListByIdAsync(distributionListId);
			model.ListTypes = model.Type.ToSelectList();

			if (!model.List.UseSsl.HasValue)
				model.List.UseSsl = false;

			if (model.List.Type.HasValue)
				model.Type = (DistributionListTypes)model.List.Type;
			else
				model.Type = DistributionListTypes.External;

			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, true);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditList(EditListView model, IFormCollection collection, CancellationToken cancellationToken)
		{
			var list = await _distributionListsService.GetDistributionListByIdAsync(model.List.DistributionListId);
			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
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

				if (collection.ContainsKey("listMembers"))
				{
					var userIds = collection["listMembers"].ToString().Split(char.Parse(","));
					var membersToRemove = list.Members.Where(x => !userIds.Contains(x.UserId)).ToList();

					foreach (var member in membersToRemove)
					{
						list.Members.Remove(member);
					}

					foreach (var userId in userIds)
					{
						if (list.Members.All(x => x.UserId != userId))
						{
							var member = new DistributionListMember();
							member.UserId = userId;

							list.Members.Add(member);
						}
					}
				}
				else
				{
					list.Members = new Collection<DistributionListMember>();
				}

				await _distributionListsService.SaveDistributionListAsync(list, cancellationToken);

				return RedirectToAction("Index", "DistributionLists", new { Area = "User" });
			}

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteList(int distributionListId, CancellationToken cancellationToken)
		{
			var list = await _distributionListsService.GetDistributionListByIdAsync(distributionListId);

			if (list.DepartmentId == DepartmentId)
			{
				await _distributionListsService.DeleteDistributionListsByIdAsync(distributionListId, cancellationToken);
			}

			return RedirectToAction("Index", "DistributionLists", new { Area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> SetListStatus(int distributionListId, bool disable, CancellationToken cancellationToken)
		{
			var list = await _distributionListsService.GetDistributionListByIdAsync(distributionListId);
			list.IsDisabled = disable;

			await _distributionListsService.SaveDistributionListOnlyAsync(list, cancellationToken);

			return RedirectToAction("Index", "DistributionLists", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> ValidateAddress(int id, string emailAddress)
		{
			string returnText = "";

			var result = await _distributionListsService.GetDistributionListByAddressAsync(emailAddress);

			if (result != null && result.DistributionListId != id)
				returnText = "Address already in use, please select another";

			return Content(returnText);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> GetMembersForList(int id)
		{
			var personnelJson = new List<PersonnelForJson>();
			var list = await _distributionListsService.GetDistributionListByIdAsync(id);

			if (list.DepartmentId != DepartmentId)
				Unauthorized();

			var members = await _distributionListsService.GetAllListMembersByListIdAsync(id);

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
