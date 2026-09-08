using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.Services.Models.v4;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// RMS-5 investigation cases (RMS plan section 4.4). RecordRestricted_View is the policy; the service enforces
	/// case membership on top of it and audits every read, so a department administrator without a membership sees
	/// nothing here. Findings never touch the linked incident revision.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Authorize(Policy = ResgridResources.RecordRestricted_View)]
	public class RecordInvestigationsController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsInvestigationsService _investigations;

		public RecordInvestigationsController(IRecordsInvestigationsService investigations, IRecordsCutoverService cutover) : base(cutover)
		{
			_investigations = investigations;
		}

		private InvestigationCaseSavedResult Wrap(RmsInvestigationCase c) => Done(new InvestigationCaseSavedResult { Data = RecordsRms5ApiMapper.ToCase(c), PageSize = 1 });

		[HttpGet("MyCases")]
		public async Task<ActionResult<InvestigationCasesResult>> MyCases(bool includeClosed = false)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new InvestigationCasesResult { Data = (await _investigations.ListMyCasesAsync(DepartmentId, UserId, includeClosed)).Select(RecordsRms5ApiMapper.ToCase).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		public async Task<ActionResult<InvestigationCaseResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var a = await _investigations.GetAsync(DepartmentId, UserId, id, IpAddressHelper.GetRequestIP(Request, true)); if (a == null) return NotFound(); return Ok(Done(new InvestigationCaseResult { Data = RecordsRms5ApiMapper.ToCaseAggregate(a), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Open")]
		public async Task<ActionResult<InvestigationCaseSavedResult>> Open([FromBody] OpenCaseInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Wrap(await _investigations.OpenAsync(DepartmentId, UserId, input.Title, input.OccupancyId, input.CallId, input.IncidentSummary, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Update")]
		public async Task<ActionResult<InvestigationCaseSavedResult>> Update([FromBody] UpdateCaseInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Wrap(await _investigations.UpdateAsync(DepartmentId, UserId, new RmsInvestigationCase { RmsInvestigationCaseId = input.CaseId, RowVersion = input.RowVersion, Title = input.Title, IncidentSummary = input.IncidentSummary, RmsOccupancyId = input.OccupancyId, CallId = input.CallId, LeadInvestigatorUserId = input.LeadInvestigatorUserId }, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("AddMember")]
		public async Task<ActionResult<CaseMemberResult>> AddMember([FromBody] CaseMemberInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new CaseMemberResult { Data = RecordsRms5ApiMapper.ToMember(await _investigations.AddMemberAsync(DepartmentId, UserId, input.CaseId, input.UserId, (RmsInvestigationRole)input.Role, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("RemoveMember")]
		public async Task<ActionResult<StandardApiResponseV4Base>> RemoveMember(string caseId, string memberId, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _investigations.RemoveMemberAsync(DepartmentId, UserId, caseId, memberId, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("LinkIncident")]
		public async Task<ActionResult<CaseIncidentResult>> LinkIncident(string caseId, string recordId, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new CaseIncidentResult { Data = RecordsRms5ApiMapper.ToIncident(await _investigations.LinkIncidentAsync(DepartmentId, UserId, caseId, recordId, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("UnlinkIncident")]
		public async Task<ActionResult<StandardApiResponseV4Base>> UnlinkIncident(string caseId, string linkId, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _investigations.UnlinkIncidentAsync(DepartmentId, UserId, caseId, linkId, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SaveNote")]
		public async Task<ActionResult<InvestigationNoteResult>> SaveNote([FromBody] CaseNoteInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var note = string.IsNullOrWhiteSpace(input.NoteId)
					? await _investigations.AddNoteAsync(DepartmentId, UserId, input.CaseId, (RmsInvestigationNoteKind)input.Kind, input.OccurredOn ?? DateTime.UtcNow, input.Subject, input.Body, cancellationToken)
					: await _investigations.UpdateNoteAsync(DepartmentId, UserId, input.NoteId, input.Subject, input.Body, cancellationToken);
				return Ok(Done(new InvestigationNoteResult { Data = RecordsRms5ApiMapper.ToNote(note), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("AddEvidence")]
		public async Task<ActionResult<InvestigationEvidenceResult>> AddEvidence([FromBody] CaseEvidenceInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var evidence = await _investigations.AddEvidenceAsync(DepartmentId, UserId, input.CaseId, new RmsInvestigationEvidence { Kind = input.Kind, Description = input.Description, CollectedOn = input.CollectedOn ?? default, CollectedByUserId = input.CollectedByUserId, CollectedFrom = input.CollectedFrom, StorageLocation = input.StorageLocation }, cancellationToken);
				return Ok(Done(new InvestigationEvidenceResult { Data = RecordsRms5ApiMapper.ToEvidence(evidence), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("TransferCustody")]
		public async Task<ActionResult<CustodyResult>> TransferCustody([FromBody] CustodyTransferInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new CustodyResult { Data = RecordsRms5ApiMapper.ToCustody(await _investigations.TransferCustodyAsync(DepartmentId, UserId, input.EvidenceId, input.ToUserId, input.ToExternal, input.Reason, (RmsEvidenceState)input.ResultingState, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("CustodyChain")]
		public async Task<ActionResult<CustodyChainResult>> CustodyChain(string evidenceId)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new CustodyChainResult { Data = (await _investigations.GetCustodyChainAsync(DepartmentId, UserId, evidenceId)).Select(RecordsRms5ApiMapper.ToCustody).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("AddReferral")]
		public async Task<ActionResult<ReferralResult>> AddReferral([FromBody] ReferralInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new ReferralResult { Data = RecordsRms5ApiMapper.ToReferral(await _investigations.AddReferralAsync(DepartmentId, UserId, input.CaseId, input.Agency, input.Reason, input.ReferenceNumber, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("UpdateReferralState")]
		public async Task<ActionResult<ReferralResult>> UpdateReferralState(string referralId, int state, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new ReferralResult { Data = RecordsRms5ApiMapper.ToReferral(await _investigations.UpdateReferralStateAsync(DepartmentId, UserId, referralId, (RmsReferralState)state, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("RecordFindings")]
		public async Task<ActionResult<InvestigationCaseSavedResult>> RecordFindings([FromBody] FindingsInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Wrap(await _investigations.RecordFindingsAsync(DepartmentId, UserId, input.CaseId, (RmsFireCauseClassification)input.Classification, input.CauseDetail, input.OriginDescription, input.Findings, input.RecommendsIncidentAmendment, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("ApproveFindings")]
		public async Task<ActionResult<InvestigationCaseSavedResult>> ApproveFindings(string caseId, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Wrap(await _investigations.ApproveFindingsAsync(DepartmentId, UserId, caseId, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("ReturnFindings")]
		public async Task<ActionResult<InvestigationCaseSavedResult>> ReturnFindings([FromBody] CaseReasonInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Wrap(await _investigations.ReturnFindingsAsync(DepartmentId, UserId, input.CaseId, input.Reason, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Close")]
		public async Task<ActionResult<InvestigationCaseSavedResult>> Close([FromBody] CaseReasonInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Wrap(await _investigations.CloseAsync(DepartmentId, UserId, input.CaseId, input.Reason, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Reopen")]
		public async Task<ActionResult<InvestigationCaseSavedResult>> Reopen([FromBody] CaseReasonInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Wrap(await _investigations.ReopenAsync(DepartmentId, UserId, input.CaseId, input.Reason, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>The case packet (JSON); audited as an export and refused while any content is concealed.</summary>
		[HttpGet("Export")]
		public async Task<IActionResult> Export(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var bytes = await _investigations.ExportAsync(DepartmentId, UserId, id, IpAddressHelper.GetRequestIP(Request, true), cancellationToken);
				return File(bytes, "application/json", $"investigation-{id}.json");
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("AccessAudit")]
		public async Task<ActionResult<CaseAuditResult>> AccessAudit(string id, int take = 100)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new CaseAuditResult { Data = (await _investigations.GetAccessAuditAsync(DepartmentId, UserId, id, take)).Select(a => new CaseAuditEntryData { Action = a.Action, ActorUserId = a.ActorUserId, Purpose = a.Purpose, Successful = a.Successful, OccurredOn = a.OccurredOn, DetailJson = a.DetailJson }).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}
	}
}
