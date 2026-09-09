using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Checklists;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public partial class ChecklistsController : ChecklistApiControllerBase
	{
		private readonly IChecklistTemplateService _templates;
		public ChecklistsController(IChecklistTemplateService templates, IChecklistsService checklists, IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> strings) : base(checklists, strings) => _templates = templates;

		/// <summary>Searches the free starter catalog for the authenticated department.</summary>
		[HttpGet("GetChecklistTemplates")]
		public async Task<ActionResult<ChecklistTemplatesResult>> GetChecklistTemplates(string query = null)
		{
			if (query?.Length > 256)
				return BadRequest("Search must be 256 characters or fewer.");
			var templates = await _templates.SearchAsync(DepartmentId, query);
			if (templates == null)
				return NotFound();
			var result = new ChecklistTemplatesResult { Data = templates, PageSize = templates.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Returns a starter template preview, including critical checks and witness requirements.</summary>
		[HttpGet("GetChecklistTemplate")]
		public async Task<ActionResult<ChecklistTemplateResult>> GetChecklistTemplate(string templateId)
		{
			if (string.IsNullOrWhiteSpace(templateId) || templateId.Length > 80)
				return BadRequest("A valid template ID is required.");
			var template = await _templates.GetByIdAsync(DepartmentId, templateId);
			if (template == null)
				return NotFound();
			var result = new ChecklistTemplateResult { Data = template, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
