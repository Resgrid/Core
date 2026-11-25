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

		/// <summary>Protocol acknowledgments the same changes assigned (P2, R-P3: members entering an audience) and withdrew.</summary>
		public int AcknowledgmentsAdded { get; set; }
		public int AcknowledgmentsWithdrawn { get; set; }

		/// <summary>T3: members who entered (or left) a learning requirement's audience through the same changes.</summary>
		public int RequirementMembersAdded { get; set; }
		public int RequirementMembersLeft { get; set; }
	}

	/// <summary>
	/// Worker 83 (Training T0 item 2): re-evaluates dynamic training audiences for exactly the members whose group, role,
	/// shift, certification or unit crew changed, or who joined (the ContentAudienceChanges queue), then runs the trainings
	/// whose one simple repeat is due. The same queue drives protocol acknowledgment (P2, R-P3): a member entering a protocol's
	/// audience is assigned every version in force that requires acknowledgment, one leaving it has open ones withdrawn. It starts from the queue and the due-repeat query, never from a sweep of every
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
				scope.Resolve<IProtocolAcknowledgmentService>(), scope.Resolve<ILmsRequirementService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run without protocol acknowledgments (the training-only tests).</summary>
		public static Task<TrainingAssignmentRunResult> Process(IContentAudienceChangesRepository changes, ITrainingAssignmentService assignments,
			ITrainingRepository trainings, DateTime utcNow, CancellationToken cancellationToken = default) =>
			Process(changes, assignments, trainings, null, utcNow, cancellationToken);

		/// <summary>The run without learning requirements (the P2 tests).</summary>
		public static Task<TrainingAssignmentRunResult> Process(IContentAudienceChangesRepository changes, ITrainingAssignmentService assignments,
			ITrainingRepository trainings, IProtocolAcknowledgmentService acknowledgments, DateTime utcNow, CancellationToken cancellationToken = default) =>
			Process(changes, assignments, trainings, acknowledgments, null, utcNow, cancellationToken);

		/// <summary>
		/// The run itself, with its services passed in (tests). T3: the same changes feed the LMS learning requirements (join, role add and
		/// any audience entry start a member's due date and onboarding); the engine returns after one query for a department without a live
		/// requirement and asks the LMS gate once for a department with one (R-L9: the queue is the due-work filter).
		/// </summary>
		public static async Task<TrainingAssignmentRunResult> Process(IContentAudienceChangesRepository changes, ITrainingAssignmentService assignments,
			ITrainingRepository trainings, IProtocolAcknowledgmentService acknowledgments, ILmsRequirementService requirements, DateTime utcNow,
			CancellationToken cancellationToken = default)
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

				if (acknowledgments != null)
				{
					// Protocol acknowledgments ride the same queue (no second queue): the same members, the same department change.
					try
					{
						if (department.Any(c => string.IsNullOrWhiteSpace(c.UserId)))
						{
							var outcome = await acknowledgments.ReevaluateDepartmentAsync(department.Key, utcNow, cancellationToken);
							result.AcknowledgmentsAdded += outcome.Item1;
							result.AcknowledgmentsWithdrawn += outcome.Item2;
						}
						else
						{
							foreach (var userId in department.Select(c => c.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
							{
								var outcome = await acknowledgments.ReevaluateMemberAsync(department.Key, userId, utcNow, cancellationToken);
								result.AcknowledgmentsAdded += outcome.Item1;
								result.AcknowledgmentsWithdrawn += outcome.Item2;
							}
						}
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Protocol acknowledgment re-evaluation failed for department {department.Key}");
						result.Failures++;
					}
				}

				if (requirements != null)
				{
					try
					{
						var users = department.Any(c => string.IsNullOrWhiteSpace(c.UserId))
							? null
							: department.Select(c => c.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
						var outcome = await requirements.OnAudienceChangesAsync(department.Key, users, utcNow, cancellationToken);
						result.RequirementMembersAdded += outcome.MembersAdded;
						result.RequirementMembersLeft += outcome.MembersLeft;
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						// Worker 93's daily pass reconciles the department anyway; the queue is not held up.
						Logging.LogException(ex, $"Learning requirement re-evaluation failed for department {department.Key}");
						result.Failures++;
					}
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
