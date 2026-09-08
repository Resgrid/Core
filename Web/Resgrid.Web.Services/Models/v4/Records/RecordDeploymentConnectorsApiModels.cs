using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Records
{
	/// <summary>
	/// External ordering-system connector over v4 (RMS plan section 4.1). The stored credential and the inbound
	/// token never leave the server: the connector reports only whether each is set. The inbound token is
	/// returned exactly once, at creation or rotation.
	/// </summary>
	public class RecordDeploymentConnectorData
	{
		public string Id { get; set; }
		public string ProviderKey { get; set; }
		public string Name { get; set; }
		public string SourceSystem { get; set; }
		public string SourceScheme { get; set; }
		public string ProfileKey { get; set; }
		public string BaseUrl { get; set; }
		public string CredentialKind { get; set; }
		public string CredentialHeaderName { get; set; }
		public bool HasCredential { get; set; }
		public bool HasInboundToken { get; set; }
		public bool ReadEnabled { get; set; }
		/// <summary>Always false in this release; write authority to an external ordering system is refused.</summary>
		public bool WriteEnabled { get; set; }
		public int PollIntervalMinutes { get; set; }
		public int MaxRequestsPerHour { get; set; }
		public int RequestsThisHour { get; set; }
		public string TermsReference { get; set; }
		public DateTime? TermsAcknowledgedOn { get; set; }
		public string TermsAcknowledgedByUserId { get; set; }
		public bool IsEnabled { get; set; }
		public bool IsReadyToRun { get; set; }
		public DateTime? LastPolledOn { get; set; }
		public DateTime? LastSuccessOn { get; set; }
		public string LastError { get; set; }
		public int ConsecutiveFailures { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
	}

	public class RecordDeploymentConnectorInputData
	{
		public string ProviderKey { get; set; }
		public string Name { get; set; }
		public string SourceSystem { get; set; }
		public string SourceScheme { get; set; }
		public string ProfileKey { get; set; }
		public string BaseUrl { get; set; }
		/// <summary>none, bearer or header.</summary>
		public string CredentialKind { get; set; }
		public string CredentialHeaderName { get; set; }
		/// <summary>Plain credential; encrypted at rest and never returned. Blank on update keeps the stored one.</summary>
		public string Credential { get; set; }
		public bool ReadEnabled { get; set; } = true;
		/// <summary>Must be false; true is refused.</summary>
		public bool WriteEnabled { get; set; }
		public int PollIntervalMinutes { get; set; } = 60;
		public int MaxRequestsPerHour { get; set; } = 12;
		/// <summary>Where the source's terms of use are recorded; required before acknowledgement.</summary>
		public string TermsReference { get; set; }
		/// <summary>Update only: the RowVersion the caller last saw.</summary>
		public long RowVersion { get; set; }
	}

	public class RecordDeploymentConnectorRunData
	{
		public string Id { get; set; }
		public string ConnectorId { get; set; }
		public string Trigger { get; set; }
		public string TriggeredByUserId { get; set; }
		public DateTime StartedOn { get; set; }
		public DateTime? FinishedOn { get; set; }
		public string Outcome { get; set; }
		public string Error { get; set; }
		public int RequestCount { get; set; }
		public int OrdersSeen { get; set; }
		public int OrdersCreated { get; set; }
		public int SnapshotsRecorded { get; set; }
		public int RequestsAdded { get; set; }
		public int Unchanged { get; set; }
		public int Rejected { get; set; }
		public int Conflicts { get; set; }
		public string SourceVersion { get; set; }
		public List<string> Messages { get; set; } = new List<string>();
	}

	public class RecordDeploymentReconciliationData
	{
		public string ConnectorId { get; set; }
		public string OrderId { get; set; }
		public string RecordId { get; set; }
		public string OrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public string Kind { get; set; }
		public string SourceStatus { get; set; }
		public string LocalStatus { get; set; }
		public string SourceVersion { get; set; }
		public DateTime? SourceCapturedOn { get; set; }
	}

	public class RecordDeploymentConnectorCreatedData
	{
		public RecordDeploymentConnectorData Connector { get; set; }
		/// <summary>Shown once. The server keeps only a hash.</summary>
		public string InboundToken { get; set; }
	}

	public class RecordDeploymentConnectorsResult : StandardApiResponseV4Base
	{
		public List<RecordDeploymentConnectorData> Data { get; set; } = new List<RecordDeploymentConnectorData>();
	}

	public class RecordDeploymentConnectorResult : StandardApiResponseV4Base
	{
		public RecordDeploymentConnectorData Data { get; set; }
	}

	public class RecordDeploymentConnectorCreatedResult : StandardApiResponseV4Base
	{
		public RecordDeploymentConnectorCreatedData Data { get; set; }
	}

	public class RecordDeploymentConnectorTokenResult : StandardApiResponseV4Base
	{
		public RecordDeploymentConnectorCreatedData Data { get; set; }
	}

	public class RecordDeploymentConnectorRunApiResult : StandardApiResponseV4Base
	{
		public RecordDeploymentConnectorRunData Data { get; set; }
	}

	public class RecordDeploymentConnectorRunsResult : StandardApiResponseV4Base
	{
		public List<RecordDeploymentConnectorRunData> Data { get; set; } = new List<RecordDeploymentConnectorRunData>();
	}

	public class RecordDeploymentReconciliationResult : StandardApiResponseV4Base
	{
		public List<RecordDeploymentReconciliationData> Data { get; set; } = new List<RecordDeploymentReconciliationData>();
	}
}
