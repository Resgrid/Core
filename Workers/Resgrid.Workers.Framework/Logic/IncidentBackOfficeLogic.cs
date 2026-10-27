using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// The Incident Back Office worker passes (large-incident-logistics-finance-plan.md §13.6; registry §4C workers 74, 75, 52-55, 57,
	/// 58). Each resolves <see cref="IIncidentAutomationService"/> in its own lifetime scope and reports how many activations or
	/// departments it touched; the service skips departments with the module switched off and never calls billing.
	/// </summary>
	public sealed class IncidentBackOfficeLogic
	{
		/// <summary>Worker 74: incremental operational snapshot per open activation; trigger 120 when the projection lags; once an hour, snapshots older than a day are thinned to one per hour.</summary>
		public Task<Tuple<bool, string>> IncidentSnapshotProjection(CancellationToken ct) => RunAsync("Incident Snapshot Projection", s => s.ProjectSnapshotsAsync(ct), ct);

		/// <summary>Worker 75: full rebuild snapshot per open activation; drift is flagged.</summary>
		public Task<Tuple<bool, string>> IncidentSnapshotReconciliation(CancellationToken ct) => RunAsync("Incident Snapshot Reconciliation", s => s.ReconcileSnapshotsAsync(ct), ct);

		/// <summary>Worker 52: meal demand versions for the next three days of meal plans.</summary>
		public Task<Tuple<bool, string>> IncidentDemandForecast(CancellationToken ct) => RunAsync("Incident Demand Forecast", s => s.ForecastDemandAsync(ct), ct);

		/// <summary>Worker 53: vendor compliance expiry (trigger 134) and expired documents.</summary>
		public Task<Tuple<bool, string>> IncidentVendorValidation(CancellationToken ct) => RunAsync("Incident Vendor Validation", s => s.ValidateVendorsAsync(ct), ct);

		/// <summary>Worker 54: capacity holds and lodging blocks past expiry (trigger 202 ahead of it).</summary>
		public Task<Tuple<bool, string>> IncidentHoldExpiry(CancellationToken ct) => RunAsync("Incident Hold Expiry", s => s.ExpireHoldsAsync(ct), ct);

		/// <summary>Worker 55: the evaluated LI-B/LI-C/LI-D triggers through the edge latch.</summary>
		public Task<Tuple<bool, string>> IncidentAlertEvaluation(CancellationToken ct) => RunAsync("Incident Alert Evaluation", s => s.EvaluateAlertsAsync(ct), ct);

		/// <summary>Worker 57: the day's cost transactions, accrual snapshot, projection and cost-share apportionment.</summary>
		public Task<Tuple<bool, string>> IncidentDailyCost(CancellationToken ct) => RunAsync("Incident Daily Cost", s => s.RunDailyCostAsync(ct), ct);

		/// <summary>Worker 58: snapshot and metric retention after close (setting 97).</summary>
		public Task<Tuple<bool, string>> IncidentRetention(CancellationToken ct) => RunAsync("Incident Retention", s => s.ApplyRetentionAsync(ct), ct);

		private static async Task<Tuple<bool, string>> RunAsync(string name, Func<IIncidentAutomationService, Task<int>> pass, CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var touched = await pass(scope.Resolve<IIncidentAutomationService>());
				return Tuple.Create(true, $"{name}: {touched} processed");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, $"{name} worker failed.");
				return Tuple.Create(false, $"{name} failed.");
			}
		}
	}
}
