using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Models.Training;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Training reviews (round 9B): the reviewer inbox of members' training hours submissions (filters, counts, oldest first, bulk
	/// approve), one submission with its evidence shown inline (images and PDFs) and its history, approve / deny (reason required) / ask for
	/// more information; and the member's side: their submissions and the resubmission with more evidence or corrections. Who reviews whom
	/// comes from ITrainingSubmissionService.GetReviewerScopeAsync (Record Training Completions 135 and T1's record reviewers, department-wide
	/// or for their group); nobody reviews their own. New submissions start on TrainingRecords/Submit. The Trainings module switch closes
	/// every page.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class TrainingReviewsController : SecureBaseController
	{
		private const int MaxEvidenceRequestBytes = 44 * 1024 * 1024;

		private readonly ITrainingSubmissionService _submissions;
		private readonly ILearningRecordService _learningRecords;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.TrainingReviews.TrainingReviews> _localizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> _trainingsLocalizer;

		private TrainingManagementScope _scope;
		private Department _department;
		private bool _departmentRead;
		private Dictionary<string, string> _names;

		public TrainingReviewsController(ITrainingSubmissionService submissions, ILearningRecordService learningRecords, IAuthorizationService authorizationService,
			IDepartmentsService departmentsService, IDepartmentSettingsService departmentSettingsService,
			IStringLocalizer<Resgrid.Localization.Areas.User.TrainingReviews.TrainingReviews> localizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> trainingsLocalizer)
		{
			_submissions = submissions;
			_learningRecords = learningRecords;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_localizer = localizer;
			_trainingsLocalizer = trainingsLocalizer;
		}

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (await IsModuleDisabledAsync())
			{
				context.Result = NotFound();
				return;
			}

			ViewData["Scope"] = await ScopeAsync();
			await next();
		}

		[NonAction]
		public async Task<bool> IsModuleDisabledAsync()
		{
			try
			{
				var modules = await _departmentSettingsService.GetDepartmentModuleSettingsAsync(DepartmentId);
				return modules != null && modules.TrainingDisabled;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		private async Task<TrainingManagementScope> ScopeAsync() =>
			_scope ??= await _authorizationService.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);

		private async Task<Department> DepartmentAsync()
		{
			if (!_departmentRead)
			{
				_department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
				_departmentRead = true;
			}

			return _department;
		}

		private async Task<Dictionary<string, string>> NamesAsync() =>
			_names ??= (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		private string L(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		/// <summary>A refusal code as text: this area's strings, then the Trainings area's (T1's codes), then the generic failure.</summary>
		[NonAction]
		public string ErrorText(string code)
		{
			if (!string.IsNullOrWhiteSpace(code))
			{
				var own = _localizer?[code];
				if (own != null && !own.ResourceNotFound && !string.IsNullOrEmpty(own.Value))
					return own.Value;
				var trainings = _trainingsLocalizer?[code];
				if (trainings != null && !trainings.ResourceNotFound && !string.IsNullOrEmpty(trainings.Value))
					return trainings.Value;
			}

			return L("Error.Generic");
		}

		private void Flash(string message, string error = null)
		{
			if (TempData == null)
				return;
			if (!string.IsNullOrWhiteSpace(message))
				TempData["TrainingReviewsMessage"] = message;
			if (!string.IsNullOrWhiteSpace(error))
				TempData["TrainingReviewsError"] = error;
		}

		private async Task<T> PageAsync<T>(T view) where T : TrainingRecordsPageView
		{
			view.Department = await DepartmentAsync();
			view.Scope = await ScopeAsync();
			view.Names = await NamesAsync();
			view.ActiveMemberIds = await _departmentsService.GetActiveMemberUserIdsAsync(DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			view.Categories = await _learningRecords.GetCategoriesAsync(DepartmentId, true) ?? new List<TrainingCategory>();
			view.Message ??= TempData?["TrainingReviewsMessage"] as string;
			view.Error ??= TempData?["TrainingReviewsError"] as string;
			return view;
		}

		/// <summary>The evidence files of one upload from a form: at most five, the kind the member chose for them.</summary>
		[NonAction]
		public static async Task<(List<TrainingEvidenceUpload> Files, string Refusal)> ReadEvidenceAsync(IEnumerable<IFormFile> evidence, int evidenceKind, CancellationToken cancellationToken)
		{
			var files = new List<TrainingEvidenceUpload>();
			var given = (evidence ?? Enumerable.Empty<IFormFile>()).Where(f => f != null && f.Length > 0).ToList();
			if (given.Count > TrainingSubmissionRules.MaxFilesPerUpload)
				return (files, TrainingEvidencePolicy.TooMany);
			if (given.Sum(f => f.Length) > TrainingSubmissionRules.MaxUploadBytes)
				return (files, TrainingEvidencePolicy.TooLarge);

			foreach (var file in given)
			{
				var name = System.IO.Path.GetFileName(file.FileName ?? string.Empty);
				var type = TrainingEvidencePolicy.ContentTypeOf(name);
				if (type == null)
					return (files, TrainingEvidencePolicy.TypeRefused);
				if (file.Length > LearningRecordFile.MaxBytes)
					return (files, TrainingEvidencePolicy.TooLarge);

				using var stream = file.OpenReadStream();
				using var memory = new System.IO.MemoryStream();
				await stream.CopyToAsync(memory, cancellationToken);
				files.Add(new TrainingEvidenceUpload { FileName = name, ContentType = type, Data = memory.ToArray(), Kind = evidenceKind });
			}

			return (files, null);
		}

		#endregion Gates and helpers

		#region Reviewer inbox

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Index(bool info = false, int? categoryId = null, string memberId = null, string q = null)
		{
			var inbox = await _submissions.GetInboxAsync(new TrainingReviewInboxQuery
			{
				DepartmentId = DepartmentId,
				ReviewerUserId = UserId,
				InfoRequested = info,
				CategoryId = categoryId,
				MemberUserId = string.IsNullOrWhiteSpace(memberId) ? null : memberId.Trim(),
				Search = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
				Take = 200
			}) ?? new TrainingReviewInbox();

			if (!inbox.IsReviewer)
				return RedirectToAction("Mine");

			var model = await PageAsync(new TrainingReviewInboxView
			{
				Inbox = inbox,
				InfoRequested = info,
				CategoryId = categoryId,
				MemberUserId = memberId,
				Search = q
			});

			// The member filter offers the members whose submissions are waiting (whatever else is filtered).
			var all = await _submissions.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = DepartmentId, ReviewerUserId = UserId, InfoRequested = info, Take = 500 });
			model.Members = (all?.Items ?? new List<LearningRecord>()).Select(r => r.UserId).Where(id => !string.IsNullOrWhiteSpace(id))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Select(id => new TrainingRecordMemberOption { UserId = id, Name = model.NameOf(id), IsFormer = model.IsFormer(id) })
				.OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> BulkApprove(int[] ids, string note, CancellationToken cancellationToken)
		{
			var selected = (ids ?? Array.Empty<int>()).Where(i => i > 0).Distinct().ToList();
			if (selected.Count == 0)
			{
				Flash(null, L("Inbox.NoneSelected"));
				return RedirectToAction("Index");
			}

			var result = await _submissions.BulkApproveAsync(DepartmentId, selected.Take(TrainingSubmissionRules.MaxBulk), UserId, note, cancellationToken) ?? new TrainingBulkReviewResult();
			Flash(result.Approved.Count > 0 ? string.Format(CultureInfo.CurrentCulture, L("Inbox.BulkApproved"), result.Approved.Count) : null,
				result.Refused.Count > 0 ? string.Format(CultureInfo.CurrentCulture, L("Inbox.BulkRefused"), result.Refused.Count) : null);
			return RedirectToAction("Index");
		}

		#endregion Reviewer inbox

		#region One submission

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Review(int id)
		{
			var detail = await _submissions.GetSubmissionAsync(DepartmentId, id, UserId);
			if (detail == null)
				return NotFound();
			if (detail.IsOwner)
				return RedirectToAction("Submission", new { id });

			var model = await PageAsync(new TrainingSubmissionPageView { Detail = detail, AsReviewer = true, AcceptAttribute = TrainingEvidencePolicy.AcceptAttribute });
			return View("Submission", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Submission(int id)
		{
			var detail = await _submissions.GetSubmissionAsync(DepartmentId, id, UserId);
			if (detail == null)
				return NotFound();
			if (!detail.IsOwner)
				return RedirectToAction("Review", new { id });

			var model = await PageAsync(new TrainingSubmissionPageView { Detail = detail, AsReviewer = false, AcceptAttribute = TrainingEvidencePolicy.AcceptAttribute });
			return View("Submission", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Approve(int id, string note, decimal? hours, CancellationToken cancellationToken)
		{
			if (hours.HasValue && (hours.Value <= 0 || hours.Value > LearningRecordService.MaxHours))
			{
				Flash(null, ErrorText("learning_record_hours_invalid"));
				return RedirectToAction("Review", new { id });
			}

			var outcome = await _submissions.ApproveAsync(DepartmentId, id, UserId, note, hours.HasValue ? Math.Round(hours.Value, 2) : (decimal?)null, cancellationToken);
			return AfterDecision(outcome, id, "Message.Approved");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Deny(int id, string reason, CancellationToken cancellationToken)
		{
			var outcome = await _submissions.DenyAsync(DepartmentId, id, UserId, reason, cancellationToken);
			return AfterDecision(outcome, id, "Message.Denied");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RequestInfo(int id, string note, CancellationToken cancellationToken)
		{
			var outcome = await _submissions.RequestInfoAsync(DepartmentId, id, UserId, note, cancellationToken);
			return AfterDecision(outcome, id, "Message.InfoRequested");
		}

		private IActionResult AfterDecision(TrainingSubmissionOutcome outcome, int id, string messageKey)
		{
			if (outcome?.Refusal == "learning_record_not_found")
				return NotFound();
			if (outcome?.Refusal != null || outcome?.Record == null)
			{
				Flash(null, ErrorText(outcome?.Refusal));
				return RedirectToAction("Review", new { id });
			}

			Flash(L(messageKey));
			return RedirectToAction("Index");
		}

		/// <summary>The member adds evidence, corrections or an answer and sends the submission back to the reviewers.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxEvidenceRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxEvidenceRequestBytes)]
		public async Task<IActionResult> Respond(int id, string note, decimal? hours, decimal? ceHours, int? categoryId, List<IFormFile> evidence, int evidenceKind,
			CancellationToken cancellationToken)
		{
			var (files, refusal) = await ReadEvidenceAsync(evidence, evidenceKind, cancellationToken);
			if (refusal != null)
			{
				Flash(null, ErrorText(refusal));
				return RedirectToAction("Submission", new { id });
			}

			var outcome = await _submissions.ResubmitAsync(DepartmentId, id, UserId, note,
				new TrainingSubmissionCorrection
				{
					Hours = hours.HasValue ? Math.Round(hours.Value, 2) : (decimal?)null,
					CeHours = ceHours.HasValue ? Math.Round(ceHours.Value, 2) : (decimal?)null,
					CategoryId = categoryId
				}, files, cancellationToken);
			if (outcome?.Refusal == "learning_record_not_found")
				return NotFound();
			if (outcome?.Refusal != null)
				Flash(null, ErrorText(outcome.Refusal));
			else
				Flash(L("Message.Resubmitted"));
			return RedirectToAction("Submission", new { id });
		}

		/// <summary>
		/// An evidence file for its member, a reviewer of the member or a T1 record reviewer: images and PDFs inline (framed only by this site,
		/// no sniffing), every other type, or on request, as a download.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Evidence(int id, bool download = false)
		{
			var file = await _submissions.GetEvidenceFileAsync(DepartmentId, id, UserId);
			if (file?.Data == null)
				return NotFound();

			var name = System.IO.Path.GetFileName(file.FileName ?? string.Empty);
			if (string.IsNullOrWhiteSpace(name))
				name = "evidence";
			// The stored type follows the accepted extension; anything else is served as bytes.
			var type = TrainingEvidencePolicy.ContentTypeOf(name) ?? "application/octet-stream";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			Response.Headers["Cache-Control"] = "private, no-store";
			var inline = !download && (TrainingEvidencePolicy.IsInlineImage(type) || TrainingEvidencePolicy.IsPdf(type));
			if (!inline)
				return File(file.Data, type, name);

			Response.Headers["Content-Disposition"] = new System.Net.Mime.ContentDisposition { Inline = true, FileName = name }.ToString();
			// Framed only by this site; images also get a sandbox (no sandbox for PDFs: it stops the browsers' built-in viewers).
			Response.Headers["Content-Security-Policy"] = TrainingEvidencePolicy.IsPdf(type)
				? "frame-ancestors 'self'"
				: "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'self'; sandbox";
			return File(file.Data, type);
		}

		#endregion One submission

		#region Member

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Mine()
		{
			var settings = await _learningRecords.GetSettingsAsync(DepartmentId) ?? new TrainingRecordSettings();
			var model = await PageAsync(new TrainingMySubmissionsView
			{
				Submissions = (await _submissions.GetMySubmissionsAsync(DepartmentId, UserId) ?? new List<LearningRecord>())
					.Where(r => !r.IsVoided).OrderByDescending(r => TrainingSubmissionRules.WaitingSince(r)).ToList(),
				SubmissionsDisabled = settings.MemberSubmissionsDisabled
			});
			return View(model);
		}

		#endregion Member
	}
}
