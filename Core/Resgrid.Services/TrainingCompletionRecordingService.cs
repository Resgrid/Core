using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 7B: the GDPR export's recorded_completions.json: the completions an instructor recorded for the member (never the recordings the
	/// member made for others), with the source, note, attestation version and any void.
	/// </summary>
	public class TrainingCompletionRecordingsGdprSection : IGdprExportSection
	{
		private readonly ITrainingCompletionRecordingsRepository _recordings;

		public TrainingCompletionRecordingsGdprSection(ITrainingCompletionRecordingsRepository recordings)
		{
			_recordings = recordings;
		}

		public string EntryName => "recorded_completions.json";

		public async Task<object> BuildAsync(string userId, int departmentId) =>
			(await _recordings.GetForUserAsync(departmentId, userId) ?? new List<TrainingCompletionRecording>())
				.Where(r => r != null && r.DepartmentId == departmentId && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.Select(r => new
				{
					r.TrainingCompletionRecordingId,
					Target = ((TrainingCompletionRecordingTargets)r.TargetKind).ToString(),
					r.TargetId,
					r.TargetName,
					Source = TrainingCompletionRecordingRules.SourceLabel(r),
					r.Note,
					r.AttestationVersion,
					r.QuizWaived,
					r.CompletedOn,
					r.RecordedByUserId,
					r.RecordedOn,
					r.LearningRecordId,
					r.VoidedOn,
					r.VoidedByUserId,
					r.VoidReason
				}).ToList();
	}

	/// <summary>
	/// Round 7B (product-owner decision 2026-11-25, permission 135; baseline/free): an instructor or training officer records that members
	/// completed a baseline training they took together. Each member is checked on their own (permission scope, active member, not the
	/// recorder, not already complete) and recorded independently: the assignment row is completed (the quiz noted as waived), a recording row
	/// keeps who, when, from where, the note and the attestation version, and the transcript record carries "recorded by" through the T1
	/// writer (credits follow). The first completion stands: a member who already completed keeps their own, and their later quiz attempt
	/// cannot overwrite a recorded one (the assignment is complete). Voids go through the T1 void path, one member's record only. Baseline code
	/// only: the LMS course target lives behind <see cref="ITrainingCompletionRecordingTarget"/>.
	/// </summary>
	public class TrainingCompletionRecordingService : ITrainingCompletionRecordingService
	{
		private readonly ITrainingCompletionRecordingsRepository _recordings;
		private readonly ITrainingService _trainings;
		private readonly ITrainingUserRepository _trainingUsers;
		private readonly ILearningRecordsRepository _records;
		private readonly ILearningRecordWriter _writer;
		private readonly IAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IEnumerable<ITrainingCompletionRecordingTarget> _targets;
		private readonly IEventAggregator _events;
		private readonly ITrainingRecordNoticeService _notices;

		public TrainingCompletionRecordingService(ITrainingCompletionRecordingsRepository recordings, ITrainingService trainings, ITrainingUserRepository trainingUsers,
			ILearningRecordsRepository records, ILearningRecordWriter writer, IAuthorizationService authorization, IDepartmentsService departments,
			IEnumerable<ITrainingCompletionRecordingTarget> targets, IEventAggregator events, ITrainingRecordNoticeService notices)
		{
			_notices = notices;
			_recordings = recordings;
			_trainings = trainings;
			_trainingUsers = trainingUsers;
			_records = records;
			_writer = writer;
			_authorization = authorization;
			_departments = departments;
			_targets = targets ?? Enumerable.Empty<ITrainingCompletionRecordingTarget>();
			_events = events;
		}

		#region Authorization

		public async Task<(TrainingManagementScope Scope, string Refusal)> AuthorizeAsync(int departmentId, TrainingCompletionRecordingRequest request)
		{
			var refusal = TrainingCompletionRecordingRules.RequestRefusal(request);
			if (refusal != null || departmentId <= 0)
				return (null, refusal ?? TrainingCompletionRecordingRules.NotAuthorized);
			var scope = await _authorization.GetTrainingManagementScopeAsync(request.RecorderUserId, departmentId);
			if (scope == null || !scope.CanRecordCompletions)
				return (scope, TrainingCompletionRecordingRules.NotAuthorized);
			return (scope, null);
		}

		/// <summary>Why this member cannot be recorded by this recorder (self, not an active member, outside a group admin's group); null when they can.</summary>
		public static string MemberRefusal(TrainingManagementScope scope, ISet<string> activeMembers, string memberUserId, string recorderUserId)
		{
			if (string.IsNullOrWhiteSpace(memberUserId))
				return TrainingCompletionRecordingRules.NotActiveMember;
			if (string.Equals(memberUserId.Trim(), recorderUserId?.Trim(), StringComparison.OrdinalIgnoreCase))
				return TrainingCompletionRecordingRules.Self;
			if (activeMembers == null || !activeMembers.Contains(memberUserId.Trim()))
				return TrainingCompletionRecordingRules.NotActiveMember;
			if (scope == null || !scope.CanRecordFor(memberUserId))
				return TrainingCompletionRecordingRules.OutsideScope;
			return null;
		}

		#endregion Authorization

		#region Record

		public async Task<TrainingCompletionRecordingOutcome> RecordTrainingAsync(int departmentId, int trainingId, TrainingCompletionRecordingRequest request,
			CancellationToken cancellationToken = default)
		{
			var (scope, refusal) = await AuthorizeAsync(departmentId, request);
			if (refusal != null)
				return TrainingCompletionRecordingOutcome.Fail(refusal);

			var training = await _trainings.GetTrainingByIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return TrainingCompletionRecordingOutcome.Fail(TrainingCompletionRecordingRules.NotFound);

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;
			var completedOn = request.CompletedOnUtc.HasValue && request.CompletedOnUtc.Value < now ? request.CompletedOnUtc.Value : now;
			var batch = string.IsNullOrWhiteSpace(request.BatchKey) ? Guid.NewGuid().ToString("N") : request.BatchKey.Trim();
			var quizWaived = (training.Questions?.Count ?? training.QuestionCount) > 0;
			var outcome = new TrainingCompletionRecordingOutcome();

			foreach (var requested in TrainingCompletionRecordingRules.Members(request))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var result = new TrainingCompletionRecordingResult { UserId = requested };
				outcome.Results.Add(result);

				// A baseline assignment belongs to a member account (TrainingUsers has a key to it): never to an external learner.
				if (requested.StartsWith(LearningRecordQuery.ExternalLearnerUserIdPrefix, StringComparison.OrdinalIgnoreCase))
				{
					result.Refusal = TrainingCompletionRecordingRules.ExternalNotAllowed;
					continue;
				}

				result.Refusal = MemberRefusal(scope, active, requested, request.RecorderUserId);
				if (result.Refusal != null)
					continue;
				var memberId = active.TryGetValue(requested, out var canonical) ? canonical : requested;
				result.UserId = memberId;

				try
				{
					result.Refusal = await RecordOneAsync(training, memberId, request, batch, completedOn, quizWaived, now, result, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"A recorded completion of training {trainingId} for {memberId} failed.");
					result.Refusal = TrainingCompletionRecordingRules.Failed;
				}
			}

			return outcome;
		}

		private async Task<string> RecordOneAsync(Training training, string memberId, TrainingCompletionRecordingRequest request, string batch, DateTime completedOn,
			bool quizWaived, DateTime now, TrainingCompletionRecordingResult result, CancellationToken cancellationToken)
		{
			var assignment = training.Users?.FirstOrDefault(u => u != null && string.Equals(u.UserId, memberId, StringComparison.OrdinalIgnoreCase))
				?? await _trainingUsers.GetTrainingUserByTrainingIdAndUserIdAsync(training.TrainingId, memberId);
			// The first completion stands: the member's own (or an earlier recording) is never replaced.
			if (assignment != null && !assignment.RemovedOn.HasValue && assignment.Complete)
				return TrainingCompletionRecordingRules.AlreadyComplete;

			var recording = await _recordings.InsertAsync(TrainingCompletionRecordingRules.NewRecording(training.DepartmentId, memberId, TrainingCompletionRecordingTargets.Training,
				training.TrainingId, null, training.Name, request, batch, completedOn, quizWaived, now), cancellationToken, true);

			if (assignment == null)
			{
				// Not assigned (a walk-in at the class): the assignment is made complete; no assignment notice is due for it.
				assignment = await _trainingUsers.InsertAssignmentAsync(new TrainingUser
				{
					TrainingId = training.TrainingId,
					UserId = memberId,
					AssignedOn = now,
					AssignmentSource = (int)TrainingAssignmentSources.Manual,
					NotifiedOn = now
				}, cancellationToken) ?? await _trainingUsers.GetTrainingUserByTrainingIdAndUserIdAsync(training.TrainingId, memberId);
				if (assignment == null)
					return TrainingCompletionRecordingRules.Failed;
			}
			else if (assignment.Complete && !assignment.RemovedOn.HasValue)
			{
				return TrainingCompletionRecordingRules.AlreadyComplete;
			}

			assignment.RemovedOn = null;
			assignment.RemovedByUserId = null;
			assignment.Viewed = true;
			assignment.ViewedOn ??= completedOn;
			assignment.Complete = true;
			assignment.CompletedOn = completedOn;
			assignment.CompletionMethod = (int)LearningCompletionMethods.RecordedByOther;
			assignment.RecordedCompletionId = recording.TrainingCompletionRecordingId;
			await _trainingUsers.UpdateColumnsAsync(assignment, new[] { "RemovedOn", "RemovedByUserId", "Viewed", "ViewedOn", "Complete", "CompletedOn", "CompletionMethod", "RecordedCompletionId" },
				cancellationToken);

			// The transcript: the live cycle's completion with the recorded-by metadata (never the member's own).
			var draft = LearningRecordWriter.CompletionDraft(training, assignment.UserId, completedOn, false, assignment);
			draft.RecordedByUserId = recording.RecordedByUserId;
			draft.RecordedOn = now;
			var written = await _writer.WriteSourceAsync(training.DepartmentId, LearningRecordSourceKinds.CourseCompletion,
				LearningRecordRules.CompletionRef(training.TrainingId, assignment.Cycle), new[] { draft }, recording.RecordedByUserId, cancellationToken);

			recording.TrainingUserId = assignment.TrainingUserId;
			recording.Cycle = assignment.Cycle;
			recording.LearningRecordId = written?.Records?.FirstOrDefault(r => r != null && !r.IsVoided)?.LearningRecordId;
			recording = await _recordings.SaveOrUpdateAsync(recording, cancellationToken, true);
			result.Recording = recording;
			Announce(recording, false);
			await NotifyMemberAsync(recording, cancellationToken);
			return null;
		}

		public async Task NotifyMemberAsync(TrainingCompletionRecording recording, CancellationToken cancellationToken = default)
		{
			if (recording == null || _notices == null)
				return;
			try
			{
				await _notices.NotifyCompletionAsync(recording, recording.IsVoided, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The member notice of recorded completion {recording.TrainingCompletionRecordingId} could not be sent.");
			}
		}

		public void Announce(TrainingCompletionRecording recording, bool isExternalLearner)
		{
			if (recording == null)
				return;
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = recording.DepartmentId,
				UserId = recording.RecordedByUserId,
				Type = AuditLogTypes.TrainingCompletionRecorded,
				After = JsonConvert.SerializeObject(new
				{
					recording.TrainingCompletionRecordingId,
					recording.UserId,
					target = ((TrainingCompletionRecordingTargets)recording.TargetKind).ToString(),
					recording.TargetId,
					recording.TargetVersionId,
					source = TrainingCompletionRecordingRules.SourceLabel(recording),
					recording.AttestationVersion,
					recording.QuizWaived,
					recording.LearningRecordId,
					recording.PersonnelCertificationId,
					recording.BatchKey
				}),
				Successful = true,
				ServerName = Environment.MachineName
			});
			_events?.SendMessage(new TrainingCompletionRecordedEvent
			{
				DepartmentId = recording.DepartmentId,
				RecordingId = recording.TrainingCompletionRecordingId,
				UserId = recording.UserId,
				IsExternalLearner = isExternalLearner,
				RecordedByUserId = recording.RecordedByUserId,
				TargetKind = ((TrainingCompletionRecordingTargets)recording.TargetKind).ToString(),
				TargetId = recording.TargetId,
				TargetName = recording.TargetName,
				Source = ((TrainingCompletionRecordingSources)recording.SourceKind).ToString(),
				SessionId = recording.SourceKind == (int)TrainingCompletionRecordingSources.ClassSession && int.TryParse(recording.SourceRefId, out var session) ? session : (int?)null,
				LearningRecordId = recording.LearningRecordId,
				CertificationId = recording.PersonnelCertificationId,
				QuizWaived = recording.QuizWaived,
				AttestationVersion = recording.AttestationVersion,
				CompletedOn = recording.CompletedOn,
				OccurredOn = recording.RecordedOn
			});
		}

		#endregion Record

		#region Void

		public async Task<TrainingCompletionRecordingResult> VoidAsync(int departmentId, int trainingCompletionRecordingId, string userId, string reason,
			CancellationToken cancellationToken = default)
		{
			var recording = departmentId <= 0 ? null : await _recordings.GetByIdForDepartmentAsync(departmentId, trainingCompletionRecordingId);
			var result = new TrainingCompletionRecordingResult { UserId = recording?.UserId, Recording = recording };
			if (recording == null || recording.DepartmentId != departmentId)
			{
				result.Refusal = TrainingCompletionRecordingRules.NotFound;
				return result;
			}
			if (recording.IsVoided)
			{
				result.Refusal = TrainingCompletionRecordingRules.AlreadyVoided;
				return result;
			}
			var why = TrainingCompletionRecordingRules.Trim(reason, TrainingCompletionRecording.ReasonMaxLength);
			if (why == null)
			{
				result.Refusal = TrainingCompletionRecordingRules.ReasonRequired;
				return result;
			}
			var scope = string.IsNullOrWhiteSpace(userId) ? null : await _authorization.GetTrainingManagementScopeAsync(userId, departmentId);
			if (scope == null || !scope.CanRecordFor(recording.UserId))
			{
				result.Refusal = TrainingCompletionRecordingRules.NotAuthorized;
				return result;
			}

			var voidedRecords = new List<LearningRecord>();
			if (recording.TargetKind == (int)TrainingCompletionRecordingTargets.Training)
			{
				voidedRecords.AddRange(await VoidTrainingAsync(recording, userId, why, cancellationToken));
			}
			else
			{
				var target = _targets.FirstOrDefault(t => (int)t.Kind == recording.TargetKind);
				if (target == null)
				{
					result.Refusal = TrainingCompletionRecordingRules.Failed;
					return result;
				}
				await target.VoidAsync(recording, userId, why, cancellationToken);
			}

			var before = JsonConvert.SerializeObject(new { recording.TrainingCompletionRecordingId, recording.UserId, recording.TargetKind, recording.TargetId, recording.LearningRecordId });
			recording.VoidedOn = DateTime.UtcNow;
			recording.VoidedByUserId = userId;
			recording.VoidReason = why;
			result.Recording = await _recordings.SaveOrUpdateAsync(recording, cancellationToken, true);
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.TrainingCompletionRecordVoided,
				Before = before,
				After = JsonConvert.SerializeObject(new { recording.TrainingCompletionRecordingId, recording.VoidReason }),
				Successful = true,
				ServerName = Environment.MachineName
			});

			// Round 9A: once the assignment or enrolment is open again, a certification the completion issued or renewed goes back to its
			// earlier state (unless another completion still covers it): the certification engine hears about the voided record here.
			try
			{
				if (recording.LearningRecordId.HasValue && voidedRecords.All(r => r.LearningRecordId != recording.LearningRecordId.Value))
				{
					var record = await _records.GetByIdForDepartmentAsync(departmentId, recording.LearningRecordId.Value);
					if (record != null && record.IsVoided)
						voidedRecords.Add(record);
				}
				await _writer.NotifyChangedAsync(departmentId, null, voidedRecords, userId, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, $"The certification engine was not told about voided recording {recording.TrainingCompletionRecordingId}.");
			}

			// Round 9B: workflow trigger 285 (ids only, never the reason) and the member's notice.
			var isExternal = (recording.UserId ?? string.Empty).StartsWith(LearningRecordQuery.ExternalLearnerUserIdPrefix, StringComparison.OrdinalIgnoreCase);
			_events?.SendMessage(new TrainingCompletionVoidedWorkflowEvent
			{
				DepartmentId = departmentId,
				RecordingId = recording.TrainingCompletionRecordingId,
				UserId = recording.UserId,
				VoidedByUserId = userId,
				TargetKind = ((TrainingCompletionRecordingTargets)recording.TargetKind).ToString(),
				TargetId = recording.TargetId,
				TargetName = recording.TargetName,
				LearningRecordId = recording.LearningRecordId,
				CompletedOn = recording.CompletedOn,
				OccurredOn = recording.VoidedOn ?? DateTime.UtcNow
			});
			if (!isExternal)
				await NotifyMemberAsync(recording, cancellationToken);
			return result;
		}

		private async Task<List<LearningRecord>> VoidTrainingAsync(TrainingCompletionRecording recording, string userId, string reason, CancellationToken cancellationToken)
		{
			var voided = new List<LearningRecord>();
			if (recording.Cycle.HasValue)
				voided = await VoidMemberRecordListAsync(_records, _writer, recording.DepartmentId, LearningRecordSourceKinds.CourseCompletion,
					LearningRecordRules.CompletionRef(recording.TargetId, recording.Cycle.Value), recording.UserId, reason, userId, cancellationToken);

			// The assignment is open again (the member takes the training themselves), only while this recording is still its completion.
			var assignment = await _trainingUsers.GetTrainingUserByTrainingIdAndUserIdAsync(recording.TargetId, recording.UserId);
			if (assignment != null && assignment.RecordedCompletionId == recording.TrainingCompletionRecordingId && assignment.Cycle == (recording.Cycle ?? assignment.Cycle))
			{
				assignment.Complete = false;
				assignment.CompletedOn = null;
				assignment.CompletionMethod = (int)LearningCompletionMethods.Self;
				assignment.RecordedCompletionId = null;
				await _trainingUsers.UpdateColumnsAsync(assignment, new[] { "Complete", "CompletedOn", "CompletionMethod", "RecordedCompletionId" }, cancellationToken);
			}
			return voided;
		}

		/// <summary>
		/// Voids one member's live records of a source (a baseline completion source is shared by every member of the cycle, so the whole
		/// source is never voided): the T1 void with the credits removed.
		/// </summary>
		public static async Task<int> VoidMemberRecordsAsync(ILearningRecordsRepository records, ILearningRecordWriter writer, int departmentId, LearningRecordSourceKinds kind,
			string sourceRefId, string memberUserId, string reason, string actingUserId, CancellationToken cancellationToken) =>
			(await VoidMemberRecordListAsync(records, writer, departmentId, kind, sourceRefId, memberUserId, reason, actingUserId, cancellationToken)).Count;

		/// <summary>As <see cref="VoidMemberRecordsAsync"/>, returning the records voided (round 9A: the certification engine is told about them).</summary>
		public static async Task<List<LearningRecord>> VoidMemberRecordListAsync(ILearningRecordsRepository records, ILearningRecordWriter writer, int departmentId,
			LearningRecordSourceKinds kind, string sourceRefId, string memberUserId, string reason, string actingUserId, CancellationToken cancellationToken)
		{
			var voided = new List<LearningRecord>();
			var now = DateTime.UtcNow;
			foreach (var record in (await records.GetBySourceAsync(departmentId, (int)kind, sourceRefId) ?? new List<LearningRecord>())
				.Where(r => r != null && !r.IsVoided && string.Equals(r.UserId, memberUserId, StringComparison.OrdinalIgnoreCase)))
			{
				if (!await records.VoidAsync(departmentId, record.LearningRecordId, "recorded_completion_voided: " + reason, actingUserId, now, cancellationToken))
					continue;
				record.Status = (int)LearningRecordStatuses.Voided;
				voided.Add(record);
				await writer.RemoveCreditsAsync(record, actingUserId, cancellationToken);
			}
			return voided;
		}

		#endregion Void

		#region Reads

		public async Task<TrainingCompletionRecording> GetAsync(int departmentId, int trainingCompletionRecordingId)
		{
			var recording = departmentId <= 0 ? null : await _recordings.GetByIdForDepartmentAsync(departmentId, trainingCompletionRecordingId);
			return recording != null && recording.DepartmentId == departmentId ? recording : null;
		}

		public async Task<List<TrainingCompletionRecording>> GetForMemberAsync(int departmentId, string userId) =>
			departmentId <= 0 || string.IsNullOrWhiteSpace(userId) ? new List<TrainingCompletionRecording>() :
			(await _recordings.GetForUserAsync(departmentId, userId) ?? new List<TrainingCompletionRecording>()).Where(r => r.DepartmentId == departmentId).ToList();

		public async Task<List<TrainingCompletionRecording>> GetForTargetAsync(int departmentId, TrainingCompletionRecordingTargets target, int targetId) =>
			departmentId <= 0 ? new List<TrainingCompletionRecording>() :
			(await _recordings.GetForTargetAsync(departmentId, (int)target, targetId) ?? new List<TrainingCompletionRecording>()).Where(r => r.DepartmentId == departmentId).ToList();

		public async Task<List<TrainingCompletionRecording>> GetForSourceAsync(int departmentId, TrainingCompletionRecordingSources source, string sourceRefId) =>
			departmentId <= 0 ? new List<TrainingCompletionRecording>() :
			(await _recordings.GetForSourceAsync(departmentId, (int)source, sourceRefId) ?? new List<TrainingCompletionRecording>()).Where(r => r.DepartmentId == departmentId).ToList();

		public async Task<TrainingCompletionRecordingView> GetTrainingRecordingViewAsync(int departmentId, int trainingId, string viewerUserId)
		{
			var training = departmentId <= 0 ? null : await _trainings.GetTrainingByIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return null;
			var view = new TrainingCompletionRecordingView
			{
				Target = TrainingCompletionRecordingTargets.Training,
				TargetId = trainingId,
				TargetName = training.Name,
				HasQuiz = (training.Questions?.Count ?? training.QuestionCount) > 0
			};
			var scope = string.IsNullOrWhiteSpace(viewerUserId) ? null : await _authorization.GetTrainingManagementScopeAsync(viewerUserId, departmentId);
			view.CanRecord = scope?.CanRecordCompletions == true;
			view.Recordings = await GetForTargetAsync(departmentId, TrainingCompletionRecordingTargets.Training, trainingId);
			var live = view.Recordings.Where(r => !r.IsVoided).GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var assignments = (training.Users ?? new List<TrainingUser>()).Where(u => u != null && !string.IsNullOrWhiteSpace(u.UserId) && !u.RemovedOn.HasValue)
				.GroupBy(u => u.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var userId in active)
			{
				assignments.TryGetValue(userId, out var assignment);
				var refusal = MemberRefusal(scope, active, userId, viewerUserId);
				if (refusal == TrainingCompletionRecordingRules.Self || refusal == TrainingCompletionRecordingRules.OutsideScope)
					continue;
				view.Candidates.Add(new TrainingCompletionCandidate
				{
					UserId = userId,
					Assigned = assignment != null && !assignment.Complete,
					Refusal = refusal ?? (assignment?.Complete == true ? TrainingCompletionRecordingRules.AlreadyComplete : null),
					Recording = live.TryGetValue(userId, out var recording) ? recording : null
				});
			}
			view.Candidates = view.Candidates.OrderByDescending(c => c.Assigned).ThenBy(c => c.Refusal != null).ToList();
			return view;
		}

		public async Task<Dictionary<int, TrainingCompletionRecording>> GetForRecordsAsync(int departmentId, IEnumerable<LearningRecord> records)
		{
			var ids = (records ?? Enumerable.Empty<LearningRecord>()).Where(r => r?.RecordedCompletionId != null).Select(r => r.RecordedCompletionId.Value).Distinct().ToList();
			if (departmentId <= 0 || ids.Count == 0)
				return new Dictionary<int, TrainingCompletionRecording>();
			return (await _recordings.GetByIdsAsync(departmentId, ids) ?? new List<TrainingCompletionRecording>())
				.Where(r => r.DepartmentId == departmentId).GroupBy(r => r.TrainingCompletionRecordingId).ToDictionary(g => g.Key, g => g.First());
		}

		#endregion Reads
	}
}
