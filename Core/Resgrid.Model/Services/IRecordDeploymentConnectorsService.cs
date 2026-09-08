using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// External ordering-system connectors for mutual-aid deployments (RMS plan section 4.1). A connector may
	/// exist only with a documented feed, an encrypted credential, a rate limit, an acknowledgement of the
	/// source's terms and explicit read authority; write authority is refused in this release. Import creates
	/// deployments for orders the department has not seen and records every later change as a new versioned
	/// snapshot. Nothing local is ever transitioned by a connector: a disagreement is reconciliation for a person.
	/// Management is department administration; every method that reads or changes a connector checks it.
	/// </summary>
	public interface IRecordDeploymentConnectorsService
	{
		Task<List<RmsExternalOrderConnector>> ListAsync(int departmentId, string userId);

		Task<RmsExternalOrderConnector> GetAsync(int departmentId, string userId, string connectorId);

		/// <summary>Creates the connector and returns the one-time inbound token; it is not recoverable afterwards.</summary>
		Task<RecordDeploymentConnectorCreated> CreateAsync(int departmentId, string userId, RecordDeploymentConnectorInput input, CancellationToken cancellationToken = default);

		Task<RmsExternalOrderConnector> UpdateAsync(int departmentId, string userId, string connectorId, long expectedRowVersion, RecordDeploymentConnectorInput input, CancellationToken cancellationToken = default);

		/// <summary>Enabling needs acknowledged terms, read authority and a stored credential where the kind needs one.</summary>
		Task<RmsExternalOrderConnector> SetEnabledAsync(int departmentId, string userId, string connectorId, bool enabled, CancellationToken cancellationToken = default);

		Task<RmsExternalOrderConnector> AcknowledgeTermsAsync(int departmentId, string userId, string connectorId, CancellationToken cancellationToken = default);

		/// <summary>Issues a new inbound token, invalidating the old one; returns the new token once.</summary>
		Task<string> RotateInboundTokenAsync(int departmentId, string userId, string connectorId, CancellationToken cancellationToken = default);

		Task DeleteAsync(int departmentId, string userId, string connectorId, CancellationToken cancellationToken = default);

		/// <summary>Reads the feed now, on behalf of the administrator who asked, and imports it.</summary>
		Task<RecordDeploymentConnectorRunResult> RunAsync(int departmentId, string userId, string connectorId, CancellationToken cancellationToken = default);

		/// <summary>Imports a feed document pushed to the inbound endpoint after the token has been verified.</summary>
		Task<RecordDeploymentConnectorRunResult> ImportInboundAsync(string connectorId, string inboundToken, string feedJson, CancellationToken cancellationToken = default);

		/// <summary>Every enabled connector whose interval has elapsed; the worker calls this on its own cadence.</summary>
		Task<int> RunDueAsync(CancellationToken cancellationToken = default);

		Task<List<RmsExternalOrderConnectorRun>> GetRunsAsync(int departmentId, string userId, string connectorId, int take);

		/// <summary>Where the source's latest snapshot and the department's own record disagree. Never applied automatically.</summary>
		Task<List<RecordDeploymentReconciliationItem>> GetReconciliationAsync(int departmentId, string userId, string connectorId = null);
	}

	/// <summary>Reads one provider's feed. Every provider speaks the same contract; the provider fixes scheme, profile and mandatory identifiers.</summary>
	public interface IExternalOrderFeedProvider
	{
		/// <summary><see cref="RmsExternalOrderConnectorProviders"/>.</summary>
		string Key { get; }

		string DefaultScheme { get; }

		string DefaultProfileKey { get; }

		/// <summary>Fetches one page of the feed. The credential is already decrypted; the cursor is what the last page returned.</summary>
		Task<string> FetchAsync(RmsExternalOrderConnector connector, string credential, string cursor, CancellationToken cancellationToken = default);

		/// <summary>Provider-specific identifier rules on a parsed feed; problems are reported per order and that order is rejected.</summary>
		List<string> ValidateOrder(ExternalOrderFeedOrder order);
	}
}
