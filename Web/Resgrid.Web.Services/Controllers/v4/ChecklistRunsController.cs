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
		private readonly IChecklistWitnessSignatureService _witness;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _mfaPolicy;
		public ChecklistRunsController(IChecklistsService checklists, IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> strings, IReadinessAccessService access, IDepartmentDataProtectionService protection,
			IChecklistWitnessSignatureService witness, IMfaEvidenceService evidence, IMfaPolicyService mfaPolicy, IStepUpCapabilityService stepUp) : base(checklists, strings)
		{ _access = access; _protection = protection; _witness = witness; _evidence = evidence; _mfaPolicy = mfaPolicy; _stepUp = stepUp; }
		private readonly IStepUpCapabilityService _stepUp;
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
		/// <summary>
		/// e-Signature S3 (free with Checklists): the witness of a form that requires a witness signature signs at L2 — a step-up recorded
		/// for this session — bound to the completion's operational content hash; the run completes in the same transaction. A refused
		/// signature (StepUpRequired...) leaves the run awaiting its witness. AttestChecklistRun answers 409 for such a form.
		/// </summary>
		[HttpPost("SignChecklistRunWitness")]
		public async Task<IActionResult> SignChecklistRunWitness([FromBody] ChecklistWitnessInput input, System.Threading.CancellationToken cancellationToken)
		{
			Required(input);
			var proof = await Resgrid.Web.Services.Helpers.ApiSignatureProof.BuildAsync(HttpContext, UserId, DepartmentId, _evidence, _mfaPolicy, cancellationToken);
			var outcome = await _witness.SignAsync(Actor, input.Id, input.SubmissionHash, input.Attestation, proof, System.Globalization.CultureInfo.CurrentUICulture.Name, cancellationToken);
			if (outcome == Resgrid.Model.SignatureOutcomes.StepUpRequired)
				return await Resgrid.Web.Services.Helpers.ApiStepUp.RefuseAsync(HttpContext, _stepUp, DepartmentId, UserId, Resgrid.Model.Security.StepUpArea.Signatures,
					cancellationToken);
			if (outcome != Resgrid.Model.SignatureOutcomes.Signed)
			{
				// StepUpRequired tells the app to step up and retry; the run still awaits its witness.
				var refused = new ChecklistApiResult<object> { Data = new { Signed = false, Outcome = outcome.ToString() }, Status = Resgrid.Web.Services.Helpers.ResponseHelper.Failure, PageSize = 1 };
				Resgrid.Web.Services.Helpers.ResponseHelper.PopulateV4ResponseData(refused);
				return StatusCode(StatusCodes.Status403Forbidden, refused);
			}
			return Reply(ChecklistRunData.From(await Checklists.GetRunAsync(Actor, input.Id), UserId));
		}
		/// <summary>S3: the witness signature panel of a run: whether the form requires it, the statement, the content hash and the signatures.</summary>
		[HttpGet("GetChecklistRunWitnessSignature")]
		public async Task<IActionResult> GetChecklistRunWitnessSignature(string id)
		{
			var view = await _witness.GetAsync(Actor, id, System.Globalization.CultureInfo.CurrentUICulture.Name);
			return Reply(new { view.Required, view.AwaitingWitness, view.CanSign, view.SubmissionHash, view.ArtifactSha256, ContentCode = Resgrid.Model.SignaturePolicy.ShortCode(view.ArtifactSha256), view.Statement,
				Signatures = view.Signatures.Select(s => new { s.Record.SignatureRecordId, s.Record.SignerNameSnapshot, s.Record.Level, s.Record.Method, s.Record.SignedOn, State = (int)s.State }).ToList() });
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
		/// <remarks>Shares the multipart action's path (the Responder and Unit apps post JSON to it), so it is hidden from the OpenAPI
		/// document: Swashbuckle rejects the whole document when two visible actions share a method and path (RESGRID-API-9F).</remarks>
		[HttpPost("UploadChecklistRunFile"), Consumes("application/json"), RequestSizeLimit(15 * 1024 * 1024), ApiExplorerSettings(IgnoreApi = true)]
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
