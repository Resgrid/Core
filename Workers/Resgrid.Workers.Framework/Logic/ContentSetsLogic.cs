using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 100 (round 10 A3, Protocols P5 / Training T5): accepts set updates for subscriptions that auto-accept and for locked items past
	/// their deadline, applies accepted updates whose apply time has come (the subscriber's update time, never mid-shift), picks up edited
	/// baseline trainings, suspends subscriptions, ends LMS sponsorships and voids countersignature requests whose Department Link no longer
	/// shares. Each step starts from a query for due or open rows (sets, subscriptions, sponsorships and requests), never a department sweep, and
	/// asks the LMS gate only for the departments a due update or sponsorship names.
	/// </summary>
	public class ContentSetsLogic
	{
		public async Task<ContentSetRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<IContentSetService>(), scope.Resolve<ILmsSponsorshipService>(), scope.Resolve<IMutualAidCountersignService>(),
				scope.Resolve<IRecordDeploymentPartnerService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with the services passed in (tests).</summary>
		public static async Task<ContentSetRunResult> Process(IContentSetService sets, ILmsSponsorshipService sponsorships, IMutualAidCountersignService countersign,
			IRecordDeploymentPartnerService deploymentPartners, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			ContentSetRunResult result;
			try
			{
				result = await sets.RunDueAsync(utcNow, cancellationToken) ?? new ContentSetRunResult();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Content set updates failed.");
				result = new ContentSetRunResult { Failed = 1 };
			}

			result.SponsorshipsEnded = await Step(() => sponsorships.EndLapsedAsync(null, utcNow, cancellationToken), "sponsorship link check", cancellationToken);
			result.CountersignaturesLapsed = await Step(() => countersign.LapseForLinkAsync(null, cancellationToken), "countersignature link check", cancellationToken);
			if (deploymentPartners != null)
				result.CountersignaturesLapsed += await Step(() => deploymentPartners.LapseForLinkAsync(null, cancellationToken), "deployment partner link check", cancellationToken);
			return result;
		}

		private static async Task<int> Step(Func<Task<int>> work, string name, CancellationToken cancellationToken)
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
				Logging.LogException(ex, $"Content sets {name} failed.");
				return 0;
			}
		}
	}
}
