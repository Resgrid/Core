using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>The work one run of worker 84 did.</summary>
	public class ProtocolLifecycleRunResult
	{
		public int Activated { get; set; }
		public int Expired { get; set; }
		public int RemindersSent { get; set; }
		public int Purged { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 84 (Protocols P1): publishes approved versions whose effective date has come, retires versions past their expiry,
	/// sends owners their review reminders at T-30, T-7 and when overdue, and purges superseded and retired versions past
	/// their kind's retention. Every step starts from a query for due work across departments (scheduled versions, expiring
	/// versions, reviews due, departments with a retention setting); it never sweeps every department and calls no billing or
	/// entitlement gate (protocols are free).
	/// </summary>
	public class ProtocolLifecycleLogic
	{
		public async Task<ProtocolLifecycleRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<IProtocolLibraryService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with the library passed in (tests).</summary>
		public static async Task<ProtocolLifecycleRunResult> Process(IProtocolLibraryService library, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new ProtocolLifecycleRunResult();

			// Activation before expiry: a replacement scheduled for the moment its predecessor expires takes over cleanly.
			result.Activated = await Step(() => library.ActivateDueVersionsAsync(utcNow, cancellationToken), result, "activation", cancellationToken);
			result.Expired = await Step(() => library.ExpireDueVersionsAsync(utcNow, cancellationToken), result, "expiry", cancellationToken);
			result.RemindersSent = await Step(() => library.SendReviewRemindersAsync(utcNow, cancellationToken), result, "review reminders", cancellationToken);
			result.Purged = await Step(() => library.PurgeExpiredRetentionAsync(utcNow, cancellationToken), result, "retention", cancellationToken);

			return result;
		}

		private static async Task<int> Step(Func<Task<int>> work, ProtocolLifecycleRunResult result, string name, CancellationToken cancellationToken)
		{
			try
			{
				return await work();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// One failing step does not stop the others; the next run picks the work up again.
				Logging.LogException(ex, $"Protocol lifecycle {name} failed.");
				result.Failures++;
				return 0;
			}
		}
	}
}
