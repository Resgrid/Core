using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models.v4.Checklists;

namespace Resgrid.Web.Services.Controllers.v4
{
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0"), ApiExplorerSettings(GroupName = "v4"), Authorize]
	public sealed partial class ChecklistRunsController : ChecklistApiControllerBase
	{
		private readonly IReadinessAccessService _access;
		private readonly IDepartmentDataProtectionService _protection;
		public ChecklistRunsController(IChecklistsService checklists, IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> strings, IReadinessAccessService access, IDepartmentDataProtectionService protection) : base(checklists, strings) { _access = access; _protection = protection; }
		/// <summary>Fresh value-free scope and access state for secure local drafts. A mutation always repeats authorization and grant validation.</summary>
		[HttpGet("GetChecklistAccess")]
		public async Task<IActionResult> GetChecklistAccess()
		{
			var manage = await Checklists.CanManageAsync(Actor);
			return Reply(new { DepartmentId, UserId, CanManage = manage, Enabled = await _access.CanUseChecklistsAsync(DepartmentId), IsProtected = await _protection.IsProtectionEnforcedAsync(DepartmentId) });
		}
		/// <summary>Paged, authorized scheduled checks and published on-demand forms. Unit scope includes currently issued serialized assets when available.</summary>
		[HttpGet("GetDueChecklists")]
		public async Task<IActionResult> GetDueChecklists([FromQuery] ChecklistMobileQuery query)
		{
			var page = await Checklists.MobileDueAsync(Actor, query);
			return Reply(new ChecklistDuePageData { Occurrences = page.Occurrences.Select(ChecklistDueData.From).ToList(), Definitions = page.Definitions.Select(v => { var d = ChecklistDefinitionData.From(v.Definition); d.Targets = v.Targets; return d; }).ToList(),
				HasMoreDefinitions = page.HasMoreDefinitions, HasMoreOccurrences = page.HasMoreOccurrences }, page.Occurrences.Count + page.Definitions.Count, page.HasMoreDefinitions || page.HasMoreOccurrences);
		}
		/// <summary>Reads a run by ID, or previews a pinned scheduled occurrence without creating a run.</summary>
		[HttpGet("GetChecklistRun")]
		public async Task<IActionResult> GetChecklistRun(string id = null, string occurrenceId = null)
		{
			if ((id == null) == (occurrenceId == null)) throw new ChecklistException(400, "The form content is invalid.");
			return Reply(ChecklistRunData.From(id == null ? await Checklists.PreviewOccurrenceAsync(Actor, occurrenceId) : await Checklists.GetRunAsync(Actor, id), UserId));
		}
		/// <summary>Starts or resumes one logical client GUID. The supplied published version is preserved for an offline on-demand run.</summary>
		[HttpPost("StartChecklistRun")]
		public async Task<IActionResult> StartChecklistRun([FromBody] ChecklistStartInput input)
		{
			Required(input);
			if (input.CompletionId == null || (input.OccurrenceId == null) == (input.DefinitionId == null) || input.OccurrenceId == null && input.VersionId == null) throw new ChecklistException(400, "The form content is invalid.");
			var id = input.OccurrenceId == null ? await Checklists.StartPinnedAsync(Actor, input.DefinitionId, input.VersionId, input.TargetId, input.CompletionId)
				: await Checklists.StartOccurrenceWithIdAsync(Actor, input.OccurrenceId, input.CompletionId);
			return Reply(ChecklistRunData.From(await Checklists.GetRunAsync(Actor, id), UserId));
		}
		[HttpPost("SaveChecklistRunProgress")]
		public Task<IActionResult> SaveChecklistRunProgress([FromBody] ChecklistProgressInput input) => Save(input, false);
		/// <summary>Replays with the same validated answers/evidence produce one completion and one set of Workflow events. Different stale content returns 409.</summary>
		[HttpPost("CompleteChecklistRun")]
		public Task<IActionResult> CompleteChecklistRun([FromBody] ChecklistProgressInput input) => Save(input, true);
		private async Task<IActionResult> Save(ChecklistProgressInput input, bool submit)
		{
			Required(input); Required(input.Input);
			await Checklists.SaveRunAsync(Actor, input.Id, input.Input, submit);
			return Reply(ChecklistRunData.From(await Checklists.GetRunAsync(Actor, input.Id), UserId));
		}
		[HttpPost("AttestChecklistRun")]
		public async Task<IActionResult> AttestChecklistRun([FromBody] ChecklistWitnessInput input)
		{ Required(input); await Checklists.WitnessAsync(Actor, input.Id, input.SubmissionHash, input.Attestation); return Reply(ChecklistRunData.From(await Checklists.GetRunAsync(Actor, input.Id), UserId)); }
		[HttpPost("SkipChecklistOccurrence")]
		public async Task<IActionResult> SkipChecklistOccurrence([FromBody] ChecklistSkipInput input)
		{ Required(input); await Checklists.SkipOccurrenceAsync(Actor, input.Id, input.Revision, input.Reason); return Reply(true); }
		[HttpPost("UploadChecklistRunFile"), Consumes("multipart/form-data"), RequestSizeLimit(11 * 1024 * 1024)]
		public async Task<IActionResult> UploadChecklistRunFile([FromForm] string id, [FromForm] string itemId, [FromForm] int revision, IFormFile file)
		{
			if (file == null || file.Length == 0 || file.Length > 10 * 1024 * 1024) throw new ChecklistException(400, "Choose an evidence image up to 10 MB.");
			using var stream = new MemoryStream(); await file.CopyToAsync(stream);
			await Checklists.AddFileAtRevisionAsync(Actor, id, itemId, revision, file.FileName, file.ContentType, stream.ToArray());
			return Reply(ChecklistRunData.From(await Checklists.GetRunAsync(Actor, id), UserId));
		}
		/// <summary>In-memory image transport for native clients, with the same size, scan, revision and ADP rules as multipart uploads.</summary>
		[HttpPost("UploadChecklistRunFile"), Consumes("application/json"), RequestSizeLimit(15 * 1024 * 1024)]
		public async Task<IActionResult> UploadChecklistRunEvidence([FromBody] ChecklistEvidenceInput input)
		{
			Required(input); await Checklists.AddFileAtRevisionAsync(Actor, input.Id, input.ItemId, input.Revision, input.Name, input.ContentType, input.Data);
			return Reply(ChecklistRunData.From(await Checklists.GetRunAsync(Actor, input.Id), UserId));
		}
		[HttpGet("GetChecklistRunFile")]
		public async Task<IActionResult> GetChecklistRunFile(string id)
		{ var file = await Checklists.GetFileAsync(Actor, id); Response.Headers["X-Content-Type-Options"] = "nosniff"; return File(file.Data, file.ContentType, file.Content); }
		[HttpPost("DeleteChecklistRunFile")]
		public async Task<IActionResult> DeleteChecklistRunFile([FromBody] ChecklistCommandInput input)
		{ Required(input); await Checklists.DeleteFileAtRevisionAsync(Actor, input.Id, input.Revision); return Reply(true); }
		[HttpGet("GetChecklistHistory")]
		public async Task<IActionResult> GetChecklistHistory([FromQuery] ChecklistMobileQuery query)
		{ var rows = await Checklists.MobileHistoryAsync(Actor, query); return Reply(rows.Take(50).Select(ChecklistHistoryData.From).ToList(), System.Math.Min(rows.Count, 50), rows.Count > 50); }
	}
}
