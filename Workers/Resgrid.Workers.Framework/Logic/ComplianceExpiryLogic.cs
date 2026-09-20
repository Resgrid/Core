using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 33 (Workforce &amp; Business Operations plan C7), daily. Active contracts whose EndOn is behind now move to
	/// Expired (ContractStatusChanged); contracts ending inside the lead window publish ContractExpiring once per day;
	/// compliance documents inside their own alert lead (or lapsed) notify the department administrators once per day.
	/// Departments without the Invoicing.ContractorBilling entitlement are skipped.
	/// </summary>
	public sealed class ComplianceExpiryLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var contracts = scope.Resolve<IServiceContractService>();
				var access = scope.Resolve<IBusinessOperationsAccessService>();
				var touched = await contracts.RunExpirySweepAsync(DateTime.UtcNow, access.CanUseContractorBillingAsync, ct);
				return Tuple.Create(true, $"Compliance expiry: contracts touched={touched}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Compliance expiry worker failed.");
				return Tuple.Create(false, "Compliance expiry failed.");
			}
		}
	}
}
