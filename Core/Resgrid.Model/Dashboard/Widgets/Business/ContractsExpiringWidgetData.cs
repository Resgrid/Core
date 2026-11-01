using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>
	/// contracts.expiring (plan §7.9): active service contracts, those ending within the window, and the department compliance
	/// documents (insurance, licences…) expired or expiring in the same window.
	/// </summary>
	public sealed class ContractsExpiringWidgetData
	{
		/// <summary>The days setting the window used.</summary>
		public int Days { get; set; }

		/// <summary>Contracts live today (Active, started, not ended).</summary>
		public int Active { get; set; }

		public int ExpiringCount { get; set; }

		/// <summary>Soonest end first; at most the maxRows setting.</summary>
		public List<ContractsExpiringRow> Rows { get; set; } = new List<ContractsExpiringRow>();

		public int ComplianceDocumentsExpired { get; set; }

		public int ComplianceDocumentsExpiring { get; set; }
	}

	public sealed class ContractsExpiringRow
	{
		public string ServiceContractId { get; set; }

		public string Name { get; set; }

		public string ContractNumber { get; set; }

		public DateTime EndOn { get; set; }

		public int DaysLeft { get; set; }
	}
}
