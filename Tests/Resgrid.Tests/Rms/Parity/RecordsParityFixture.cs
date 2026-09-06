using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Tests.Rms.Parity
{
	/// <summary>
	/// One golden Logs-parity fixture (RMS plan section 6, RMS-0 deliverable): the legacy row exactly as the
	/// Logs module would hold it — every column, association and attachment — and the list, detail, print and
	/// export projections Records must produce for it. Product signs the content; the harness replays it.
	/// Dates are ISO-8601 UTC strings and are compared as strings so a fixture never drifts with the clock.
	/// </summary>
	public sealed class RecordsParityFixture
	{
		[JsonIgnore]
		public string FileName { get; set; }

		public string RecordType { get; set; }

		public string DefinitionKey { get; set; }

		public ParityLegacy Legacy { get; set; }

		public ParityExpected Expected { get; set; }

		public override string ToString() => RecordType;
	}

	public sealed class ParityLegacy
	{
		/// <summary>"Logs" for the Log family, "UnitLogs" for the Units-area writer.</summary>
		public string Source { get; set; }

		/// <summary>Every column of <see cref="Resgrid.Model.Log"/>, value or null.</summary>
		public JObject Log { get; set; }

		/// <summary>Every column of <see cref="Resgrid.Model.UnitLog"/> when Source is UnitLogs.</summary>
		public JObject UnitLog { get; set; }

		public List<JObject> Users { get; set; } = new List<JObject>();

		public List<JObject> Units { get; set; } = new List<JObject>();

		public List<ParityAttachment> Attachments { get; set; } = new List<ParityAttachment>();
	}

	public sealed class ParityAttachment
	{
		public int LogAttachmentId { get; set; }
		public int LogId { get; set; }
		public string FileName { get; set; }
		public string Type { get; set; }
		public int Size { get; set; }
		public string DataBase64 { get; set; }
		public string UserId { get; set; }
		public string Timestamp { get; set; }
		public string Description { get; set; }
	}

	public sealed class ParityExpected
	{
		public string NumberPrefix { get; set; }
		public ParityListExpectation List { get; set; } = new ParityListExpectation();
		public ParityDetailExpectation Detail { get; set; } = new ParityDetailExpectation();
		public ParityExportExpectation Export { get; set; } = new ParityExportExpectation();
		public ParityPrintExpectation Print { get; set; } = new ParityPrintExpectation();
	}

	public sealed class ParityListExpectation
	{
		public string DisplaySummary { get; set; }
		public List<string> SearchTextContains { get; set; } = new List<string>();
		/// <summary>Fields that must never reach the safe list projection (narrative, restricted, contact detail).</summary>
		public List<string> SearchTextExcludes { get; set; } = new List<string>();
		public string OccurredOn { get; set; }
		public string CallNumber { get; set; }
		public List<string> ParticipantUserIds { get; set; } = new List<string>();
		public List<int> UnitIds { get; set; } = new List<int>();
	}

	public sealed class ParityDetailExpectation
	{
		/// <summary>Expected RmsOperationalRecordDetail values keyed by property name.</summary>
		public JObject Details { get; set; } = new JObject();
		public List<JObject> Participants { get; set; } = new List<JObject>();
		public List<JObject> Units { get; set; } = new List<JObject>();
		public List<JObject> Attachments { get; set; } = new List<JObject>();
		public string ExternalId { get; set; }
		public int? StationGroupId { get; set; }
		public int? CallId { get; set; }
		public string StartedOn { get; set; }
		public string EndedOn { get; set; }
	}

	public sealed class ParityExportExpectation
	{
		public List<string> MustContain { get; set; } = new List<string>();
		/// <summary>Detail fields withheld from a viewer without RecordRestricted_View.</summary>
		public List<string> RestrictedFields { get; set; } = new List<string>();
	}

	public sealed class ParityPrintExpectation
	{
		public List<string> Contains { get; set; } = new List<string>();
		/// <summary>Text that must be absent from the print produced for a viewer without RecordRestricted_View.</summary>
		public List<string> WithheldWithoutRestrictedAccess { get; set; } = new List<string>();
	}
}
