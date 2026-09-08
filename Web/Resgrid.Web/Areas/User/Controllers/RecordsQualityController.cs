using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>RMS-4 optional post-finalization quality review (RMS plan section 4.7): rubrics, deterministic sampling, scoring and trends.</summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_Review)]
	public class RecordsQualityController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsQualityReviewService _quality;
		private readonly IRecordDefinitionsService _definitions;

		public RecordsQualityController(IRecordsQualityReviewService quality, IRecordDefinitionsService definitions, IRecordsCutoverService cutover, IFeatureToggleService featureToggles,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_quality = quality;
			_definitions = definitions;
		}

		private const string Flag = FeatureFlagKeys.RecordsQualityReview;

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordsQualityIndexView { Rubrics = await _quality.GetRubricsAsync(DepartmentId, UserId, true), Pending = await _quality.GetPendingAsync(DepartmentId, UserId, 200) });
				model.CanManageRubrics = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpGet]
		public async Task<IActionResult> Rubric(string id = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordsQualityRubricView());
				if (!string.IsNullOrWhiteSpace(id))
				{
					var rubric = await _quality.GetRubricAsync(DepartmentId, UserId, id);
					if (rubric == null) return NotFound();
					model.Rubric = rubric;
					model.CriteriaText = RecordsQualityRubricView.FormatCriteria(ParseCriteria(rubric.CriteriaJson));
				}
				await PopulateAsync(model);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Rubric(RecordsQualityRubricView model, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				await _quality.SaveRubricAsync(DepartmentId, UserId, model.Rubric, RecordsQualityRubricView.ParseCriteria(model.CriteriaText), cancellationToken);
				Notify("RubricSaved");
				return RedirectToAction(nameof(Index));
			}
			catch (Exception ex)
			{
				var failure = Fail(ex);
				if (failure != null) return failure;
				Prepare(model);
				model.ErrorMessage = TempData["RecordsError"] as string;
				await PopulateAsync(model);
				return View(model);
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Sample(string rubricId, string since, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { var reviews = await _quality.SampleAsync(DepartmentId, UserId, rubricId, ParseUtc(since) ?? DateTime.UtcNow.AddDays(-30), cancellationToken); Notify("SampleCreated", reviews.Count); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Index));
		}

		[HttpGet]
		public async Task<IActionResult> Review(string id)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var review = await _quality.GetReviewAsync(DepartmentId, UserId, id);
				if (review == null) return NotFound();
				var model = Prepare(new RecordsQualityReviewView { Review = review, Criteria = ParseCriteria(review.CriteriaJson) });
				foreach (var f in ParseFindings(review.FindingsJson)) if (!string.IsNullOrWhiteSpace(f.Key)) model.Findings[f.Key] = f;
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		/// <summary>Findings arrive as score_{key} (0-4) and note_{key}.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Score(string id, string note, bool amendmentRecommended, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var findings = new List<RmsQualityFinding>();
				foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("score_", StringComparison.Ordinal)))
				{
					var criterion = key.Substring(6);
					int.TryParse(Request.Form[key].ToString(), out var score);
					findings.Add(new RmsQualityFinding { Key = criterion, Score = score, Note = Request.Form["note_" + criterion].ToString() });
				}
				await _quality.ScoreAsync(DepartmentId, UserId, id, findings, note, amendmentRecommended, cancellationToken);
				Notify("ReviewScored");
				return RedirectToAction(nameof(Index));
			}
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(Review), new { id }); }
		}

		[HttpGet]
		public async Task<IActionResult> Trends(string since = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var from = ParseUtc(since) ?? DateTime.UtcNow.AddDays(-90);
				return View(Prepare(new RecordsQualityTrendsView { Since = from, Trends = await _quality.GetTrendsAsync(DepartmentId, UserId, from) }));
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		private async Task PopulateAsync(RecordsQualityRubricView model)
		{
			model.Definitions = new[] { new SelectListItem { Value = "", Text = Localizer["AllDefinitions"].Value } }
				.Concat((await _definitions.ListAsync(DepartmentId)).Select(d => new SelectListItem { Value = d.Key, Text = d.Name, Selected = d.Key == model.Rubric?.DefinitionKey }))
				.ToList();
		}

		private static List<RmsQualityCriterion> ParseCriteria(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<RmsQualityCriterion>();
			try { return Newtonsoft.Json.JsonConvert.DeserializeObject<List<RmsQualityCriterion>>(json) ?? new List<RmsQualityCriterion>(); }
			catch (Newtonsoft.Json.JsonException) { return new List<RmsQualityCriterion>(); }
		}

		private static List<RmsQualityFinding> ParseFindings(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<RmsQualityFinding>();
			try { return Newtonsoft.Json.JsonConvert.DeserializeObject<List<RmsQualityFinding>>(json) ?? new List<RmsQualityFinding>(); }
			catch (Newtonsoft.Json.JsonException) { return new List<RmsQualityFinding>(); }
		}
	}

	/// <summary>RMS-4 release telemetry (RMS plan section 6): one page a department administrator can read during activation and after.</summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordsHealthController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsReleaseTelemetryService _telemetry;
		private readonly IRecordsPreventionSweepService _prevention;

		public RecordsHealthController(IRecordsReleaseTelemetryService telemetry, IRecordsPreventionSweepService prevention, IRecordsCutoverService cutover, IFeatureToggleService featureToggles,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_telemetry = telemetry;
			_prevention = prevention;
		}

		[HttpGet]
		public async Task<IActionResult> Index(int windowHours = 24)
		{
			if (!await ModuleOnAsync(null)) return NotFound();
			try
			{
				var model = Prepare(new RecordsHealthView { WindowHours = Math.Clamp(windowHours, 1, 24 * 30) });
				model.Telemetry = await _telemetry.GetAsync(DepartmentId, UserId, model.WindowHours);
				model.Prevention = await _prevention.GetSummaryAsync(DepartmentId, UserId);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}
	}

	/// <summary>Files on prevention and investigation aggregates; the service authorizes through the parent.</summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordPreventionAttachmentsController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsPreventionAttachmentsService _attachments;

		public RecordPreventionAttachmentsController(IRecordsPreventionAttachmentsService attachments, IRecordsCutoverService cutover, IFeatureToggleService featureToggles,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_attachments = attachments;
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Add(int parentKind, string parentId, string description, bool restricted, string returnUrl, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(null)) return NotFound();
			if (file == null || file.Length == 0) { NotifyError(Localizer["AttachmentFileRequired"].Value); return Back(returnUrl); }
			try
			{
				byte[] bytes;
				using (var stream = new System.IO.MemoryStream()) { await file.CopyToAsync(stream, cancellationToken); bytes = stream.ToArray(); }
				await _attachments.AddAsync(DepartmentId, UserId, (RmsPreventionParentKind)parentKind, parentId, System.IO.Path.GetFileName(file.FileName), file.ContentType, bytes, description, restricted, cancellationToken);
				Notify("AttachmentAdded");
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return Back(returnUrl);
		}

		[HttpGet]
		public async Task<IActionResult> Download(string id)
		{
			if (!await ModuleOnAsync(null)) return NotFound();
			try
			{
				var attachment = await _attachments.GetWithDataAsync(DepartmentId, UserId, id);
				if (attachment?.Data == null) return NotFound();
				return File(attachment.Data, attachment.ContentType ?? "application/octet-stream", attachment.FileName ?? "attachment");
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordProtectedContentException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Remove(string id, string returnUrl, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(null)) return NotFound();
			try { await _attachments.RemoveAsync(DepartmentId, UserId, id, cancellationToken); Notify("AttachmentRemoved"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return Back(returnUrl);
		}

		private IActionResult Back(string returnUrl) => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("Index", "Records");
	}
}
