using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
		public async Task<IActionResult> Index()
		{
			var model = new TemplateIndexModel();
			model.CallQuickTemplates = await _templatesService.GetAllCallQuickTemplatesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]

		public async Task<IActionResult> New()
		{
			var model = new NewTemplateModel();
			model.Template = new CallQuickTemplate();

			var priorites = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId);
			model.CallPriorities = new SelectList(priorites, "DepartmentCallPriorityId", "Name", priorites.FirstOrDefault(x => x.IsDefault));


			List<CallType> types = new List<CallType>();
			types.Add(new CallType { CallTypeId = 0, Type = "No Type" });
			types.AddRange(await _callsService.GetCallTypesForDepartmentAsync(DepartmentId));
			model.CallTypes = new SelectList(types, "Type", "Type");

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> New(NewTemplateModel model, CancellationToken cancellationToken)
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

				await _templatesService.SaveCallQuickTemplateAsync(model.Template, cancellationToken);
				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
		{
			var template = await _templatesService.GetCallQuickTemplateByIdAsync(id);

			if (template == null || template.DepartmentId != DepartmentId)
				Unauthorized();

			await _templatesService.DeleteCallQuickTemplateAsync(id, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> GetTemplate(int id)
		{
			var template = await _templatesService.GetCallQuickTemplateByIdAsync(id);

			return Json(template);
		}
	}
}
