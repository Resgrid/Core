using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 91 (member wellness WL0 and WL1, protocols-training-knowledge-plan §13b): builds post-call exposure suggestions for flagged calls
	/// that have ended, sends the opt-in critical-incident anniversary reminders (daily, from 09:00 department-local, claimed before
	/// sending) and erases entries past their retention (never under 30 years) that no legal hold covers. Every pass starts from a
	/// query for due work, never from a sweep of every department. Workforce Pro (2026-11-26): the services ask the add-on gate once per
	/// department with due work and skip (after claiming) the work of departments that are not licensed and of members without a seat;
	/// retention erasure is never gated. A failing pass does not stop
	/// the others. WL1 adds two steps: overdue Ryan White answers and due return-to-duty reviews (to the officers, claimed first), and the
	/// retention of exposure cases (30 years), injuries (5 years after their year) and medical clearances (30 years).
	/// </summary>
	public class WellnessLogic
	{
		public async Task<WellnessRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<IMemberWellnessService>(), scope.Resolve<IMemberHealthService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The WL0 passes only (tests written before WL1).</summary>
		public static Task<WellnessRunResult> Process(IMemberWellnessService wellness, DateTime utcNow, CancellationToken cancellationToken = default) =>
			Process(wellness, null, utcNow, cancellationToken);

		/// <summary>The run itself, with the services passed in (tests). <paramref name="health"/> adds the WL1 steps when given.</summary>
		public static async Task<WellnessRunResult> Process(IMemberWellnessService wellness, IMemberHealthService health, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new WellnessRunResult();

			try
			{
				var flags = await wellness.ProcessPendingFlagsAsync(utcNow, cancellationToken) ?? new WellnessRunResult();
				result.FlagsProcessed = flags.FlagsProcessed;
				result.SuggestionsCreated = flags.SuggestionsCreated;
				result.Failures += flags.Failures;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Wellness: post-call suggestions failed.");
				result.Failures++;
			}

			try
			{
				result.RemindersSent = await wellness.SendAnniversaryRemindersAsync(utcNow, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Wellness: anniversary reminders failed.");
				result.Failures++;
			}

			try
			{
				result.EntriesErased = await wellness.EraseExpiredAsync(utcNow, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Wellness: retention failed.");
				result.Failures++;
			}

			if (health == null)
				return result;

			// WL1 steps: overdue Ryan White answers and due return-to-duty reviews (claimed before sending), then WL1 retention.
			try
			{
				result.HealthNoticesSent = await health.SendDueNoticesAsync(utcNow, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Wellness: member health notices failed.");
				result.Failures++;
			}

			try
			{
				result.HealthRecordsErased = await health.EraseExpiredAsync(utcNow, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Wellness: member health retention failed.");
				result.Failures++;
			}

			return result;
		}
	}
}
