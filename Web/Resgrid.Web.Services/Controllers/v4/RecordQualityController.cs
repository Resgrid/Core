using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.Services.Models.v4;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>RMS-4 optional post-finalization quality review (RMS plan section 4.7): rubrics, sampling, scoring and trends. Rubrics are department-administrator work; sampling and scoring need Record_Review.</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordQualityController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsQualityReviewService _quality;

		public RecordQualityController(IRecordsQualityReviewService quality, IRecordsCutoverService cutover) : base(cutover)
		{
			_quality = quality;
		}

		[HttpGet("Rubrics")]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<QualityRubricsResult>> Rubrics(bool includeInactive = false)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new QualityRubricsResult { Data = (await _quality.GetRubricsAsync(DepartmentId, UserId, includeInactive)).Select(RecordsRms5ApiMapper.ToRubric).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SaveRubric")]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<QualityRubricResult>> SaveRubric([FromBody] QualityRubricInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new QualityRubricResult { Data = RecordsRms5ApiMapper.ToRubric(await _quality.SaveRubricAsync(DepartmentId, UserId, new RmsQualityRubric { RmsQualityRubricId = input.RubricId, Name = input.Name, DefinitionKey = input.DefinitionKey, SampleSize = input.SampleSize, IsActive = input.IsActive }, input.Criteria, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Sample")]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<QualityReviewsResult>> Sample([FromBody] QualitySampleInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { var r = new QualityReviewsResult { Data = (await _quality.SampleAsync(DepartmentId, UserId, input.RubricId, input.Since ?? DateTime.UtcNow.AddDays(-30), cancellationToken)).Select(RecordsRms5ApiMapper.ToReview).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Pending")]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<QualityReviewsResult>> Pending(int take = 100)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new QualityReviewsResult { Data = (await _quality.GetPendingAsync(DepartmentId, UserId, take)).Select(RecordsRms5ApiMapper.ToReview).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<QualityReviewResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var review = await _quality.GetReviewAsync(DepartmentId, UserId, id); if (review == null) return NotFound(); return Ok(Done(new QualityReviewResult { Data = RecordsRms5ApiMapper.ToReview(review), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Score")]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<QualityReviewResult>> Score([FromBody] QualityScoreInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new QualityReviewResult { Data = RecordsRms5ApiMapper.ToReview(await _quality.ScoreAsync(DepartmentId, UserId, input.ReviewId, input.Findings, input.Note, input.AmendmentRecommended, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>QA notes attached to a record, for the record detail view. Needs only visibility of the record.</summary>
		[HttpGet("ForRecord")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<QualityReviewsResult>> ForRecord(string recordId)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new QualityReviewsResult { Data = (await _quality.GetForRecordAsync(DepartmentId, UserId, recordId)).Select(RecordsRms5ApiMapper.ToReview).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Trends")]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<ActionResult<QualityTrendsResult>> Trends(DateTime? since = null)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new QualityTrendsResult { Data = await _quality.GetTrendsAsync(DepartmentId, UserId, since ?? DateTime.UtcNow.AddDays(-90)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}
	}

	/// <summary>Files on prevention and investigation aggregates. Authorization follows the parent (see the service).</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordPreventionAttachmentsController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsPreventionAttachmentsService _attachments;

		public RecordPreventionAttachmentsController(IRecordsPreventionAttachmentsService attachments, IRecordsCutoverService cutover) : base(cutover)
		{
			_attachments = attachments;
		}

		[HttpGet("List")]
		public async Task<ActionResult<PreventionAttachmentsResult>> List(int parentKind, string parentId)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new PreventionAttachmentsResult { Data = (await _attachments.GetMetadataAsync(DepartmentId, UserId, (RmsPreventionParentKind)parentKind, parentId)).Select(a => RecordsRms5ApiMapper.ToAttachment(a, false)).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Uploads a base64 file through the record-attachment hygiene and scanner pipeline.</summary>
		[HttpPost("Add")]
		public async Task<ActionResult<PreventionAttachmentResult>> Add([FromBody] PreventionAttachmentInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null || string.IsNullOrWhiteSpace(input.Data)) return BadRequest();
			try
			{
				byte[] bytes;
				try { bytes = Convert.FromBase64String(input.Data); }
				catch (FormatException) { return Problem(statusCode: 400, title: "Data must be base64.", type: "record_prevention_validation"); }
				var attachment = await _attachments.AddAsync(DepartmentId, UserId, (RmsPreventionParentKind)input.ParentKind, input.ParentId, input.FileName, input.ContentType, bytes, input.Description, false, cancellationToken);
				return Ok(Done(new PreventionAttachmentResult { Data = RecordsRms5ApiMapper.ToAttachment(attachment, false), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		public async Task<ActionResult<PreventionAttachmentResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var a = await _attachments.GetWithDataAsync(DepartmentId, UserId, id); if (a == null) return NotFound(); return Ok(Done(new PreventionAttachmentResult { Data = RecordsRms5ApiMapper.ToAttachment(a, true), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Download")]
		public async Task<IActionResult> Download(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var a = await _attachments.GetWithDataAsync(DepartmentId, UserId, id); if (a?.Data == null) return NotFound(); return File(a.Data, a.ContentType ?? "application/octet-stream", a.FileName ?? "attachment"); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("Remove")]
		public async Task<ActionResult<StandardApiResponseV4Base>> Remove(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _attachments.RemoveAsync(DepartmentId, UserId, id, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}
	}

	/// <summary>RMS-4 release telemetry and the prevention summary (plan section 6, RMS-4 "baseline telemetry").</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordsHealthController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsReleaseTelemetryService _telemetry;
		private readonly IRecordsPreventionSweepService _prevention;

		public RecordsHealthController(IRecordsReleaseTelemetryService telemetry, IRecordsPreventionSweepService prevention, IRecordsCutoverService cutover) : base(cutover)
		{
			_telemetry = telemetry;
			_prevention = prevention;
		}

		/// <summary>Department-administrator only: legacy-write attempts, outbox lag, Workflow run outcomes, scan states and the prevention counters.</summary>
		[HttpGet("Telemetry")]
		public async Task<ActionResult<RecordsReleaseTelemetryResult>> Telemetry(int windowHours = 24)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new RecordsReleaseTelemetryResult { Data = await _telemetry.GetAsync(DepartmentId, UserId, windowHours), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("PreventionSummary")]
		public async Task<ActionResult<PreventionSummaryResult>> PreventionSummary()
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new PreventionSummaryResult { Data = await _prevention.GetSummaryAsync(DepartmentId, UserId), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}
	}
}
