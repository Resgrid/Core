using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The Incident Back Office worker passes (registry §4C worker IDs). Each pass walks the open activations of departments with the
	/// module switched on, never calls billing, and is idempotent: a threshold trigger fires once when its condition starts and once
	/// (resolved) when it ends, through an edge latch, so a pass that runs every minute does not repeat itself.
	/// </summary>
	public interface IIncidentAutomationService
	{
		/// <summary>Worker 74: an incremental operational snapshot per open activation; trigger 120 when the projection lags.</summary>
		Task<int> ProjectSnapshotsAsync(CancellationToken cancellationToken = default);

		/// <summary>Worker 75: a full rebuild snapshot per open activation (drift between incremental and rebuilt numbers is flagged).</summary>
		Task<int> ReconcileSnapshotsAsync(CancellationToken cancellationToken = default);

		/// <summary>Worker 52: meal demand versions per upcoming meal plan from the population, arrivals, departures and policy coefficients.</summary>
		Task<int> ForecastDemandAsync(CancellationToken cancellationToken = default);

		/// <summary>Worker 53: vendor compliance expiry (trigger 134) and expired documents.</summary>
		Task<int> ValidateVendorsAsync(CancellationToken cancellationToken = default);

		/// <summary>Worker 54: capacity holds and lodging blocks past their expiry (trigger 202 ahead of it).</summary>
		Task<int> ExpireHoldsAsync(CancellationToken cancellationToken = default);

		/// <summary>Worker 55: the evaluated LI-B/LI-C/LI-D triggers (arrivals, capacity, lodging, meals, water, budgets, time, timeouts, coordination items, backlogs, deadlines).</summary>
		Task<int> EvaluateAlertsAsync(CancellationToken cancellationToken = default);

		/// <summary>Worker 57: the day's cost transactions, accrual snapshot and projection; cost-share apportionment; trigger 145.</summary>
		Task<int> RunDailyCostAsync(CancellationToken cancellationToken = default);

		/// <summary>Worker 58: snapshot and metric retention after close (setting 97).</summary>
		Task<int> ApplyRetentionAsync(CancellationToken cancellationToken = default);
	}
}
