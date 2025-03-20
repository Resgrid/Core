using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Training;
using Resgrid.WebCore.Areas.User.Models.Templates;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class TemplatesController : SecureBaseController
	{
		private readonly ITemplatesService _templatesService;
		private readonly ICallsService _callsService;
		private readonly IAutofillsService _autofillsService;

		public TemplatesController(ITemplatesService templatesService, ICallsService callsService,
			IAutofillsService autofillsService)
		{
			_templatesService = templatesService;
			_callsService = callsService;
			_autofillsService = autofillsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> Index()
		{
			var model = new TemplateIndexModel();
			model.CallQuickTemplates = await _templatesService.GetAllCallQuickTemplatesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> CallNotes()
		{
			var model = new CallNotesModel();
			model.CallNotes = await _autofillsService.GetAllAutofillsForDepartmentByTypeAsync(DepartmentId, AutofillTypes.CallNote);
			model.CallNotes = model.CallNotes.OrderBy(x => x.Sort).ToList();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
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
		[Authorize(Policy = ResgridResources.Department_Update)]
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
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Edit(int id)
		{
			var model = new NewTemplateModel();
			model.Template = new CallQuickTemplate();

			model.Template = await _templatesService.GetCallQuickTemplateByIdAsync(id);

			if (model.Template == null || model.Template.DepartmentId != DepartmentId)
				Unauthorized();	

			var priorites = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId);
			model.CallPriorities = new SelectList(priorites, "DepartmentCallPriorityId", "Name", priorites.FirstOrDefault(x => x.IsDefault));


			List<CallType> types = new List<CallType>();
			types.Add(new CallType { CallTypeId = 0, Type = "No Type" });
			types.AddRange(await _callsService.GetCallTypesForDepartmentAsync(DepartmentId));
			model.CallTypes = new SelectList(types, "Type", "Type");

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Edit(NewTemplateModel model, CancellationToken cancellationToken)
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
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewCallNote()
		{
			var model = new NewCallNoteModel();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewCallNote(NewCallNoteModel model, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(model.Name) &&
				String.IsNullOrWhiteSpace(model.Data))
			{
				model.Message = "You must specify a call name and/or call nature to set to save the template";
				return View(model);
			}

			if (ModelState.IsValid)
			{
				var autofill = new Autofill();
				autofill.Name = model.Name;
				autofill.Data = model.Data;
				autofill.Type = (int)AutofillTypes.CallNote;
				autofill.Sort = model.Sort;
				autofill.DepartmentId = DepartmentId;
				autofill.AddedByUserId = UserId;
				autofill.AddedOn = DateTime.UtcNow;

				await _autofillsService.SaveAutofillAsync(autofill, cancellationToken);
				return RedirectToAction("CallNotes");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditCallNote(string id)
		{
			var model = new EditCallNoteModel();
			model.Autofill = await _autofillsService.GetAutofillByIdAsync(id);

			if (model.Autofill == null || model.Autofill.DepartmentId != DepartmentId)
				Unauthorized();	

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditCallNote(EditCallNoteModel model, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(model.Autofill.Name) &&
				String.IsNullOrWhiteSpace(model.Autofill.Data))
			{
				model.Message = "You must specify a call name and/or call nature to set to save the template";
				return View(model);
			}

			if (ModelState.IsValid)
			{
				model.Autofill.Type = (int)AutofillTypes.CallNote;
				model.Autofill.DepartmentId = DepartmentId;
				model.Autofill.AddedByUserId = UserId;
				model.Autofill.AddedOn = DateTime.UtcNow;

				await _autofillsService.SaveAutofillAsync(model.Autofill, cancellationToken);
				return RedirectToAction("CallNotes");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
		{
			var template = await _templatesService.GetCallQuickTemplateByIdAsync(id);

			if (template == null || template.DepartmentId != DepartmentId)
				Unauthorized();

			await _templatesService.DeleteCallQuickTemplateAsync(id, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteCallNote(string id, CancellationToken cancellationToken)
		{
			var template = await _autofillsService.GetAutofillByIdAsync(id);

			if (template == null || template.DepartmentId != DepartmentId)
				Unauthorized();

			await _autofillsService.DeleteAutofillAsync(id, cancellationToken);

			return RedirectToAction("CallNotes");
		}

		[HttpGet]
		public async Task<IActionResult> GetTemplate(int id)
		{
			var template = await _templatesService.GetCallQuickTemplateByIdAsync(id);

			return Json(template);
		}
	}
}
