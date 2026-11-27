using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>The work one run of worker 85 did.</summary>
	public class TrainingRecordsRunResult
	{
		public int Jobs { get; set; }
		public int Completed { get; set; }
		public int Written { get; set; }
		public int Failures { get; set; }

		/// <summary>Round 9B: reviewer digest notices sent.</summary>
		public int DigestNotices { get; set; }
	}

	/// <summary>
	/// Worker 85 (Training T1 item 3, R-T3): runs the queued, resumable per-department history backfills - training completions
	/// and Training Record participants become Verified, IsBackfill learning records; a dry run only counts. It starts from the
	/// job table, never from a sweep of every department, and asks no billing gate (the transcript is free, R-L9). A job whose
	/// worker stopped is picked up again after <see cref="StaleAfterMinutes"/>.
	/// </summary>
	public class TrainingRecordsLogic
	{
		public const int JobsPerRun = 3;
		public const int SourcesPerJob = 2000;
		public const int StaleAfterMinutes = 30;

		public async Task<TrainingRecordsRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			var result = await Process(scope.Resolve<ILearningRecordBackfillsRepository>(), scope.Resolve<ILearningRecordBackfillService>(), DateTime.UtcNow, cancellationToken);
			result.DigestNotices = await SendDigests(scope.Resolve<ITrainingSubmissionService>(), DateTime.UtcNow, cancellationToken);
			return result;
		}

		/// <summary>
		/// Round 9B: the daily reviewer digest of training hours submissions, for the departments that chose it. Reads only departments with
		/// submissions waiting; claimed per department and local day, so the five-minute schedule sends each day once.
		/// </summary>
		public static async Task<int> SendDigests(ITrainingSubmissionService submissions, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			try
			{
				return await submissions.SendReviewerDigestsAsync(utcNow, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Training review digests stopped.");
				return 0;
			}
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<TrainingRecordsRunResult> Process(ILearningRecordBackfillsRepository backfills, ILearningRecordBackfillService runner, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var result = new TrainingRecordsRunResult();
			foreach (var job in await backfills.GetRunnableAsync(utcNow.AddMinutes(-StaleAfterMinutes), JobsPerRun) ?? new List<LearningRecordBackfill>())
			{
				// Claim first: two worker hosts never run one job at the same time.
				if (!await backfills.ClaimAsync(job.LearningRecordBackfillId, job.State, job.StartedOn, utcNow, cancellationToken))
					continue;

				job.State = (int)LearningRecordBackfillStates.Running;
				job.StartedOn = utcNow;
				job.Attempts++;
				result.Jobs++;
				var before = job.RecordsWritten;
				try
				{
					var done = await runner.RunAsync(job, SourcesPerJob, cancellationToken);
					result.Written += Math.Max(0, (done?.RecordsWritten ?? before) - before);
					if (done != null && done.State == (int)LearningRecordBackfillStates.Completed)
						result.Completed++;
					if (done != null && done.State == (int)LearningRecordBackfillStates.Failed)
						result.Failures++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Training history backfill {job.LearningRecordBackfillId} stopped for department {job.DepartmentId}");
					result.Failures++;
				}
			}

			return result;
		}
	}
}
