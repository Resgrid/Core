using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 95 (Training T4 part 1, round 5B): class reminders, waitlist promotion and the roster retention purge. Due-work queries only
	/// (sessions due a reminder, sessions with someone waitlisted, deleted sessions past retention); the LMS gate once per department with
	/// due work (R-L9). A failure is logged and the next run picks the work up again.
	/// </summary>
	public class LmsClassesLogic
	{
		public async Task<LmsClassSweepResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<ILmsClassSweepService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its service passed in (tests).</summary>
		public static async Task<LmsClassSweepResult> Process(ILmsClassSweepService sweep, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			try
			{
				return await sweep.RunDueAsync(utcNow, cancellationToken) ?? new LmsClassSweepResult();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "LMS class sweep failed.");
				return new LmsClassSweepResult { Failures = 1 };
			}
		}
	}
}
