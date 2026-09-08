using System;

namespace Resgrid.Model
{
	/// <summary>
	/// A definition's <see cref="RmsRecordCardinality"/> already has its Record for this Call (plan section 5.2.1).
	/// The existing Record is carried on the exception rather than discarded, because the answer for the author is
	/// "here is the one that exists, open it" — not an error page.
	/// </summary>
	public sealed class RecordCardinalityException : InvalidOperationException
	{
		public RecordCardinalityException(string existingRecordId, string definitionKey, RmsRecordCardinality cardinality, string message)
			: base(message)
		{
			ExistingRecordId = existingRecordId;
			DefinitionKey = definitionKey;
			Cardinality = cardinality;
		}

		/// <summary>The Record that already holds the key; the caller opens this instead of creating another.</summary>
		public string ExistingRecordId { get; }

		public string DefinitionKey { get; }

		public RmsRecordCardinality Cardinality { get; }
	}
}
