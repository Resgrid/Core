using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>What one run of worker 96 did.</summary>
	public class LmsSkillsRunResult
	{
		public int NoticesSent { get; set; }
		public int TaskBooksExpired { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 96 (Training T4 part 2, round 5C): sends the skill notices that are due (immediate ones and the daily or weekly digests) and
	/// expires position task books past their validity. Both steps start from a due-work query (unsent notices due by now; open books past
	/// their expiry) and never sweep departments or ask billing (R-L9): the notices and expiries record work already done. One failing step
	/// never stops the other; the next run picks the work up again.
	/// </summary>
	public class LmsSkillsLogic
	{
		public async Task<LmsSkillsRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<ILmsSkillNotificationService>(), scope.Resolve<ILmsTaskBookService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<LmsSkillsRunResult> Process(ILmsSkillNotificationService notifications, ILmsTaskBookService taskBooks, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var result = new LmsSkillsRunResult();
			result.NoticesSent = await Step(() => notifications.SendDueAsync(utcNow, cancellationToken), result, "notices", cancellationToken);
			result.TaskBooksExpired = await Step(() => taskBooks.ExpireDueAsync(utcNow, cancellationToken), result, "task book expiry", cancellationToken);
			return result;
		}

		private static async Task<int> Step(Func<Task<int>> work, LmsSkillsRunResult result, string name, CancellationToken cancellationToken)
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
				Logging.LogException(ex, $"LMS skills {name} failed.");
				result.Failures++;
				return 0;
			}
		}
	}
}
