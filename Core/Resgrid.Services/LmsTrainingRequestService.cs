using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Training requests (§13a.2, Vector "training request" parity; LMS add-on, decided from §13a/§17.2): a member asks to attend an
	/// outside class with its cost; the approval chain is the member's supervisor (the admins of their group and its parents, else the
	/// department admins) and then a training officer (a department-wide training records manager or a department admin), each step on
	/// or off in the program settings and fixed on the request when it is submitted. An approved request, once taken, becomes an external
	/// submission (T1) the officer verifies on the existing queue; costs roll up per member, course and year. Nobody decides their own
	/// request. Decisions use a conditional update, so two approvers deciding at once cannot both win.
	/// </summary>
	public class LmsTrainingRequestService : ILmsTrainingRequestService
	{
		public const string SourcePrefix = "treq:";
		private const decimal MaxHours = 1000m;

		private readonly ILmsTrainingRequestsRepository _requests;
		private readonly ILmsTrainingRequestDecisionsRepository _decisions;
		private readonly ILmsProgramSettingsService _settings;
		private readonly ILearningRecordService _learningRecords;
		private readonly ILearningRecordWriter _writer;
		private readonly ILearningRecordFilesRepository _files;
		private readonly IAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly ILmsProgramNotifier _notifier;
		private readonly IEventAggregator _events;

		public LmsTrainingRequestService(ILmsTrainingRequestsRepository requests, ILmsTrainingRequestDecisionsRepository decisions, ILmsProgramSettingsService settings,
			ILearningRecordService learningRecords, ILearningRecordWriter writer, ILearningRecordFilesRepository files, IAuthorizationService authorization,
			IDepartmentsService departments, IDepartmentGroupsService groups, ILmsProgramNotifier notifier, IEventAggregator events)
		{
			_requests = requests;
			_decisions = decisions;
			_settings = settings;
			_learningRecords = learningRecords;
			_writer = writer;
			_files = files;
			_authorization = authorization;
			_departments = departments;
			_groups = groups;
			_notifier = notifier;
			_events = events;
		}

		#region Who decides

		private static bool Same(string a, string b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		private async Task<bool> IsCurrentMemberAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			var member = await _departments.GetDepartmentMemberAsync(userId, departmentId, false);
			return DepartmentMemberStateHelper.IsCurrentMember(member, departmentId);
		}

		private async Task<HashSet<string>> AdminsAsync(int departmentId) =>
			new HashSet<string>((await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>())
				.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id)).Select(a => a.Id), StringComparer.OrdinalIgnoreCase);

		/// <summary>The member's supervisors: the admins of their group and its parents, else the department's active admins; never the member.</summary>
		public async Task<HashSet<string>> SupervisorsOfAsync(int departmentId, string userId)
		{
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var group = await _groups.GetGroupForUserAsync(userId, departmentId);
			if (group != null && group.DepartmentId == departmentId)
				foreach (var admin in await _groups.GetAllAdminsForGroupAndAncestorsAsync(group.DepartmentGroupId) ?? new List<DepartmentGroupMember>())
					if (!string.IsNullOrWhiteSpace(admin?.UserId))
						ids.Add(admin.UserId);

			ids.RemoveWhere(id => Same(id, userId));
			if (ids.Count == 0)
				foreach (var admin in await AdminsAsync(departmentId))
					ids.Add(admin);
			ids.RemoveWhere(id => Same(id, userId));
			return ids;
		}

		public async Task<bool> CanDecideAsync(int departmentId, string approverUserId, string requesterUserId, LmsTrainingRequestSteps step)
		{
			if (string.IsNullOrWhiteSpace(approverUserId) || Same(approverUserId, requesterUserId) || !await IsCurrentMemberAsync(departmentId, approverUserId))
				return false;
			if ((await AdminsAsync(departmentId)).Contains(approverUserId))
				return true;

			switch (step)
			{
				case LmsTrainingRequestSteps.Supervisor:
					return (await SupervisorsOfAsync(departmentId, requesterUserId)).Contains(approverUserId);
				case LmsTrainingRequestSteps.TrainingOfficer:
					var scope = await _authorization.GetTrainingManagementScopeAsync(approverUserId, departmentId);
					return scope != null && scope.CanManageRecords;
				default:
					return false;
			}
		}

		/// <summary>Who to tell that a step waits for them (active members only; the notifier filters again).</summary>
		private async Task<List<string>> ApproversForStepAsync(int departmentId, string requesterUserId, LmsTrainingRequestSteps step)
		{
			var ids = step == LmsTrainingRequestSteps.Supervisor
				? await SupervisorsOfAsync(departmentId, requesterUserId)
				: new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (step == LmsTrainingRequestSteps.TrainingOfficer)
			{
				var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				// The officer step is announced to records managers; a very large roster is capped (department admins decide too).
				foreach (var userId in active.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).Take(500))
				{
					if (Same(userId, requesterUserId))
						continue;
					var scope = await _authorization.GetTrainingManagementScopeAsync(userId, departmentId);
					if (scope != null && scope.CanManageRecords)
						ids.Add(userId);
				}
				if (ids.Count == 0)
					foreach (var admin in await AdminsAsync(departmentId))
						if (!Same(admin, requesterUserId))
							ids.Add(admin);
			}
			return ids.ToList();
		}

		#endregion Who decides

		#region Lifecycle

		public async Task<LmsTrainingRequest> SubmitAsync(int departmentId, string userId, LmsTrainingRequest request, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || request == null || !await IsCurrentMemberAsync(departmentId, userId))
				throw new InvalidOperationException("lms_program_not_allowed");

			var title = Trim(request.Title, LmsTrainingRequest.TitleMaxLength);
			if (title == null)
				throw new InvalidOperationException("lms_program_title_required");
			if (request.Hours < 0 || request.Hours > MaxHours)
				throw new InvalidOperationException("lms_program_hours_invalid");
			foreach (var cost in new[] { request.CostTuition, request.CostTravel, request.CostOther })
				if (cost < 0 || cost > LmsTrainingRequest.MaxCost)
					throw new InvalidOperationException("lms_program_cost_invalid");
			if (request.StartsOn.HasValue && request.EndsOn.HasValue && request.EndsOn.Value.Date < request.StartsOn.Value.Date)
				throw new InvalidOperationException("lms_program_dates_invalid");
			if (request.CategoryId.HasValue && !(await _learningRecords.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>())
				    .Any(c => c.TrainingCategoryId == request.CategoryId.Value && !c.IsDeleted))
				throw new InvalidOperationException("lms_program_category_invalid");

			var settings = await _settings.GetAsync(departmentId);
			var steps = settings.RequestSteps();
			var now = DateTime.UtcNow;
			var stored = new LmsTrainingRequest
			{
				DepartmentId = departmentId,
				UserId = userId,
				Title = title,
				Provider = Trim(request.Provider, LmsTrainingRequest.TextMaxLength),
				Location = Trim(request.Location, LmsTrainingRequest.TextMaxLength),
				CourseCode = Trim(request.CourseCode, 100),
				StartsOn = request.StartsOn?.Date,
				EndsOn = request.EndsOn?.Date,
				Hours = Math.Round(request.Hours, 2, MidpointRounding.AwayFromZero),
				CategoryId = request.CategoryId,
				CostTuition = Math.Round(request.CostTuition, 2, MidpointRounding.AwayFromZero),
				CostTravel = Math.Round(request.CostTravel, 2, MidpointRounding.AwayFromZero),
				CostOther = Math.Round(request.CostOther, 2, MidpointRounding.AwayFromZero),
				Currency = settings.RequestCurrency,
				Justification = Trim(request.Justification, LmsTrainingRequest.JustificationMaxLength),
				Steps = string.Join(",", steps.Select(s => ((int)s).ToString(CultureInfo.InvariantCulture))),
				SubmittedOn = now,
				UpdatedOn = now
			};

			if (steps.Count == 0)
			{
				// No approval configured: the request is approved as submitted.
				stored.Status = (int)LmsTrainingRequestStatuses.Approved;
				stored.DecidedOn = now;
			}
			else
			{
				stored.Status = (int)LmsTrainingRequestStatuses.Submitted;
				stored.CurrentStep = (int)steps[0];
			}

			var saved = await _requests.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsTrainingRequestSubmitted, null, JsonConvert.SerializeObject(saved));

			if (saved.CurrentStep.HasValue)
				await NotifyStepAsync(saved, cancellationToken);
			return saved;
		}

		public async Task<LmsTrainingRequest> WithdrawAsync(int departmentId, int lmsTrainingRequestId, string userId, CancellationToken cancellationToken = default)
		{
			var request = await RequireAsync(departmentId, lmsTrainingRequestId);
			if (!Same(request.UserId, userId))
				throw new InvalidOperationException("lms_program_not_allowed");
			if (request.Status != (int)LmsTrainingRequestStatuses.Submitted)
				throw new InvalidOperationException("lms_program_request_not_open");

			var expectedStep = request.CurrentStep;
			request.Status = (int)LmsTrainingRequestStatuses.Withdrawn;
			request.CurrentStep = null;
			request.UpdatedOn = DateTime.UtcNow;
			if (!await _requests.TryTransitionAsync(request, (int)LmsTrainingRequestStatuses.Submitted, expectedStep, cancellationToken))
				throw new InvalidOperationException("lms_program_request_changed");
			Audit(departmentId, userId, AuditLogTypes.LmsTrainingRequestDecided, null, JsonConvert.SerializeObject(request));
			return request;
		}

		public async Task<LmsTrainingRequest> DecideAsync(int departmentId, int lmsTrainingRequestId, string approverUserId, bool approve, string note,
			CancellationToken cancellationToken = default)
		{
			var request = await RequireAsync(departmentId, lmsTrainingRequestId);
			if (request.Status != (int)LmsTrainingRequestStatuses.Submitted || !request.CurrentStep.HasValue)
				throw new InvalidOperationException("lms_program_request_not_open");
			var step = (LmsTrainingRequestSteps)request.CurrentStep.Value;
			if (!await CanDecideAsync(departmentId, approverUserId, request.UserId, step))
				throw new InvalidOperationException("lms_program_not_allowed");
			if (!approve && string.IsNullOrWhiteSpace(note))
				throw new InvalidOperationException("lms_program_reason_required");

			var now = DateTime.UtcNow;
			var steps = request.StepList();
			var position = steps.IndexOf(step);
			var expectedStep = request.CurrentStep;
			if (!approve)
			{
				request.Status = (int)LmsTrainingRequestStatuses.Rejected;
				request.CurrentStep = null;
				request.DecidedOn = now;
				request.DecidedByUserId = approverUserId;
			}
			else if (position >= 0 && position + 1 < steps.Count)
			{
				request.CurrentStep = (int)steps[position + 1];
			}
			else
			{
				request.Status = (int)LmsTrainingRequestStatuses.Approved;
				request.CurrentStep = null;
				request.DecidedOn = now;
				request.DecidedByUserId = approverUserId;
			}
			request.UpdatedOn = now;

			if (!await _requests.TryTransitionAsync(request, (int)LmsTrainingRequestStatuses.Submitted, expectedStep, cancellationToken))
				throw new InvalidOperationException("lms_program_request_changed");

			await _decisions.SaveOrUpdateAsync(new LmsTrainingRequestDecision
			{
				LmsTrainingRequestId = request.LmsTrainingRequestId,
				DepartmentId = departmentId,
				Step = (int)step,
				Decision = approve ? (int)LmsTrainingRequestDecisions.Approved : (int)LmsTrainingRequestDecisions.Rejected,
				DecidedByUserId = approverUserId,
				Note = Trim(note, LmsTrainingRequestDecision.NoteMaxLength),
				DecidedOn = now
			}, cancellationToken, true);
			Audit(departmentId, approverUserId, AuditLogTypes.LmsTrainingRequestDecided, null, JsonConvert.SerializeObject(new { request.LmsTrainingRequestId, Step = step, approve, request.Status }));

			if (request.Status == (int)LmsTrainingRequestStatuses.Submitted)
				await NotifyStepAsync(request, cancellationToken);
			else
				await NotifyRequesterAsync(request, approve ? "Prg.Notify.RequestApproved" : "Prg.Notify.RequestRejected", cancellationToken);
			return request;
		}

		public async Task<LmsTrainingRequest> CancelAsync(int departmentId, int lmsTrainingRequestId, string userId, string note, CancellationToken cancellationToken = default)
		{
			var request = await RequireAsync(departmentId, lmsTrainingRequestId);
			if (request.Status != (int)LmsTrainingRequestStatuses.Approved)
				throw new InvalidOperationException("lms_program_request_not_open");
			if (!await CanDecideAsync(departmentId, userId, request.UserId, LmsTrainingRequestSteps.TrainingOfficer))
				throw new InvalidOperationException("lms_program_not_allowed");

			request.Status = (int)LmsTrainingRequestStatuses.Cancelled;
			request.UpdatedOn = DateTime.UtcNow;
			if (!await _requests.TryTransitionAsync(request, (int)LmsTrainingRequestStatuses.Approved, null, cancellationToken))
				throw new InvalidOperationException("lms_program_request_changed");
			Audit(departmentId, userId, AuditLogTypes.LmsTrainingRequestDecided, null, JsonConvert.SerializeObject(new { request.LmsTrainingRequestId, request.Status, note }));
			await NotifyRequesterAsync(request, "Prg.Notify.RequestCancelled", cancellationToken);
			return request;
		}

		public async Task<LmsTrainingRequest> CompleteAsync(int departmentId, int lmsTrainingRequestId, string userId, DateTime completedOn, decimal hours, decimal? actualCost,
			string fileName, string contentType, byte[] evidence, CancellationToken cancellationToken = default)
		{
			var request = await RequireAsync(departmentId, lmsTrainingRequestId);
			if (!Same(request.UserId, userId))
				throw new InvalidOperationException("lms_program_not_allowed");
			if (request.Status != (int)LmsTrainingRequestStatuses.Approved)
				throw new InvalidOperationException("lms_program_request_not_approved");
			if (completedOn == default || completedOn > DateTime.UtcNow.AddDays(1) || completedOn.Year < 2000)
				throw new InvalidOperationException("lms_program_date_invalid");
			if (hours < 0 || hours > MaxHours)
				throw new InvalidOperationException("lms_program_hours_invalid");
			if (actualCost.HasValue && (actualCost.Value < 0 || actualCost.Value > LmsTrainingRequest.MaxCost))
				throw new InvalidOperationException("lms_program_cost_invalid");

			int? fileId = null;
			if (evidence != null && evidence.Length > 0)
			{
				if (evidence.Length > LearningRecordFile.MaxBytes)
					throw new InvalidOperationException("learning_record_file_too_large");
				var file = await _files.InsertAsync(new LearningRecordFile
				{
					DepartmentId = departmentId,
					UserId = userId,
					FileName = Trim(System.IO.Path.GetFileName(fileName ?? "evidence"), 255) ?? "evidence",
					ContentType = Trim(contentType, 100),
					Data = evidence,
					UploadedOn = DateTime.UtcNow,
					UploadedByUserId = userId
				}, cancellationToken, true);
				fileId = file?.LearningRecordFileId;
			}

			// The expected external submission (T1): idempotent by the request, Pending until an officer verifies it on the queue.
			var written = await _writer.WriteSourceAsync(departmentId, LearningRecordSourceKinds.ExternalSubmission, SourceRef(request.LmsTrainingRequestId), new[]
			{
				new LearningRecordDraft
				{
					UserId = request.UserId,
					Title = request.Title,
					CourseCode = request.CourseCode,
					CompletedOn = completedOn,
					StartedOn = request.StartsOn,
					EndedOn = request.EndsOn,
					Hours = Math.Round(hours, 2, MidpointRounding.AwayFromZero),
					CategoryId = request.CategoryId,
					Location = request.Location,
					CeProvider = request.Provider,
					ExternalId = SourceRef(request.LmsTrainingRequestId),
					Notes = request.Justification,
					Status = LearningRecordStatuses.Pending,
					EvidenceFileId = fileId
				}
			}, userId, cancellationToken);

			var record = written?.Records?.FirstOrDefault(r => r.Status != (int)LearningRecordStatuses.Voided);
			request.Status = (int)LmsTrainingRequestStatuses.Completed;
			request.LearningRecordId = record?.LearningRecordId;
			request.CompletedOn = completedOn;
			request.Hours = Math.Round(hours, 2, MidpointRounding.AwayFromZero);
			request.ActualCost = actualCost.HasValue ? Math.Round(actualCost.Value, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
			request.UpdatedOn = DateTime.UtcNow;
			if (!await _requests.TryTransitionAsync(request, (int)LmsTrainingRequestStatuses.Approved, null, cancellationToken))
				throw new InvalidOperationException("lms_program_request_changed");
			Audit(departmentId, userId, AuditLogTypes.LmsTrainingRequestCompleted, null, JsonConvert.SerializeObject(request));
			return request;
		}

		public static string SourceRef(int lmsTrainingRequestId) => SourcePrefix + lmsTrainingRequestId.ToString(CultureInfo.InvariantCulture);

		#endregion Lifecycle

		#region Reads

		private async Task<LmsTrainingRequest> RequireAsync(int departmentId, int lmsTrainingRequestId)
		{
			var request = await _requests.GetByIdForDepartmentAsync(departmentId, lmsTrainingRequestId);
			return request == null || request.DepartmentId != departmentId ? throw new InvalidOperationException("lms_program_request_not_found") : request;
		}

		public async Task<LmsTrainingRequestView> GetAsync(int departmentId, int lmsTrainingRequestId, string viewerUserId)
		{
			var request = await _requests.GetByIdForDepartmentAsync(departmentId, lmsTrainingRequestId);
			if (request == null || request.DepartmentId != departmentId)
				return null;

			var own = Same(request.UserId, viewerUserId);
			var canDecide = request.IsOpen && request.CurrentStep.HasValue &&
				await CanDecideAsync(departmentId, viewerUserId, request.UserId, (LmsTrainingRequestSteps)request.CurrentStep.Value);
			var officer = await CanDecideAsync(departmentId, viewerUserId, request.UserId, LmsTrainingRequestSteps.TrainingOfficer);
			var supervisor = await CanDecideAsync(departmentId, viewerUserId, request.UserId, LmsTrainingRequestSteps.Supervisor);
			if (!own && !officer && !supervisor)
				return null;

			return new LmsTrainingRequestView
			{
				Request = request,
				Decisions = await _decisions.GetForRequestAsync(departmentId, request.LmsTrainingRequestId) ?? new List<LmsTrainingRequestDecision>(),
				CanDecide = canDecide,
				CanWithdraw = own && request.IsOpen,
				CanComplete = own && request.Status == (int)LmsTrainingRequestStatuses.Approved,
				CanCancel = officer && request.Status == (int)LmsTrainingRequestStatuses.Approved
			};
		}

		public async Task<List<LmsTrainingRequest>> GetMineAsync(int departmentId, string userId) =>
			string.IsNullOrWhiteSpace(userId) ? new List<LmsTrainingRequest>() : await _requests.GetForUserAsync(departmentId, userId) ?? new List<LmsTrainingRequest>();

		public async Task<List<LmsTrainingRequest>> GetAwaitingDecisionAsync(int departmentId, string approverUserId)
		{
			var result = new List<LmsTrainingRequest>();
			foreach (var request in await _requests.GetOpenAsync(departmentId) ?? new List<LmsTrainingRequest>())
				if (request.CurrentStep.HasValue && await CanDecideAsync(departmentId, approverUserId, request.UserId, (LmsTrainingRequestSteps)request.CurrentStep.Value))
					result.Add(request);
			return result;
		}

		public async Task<List<LmsTrainingRequest>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc) =>
			await _requests.GetForDepartmentAsync(departmentId, fromUtc, toUtc) ?? new List<LmsTrainingRequest>();

		public async Task<LmsTrainingRequestCostSummary> GetCostSummaryAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, LmsTrainingRequestCostGroupings groupBy)
		{
			var requests = await GetForDepartmentAsync(departmentId, fromUtc, toUtc);
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
			var settings = await _settings.GetAsync(departmentId);
			return new LmsTrainingRequestCostSummary
			{
				GroupBy = groupBy,
				Currency = settings.RequestCurrency,
				Rows = LmsTrainingRequestCostMath.Group(requests, groupBy, id => id != null && names.TryGetValue(id, out var name) ? name : id)
			};
		}

		#endregion Reads

		#region Notifications

		private async Task NotifyStepAsync(LmsTrainingRequest request, CancellationToken cancellationToken)
		{
			try
			{
				var approvers = await ApproversForStepAsync(request.DepartmentId, request.UserId, (LmsTrainingRequestSteps)request.CurrentStep.Value);
				var requester = await NameAsync(request.DepartmentId, request.UserId);
				await _notifier.SendAsync(request.DepartmentId, approvers, "Prg.Notify.RequestTitle", "Prg.Notify.RequestWaiting",
					culture => new object[] { requester, request.Title, request.RequestedCost.ToString("N2", culture), request.Currency }, "TRQ:" + request.LmsTrainingRequestId, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Training request {request.LmsTrainingRequestId} approvers could not be notified.");
			}
		}

		private async Task NotifyRequesterAsync(LmsTrainingRequest request, string messageKey, CancellationToken cancellationToken)
		{
			try
			{
				await _notifier.SendAsync(request.DepartmentId, new[] { request.UserId }, "Prg.Notify.RequestTitle", messageKey,
					culture => new object[] { request.Title }, "TRQ:" + request.LmsTrainingRequestId, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Training request {request.LmsTrainingRequestId} requester could not be notified.");
			}
		}

		private async Task<string> NameAsync(int departmentId, string userId)
		{
			var names = await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>();
			return names.FirstOrDefault(n => Same(n?.UserId, userId))?.Name ?? userId;
		}

		#endregion Notifications

		private static string Trim(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage(new AuditEvent { DepartmentId = departmentId, UserId = userId, Type = type, Before = before, After = after, Successful = true });
		}
	}
}
