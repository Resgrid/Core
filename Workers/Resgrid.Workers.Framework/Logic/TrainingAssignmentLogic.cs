using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>The work one run of worker 83 did.</summary>
	public class TrainingAssignmentRunResult
	{
		public int ChangesProcessed { get; set; }
		public int Added { get; set; }
		public int Withdrawn { get; set; }
		public int Repeated { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 83 (Training T0 item 2): re-evaluates dynamic training audiences for exactly the members whose group, role,
	/// shift, certification or unit crew changed, or who joined (the ContentAudienceChanges queue), then runs the trainings
	/// whose one simple repeat is due. It starts from the queue and the due-repeat query, never from a sweep of every
	/// department (§17.3, R-L9), and asks no billing gate (baseline training is free).
	/// </summary>
	public class TrainingAssignmentLogic
	{
		public const int ChangesPerRun = 500;
		public const int RepeatsPerRun = 50;
		public const int ProcessedRetentionDays = 7;

		public async Task<TrainingAssignmentRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<IContentAudienceChangesRepository>(), scope.Resolve<ITrainingAssignmentService>(), scope.Resolve<ITrainingRepository>(),
				DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<TrainingAssignmentRunResult> Process(IContentAudienceChangesRepository changes, ITrainingAssignmentService assignments,
			ITrainingRepository trainings, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new TrainingAssignmentRunResult();

			var pending = await changes.GetPendingAsync(ChangesPerRun) ?? new List<ContentAudienceChange>();
			foreach (var department in pending.GroupBy(c => c.DepartmentId))
			{
				var ids = department.Select(c => c.ContentAudienceChangeId).ToList();
				try
				{
					// A change without a member (a group, role or shift deleted) re-evaluates the department's dynamic trainings once.
					if (department.Any(c => string.IsNullOrWhiteSpace(c.UserId)))
					{
						var outcome = await assignments.ReevaluateDepartmentAsync(department.Key, utcNow, cancellationToken);
						result.Added += outcome.Item1;
						result.Withdrawn += outcome.Item2;
					}
					else
					{
						foreach (var userId in department.Select(c => c.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
						{
							var outcome = await assignments.ReevaluateMemberAsync(department.Key, userId, utcNow, cancellationToken);
							result.Added += outcome.Item1;
							result.Withdrawn += outcome.Item2;
						}
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// The changes are still marked: a failing department must not block the queue; the next change or the
					// member's own My Training view re-evaluates them again.
					Logging.LogException(ex, $"Training audience re-evaluation failed for department {department.Key}");
					result.Failures++;
				}

				await changes.MarkProcessedAsync(ids, utcNow, cancellationToken);
				result.ChangesProcessed += ids.Count;
			}

			foreach (var training in await trainings.GetTrainingsDueForRepeatAsync(utcNow, RepeatsPerRun) ?? new List<Training>())
			{
				try
				{
					result.Repeated += await assignments.RunRepeatAsync(training, utcNow, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Training repeat failed for training {training?.TrainingId}");
					result.Failures++;
				}
			}

			await changes.PurgeProcessedAsync(utcNow.AddDays(-ProcessedRetentionDays), cancellationToken);
			return result;
		}
	}
}
