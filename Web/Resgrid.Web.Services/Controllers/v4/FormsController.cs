using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Web.Services.Models.v4.Forms;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// User generated forms that are displayed to get custom information for New Calls, Unit Checks, etc
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class FormsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IFormsService _formsService;

		public FormsController(IFormsService formsService)
		{
			_formsService = formsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the Department Form that can be used for the new call process (i.e. call intake/triage form)
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetNewCallForm")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Forms_View)]
		public async Task<ActionResult<FormResult>> GetNewCallForm()
		{
			var result = new FormResult();
			var form = await _formsService.GetNewCallFormByDepartmentIdAsync(DepartmentId);

			if (form != null)
			{
				result.Data = ConvertFormResultData(form);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all active (non-deleted) forms for the department. Used by mobile apps to
		/// retrieve available forms for calls and other entities.
		/// </summary>
		/// <returns>Array of FormResultData for each active form in the department</returns>
		[HttpGet("GetAllForms")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Forms_View)]
		public async Task<ActionResult<FormsResult>> GetAllForms()
		{
			var result = new FormsResult();

			var forms = await _formsService.GetAllNonDeletedFormsForDepartmentAsync(DepartmentId);

			if (forms != null && forms.Any())
			{
				foreach (var form in forms)
				{
					result.Data.Add(ConvertFormResultData(form));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets a specific form by its identifier
		/// </summary>
		/// <param name="formId">The form identifier</param>
		/// <returns>A FormResultData for the requested form</returns>
		[HttpGet("GetFormById")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Forms_View)]
		public async Task<ActionResult<FormResult>> GetFormById(string formId)
		{
			if (string.IsNullOrWhiteSpace(formId))
				return BadRequest();

			var result = new FormResult();
			var form = await _formsService.GetFormByIdAsync(formId);

			if (form == null || form.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			result.Data = ConvertFormResultData(form);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		public static FormResultData ConvertFormResultData(Model.Form form)
		{
			var formResult = new FormResultData
			{
				Id = form.FormId,
				Name = form.Name,
				Type = form.Type,
				Data = form.Data
			};

			if (form.Automations != null && form.Automations.Any())
			{
				formResult.Automations = new List<FormDataAutomationResult>();

				foreach (var automation in form.Automations)
				{
					formResult.Automations.Add(new FormDataAutomationResult
					{
						Id = automation.FormAutomationId,
						FormId = automation.FormId,
						TriggerField = automation.TriggerField,
						TriggerValue = automation.TriggerValue,
						OperationType = automation.OperationType,
						OperationValue = automation.OperationValue
					});
				}
			}

			return formResult;
		}
	}
}
