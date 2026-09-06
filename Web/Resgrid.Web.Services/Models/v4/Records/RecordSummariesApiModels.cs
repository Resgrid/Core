using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.Records
{
	/// <summary>One RecordOperationalSummaryV1 (RMS plan section 5.1); the model class is the versioned contract.</summary>
	public class RecordOperationalSummaryResult : StandardApiResponseV4Base
	{
		public RecordOperationalSummaryV1 Data { get; set; }
	}

	/// <summary>Paged summary feed for downstream consumers and customer analytics egress (RMS plan section 4.7).</summary>
	public class RecordOperationalSummariesResult : StandardApiResponseV4Base
	{
		public List<RecordOperationalSummaryV1> Data { get; set; } = new List<RecordOperationalSummaryV1>();

		public bool HasMore { get; set; }

		/// <summary>Opaque cursor for the next page; absent at the end of the feed.</summary>
		public string NextCursor { get; set; }

		/// <summary>Contract version every row in <see cref="Data"/> conforms to.</summary>
		public int ContractVersion { get; set; } = RecordOperationalSummaryV1.CurrentContractVersion;
	}
}
