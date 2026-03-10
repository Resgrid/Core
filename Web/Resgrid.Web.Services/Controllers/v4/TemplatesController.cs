using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Templates;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Templates in the system. Templates can be call Templates, Autofills (i.e. Call Notes)
	/// and a few other template types.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class TemplatesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IAutofillsService _autofillsService;
		private readonly ITemplatesService _templatesService;

		public TemplatesController(IAutofillsService autofillsService, ITemplatesService templatesService)
		{
			_autofillsService = autofillsService;
			_templatesService = templatesService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all call note Templates for the department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllCallNoteTemplates")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<CallNoteTemplatesResult>> GetAllCallNoteTemplates()
		{
			var result = new CallNoteTemplatesResult();

			var templates = await _autofillsService.GetAllAutofillsForDepartmentByTypeAsync(DepartmentId, AutofillTypes.CallNote);

			if (templates != null && templates.Any())
			{
				foreach (var template in templates)
				{
					result.Data.Add(ConvertCallNoteTemplateResultData(template));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			result.Data = result.Data.OrderBy(x => x.Sort).ToList();

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets all call quick templates for the department. Call quick templates are used to
		/// rapidly dispatch pre-configured calls from mobile apps.
		/// </summary>
		/// <returns>Array of CallQuickTemplateResultData for each active call quick template</returns>
		[HttpGet("GetAllCallQuickTemplates")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallQuickTemplatesResult>> GetAllCallQuickTemplates()
		{
			var result = new CallQuickTemplatesResult();

			var templates = await _templatesService.GetAllCallQuickTemplatesForDepartmentAsync(DepartmentId);

			if (templates != null && templates.Any())
			{
				foreach (var template in templates)
				{
					result.Data.Add(ConvertCallQuickTemplateResultData(template));
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

		public static CallNoteTemplateResultData ConvertCallNoteTemplateResultData(Autofill template)
		{
			var callNoteTemplateResultData = new CallNoteTemplateResultData();
			callNoteTemplateResultData.Id = template.AutofillId;
			callNoteTemplateResultData.Type = template.Type;
			callNoteTemplateResultData.Sort = template.Sort;
			callNoteTemplateResultData.Name = template.Name;
			callNoteTemplateResultData.Data = template.Data;
			callNoteTemplateResultData.AddedOn = template.AddedOn.ToString();
			callNoteTemplateResultData.AddedByUserId = template.AddedByUserId;

			return callNoteTemplateResultData;
		}

		public static CallQuickTemplateResultData ConvertCallQuickTemplateResultData(CallQuickTemplate template)
		{
			return new CallQuickTemplateResultData
			{
				Id = template.CallQuickTemplateId.ToString(),
				IsDisabled = template.IsDisabled,
				Name = template.Name,
				CallName = template.CallName,
				CallNature = template.CallNature,
				CallType = template.CallType,
				CallPriority = template.CallPriority,
				CreatedByUserId = template.CreatedByUserId,
				CreatedOn = template.CreatedOn
			};
		}
	}
}
