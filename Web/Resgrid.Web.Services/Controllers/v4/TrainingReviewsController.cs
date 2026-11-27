using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Trainings;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Training hours submissions and their review (round 9B), for Omni: a reviewer's inbox (oldest first, with counts), a submission with its
	/// evidence and history, the evidence files, approve / deny (reason required) / ask for more information, bulk approve; a member's own
	/// submissions and their resubmission with more evidence (the camera) or corrections. New submissions go through
	/// Trainings/SubmitExternalTraining. Reviewers are the holders of Record Training Completions (135) and T1's record reviewers, department-
	/// wide or for their group (ITrainingSubmissionService.GetReviewerScopeAsync); nobody reviews their own. Every endpoint answers 404 while the
	/// Trainings module is switched off.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class TrainingReviewsController : V4AuthenticatedApiControllerbase
	{
		private readonly ITrainingSubmissionService _submissions;
		private readonly ILearningRecordService _learningRecords;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IDepartmentsService _departments;

		public TrainingReviewsController(ITrainingSubmissionService submissions, ILearningRecordService learningRecords, IDepartmentSettingsService departmentSettings,
			IDepartmentsService departments)
		{
			_submissions = submissions;
			_learningRecords = learningRecords;
			_departmentSettings = departmentSettings;
			_departments = departments;
		}

		private async Task<bool> TrainingDisabledAsync()
		{
			try
			{
				var modules = await _departmentSettings.GetDepartmentModuleSettingsAsync(DepartmentId);
				return modules != null && modules.TrainingDisabled;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Training module state could not be read for department {DepartmentId}");
				return false;
			}
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private async Task<Dictionary<int, string>> CategoryNamesAsync() =>
			(await _learningRecords.GetCategoriesAsync(DepartmentId, true) ?? new List<TrainingCategory>()).Where(c => c != null)
				.GroupBy(c => c.TrainingCategoryId).ToDictionary(g => g.Key, g => g.First().Name);

		private async Task<Dictionary<string, string>> NamesAsync() =>
			(await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		public static TrainingEvidenceFileData MapFile(LearningRecordFile file) => new TrainingEvidenceFileData
		{
			FileId = file.LearningRecordFileId,
			FileName = file.FileName,
			ContentType = file.ContentType,
			SizeBytes = file.SizeBytes,
			Kind = Enum.IsDefined(typeof(TrainingEvidenceKinds), file.EvidenceKind) ? ((TrainingEvidenceKinds)file.EvidenceKind).ToString() : null,
			Previewable = TrainingEvidencePolicy.IsInlineImage(file.ContentType) || TrainingEvidencePolicy.IsPdf(file.ContentType),
			UploadedOn = file.UploadedOn
		};

		public static TrainingSubmissionData MapSubmission(TrainingSubmissionDetail detail, IReadOnlyDictionary<int, string> categories, IReadOnlyDictionary<string, string> names)
		{
			var record = detail.Record;
			return new TrainingSubmissionData
			{
				Record = TrainingsController.MapLearningRecord(record, categories),
				UserId = record.UserId,
				MemberName = names != null && names.TryGetValue(record.UserId ?? string.Empty, out var name) ? name : null,
				Notes = record.Notes,
				Location = record.Location,
				IsOwner = detail.IsOwner,
				CanReview = detail.CanReview,
				CanRespond = detail.IsOwner && record.Status == (int)LearningRecordStatuses.Pending,
				Files = detail.Files.Select(MapFile).ToList(),
				History = detail.Events.Select(e => new TrainingSubmissionEventData
				{
					Step = Enum.IsDefined(typeof(TrainingSubmissionEventKinds), e.Kind) ? ((TrainingSubmissionEventKinds)e.Kind).ToString() : null,
					ActorUserId = e.ActorUserId,
					Note = e.Note,
					Hours = e.Hours,
					FileCount = e.FileCount,
					On = e.CreatedOn
				}).ToList()
			};
		}

		/// <summary>
		/// The caller's review inbox: submissions of the members they review waiting for a decision (or, with <paramref name="infoRequested"/>,
		/// waiting for the member's answer), oldest first, with the counts.
		/// </summary>
		[HttpGet("GetReviewInbox")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingReviewInboxResult>> GetReviewInbox(bool infoRequested = false, int? categoryId = null, string memberUserId = null, string search = null)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var inbox = await _submissions.GetInboxAsync(new TrainingReviewInboxQuery
			{
				DepartmentId = DepartmentId,
				ReviewerUserId = UserId,
				InfoRequested = infoRequested,
				CategoryId = categoryId,
				MemberUserId = memberUserId,
				Search = search,
				Take = 200
			}) ?? new TrainingReviewInbox();
			var categories = await CategoryNamesAsync();
			var names = inbox.Items.Count > 0 ? await NamesAsync() : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			var result = new TrainingReviewInboxResult
			{
				AwaitingCount = inbox.AwaitingCount,
				InfoRequestedCount = inbox.InfoRequestedCount,
				OldestWaitingSince = inbox.OldestWaitingSince,
				IsReviewer = inbox.IsReviewer,
				Data = inbox.Items.Select(r => new TrainingReviewItemData
				{
					Record = TrainingsController.MapLearningRecord(r, categories),
					UserId = r.UserId,
					MemberName = names.TryGetValue(r.UserId ?? string.Empty, out var name) ? name : null,
					Notes = r.Notes,
					EvidenceFiles = inbox.FileCounts.TryGetValue(r.LearningRecordId, out var count) ? count : 0,
					WaitingSince = TrainingSubmissionRules.WaitingSince(r)
				}).ToList()
			};
			return Populate(result, result.Data.Count);
		}

		/// <summary>One submission with its evidence and history, for its member or a reviewer of the member.</summary>
		[HttpGet("GetSubmission")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingSubmissionResult>> GetSubmission(int id)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var detail = await _submissions.GetSubmissionAsync(DepartmentId, id, UserId);
			if (detail == null)
				return NotFound();

			return Populate(new TrainingSubmissionResult { Data = MapSubmission(detail, await CategoryNamesAsync(), await NamesAsync()) }, 1);
		}

		/// <summary>An evidence file's bytes, for its member or a reviewer of the member.</summary>
		[HttpGet("GetEvidenceFile")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetEvidenceFile(int fileId)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var file = await _submissions.GetEvidenceFileAsync(DepartmentId, fileId, UserId);
			if (file?.Data == null)
				return NotFound();

			Response.Headers["X-Content-Type-Options"] = "nosniff";
			var name = System.IO.Path.GetFileName(file.FileName ?? string.Empty);
			var type = TrainingEvidencePolicy.ContentTypeOf(name) ?? "application/octet-stream";
			return File(file.Data, type, string.IsNullOrWhiteSpace(name) ? "evidence" : name);
		}

		private async Task<ActionResult<TrainingSubmissionResult>> OutcomeAsync(TrainingSubmissionOutcome outcome)
		{
			if (outcome?.Refusal != null || outcome?.Record == null)
			{
				var code = outcome?.Refusal ?? "learning_record_not_found";
				if (code == "learning_record_not_found")
					return NotFound();
				return BadRequest(Populate(new TrainingSubmissionResult { Error = code }, 0, ResponseHelper.Failure));
			}

			var detail = await _submissions.GetSubmissionAsync(DepartmentId, outcome.Record.LearningRecordId, UserId)
				?? new TrainingSubmissionDetail { Record = outcome.Record };
			return Populate(new TrainingSubmissionResult { Data = MapSubmission(detail, await CategoryNamesAsync(), await NamesAsync()) }, 1);
		}

		/// <summary>Approves a submission (optional note, optional corrected hours); credits follow.</summary>
		[HttpPost("ApproveSubmission")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingSubmissionResult>> ApproveSubmission([FromBody] ReviewTrainingSubmissionInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync() || input == null)
				return NotFound();
			if (input.Hours.HasValue && (input.Hours.Value <= 0 || input.Hours.Value > LearningRecordService.MaxHours))
				return BadRequest(Populate(new TrainingSubmissionResult { Error = "learning_record_hours_invalid" }, 0, ResponseHelper.Failure));
			return await OutcomeAsync(await _submissions.ApproveAsync(DepartmentId, input.Id, UserId, input.Note, input.Hours, cancellationToken));
		}

		/// <summary>Denies a submission; the reason is required and the member is told it.</summary>
		[HttpPost("DenySubmission")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingSubmissionResult>> DenySubmission([FromBody] ReviewTrainingSubmissionInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync() || input == null)
				return NotFound();
			return await OutcomeAsync(await _submissions.DenyAsync(DepartmentId, input.Id, UserId, input.Note, cancellationToken));
		}

		/// <summary>Asks the member for more information (required: what is needed); the member can add evidence and resubmit.</summary>
		[HttpPost("RequestSubmissionInfo")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingSubmissionResult>> RequestSubmissionInfo([FromBody] ReviewTrainingSubmissionInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync() || input == null)
				return NotFound();
			return await OutcomeAsync(await _submissions.RequestInfoAsync(DepartmentId, input.Id, UserId, input.Note, cancellationToken));
		}

		/// <summary>Approves several submissions at once (at most 100); each is checked on its own and refusals are listed.</summary>
		[HttpPost("BulkApproveSubmissions")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingBulkReviewApiResult>> BulkApproveSubmissions([FromBody] BulkApproveTrainingSubmissionsInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync() || input == null)
				return NotFound();

			var bulk = await _submissions.BulkApproveAsync(DepartmentId, (input.Ids ?? new List<int>()).Take(TrainingSubmissionRules.MaxBulk), UserId, input.Note, cancellationToken)
				?? new Resgrid.Model.TrainingBulkReviewResult();
			var result = new TrainingBulkReviewApiResult
			{
				Data = new TrainingBulkReviewResultData { Approved = bulk.Approved, Refused = bulk.Refused }
			};
			return Populate(result, bulk.Approved.Count);
		}

		/// <summary>The caller's own submissions, newest first, with their review state and the reviewer's note.</summary>
		[HttpGet("GetMySubmissions")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingSubmissionsResult>> GetMySubmissions()
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var categories = await CategoryNamesAsync();
			var rows = (await _submissions.GetMySubmissionsAsync(DepartmentId, UserId) ?? new List<LearningRecord>())
				.Where(r => !r.IsVoided).Select(r => TrainingsController.MapLearningRecord(r, categories)).ToList();
			return Populate(new TrainingSubmissionsResult { Data = rows }, rows.Count);
		}

		/// <summary>
		/// The member answers a request for more information, or adds evidence while it waits: a note, corrected hours or category, and more files
		/// (base64; the camera). The submission goes back to the reviewers.
		/// </summary>
		[HttpPost("RespondToSubmission")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(TrainingsController.MaxSubmissionRequestBytes)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingSubmissionResult>> RespondToSubmission([FromBody] RespondToTrainingSubmissionInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync() || input == null)
				return NotFound();

			var error = TrainingsController.BuildEvidenceFiles(input.Files, out var files);
			if (error != null)
				return BadRequest(Populate(new TrainingSubmissionResult { Error = error }, 0, ResponseHelper.Failure));

			var correction = new TrainingSubmissionCorrection { Hours = input.Hours, CeHours = input.CeHours, CategoryId = input.CategoryId };
			return await OutcomeAsync(await _submissions.ResubmitAsync(DepartmentId, input.Id, UserId, input.Note, correction, files, cancellationToken));
		}
	}
}
