using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 31 (Workforce &amp; Business Operations plan C7), daily. Submitted bids past ValidUntil move to Expired
	/// (audit + BidExpired through the domain outbox). Departments without the Invoicing.ContractorBilling
	/// entitlement are skipped; cheap when nothing qualifies (one indexed query).
	/// </summary>
	public sealed class BidExpirationLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var bids = scope.Resolve<IBidsService>();
				var access = scope.Resolve<IBusinessOperationsAccessService>();
				var expired = await bids.RunExpirySweepAsync(DateTime.UtcNow, access.CanUseContractorBillingAsync, ct);
				return Tuple.Create(true, $"Bid expiration: expired={expired}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Bid expiration worker failed.");
				return Tuple.Create(false, "Bid expiration failed.");
			}
		}
	}
}
