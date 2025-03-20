using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.WebCore.Areas.User.Models.Forms;
using Resgrid.WebCore.Areas.User.Models.Protocols;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class FormsController : SecureBaseController
	{
		private readonly IFormsService _formsService;
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;

		public FormsController(IFormsService formsService, ICallsService callsService, IAuthorizationService authorizationService, IDepartmentsService departmentsService)
		{
			_formsService = formsService;
			_callsService = callsService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Forms_View)]
		public async Task<IActionResult> Index()
		{
			var model = new FormIndexModel();
			model.Forms = await _formsService.GetAllNonDeletedFormsForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Forms_Create)]
		public async Task<IActionResult> New()
		{
			var model = new NewFormModel();
			model.FormTypes = model.FormType.ToSelectListDescription();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Forms_Create)]
		public async Task<IActionResult> New(NewFormModel model, IFormCollection form, CancellationToken cancellationToken)
		{
			model.FormTypes = model.FormType.ToSelectListDescription();

			if (ModelState.IsValid)
			{
				var newForm = new Form();
				newForm.DepartmentId = DepartmentId;
				newForm.CreatedBy = UserId;
				newForm.UpdatedBy = UserId;
				newForm.Name = model.FormName;
				newForm.Data = model.Data;
				newForm.Type = (int)model.FormType;

				List<int> questions = (from object key in form.Keys where key.ToString().StartsWith("callAutomationTriggerField_") select int.Parse(key.ToString().Replace("callAutomationTriggerField_", ""))).ToList();

				if (questions.Count > 0)
					newForm.Automations = new Collection<FormAutomation>();

				foreach (var i in questions)
				{
					if (form.ContainsKey("callAutomationTriggerField_" + i))
					{
						var callAutomationTriggerField = form["callAutomationTriggerField_" + i];
						var callAutomationTriggerValue = form["callAutomationTriggerValue_" + i];
						var callAutomationOperationType = form["callAutomationOperationType_" + i];
						var callAutomationOperationValue = form["callAutomationOperationValue_" + i];

						var automation = new FormAutomation();
						automation.TriggerField = callAutomationTriggerField;
						automation.TriggerValue = callAutomationTriggerValue;
						automation.OperationType = int.Parse(callAutomationOperationType);
						automation.OperationValue = callAutomationOperationValue;

						newForm.Automations.Add(automation);
					}
				}


				await _formsService.SaveFormAsync(newForm, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Forms_View)]
		public async Task<IActionResult> View(string id)
		{
			var model = new ViewFormModel();
			var form = await _formsService.GetFormByIdAsync(id);

			if (form != null)
			{
				if (form.DepartmentId != DepartmentId)
					Unauthorized();

				model.Form = form;
				return View(model);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Forms_Update)]
		public async Task<IActionResult> Enable(string id)
		{
			var form = await _formsService.GetFormByIdAsync(id);

			if (form != null)
			{
				if (form.DepartmentId != DepartmentId)
					Unauthorized();

				await _formsService.EnableFormByIdAsync(id);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Forms_Update)]
		public async Task<IActionResult> Disable(string id)
		{
			var form = await _formsService.GetFormByIdAsync(id);

			if (form != null)
			{
				if (form.DepartmentId != DepartmentId)
					Unauthorized();

				await _formsService.DisableFormByIdAsync(id);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Forms_Delete)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var form = await _formsService.GetFormByIdAsync(id);

			if (form != null)
			{
				if (form.DepartmentId != DepartmentId)
					Unauthorized();

				await _formsService.DeleteForm(id, cancellationToken);
			}

			return RedirectToAction("Index");
		}
	}
}
