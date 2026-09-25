using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public sealed record RetentionImpactRequest(string ExpectedRevision, int? ProposedDefaultYears);
	/// <summary>Internal metadata projection. Record identifiers never leave the impact service.</summary>
	public sealed record RetentionImpactHeader
	{
		public string RecordId { get; set; }
		public int Kind { get; set; }
		public string DefinitionKey { get; set; }
		public int State { get; set; }
		public DateTime? FinalizedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string AmendsRevisionId { get; set; }
		public long RowVersion { get; set; }
		public int HoldOrPermanentContent { get; set; }
		public int HistoricalHoldUncertainty { get; set; }
	}
	public interface IRetentionImpactStore
	{
		Task<IReadOnlyList<RetentionImpactHeader>> ReadRetentionHeadersAsync(int departmentId, int bound, CancellationToken ct);
	}
	public interface IRetentionImpactService
	{
		Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, RetentionImpactRequest request, CancellationToken ct = default);
	}
}
