using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;

using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The history backfill (R-T3), run by worker 85 one claimed job at a time: training completions (live rows and the T0
	/// completion history), legacy Training Record (Log) participants and finalized Records training records become Verified,
	/// IsBackfill learning records with their original dates. Sources that already have a record are skipped, so a re-run or a
	/// resumed run never duplicates. A dry run only counts. Backfilled records never credit a certification by themselves;
	/// "apply history to credits" does that per certification type. Calendar check-ins are not backfilled (no existing type is
	/// marked as training).
	/// </summary>
	public class LearningRecordBackfillService : ILearningRecordBackfillService
	{
		public const string StageTrainings = "trainings";
		public const string StageLogs = "logs";
		public const string StageRecords = "records";

		private static readonly string[] Stages = { StageTrainings, StageLogs, StageRecords };
		private static readonly int[] RecordsFinalStates = { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended, (int)RmsRecordState.Submitted, (int)RmsRecordState.Accepted };

		private readonly ILearningRecordBackfillsRepository _backfills;
		private readonly ILearningRecordsRepository _records;
		private readonly ILearningRecordWriter _writer;
		private readonly ITrainingRepository _trainings;
		private readonly ITrainingUserRepository _trainingUsers;
		private readonly ITrainingCompletionHistoriesRepository _histories;
		private readonly ILogsRepository _logs;
		private readonly ILogUsersRepository _logUsers;
		private readonly IRmsOperationalRecordsRepository _rmsRecords;
		private readonly IRmsRecordParticipantsRepository _rmsParticipants;

		public LearningRecordBackfillService(ILearningRecordBackfillsRepository backfills, ILearningRecordsRepository records, ILearningRecordWriter writer,
			ITrainingRepository trainings, ITrainingUserRepository trainingUsers, ITrainingCompletionHistoriesRepository histories, ILogsRepository logs,
			ILogUsersRepository logUsers, IRmsOperationalRecordsRepository rmsRecords, IRmsRecordParticipantsRepository rmsParticipants)
		{
			_backfills = backfills;
			_records = records;
			_writer = writer;
			_trainings = trainings;
			_trainingUsers = trainingUsers;
			_histories = histories;
			_logs = logs;
			_logUsers = logUsers;
			_rmsRecords = rmsRecords;
			_rmsParticipants = rmsParticipants;
		}

		public async Task<LearningRecordBackfill> RunAsync(LearningRecordBackfill backfill, int budget, CancellationToken cancellationToken = default)
		{
			if (backfill == null || backfill.IsFinished)
				return backfill;

			var (stage, after) = Parse(backfill.ResumeAfter);
			var left = Math.Max(1, budget);
			try
			{
				for (var index = Array.IndexOf(Stages, stage); index >= 0 && index < Stages.Length && left > 0; index++)
				{
					var current = Stages[index];
					var cursor = current == stage ? after : null;
					string last;
					switch (current)
					{
						case StageTrainings:
							(last, left) = await TrainingsAsync(backfill, cursor, left, cancellationToken);
							break;
						case StageLogs:
							(last, left) = await LogsAsync(backfill, cursor, left, cancellationToken);
							break;
						default:
							(last, left) = await RecordsAsync(backfill, cursor, left, cancellationToken);
							break;
					}

					if (left <= 0 && last != null)
					{
						// The budget ran out inside this stage: the next run (five minutes on) resumes after the last source handled.
						backfill.ResumeAfter = current + ":" + last;
						backfill.State = (int)LearningRecordBackfillStates.Queued;
						backfill.Attempts = 0;
						await _backfills.SaveProgressAsync(backfill, cancellationToken);
						return backfill;
					}

					backfill.ResumeAfter = index + 1 < Stages.Length ? Stages[index + 1] + ":" : null;
					await _backfills.SaveProgressAsync(backfill, cancellationToken);
				}

				backfill.State = (int)LearningRecordBackfillStates.Completed;
				backfill.CompletedOn = DateTime.UtcNow;
				backfill.ResumeAfter = null;
				backfill.Error = null;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				await _backfills.SaveProgressAsync(backfill, CancellationToken.None);
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Training history backfill {backfill.LearningRecordBackfillId} failed for department {backfill.DepartmentId}");
				backfill.Error = LearningRecordRules.Trim(ex.Message, 1000);
				// Three attempts, then the job stops and the officer sees the error; progress so far stays written.
				if (backfill.Attempts >= 3)
				{
					backfill.State = (int)LearningRecordBackfillStates.Failed;
					backfill.CompletedOn = DateTime.UtcNow;
				}
				else
				{
					backfill.State = (int)LearningRecordBackfillStates.Queued;
				}
			}

			await _backfills.SaveProgressAsync(backfill, CancellationToken.None);
			return backfill;
		}

		public static (string Stage, string After) Parse(string resumeAfter)
		{
			if (string.IsNullOrWhiteSpace(resumeAfter))
				return (StageTrainings, null);

			var separator = resumeAfter.IndexOf(':');
			var stage = separator < 0 ? resumeAfter : resumeAfter.Substring(0, separator);
			var after = separator < 0 ? null : resumeAfter.Substring(separator + 1);
			return (Stages.Contains(stage) ? stage : StageTrainings, string.IsNullOrEmpty(after) ? null : after);
		}

		private async Task<(string Last, int Left)> TrainingsAsync(LearningRecordBackfill job, string after, int left, CancellationToken ct)
		{
			var afterId = int.TryParse(after, out var parsed) ? parsed : 0;
			string last = null;
			foreach (var training in ((await _trainings.GetTrainingsByDepartmentIdAsync(job.DepartmentId)) ?? Enumerable.Empty<Training>())
				.Where(t => t != null && t.DepartmentId == job.DepartmentId && t.TrainingId > afterId).OrderBy(t => t.TrainingId))
			{
				if (left <= 0)
					break;

				var completions = new Dictionary<string, (string UserId, DateTime CompletedOn)>(StringComparer.Ordinal);
				var liveRows = new Dictionary<string, TrainingUser>(StringComparer.Ordinal);
				foreach (var row in (await _trainingUsers.GetByTrainingIdAsync(training.TrainingId) ?? new List<TrainingUser>()).Where(r => r.Complete && r.CompletedOn.HasValue))
				{
					var liveKey = LearningRecordRules.CompletionRef(training.TrainingId, row.Cycle) + "|" + row.UserId.ToUpperInvariant();
					completions[liveKey] = (row.UserId, row.CompletedOn.Value);
					liveRows[liveKey] = row;
				}
				foreach (var history in (await _histories.GetForTrainingAsync(job.DepartmentId, training.TrainingId) ?? new List<TrainingCompletionHistory>()).Where(h => h.CompletedOn.HasValue))
				{
					var key = LearningRecordRules.CompletionRef(training.TrainingId, history.Cycle) + "|" + history.UserId.ToUpperInvariant();
					if (!completions.ContainsKey(key))
						completions[key] = (history.UserId, history.CompletedOn.Value);
				}

				job.CompletionsFound += completions.Count;
				var existing = await ExistingAsync(job.DepartmentId, LearningRecordSourceKinds.CourseCompletion, completions.Keys.Select(k => k.Substring(0, k.IndexOf('|'))));
				foreach (var (key, completion) in completions)
				{
					var reference = key.Substring(0, key.IndexOf('|'));
					if (existing.Contains(key))
					{
						job.RecordsSkipped++;
						continue;
					}

					if (!job.DryRun)
					{
						var written = await _writer.WriteSourceAsync(job.DepartmentId, LearningRecordSourceKinds.CourseCompletion, reference,
							new[] { LearningRecordWriter.CompletionDraft(training, completion.UserId, completion.CompletedOn, true, liveRows.TryGetValue(key, out var liveRow) ? liveRow : null) },
							LearningRecordWriter.SystemUserId, ct);
						job.RecordsWritten += written.Written;
						job.RecordsSkipped += written.Unchanged;
					}
					else
					{
						job.RecordsWritten++;
					}
				}

				left -= Math.Max(1, completions.Count);
				last = training.TrainingId.ToString();
			}

			return (last, left);
		}

		private async Task<(string Last, int Left)> LogsAsync(LearningRecordBackfill job, string after, int left, CancellationToken ct)
		{
			var afterId = int.TryParse(after, out var parsed) ? parsed : 0;
			string last = null;
			foreach (var log in ((await _logs.GetAllByDepartmentIdAsync(job.DepartmentId)) ?? Enumerable.Empty<Log>())
				.Where(l => l != null && l.DepartmentId == job.DepartmentId && l.LogType == (int)LogTypes.Training && l.LogId > afterId).OrderBy(l => l.LogId))
			{
				if (left <= 0)
					break;

				var users = (await _logUsers.GetLogsByLogIdAsync(log.LogId) ?? Enumerable.Empty<LogUser>()).ToList();
				var drafts = LearningRecordWriter.LogDrafts(log, users, true);
				await BackfillSourceAsync(job, LearningRecordSourceKinds.TrainingRecord, LearningRecordRules.LogRef(log.LogId), drafts, ct);
				left -= Math.Max(1, drafts.Count);
				last = log.LogId.ToString();
			}

			return (last, left);
		}

		private async Task<(string Last, int Left)> RecordsAsync(LearningRecordBackfill job, string after, int left, CancellationToken ct)
		{
			string last = null;
			var records = ((await _rmsRecords.GetByDefinitionAndStartedRangeAsync(job.DepartmentId, RmsDefinitionKeys.Training, RecordsFinalStates,
					new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(2))) ?? Enumerable.Empty<RmsOperationalRecord>())
				.Where(r => r != null && r.DeletedOn == null && r.PurgedOn == null && !string.IsNullOrEmpty(r.CurrentRevisionId) &&
					(after == null || string.CompareOrdinal(r.RmsOperationalRecordId, after) > 0))
				.OrderBy(r => r.RmsOperationalRecordId, StringComparer.Ordinal);

			foreach (var record in records)
			{
				if (left <= 0)
					break;

				var participants = (await _rmsParticipants.GetForRecordAsync(job.DepartmentId, record.RmsOperationalRecordId, record.CurrentRevisionId) ?? Enumerable.Empty<RmsRecordParticipant>())
					.Where(p => p != null && p.DeletedOn == null && !string.IsNullOrWhiteSpace(p.UserId)).ToList();
				var drafts = LearningRecordWriter.RecordsDrafts(record, participants, true);
				await BackfillSourceAsync(job, LearningRecordSourceKinds.TrainingRecord, LearningRecordRules.RecordsRef(record.RmsOperationalRecordId), drafts, ct);
				left -= Math.Max(1, drafts.Count);
				last = record.RmsOperationalRecordId;
			}

			return (last, left);
		}

		/// <summary>A Training Record already on the ledger is left as it is; only a source with no record is written.</summary>
		private async Task BackfillSourceAsync(LearningRecordBackfill job, LearningRecordSourceKinds kind, string reference, List<LearningRecordDraft> drafts, CancellationToken ct)
		{
			job.TrainingRecordsFound += drafts.Count;
			if (drafts.Count == 0)
				return;

			var existing = await _records.GetExistingSourceRefIdsAsync(job.DepartmentId, (int)kind, new[] { reference });
			if (existing.Contains(reference))
			{
				job.RecordsSkipped += drafts.Count;
				return;
			}

			if (job.DryRun)
			{
				job.RecordsWritten += drafts.Count;
				return;
			}

			var written = await _writer.WriteSourceAsync(job.DepartmentId, kind, reference, drafts, LearningRecordWriter.SystemUserId, ct);
			job.RecordsWritten += written.Written;
			job.RecordsSkipped += written.Unchanged;
		}

		/// <summary>The completion keys (ref|USER) that already have a record.</summary>
		private async Task<HashSet<string>> ExistingAsync(int departmentId, LearningRecordSourceKinds kind, IEnumerable<string> references)
		{
			var result = new HashSet<string>(StringComparer.Ordinal);
			var refs = references.Distinct(StringComparer.Ordinal).ToList();
			if (refs.Count == 0)
				return result;

			foreach (var record in await _records.GetBySourceRefIdsAsync(departmentId, (int)kind, refs) ?? new List<LearningRecord>())
				result.Add(record.SourceRefId + "|" + (record.UserId ?? string.Empty).ToUpperInvariant());
			return result;
		}
	}
}
