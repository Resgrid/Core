using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>External ordering-system connectors (RMS-1C completion, registry M0181).</summary>
	public interface IRmsExternalOrderConnectorsRepository : IRepository<RmsExternalOrderConnector>
	{
		Task<RmsExternalOrderConnector> GetByIdForDepartmentAsync(int departmentId, string connectorId);

		/// <summary>The connector regardless of department, for the inbound endpoint that has only the id.</summary>
		Task<RmsExternalOrderConnector> GetByIdAsync(string connectorId);

		Task<IEnumerable<RmsExternalOrderConnector>> GetForDepartmentAsync(int departmentId);

		/// <summary>Enabled connectors across departments whose poll interval has elapsed, bounded.</summary>
		Task<IEnumerable<RmsExternalOrderConnector>> GetDueAsync(DateTime utcNow, int take);

		Task<bool> TryBumpRowVersionAsync(int departmentId, string connectorId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsExternalOrderConnectorRunsRepository : IRepository<RmsExternalOrderConnectorRun>
	{
		Task<IEnumerable<RmsExternalOrderConnectorRun>> GetForConnectorAsync(int departmentId, string connectorId, int take);

		/// <summary>Trims the run log to the newest <paramref name="keep"/> rows for one connector.</summary>
		Task<int> TrimAsync(int departmentId, string connectorId, int keep, CancellationToken cancellationToken = default);
	}
}
