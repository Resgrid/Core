using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>The work one run of worker 93 did.</summary>
	public class LmsRequirementsRunResult
	{
		public int DueDepartments { get; set; }
		public int DailyPasses { get; set; }
		public int Paused { get; set; }
		public int Transitions { get; set; }
		public int Notified { get; set; }
		public int Assigned { get; set; }
		public int Enforced { get; set; }
		public int Refused { get; set; }
		public int Undone { get; set; }
		public int PathsLinked { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 93 (Training T3, LMS): links Academy role-path assignments to their system paths, then runs each department's daily pass of
	/// the learning requirements engine (due dates and states, at-risk and overdue notices, onboarding assignment, training-backed
	/// enforcement under the blast-radius guard, path advancement), and undoes enforcement for members who completed. It starts from the
	/// due-work query (departments with a live requirement, an open enforcement action or an open department path) and claims the
	/// department-local day before a daily pass, so the LMS gate is asked once per department per day (R-L9); the undo pass needs no gate.
	/// </summary>
	public class LmsRequirementsLogic
	{
		public async Task<LmsRequirementsRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<ILmsRequirementsRepository>(), scope.Resolve<ILmsRequirementEnforcementsRepository>(),
				scope.Resolve<ILmsRequirementDepartmentStatesRepository>(), scope.Resolve<ILmsRequirementService>(), scope.Resolve<ILmsPathService>(),
				scope.Resolve<IDepartmentsService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<LmsRequirementsRunResult> Process(ILmsRequirementsRepository requirements, ILmsRequirementEnforcementsRepository enforcements,
			ILmsRequirementDepartmentStatesRepository states, ILmsRequirementService engine, ILmsPathService paths, IDepartmentsService departments, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var result = new LmsRequirementsRunResult();

			try
			{
				result.PathsLinked = await paths.EnsureSystemPathsAsync(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Worker 93: the Academy role paths could not be seeded or linked.");
				result.Failures++;
			}

			// Daily passes: only departments the due-work query names, each once per department-local day (claimed first).
			var due = await requirements.GetDepartmentIdsWithDueWorkAsync() ?? new List<int>();
			result.DueDepartments = due.Count;
			foreach (var departmentId in due)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var department = await departments.GetDepartmentByIdAsync(departmentId, false);
				if (department == null)
					continue;

				var localToday = TrainingDueDates.LocalDate(utcNow, department);
				if (!await states.TryClaimDayAsync(departmentId, localToday, utcNow, cancellationToken))
					continue;

				try
				{
					var pass = await engine.RunDailyAsync(departmentId, utcNow, cancellationToken);
					result.DailyPasses++;
					result.Paused += pass.Paused ? 1 : 0;
					result.Transitions += pass.Transitions;
					result.Notified += pass.Notified;
					result.Assigned += pass.Assigned;
					result.Enforced += pass.Enforced;
					result.Refused += pass.Refused ? 1 : 0;
					result.Undone += pass.Undone;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					await states.ReleaseDayAsync(departmentId, localToday, CancellationToken.None);
					throw;
				}
				catch (Exception ex)
				{
					// The day goes back, so the next tick retries this department; the others go on.
					Logging.LogException(ex, $"Worker 93: the learning requirement pass for department {departmentId} failed.");
					await states.ReleaseDayAsync(departmentId, localToday, CancellationToken.None);
					result.Failures++;
				}
			}

			// Undo pass: members who completed get their certification and roles back within a run, not a day. No gate.
			foreach (var departmentId in await enforcements.GetDepartmentIdsWithOpenAsync() ?? new List<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					result.Undone += await engine.UndoCompletedAsync(departmentId, utcNow, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Worker 93: enforcement undo for department {departmentId} failed.");
					result.Failures++;
				}
			}

			return result;
		}
	}
}
