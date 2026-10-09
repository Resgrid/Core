using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>One sequence the department pattern produces for a year, as the Records Settings screen lists it.</summary>
	public class RecordNumberSequenceStatus
	{
		/// <summary><see cref="RecordNumberScope.Key"/>; what a raised next number is saved against.</summary>
		public string ScopeKey { get; set; }

		/// <summary>The record types drawing on this sequence: every type at once when the pattern leaves out {PREFIX}, or several that share a prefix.</summary>
		public List<string> DefinitionKeys { get; set; } = new List<string>();

		/// <summary>Set when the pattern includes {GROUP}; null is the sequence for records with no group.</summary>
		public int? GroupId { get; set; }

		public int HighestIssued { get; set; }

		/// <summary>The sequence the next record in this scope receives; a raised next number may not go below it.</summary>
		public int NextSequence { get; set; }

		/// <summary>The full number the next record receives, e.g. "INC-2026-0153".</summary>
		public string NextNumber { get; set; }
	}

	/// <summary>A request to raise one sequence's next number.</summary>
	public class RecordNextNumberRequest
	{
		public string ScopeKey { get; set; }

		public int NextSequence { get; set; }
	}

	public class RecordsNumberingUpdate
	{
		public string Pattern { get; set; }

		public int SequenceWidth { get; set; }

		/// <summary>The numbering year the requested next numbers were shown for (a fiscal year's name when the year starts later than January 1).</summary>
		public int Year { get; set; }

		public List<RecordNextNumberRequest> NextNumbers { get; set; } = new List<RecordNextNumberRequest>();

		/// <summary>Record type prefixes to save; null leaves every type's prefix as it is. A blank prefix returns the type to its default.</summary>
		public List<RecordNumberPrefixRequest> Prefixes { get; set; }

		/// <summary>Month the numbering year starts (1-12); null leaves the saved one as it is.</summary>
		public int? YearStartMonth { get; set; }

		/// <summary>Day of the month the numbering year starts; null leaves the saved one as it is.</summary>
		public int? YearStartDay { get; set; }

		/// <summary><see cref="NumberingYearLabel"/> value; null leaves the saved one as it is.</summary>
		public int? YearLabel { get; set; }
	}

	/// <summary>A request to set one system record type's {PREFIX}.</summary>
	public class RecordNumberPrefixRequest
	{
		public string DefinitionKey { get; set; }

		public string Prefix { get; set; }
	}

	public class RecordsNumberingSaveResult
	{
		/// <summary>The pattern did not validate; nothing in the numbering setting was saved.</summary>
		public bool PatternRejected { get; set; }

		/// <summary>The year start is not a month and day every year has (February 29 included); nothing in the numbering setting was saved.</summary>
		public bool YearStartRejected { get; set; }

		/// <summary>Requested next numbers below what the sequence would already issue; those sequences were left as they were.</summary>
		public List<RecordNumberSequenceStatus> BelowCurrent { get; set; } = new List<RecordNumberSequenceStatus>();

		/// <summary>Requested next numbers for a sequence the saved pattern no longer produces (the pattern, a prefix or the year start changed in the same save).</summary>
		public int NotApplied { get; set; }

		/// <summary>Definition keys whose requested prefix did not validate; those types kept the prefix they had.</summary>
		public List<string> PrefixesRejected { get; set; } = new List<string>();
	}
}
