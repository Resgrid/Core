using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Training;
using Resgrid.WebCore.Areas.User.Models.Templates;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class TemplatesController : SecureBaseController
	{
		private readonly ITemplatesService _templatesService;
		private readonly ICallsService _callsService;

		public TemplatesController(ITemplatesService templatesService, ICallsService callsService)
		{
			_templatesService = templatesService;
			_callsService = callsService;
		}

		[HttpGet]
		public IActionResult Index()
		{
			var model = new TemplateIndexModel();
			model.CallQuickTemplates = _templatesService.GetAllCallQuickTemplatesForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]

		public IActionResult New()
		{
			var model = new NewTemplateModel();
			model.Template = new CallQuickTemplate();

			var priorites = _callsService.GetCallPrioritesForDepartment(DepartmentId);
			model.CallPriorities = new SelectList(priorites, "DepartmentCallPriorityId", "Name", priorites.FirstOrDefault(x => x.IsDefault));


			List<CallType> types = new List<CallType>();
			types.Add(new CallType { CallTypeId = 0, Type = "No Type" });
			types.AddRange(_callsService.GetCallTypesForDepartment(DepartmentId));
			model.CallTypes = new SelectList(types, "Type", "Type");

			return View(model);
		}

		[HttpPost]
		public IActionResult New(NewTemplateModel model)
		{
			if (String.IsNullOrWhiteSpace(model.Template.CallName) &&
				String.IsNullOrWhiteSpace(model.Template.CallNature))
			{
				model.Message = "You must specify a call name and/or call nature to set to save the template";
				return View(model);
			}

			if (ModelState.IsValid)
			{
				model.Template.DepartmentId = DepartmentId;
				model.Template.CreatedOn = DateTime.UtcNow;
				model.Template.CreatedByUserId = UserId;

				_templatesService.SaveCallQuickTemplate(model.Template);
				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		public IActionResult Delete(int id)
		{
			var template = _templatesService.GetCallQuickTemplateById(id);

			if (template == null || template.DepartmentId != DepartmentId)
				Unauthorized();

			_templatesService.DeleteCallQuickTemplate(id);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public IActionResult GetTemplate(int id)
		{
			var template = _templatesService.GetCallQuickTemplateById(id);

			return Json(template);
		}
	}
}
