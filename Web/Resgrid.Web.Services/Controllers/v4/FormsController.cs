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
	/// User generated forms that are dispayed to get custom information for New Calls, Unit Checks, etc
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
				var formResult = new FormResultData();
				formResult.Id = form.FormId;
				formResult.Name = form.Name;
				formResult.Type = form.Type;
				formResult.Data = form.Data;

				if (form.Automations != null && form.Automations.Any())
				{
					formResult.Automations = new List<FormDataAutomationResult>();

					foreach (var automation in form.Automations)
					{
						var automationResult = new FormDataAutomationResult();
						automationResult.Id = automation.FormAutomationId;
						automationResult.FormId = automation.FormId;
						automationResult.TriggerField = automation.TriggerField;
						automationResult.TriggerValue = automation.TriggerValue;
						automationResult.OperationType = automation.OperationType;
						automationResult.OperationValue = automation.OperationValue;

						formResult.Automations.Add(automationResult);
					}
				}

				result.Data = formResult;
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
	}
}
