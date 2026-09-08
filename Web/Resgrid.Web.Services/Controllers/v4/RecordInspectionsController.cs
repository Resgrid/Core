using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.Services.Models.v4;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>RMS-5 inspection programs, adopted code sets, inspections, violations and re-inspection (RMS plan section 4.3).</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordInspectionsController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsInspectionsService _inspections;

		public RecordInspectionsController(IRecordsInspectionsService inspections, IRecordsCutoverService cutover) : base(cutover)
		{
			_inspections = inspections;
		}

		#region Code sets and programs

		[HttpGet("CodeSets")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<CodeSetsResult>> CodeSets(bool includeInactive = false)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new CodeSetsResult { Data = (await _inspections.GetCodeSetsAsync(DepartmentId, UserId, includeInactive)).Select(RecordsRms5ApiMapper.ToCodeSet).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SaveCodeSet")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<CodeSetResult>> SaveCodeSet([FromBody] CodeSetData input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new CodeSetResult { Data = RecordsRms5ApiMapper.ToCodeSet(await _inspections.SaveCodeSetAsync(DepartmentId, UserId, new RmsCodeSet { RmsCodeSetId = input.CodeSetId, Name = input.Name, Edition = input.Edition, Jurisdiction = input.Jurisdiction, IsActive = input.IsActive }, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("CodeSections")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<CodeSectionsResult>> CodeSections(string codeSetId)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new CodeSectionsResult { Data = (await _inspections.GetCodeSectionsAsync(DepartmentId, UserId, codeSetId)).Select(RecordsRms5ApiMapper.ToCodeSection).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SaveCodeSection")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<CodeSectionResult>> SaveCodeSection([FromBody] CodeSectionData input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new CodeSectionResult { Data = RecordsRms5ApiMapper.ToCodeSection(await _inspections.SaveCodeSectionAsync(DepartmentId, UserId, new RmsCodeSection { RmsCodeSectionId = input.CodeSectionId, RmsCodeSetId = input.CodeSetId, SectionNumber = input.SectionNumber, Title = input.Title, Text = input.Text, DefaultSeverity = input.DefaultSeverity, DefaultCorrectionDays = input.DefaultCorrectionDays }, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>CSV import of code sections: section,title,text,severity,days.</summary>
		[HttpPost("ImportCodeSections")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<StandardApiResponseV4Base>> ImportCodeSections(string codeSetId, [FromBody] HydrantImportInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var created = await _inspections.ImportCodeSectionsAsync(DepartmentId, UserId, codeSetId, input?.Csv, cancellationToken); return Ok(Done(new StandardApiResponseV4Base { PageSize = created })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Programs")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<InspectionProgramsResult>> Programs(bool includeInactive = false)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new InspectionProgramsResult { Data = (await _inspections.GetProgramsAsync(DepartmentId, UserId, includeInactive)).Select(RecordsRms5ApiMapper.ToProgram).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SaveProgram")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<InspectionProgramResult>> SaveProgram([FromBody] InspectionProgramInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var program = await _inspections.SaveProgramAsync(DepartmentId, UserId, new RmsInspectionProgram { RmsInspectionProgramId = input.ProgramId, Name = input.Name, Description = input.Description, OccupancyTypesCsv = input.OccupancyTypesCsv, FrequencyMonths = input.FrequencyMonths, RmsCodeSetId = input.CodeSetId, IsActive = input.IsActive }, input.Checklist, cancellationToken);
				return Ok(Done(new InspectionProgramResult { Data = RecordsRms5ApiMapper.ToProgram(program), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		#endregion

		#region Inspections

		[HttpGet("List")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<InspectionsResult>> List(string occupancyId = null, string programId = null, [FromQuery] List<int> states = null, string inspectorUserId = null, DateTime? scheduledBefore = null, int skip = 0, int take = 50)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var query = new RmsInspectionQuery { OccupancyId = occupancyId, ProgramId = programId, States = states, InspectorUserId = inspectorUserId, ScheduledBefore = scheduledBefore, Skip = skip, Take = take };
				var r = new InspectionsResult { Data = (await _inspections.ListAsync(DepartmentId, UserId, query)).Select(RecordsRms5ApiMapper.ToInspection).ToList(), TotalCount = await _inspections.CountAsync(DepartmentId, UserId, query) };
				r.PageSize = r.Data.Count;
				return Ok(Done(r));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<InspectionResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var a = await _inspections.GetAsync(DepartmentId, UserId, id); if (a == null) return NotFound(); return Ok(Done(new InspectionResult { Data = RecordsRms5ApiMapper.ToInspectionAggregate(a), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Schedule")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<InspectionSavedResult>> Schedule([FromBody] ScheduleInspectionInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new InspectionSavedResult { Data = RecordsRms5ApiMapper.ToInspection(await _inspections.ScheduleAsync(DepartmentId, UserId, input.OccupancyId, input.ProgramId, input.ScheduledOn ?? DateTime.UtcNow, input.InspectorUserId, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Start")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<InspectionSavedResult>> Start(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new InspectionSavedResult { Data = RecordsRms5ApiMapper.ToInspection(await _inspections.StartAsync(DepartmentId, UserId, id, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Complete")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<InspectionResult>> Complete([FromBody] CompleteInspectionInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new InspectionResult { Data = RecordsRms5ApiMapper.ToInspectionAggregate(await _inspections.CompleteAsync(DepartmentId, UserId, input.InspectionId, input.Items, input.Notes, input.SignatureName, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("ScheduleReinspection")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<InspectionSavedResult>> ScheduleReinspection(string id, DateTime? scheduledOn = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new InspectionSavedResult { Data = RecordsRms5ApiMapper.ToInspection(await _inspections.ScheduleReinspectionAsync(DepartmentId, UserId, id, scheduledOn ?? DateTime.UtcNow.AddDays(30), cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Close")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<InspectionSavedResult>> Close(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new InspectionSavedResult { Data = RecordsRms5ApiMapper.ToInspection(await _inspections.CloseAsync(DepartmentId, UserId, id, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Cancel")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<InspectionSavedResult>> Cancel(string id, string reason = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new InspectionSavedResult { Data = RecordsRms5ApiMapper.ToInspection(await _inspections.CancelAsync(DepartmentId, UserId, id, reason, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("IssueNotice")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<InspectionSavedResult>> IssueNotice(string id, string noticeReference = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new InspectionSavedResult { Data = RecordsRms5ApiMapper.ToInspection(await _inspections.IssueNoticeAsync(DepartmentId, UserId, id, noticeReference, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		#endregion

		#region Violations

		[HttpGet("Violations")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<ViolationsResult>> Violations(string occupancyId = null, bool openOnly = true, int take = 200)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var rows = string.IsNullOrWhiteSpace(occupancyId) ? await _inspections.GetOpenViolationsAsync(DepartmentId, UserId, take) : await _inspections.GetViolationsForOccupancyAsync(DepartmentId, UserId, occupancyId, openOnly);
				var r = new ViolationsResult { Data = rows.Select(RecordsRms5ApiMapper.ToViolation).ToList() }; r.PageSize = r.Data.Count;
				return Ok(Done(r));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SaveViolation")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<ViolationResult>> SaveViolation([FromBody] ViolationInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new ViolationResult { Data = RecordsRms5ApiMapper.ToViolation(await _inspections.SaveViolationAsync(DepartmentId, UserId, new RmsViolation { RmsViolationId = input.ViolationId, RmsInspectionId = input.InspectionId, RmsCodeSetId = input.CodeSetId, RmsCodeSectionId = input.CodeSectionId, Description = input.Description, Severity = input.Severity, CorrectiveAction = input.CorrectiveAction, DueOn = input.DueOn }, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("TransitionViolation")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<ViolationResult>> TransitionViolation([FromBody] ViolationTransitionInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new ViolationResult { Data = RecordsRms5ApiMapper.ToViolation(await _inspections.TransitionViolationAsync(DepartmentId, UserId, input.ViolationId, (RmsViolationState)input.TargetState, input.Note, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		#endregion
	}
}
