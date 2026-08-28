using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>One row fetched by the ADP bulk repository: the stringified primary key plus the raw column values.</summary>
	public sealed class AdpBulkFieldRow
	{
		/// <summary>Primary key rendered invariantly as a string — also the AAD row key.</summary>
		public string RowKey { get; set; }

		/// <summary>Raw values keyed by column name (string, byte[], decimal, bool, or null).</summary>
		public Dictionary<string, object> Values { get; set; } = new Dictionary<string, object>();
	}

	/// <summary>One row's column updates to apply in a transactional batch.</summary>
	public sealed class AdpBulkRowUpdate
	{
		public string RowKey { get; set; }

		/// <summary>New values keyed by column name; a null value writes SQL NULL.</summary>
		public Dictionary<string, object> SetValues { get; set; } = new Dictionary<string, object>();
	}
}
