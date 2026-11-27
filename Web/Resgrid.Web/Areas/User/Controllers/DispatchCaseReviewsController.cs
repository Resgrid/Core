using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Protocols;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Protocols P5 QA (free with Protocols): dispatch case review. Officers (Review Dispatch Cases 134, or department admins) sample calls by
	/// protocol, version, determinant, dispatcher and dates into a queue, score each recorded case question by question and instruction by
	/// instruction beside the version's case flow with a Records QA rubric, and send it to the dispatcher; the dispatcher reads it under
	/// "My case reviews", acknowledges it once and may reply once. Compliance report with CSV; dispatch case rubrics (admins).
	/// </summary>
	[Area("User")]
	[DepartmentLocalTime]
	public class DispatchCaseReviewsController : SecureBaseController
	{
		private readonly IDispatchCaseReviewService _reviews;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolsService _protocols;
		private readonly IProtocolLibraryService _library;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.DispatchCaseReviews.DispatchCaseReviews> _localizer;

		public DispatchCaseReviewsController(IDispatchCaseReviewService reviews, IProtocolAccessService access, IProtocolsService protocols, IProtocolLibraryService library,
			IDepartmentsService departments, IStringLocalizer<Resgrid.Localization.Areas.User.DispatchCaseReviews.DispatchCaseReviews> localizer)
		{
			_reviews = reviews;
			_access = access;
			_protocols = protocols;
			_library = library;
			_departments = departments;
			_localizer = localizer;
		}

		private string Text(string key) => _localizer?[key]?.Value ?? key;

		private async Task<ProtocolReader> ShellAsync(string section)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId) ?? ProtocolReader.Nobody(DepartmentId);
			ViewData["Section"] = section;
			ViewData["CanCreate"] = reader.MayAuthor;
			ViewData["IsManager"] = reader.IsManager;
			ViewData["IsAdmin"] = reader.IsDepartmentAdmin;
			ViewData["ManagesAcknowledgments"] = reader.IsCurrentMember && (reader.IsDepartmentAdmin || reader.CanManageAcknowledgments || reader.CanApprove);
			ViewData["ReviewsDispatchCases"] = reader.MayReviewDispatchCases;
			return reader;
		}

		private string ResultText(DispatchCaseReviewResults result) => Text("Result" + result);

		private void Flash(DispatchCaseReviewResults result, string doneKey)
		{
			if (result == DispatchCaseReviewResults.Done)
				TempData["CaseReviewMessage"] = Text(doneKey);
			else
				TempData["CaseReviewError"] = ResultText(result);
		}

		private async Task<Dictionary<string, string>> NamesAsync()
		{
			return ((await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
		}

		private async Task<List<SelectListItem>> ProtocolOptionsAsync(int? selected)
		{
			var options = new List<SelectListItem> { new SelectListItem(Text("AnyProtocol"), string.Empty) };
			options.AddRange(((await _protocols.GetProtocolSummariesForDepartmentAsync(DepartmentId)) ?? new List<DispatchProtocol>())
				.Where(p => p != null && p.DepartmentId == DepartmentId && p.Kind == (int)ProtocolKinds.Dispatch)
				.OrderBy(p => p.Code, StringComparer.CurrentCultureIgnoreCase)
				.Select(p => new SelectListItem($"{p.Code} {p.Name}", p.DispatchProtocolId.ToString(CultureInfo.InvariantCulture), selected == p.DispatchProtocolId)));
			return options;
		}

		private async Task<List<SelectListItem>> VersionOptionsAsync(int? protocolId, int? selected)
		{
			var options = new List<SelectListItem> { new SelectListItem(Text("AnyVersion"), string.Empty) };
			if (protocolId.HasValue && protocolId.Value > 0)
				options.AddRange(((await _library.GetVersionsAsync(DepartmentId, protocolId.Value)) ?? new List<ProtocolVersion>())
					.Where(v => v != null && v.DepartmentId == DepartmentId)
					.OrderByDescending(v => v.ProtocolVersionId)
					.Select(v => new SelectListItem("v" + v.Label, v.ProtocolVersionId.ToString(CultureInfo.InvariantCulture), selected == v.ProtocolVersionId)));
			return options;
		}

		private async Task<List<SelectListItem>> PeopleOptionsAsync(string anyKey, string selected)
		{
			var options = new List<SelectListItem> { new SelectListItem(Text(anyKey), string.Empty) };
			options.AddRange(((await _departments.GetSelectablePersonnelNamesAsync(DepartmentId)) ?? new List<PersonName>())
				.Where(p => p?.UserId != null)
				.Select(p => new SelectListItem(p.Name, p.UserId, string.Equals(p.UserId, selected, StringComparison.OrdinalIgnoreCase))));
			return options;
		}

		#region Queue

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Index(bool mine = false)
		{
			var reader = await ShellAsync("CaseReviews");
			if (!reader.MayReviewDispatchCases)
				return RedirectToAction(nameof(Mine));

			return View(new DispatchCaseQueueModel
			{
				Summary = await _reviews.GetSummaryAsync(DepartmentId, UserId),
				Reviews = await _reviews.GetQueueAsync(DepartmentId, UserId, mine),
				MineOnly = mine,
				IsAdmin = reader.IsDepartmentAdmin,
				UserId = UserId,
				Names = await NamesAsync(),
				Message = TempData?["CaseReviewMessage"] as string,
				Error = TempData?["CaseReviewError"] as string
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Take(int id, bool open = false, CancellationToken cancellationToken = default)
		{
			var result = await _reviews.TakeAsync(DepartmentId, UserId, id, cancellationToken);
			Flash(result, "TakenMessage");
			return result == DispatchCaseReviewResults.Done && open ? RedirectToAction(nameof(Review), new { id }) : RedirectToAction(nameof(Index));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken = default)
		{
			Flash(await _reviews.RemoveAsync(DepartmentId, UserId, id, cancellationToken), "RemovedMessage");
			return RedirectToAction(nameof(Index));
		}

		#endregion Queue

		#region Sampling

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Sample()
		{
			var reader = await ShellAsync("CaseReviews");
			if (!reader.MayReviewDispatchCases)
				return Unauthorized();

			var time = DepartmentTime.From(ViewData);
			var form = new DispatchCaseSampleForm { From = time.Today.AddDays(-30), To = time.Today, AssignToUserId = UserId };
			return View(await SampleModelAsync(form, null, null));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Sample(DispatchCaseSampleForm form, string preview, CancellationToken cancellationToken = default)
		{
			var reader = await ShellAsync("CaseReviews");
			if (!reader.MayReviewDispatchCases)
				return Unauthorized();

			form ??= new DispatchCaseSampleForm();
			var request = ToRequest(form);
			if (request == null)
				return View(await SampleModelAsync(form, null, ResultText(DispatchCaseReviewResults.Invalid)));

			if (!string.IsNullOrEmpty(preview))
			{
				var counted = await _reviews.PreviewSampleAsync(DepartmentId, UserId, request);
				return View(await SampleModelAsync(form, counted, counted.Result == DispatchCaseReviewResults.Done ? null : ResultText(counted.Result)));
			}

			var result = await _reviews.SampleAsync(DepartmentId, UserId, request, cancellationToken);
			if (result.Result != DispatchCaseReviewResults.Done)
				return View(await SampleModelAsync(form, result, ResultText(result.Result)));

			TempData["CaseReviewMessage"] = string.Format(CultureInfo.CurrentCulture, Text("SampledMessage"), result.Sampled.Count, result.Eligible, result.AlreadyReviewed);
			return RedirectToAction(nameof(Index));
		}

		private DispatchCaseSampleRequest ToRequest(DispatchCaseSampleForm form)
		{
			if (!form.From.HasValue || !form.To.HasValue || form.To.Value.Date < form.From.Value.Date)
				return null;

			var time = DepartmentTime.From(ViewData);
			var percent = string.Equals(form.Mode, "percent", StringComparison.OrdinalIgnoreCase);
			return new DispatchCaseSampleRequest
			{
				// The dates are department-local and inclusive: the window runs to the start of the day after "To".
				FromUtc = time.ToUtc(DateTime.SpecifyKind(form.From.Value.Date, DateTimeKind.Unspecified)),
				ToUtc = time.ToUtc(DateTime.SpecifyKind(form.To.Value.Date.AddDays(1), DateTimeKind.Unspecified)),
				DispatchProtocolId = form.ProtocolId > 0 ? form.ProtocolId : null,
				ProtocolVersionId = form.ProtocolId > 0 && form.VersionId > 0 ? form.VersionId : null,
				Determinant = form.Determinant,
				DispatcherUserId = string.IsNullOrWhiteSpace(form.DispatcherUserId) ? null : form.DispatcherUserId,
				Count = percent ? null : form.Count,
				Percent = percent ? form.Percent : null,
				RubricId = string.IsNullOrWhiteSpace(form.RubricId) ? null : form.RubricId,
				AssignToUserId = string.IsNullOrWhiteSpace(form.AssignToUserId) ? null : form.AssignToUserId,
				TemplateTexts = TemplateTexts()
			};
		}

		private DispatchCaseRubricTexts TemplateTexts() => new DispatchCaseRubricTexts
		{
			Name = Text("TemplateRubricName"),
			CaseEntry = Text("TemplateCaseEntry"),
			Questions = Text("TemplateQuestions"),
			Determinant = Text("TemplateDeterminant"),
			Instructions = Text("TemplateInstructions"),
			CallerCare = Text("TemplateCallerCare")
		};

		private async Task<DispatchCaseSampleModel> SampleModelAsync(DispatchCaseSampleForm form, DispatchCaseSampleResult preview, string error)
		{
			var rubrics = await _reviews.GetRubricsAsync(DepartmentId, UserId);
			var rubricOptions = rubrics.Select(r => new SelectListItem(r.Name, r.RmsQualityRubricId, string.Equals(r.RmsQualityRubricId, form.RubricId, StringComparison.OrdinalIgnoreCase))).ToList();
			if (rubricOptions.Count == 0)
				rubricOptions.Add(new SelectListItem(Text("TemplateRubricWillBeCreated"), string.Empty));

			// Only officers who review dispatch cases can be handed a sample.
			var reviewerIds = await _reviews.GetReviewerUserIdsAsync(DepartmentId);
			reviewerIds.Add(UserId);
			var reviewers = new List<SelectListItem> { new SelectListItem(Text("Unassigned"), string.Empty) };
			reviewers.AddRange(((await _departments.GetSelectablePersonnelNamesAsync(DepartmentId)) ?? new List<PersonName>())
				.Where(p => p?.UserId != null && reviewerIds.Contains(p.UserId))
				.Select(p => new SelectListItem(p.Name, p.UserId, string.Equals(p.UserId, form.AssignToUserId, StringComparison.OrdinalIgnoreCase))));

			return new DispatchCaseSampleModel
			{
				Form = form,
				Protocols = await ProtocolOptionsAsync(form.ProtocolId),
				Versions = await VersionOptionsAsync(form.ProtocolId, form.VersionId),
				Dispatchers = await PeopleOptionsAsync("AnyDispatcher", form.DispatcherUserId),
				Reviewers = reviewers,
				Rubrics = rubricOptions,
				Preview = preview,
				Error = error
			};
		}

		#endregion Sampling

		#region Review

		/// <summary>
		/// ADP catalog 39 reveal: the reveal module posts the grant here after step-up (in the hidden field the grant context reads) and the review
		/// renders with the case answers and reasons as written; the scoring form carries the same grant on, so the score is saved against a case
		/// the reviewer could read. Without a valid grant the page renders as Review does (withheld).
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> ReviewRevealed(int id)
		{
			var result = await Review(id);
			Response.Headers["Cache-Control"] = "no-store";
			if (result is ViewResult view && view.Model is DispatchCaseReviewModel model)
			{
				if (!model.Detail.CaseWithheld)
				{
					var grant = Request.HasFormContentType ? Request.Form[HttpProtectedGrantContext.FormFieldName].ToString() : null;
					model.ProtectedGrant = string.IsNullOrWhiteSpace(grant) ? null : grant.Trim();
					model.ProtectedGrantExpiresOnUtc = model.ProtectedGrant == null ? null : HttpProtectedGrantContext.ReadExpiry(Request);
				}
				return View("Review", model);
			}
			return result;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Review(int id)
		{
			var detail = await _reviews.GetDetailAsync(DepartmentId, UserId, id);
			if (detail == null)
				return NotFound();

			await ShellAsync(detail.IsReviewer ? "CaseReviews" : "MyCaseReviews");
			var names = await NamesAsync();
			string Name(string userId) => userId != null && names.TryGetValue(userId, out var name) ? name : userId;
			var model = new DispatchCaseReviewModel
			{
				Detail = detail,
				DispatcherName = Name(detail.Review.DispatcherUserId),
				ReviewerName = Name(detail.Review.ReviewerUserId),
				AssigneeName = Name(detail.Review.AssignedToUserId),
				OverriddenByName = Name(detail.Case?.OverriddenByUserId),
				GraphHtml = detail.Graph == null ? null : ProtocolDecisionGraphs.RenderHtml(detail.Graph, ProtocolBlockLabelsProvider.GraphFor(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)),
				PathKeys = (detail.Case?.Steps ?? new List<ProtocolCaseStep>()).Select(s => s.NodeKey).Where(ProtocolDecisionGraphs.IsValidKey).Distinct().ToList(),
				Message = TempData?["CaseReviewMessage"] as string,
				Error = TempData?["CaseReviewError"] as string
			};
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Score(int id, DispatchCaseScoreForm form, string send, CancellationToken cancellationToken = default)
		{
			form ??= new DispatchCaseScoreForm();
			var input = new DispatchCaseScoreInput { Comments = form.Comments };
			foreach (var item in form.Items ?? new List<DispatchCaseItemPost>())
				if (!string.IsNullOrWhiteSpace(item?.Key))
					input.Items[item.Key] = new DispatchCaseItemScore { Compliance = item.Compliance, Note = item.Note };
			foreach (var finding in form.Findings ?? new List<DispatchCaseFindingPost>())
				if (!string.IsNullOrWhiteSpace(finding?.Key) && finding.Score.HasValue)
					input.Findings.Add(new RmsQualityFinding { Key = finding.Key, Score = finding.Score.Value, Note = finding.Note });

			var sending = !string.IsNullOrEmpty(send);
			var result = await _reviews.SaveScoresAsync(DepartmentId, UserId, id, input, sending, cancellationToken);
			Flash(result, sending ? "SentMessage" : "DraftSavedMessage");
			return result == DispatchCaseReviewResults.Done && sending ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Review), new { id });
		}

		#endregion Review

		#region Dispatcher

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Mine()
		{
			var reader = await ShellAsync("MyCaseReviews");
			return View(new MyDispatchCaseReviewsModel
			{
				Reviews = await _reviews.GetMineAsync(DepartmentId, UserId),
				IsReviewer = reader.MayReviewDispatchCases,
				Message = TempData?["CaseReviewMessage"] as string,
				Error = TempData?["CaseReviewError"] as string
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Acknowledge(int id, string comment, CancellationToken cancellationToken = default)
		{
			Flash(await _reviews.AcknowledgeAsync(DepartmentId, UserId, id, comment, cancellationToken), "AcknowledgedMessage");
			return RedirectToAction(nameof(Review), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Reply(int id, string comment, CancellationToken cancellationToken = default)
		{
			Flash(await _reviews.CommentAsync(DepartmentId, UserId, id, comment, cancellationToken), "RepliedMessage");
			return RedirectToAction(nameof(Review), new { id });
		}

		#endregion Dispatcher

		#region Reports

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Reports(DispatchCaseReportForm form)
		{
			var reader = await ShellAsync("CaseReviews");
			if (!reader.MayReviewDispatchCases)
				return Unauthorized();

			form = Defaults(form);
			var model = new DispatchCaseReportModel
			{
				Form = form,
				Protocols = await ProtocolOptionsAsync(form.ProtocolId),
				Versions = await VersionOptionsAsync(form.ProtocolId, form.VersionId),
				Dispatchers = await PeopleOptionsAsync("AnyDispatcher", form.DispatcherUserId),
				Names = await NamesAsync()
			};
			model.Report = await _reviews.GetReportAsync(DepartmentId, UserId, ToFilter(form));
			if (model.Report == null)
				model.Error = ResultText(DispatchCaseReviewResults.Invalid);
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> ReportCsv(DispatchCaseReportForm form)
		{
			var reader = await ShellAsync("CaseReviews");
			if (!reader.MayReviewDispatchCases)
				return Unauthorized();

			form = Defaults(form);
			var report = await _reviews.GetReportAsync(DepartmentId, UserId, ToFilter(form));
			if (report == null)
				return BadRequest();

			var time = DepartmentTime.From(ViewData);
			var names = await NamesAsync();
			string Name(string userId) => userId != null && names.TryGetValue(userId, out var name) ? name : userId;
			string Number(double? value) => value?.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;
			var csv = new StringBuilder();
			csv.AppendLine(CsvCell.Line(new[]
			{
				Text("CsvCall"), Text("CsvCallDate"), Text("CsvProtocol"), Text("CsvVersion"), Text("CsvDeterminant"), Text("CsvOverridden"), Text("CsvDispatcher"),
				Text("CsvReviewer"), Text("CsvScore"), Text("CsvQuestionCompliance"), Text("CsvInstructionCompliance"), Text("CsvSent"), Text("CsvAcknowledged")
			}));
			foreach (var review in report.Reviews)
			{
				csv.AppendLine(CsvCell.Line(new[]
				{
					review.CallNumber ?? review.CallId.ToString(CultureInfo.InvariantCulture), time.Format(review.CallLoggedOn), (review.ProtocolCode + " " + review.ProtocolName).Trim(),
					review.VersionLabel, review.Determinant, review.Overridden ? Text("Yes") : Text("No"), Name(review.DispatcherUserId), Name(review.ReviewerUserId),
					review.Score?.ToString(CultureInfo.InvariantCulture), Number(DispatchCaseScoring.Percent(review.QuestionCount, review.QuestionPoints)),
					Number(DispatchCaseScoring.Percent(review.InstructionCount, review.InstructionPoints)), time.Format(review.SentOn), time.Format(review.AcknowledgedOn)
				}));
			}

			return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv", "dispatch-case-reviews-" + time.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
		}

		private DispatchCaseReportForm Defaults(DispatchCaseReportForm form)
		{
			form ??= new DispatchCaseReportForm();
			var time = DepartmentTime.From(ViewData);
			form.To ??= time.Today;
			form.From ??= form.To.Value.AddMonths(-6);
			return form;
		}

		private DispatchCaseReportFilter ToFilter(DispatchCaseReportForm form)
		{
			var time = DepartmentTime.From(ViewData);
			return new DispatchCaseReportFilter
			{
				FromUtc = time.ToUtc(DateTime.SpecifyKind(form.From.Value.Date, DateTimeKind.Unspecified)),
				ToUtc = time.ToUtc(DateTime.SpecifyKind(form.To.Value.Date.AddDays(1), DateTimeKind.Unspecified)),
				DispatchProtocolId = form.ProtocolId > 0 ? form.ProtocolId : null,
				ProtocolVersionId = form.ProtocolId > 0 && form.VersionId > 0 ? form.VersionId : null,
				DispatcherUserId = string.IsNullOrWhiteSpace(form.DispatcherUserId) ? null : form.DispatcherUserId
			};
		}

		#endregion Reports

		#region Rubrics

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Rubrics(string edit = null)
		{
			var reader = await ShellAsync("CaseReviews");
			if (!reader.MayReviewDispatchCases)
				return Unauthorized();

			var rubrics = await _reviews.GetRubricsAsync(DepartmentId, UserId, true);
			var model = new DispatchCaseRubricsModel
			{
				Rubrics = rubrics,
				EditId = edit,
				IsAdmin = reader.IsCurrentMember && reader.IsDepartmentAdmin,
				Message = TempData?["CaseReviewMessage"] as string,
				Error = TempData?["CaseReviewError"] as string
			};

			var editing = string.IsNullOrWhiteSpace(edit) ? null : rubrics.FirstOrDefault(r => string.Equals(r.RmsQualityRubricId, edit, StringComparison.OrdinalIgnoreCase));
			model.Form = editing == null
				? new DispatchCaseRubricForm { Name = string.Empty, IsActive = true }
				: new DispatchCaseRubricForm { RubricId = editing.RmsQualityRubricId, Name = editing.Name, IsActive = editing.IsActive, Criteria = Resgrid.Services.Records.RecordsQualityReviewService.ParseCriteria(editing.CriteriaJson) };
			// Room for three more criteria on the form.
			for (var i = 0; i < 3; i++)
				model.Form.Criteria.Add(new RmsQualityCriterion { Weight = 1 });
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> SaveRubric(DispatchCaseRubricForm form, CancellationToken cancellationToken = default)
		{
			form ??= new DispatchCaseRubricForm();
			var saved = await _reviews.SaveRubricAsync(DepartmentId, UserId, form.RubricId, form.Name, form.IsActive, form.Criteria ?? new List<RmsQualityCriterion>(), cancellationToken);
			if (saved == null)
			{
				TempData["CaseReviewError"] = Text("RubricNotSaved");
				return RedirectToAction(nameof(Rubrics), new { edit = form.RubricId });
			}

			TempData["CaseReviewMessage"] = Text("RubricSavedMessage");
			return RedirectToAction(nameof(Rubrics));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> CreateTemplateRubric(CancellationToken cancellationToken = default)
		{
			var created = await _reviews.CreateTemplateRubricAsync(DepartmentId, UserId, TemplateTexts(), cancellationToken);
			if (created == null)
				TempData["CaseReviewError"] = Text("RubricNotSaved");
			else
				TempData["CaseReviewMessage"] = Text("RubricSavedMessage");
			return RedirectToAction(nameof(Rubrics));
		}

		#endregion Rubrics
	}
}
