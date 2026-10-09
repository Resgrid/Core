using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ProtoBuf;
using Resgrid.Model.Invoicing;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Model
{
	/// <summary>
	/// Department setting 117 (DocumentNumberingConfig): how the department's work order, invoice, bid and daily time report
	/// numbers are written, and the day the numbering year starts for them (a fiscal year when it is not January 1). A kind
	/// with no saved pattern keeps the numbers Resgrid has always issued for it. Records request and prevention numbers are
	/// configured with the rest of Records numbering (<see cref="RecordsNumberingConfig.DocumentPatterns"/>) and follow its
	/// year start. The custom sequences are counted in DocumentNumberSequences.
	/// </summary>
	[ProtoContract]
	public class DocumentNumberingConfig
	{
		public DocumentNumberingConfig()
		{
			Patterns = new List<DocumentNumberPattern>();
		}

		/// <summary>Month (1-12) the numbering year starts; 0, unset, is January.</summary>
		[ProtoMember(1)]
		public int YearStartMonth { get; set; }

		/// <summary>Day of <see cref="YearStartMonth"/> the numbering year starts; 0, unset, is the 1st.</summary>
		[ProtoMember(2)]
		public int YearStartDay { get; set; }

		/// <summary><see cref="NumberingYearLabel"/> value: which year names a numbering year that does not start on January 1. 0 is the year it ends in.</summary>
		[ProtoMember(3)]
		public int YearLabel { get; set; }

		/// <summary>The department's own pattern per document kind; a kind with no entry keeps its built-in numbers.</summary>
		[ProtoMember(4)]
		public List<DocumentNumberPattern> Patterns { get; set; }

		public NumberingYearStart YearStart()
		{
			return new NumberingYearStart(YearStartMonth, YearStartDay, (NumberingYearLabel)YearLabel);
		}
	}

	/// <summary>One document kind's number pattern, in the call number grammar (see <see cref="CallNumberFormat"/>).</summary>
	[ProtoContract]
	public class DocumentNumberPattern
	{
		/// <summary>A <see cref="DocumentNumberKinds"/> key, e.g. "work-order".</summary>
		[ProtoMember(1)]
		public string Kind { get; set; }

		/// <summary>Fixed text around {YYYY}/{YY}, {MM}, {DD} and {SEQ}, e.g. "MNT-{YY}-{SEQ}".</summary>
		[ProtoMember(2)]
		public string Pattern { get; set; }

		/// <summary>Zero-padded width of the sequence, 1 to 8.</summary>
		[ProtoMember(3)]
		public int SequenceWidth { get; set; }
	}

	/// <summary>A numbered document kind: its key, the numbers Resgrid always issued for it and the room its column has.</summary>
	public sealed class DocumentNumberKind
	{
		public DocumentNumberKind(string key, string legacyPattern, int legacyWidth, int maxLength, bool records)
		{
			Key = key;
			LegacyPattern = legacyPattern;
			LegacyWidth = legacyWidth;
			MaxLength = maxLength;
			IsRecords = records;
		}

		public string Key { get; }

		/// <summary>The built-in numbers in the pattern grammar, e.g. "WO-{YYYY}-{SEQ}" with <see cref="LegacyWidth"/> 6.</summary>
		public string LegacyPattern { get; }

		public int LegacyWidth { get; }

		/// <summary>The longest number the kind's column holds; a pattern that could write longer is refused.</summary>
		public int MaxLength { get; }

		/// <summary>Records kinds are saved with setting 72 and number by its year start; the others by setting 117's.</summary>
		public bool IsRecords { get; }
	}

	/// <summary>Every document kind whose number a department can format.</summary>
	public static class DocumentNumberKinds
	{
		public const string WorkOrder = "work-order";
		public const string Invoice = "invoice";
		public const string Bid = "bid";
		public const string TimeReport = "time-report";
		public const string RecordsRequest = "records-request";
		public const string Occupancy = "occupancy";
		public const string Inspection = "inspection";
		public const string Permit = "permit";
		public const string Investigation = "investigation";
		public const string Evidence = "evidence";

		public static readonly IReadOnlyList<DocumentNumberKind> All = new[]
		{
			new DocumentNumberKind(WorkOrder, "WO-{YYYY}-{SEQ}", 6, 50, false),
			new DocumentNumberKind(Invoice, "{SEQ}", 1, 50, false),
			new DocumentNumberKind(Bid, "{SEQ}", 1, 50, false),
			new DocumentNumberKind(TimeReport, "{SEQ}", 1, 50, false),
			new DocumentNumberKind(RecordsRequest, "PRR-{YYYY}-{SEQ}", 4, 50, true),
			new DocumentNumberKind(Occupancy, "OCC-{YYYY}-{SEQ}", 4, 32, true),
			new DocumentNumberKind(Inspection, "INSP-{YYYY}-{SEQ}", 4, 32, true),
			new DocumentNumberKind(Permit, "PRM-{YYYY}-{SEQ}", 4, 32, true),
			new DocumentNumberKind(Investigation, "INV-{YYYY}-{SEQ}", 4, 32, true),
			new DocumentNumberKind(Evidence, "EV-{YYYY}-{SEQ}", 4, 32, true)
		};

		public static DocumentNumberKind Find(string key)
		{
			return All.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.Ordinal));
		}

		/// <summary>The document kind of a <see cref="RmsPreventionNumberKinds"/> sequence name (OCC, INSP, PRM, INV, EV).</summary>
		public static string ForPreventionKind(string preventionKind)
		{
			switch (preventionKind)
			{
				case RmsPreventionNumberKinds.Occupancy: return Occupancy;
				case RmsPreventionNumberKinds.Inspection: return Inspection;
				case RmsPreventionNumberKinds.Permit: return Permit;
				case RmsPreventionNumberKinds.Investigation: return Investigation;
				case RmsPreventionNumberKinds.Evidence: return Evidence;
				default: return null;
			}
		}
	}

	/// <summary>
	/// Document number patterns. They use the call number grammar (<see cref="CallNumberFormat"/>): fixed text (letters, digits,
	/// '-', '_', '.', '/') around {YYYY} or {YY}, {MM}, {DD} and {SEQ}. A yearly pattern restarts on the numbering year start;
	/// one with {MM} or {DD} on the calendar date. A pattern saved for a kind that matches its built-in numbers keeps them.
	/// </summary>
	public static class DocumentNumbering
	{
		private static readonly Regex AllDigits = new Regex("^[0-9]+$", RegexOptions.CultureInvariant);

		/// <summary>The longest text <paramref name="pattern"/> can write: date tokens at their width, {SEQ} at the largest sequence.</summary>
		public static int MaxRenderedLength(string pattern)
		{
			var normalized = CallNumberFormat.Normalize(pattern);
			if (normalized == null)
				return int.MaxValue;
			return normalized.Replace(CallNumberFormat.YearToken, "0000").Replace(CallNumberFormat.ShortYearToken, "00")
				.Replace(CallNumberFormat.MonthToken, "00").Replace(CallNumberFormat.DayToken, "00")
				.Replace(CallNumberFormat.SequenceToken, CallNumberFormat.MaxSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)).Length;
		}

		/// <summary>True when <paramref name="pattern"/> is in the grammar and every number it writes fits the kind's column.</summary>
		public static bool IsValid(DocumentNumberKind kind, string pattern)
		{
			return kind != null && CallNumberFormat.IsValid(pattern?.Trim()) && MaxRenderedLength(pattern) <= kind.MaxLength;
		}

		/// <summary>
		/// The department's own pattern for <paramref name="kind"/>, or null when it keeps the built-in numbers: no entry, an entry
		/// that no longer validates, or one that is the built-in pattern and width.
		/// </summary>
		public static DocumentNumberPattern EffectivePattern(IEnumerable<DocumentNumberPattern> patterns, string kind)
		{
			var info = DocumentNumberKinds.Find(kind);
			var saved = patterns?.FirstOrDefault(p => p != null && string.Equals(p.Kind, kind, StringComparison.Ordinal));
			if (info == null || saved == null || !IsValid(info, saved.Pattern))
				return null;

			var pattern = CallNumberFormat.Normalize(saved.Pattern);
			var width = CallNumberFormat.EffectiveWidth(saved.SequenceWidth);
			if (string.Equals(pattern, info.LegacyPattern, StringComparison.Ordinal) && width == info.LegacyWidth)
				return null;

			return new DocumentNumberPattern { Kind = kind, Pattern = pattern, SequenceWidth = width };
		}

		/// <summary>Replaces a kind's entry; a null or blank pattern removes it, so the kind goes back to its built-in numbers.</summary>
		public static void SetPattern(List<DocumentNumberPattern> patterns, string kind, string pattern, int width)
		{
			patterns.RemoveAll(p => p == null || string.Equals(p.Kind, kind, StringComparison.Ordinal));
			if (!string.IsNullOrWhiteSpace(pattern))
				patterns.Add(new DocumentNumberPattern { Kind = kind, Pattern = CallNumberFormat.Normalize(pattern), SequenceWidth = CallNumberFormat.EffectiveWidth(width) });
		}

		/// <summary>
		/// How a number reads in a sentence or heading: a number of digits alone is written "#1042", as invoice, bid and time
		/// report numbers always have been; any other number is shown as issued, e.g. "INV-2026-0001".
		/// </summary>
		public static string Display(string number)
		{
			if (string.IsNullOrEmpty(number))
				return number;
			return AllDigits.IsMatch(number) ? "#" + number : number;
		}

		/// <summary>The built-in text of a work order number, for rows issued before numbers were stored as text.</summary>
		public static string LegacyWorkOrderNumber(int year, int sequence)
		{
			return "WO-" + year.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + sequence.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
		}

		/// <summary>A number made safe for a file name: anything other than letters, digits, '-', '_' and '.' becomes '-'.</summary>
		public static string FileSafe(string number)
		{
			if (string.IsNullOrEmpty(number))
				return number;
			var chars = number.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '-').ToArray();
			return new string(chars).Trim('-');
		}
	}

	/// <summary>
	/// The number text of each numbered document (<c>NumberText</c>: as issued, for files, exports and search) and how it reads
	/// on screen (<c>NumberLabel</c>: "#1042" for a plain number). Rows written before M0268 by an older server have no
	/// DisplayNumber and read as their built-in number.
	/// </summary>
	public static class DocumentNumberExtensions
	{
		public static string NumberText(this Invoice invoice) =>
			invoice == null ? null : invoice.DisplayNumber ?? invoice.InvoiceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

		public static string NumberText(this Bid bid) =>
			bid == null ? null : bid.DisplayNumber ?? bid.BidNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

		public static string NumberText(this DeploymentTimeReport report) =>
			report == null ? null : report.DisplayNumber ?? report.ReportNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

		public static string NumberText(this WorkOrder order) =>
			order == null ? null : order.DisplayNumber ?? DocumentNumbering.LegacyWorkOrderNumber(order.NumberYear, order.NumberSequence);

		public static string NumberLabel(this Invoice invoice) => DocumentNumbering.Display(invoice.NumberText());

		public static string NumberLabel(this Bid bid) => DocumentNumbering.Display(bid.NumberText());

		public static string NumberLabel(this DeploymentTimeReport report) => DocumentNumbering.Display(report.NumberText());
	}

	/// <summary>One document kind as a numbering screen shows it: its pattern and, for a custom pattern, what it issues next.</summary>
	public class DocumentNumberStatus
	{
		public string Kind { get; set; }

		/// <summary>True when the department's own pattern numbers the kind; false while it keeps the built-in numbers.</summary>
		public bool Custom { get; set; }

		public string Pattern { get; set; }

		public int SequenceWidth { get; set; }

		/// <summary><see cref="CallNumberScope.Key"/> of the current sequence; null for the built-in numbers.</summary>
		public string ScopeKey { get; set; }

		public CallNumberResetPeriod Period { get; set; }

		/// <summary>The sequence the next document receives; 0 for the built-in numbers.</summary>
		public int NextSequence { get; set; }

		/// <summary>The full number the next document receives under a custom pattern; null for the built-in numbers.</summary>
		public string NextNumber { get; set; }

		/// <summary>A sample number in the pattern for today (sequence 153), for the built-in numbers too.</summary>
		public string Example { get; set; }

		public int FloorSequence { get; set; }
	}

	/// <summary>A request to save one kind's pattern (and optionally raise its current sequence).</summary>
	public class DocumentNumberPatternUpdate
	{
		public string Kind { get; set; }

		/// <summary>Blank returns the kind to its built-in numbers.</summary>
		public string Pattern { get; set; }

		public int SequenceWidth { get; set; }

		/// <summary>The sequence shown on the screen when the raise was typed; a raise against any other scope is not applied.</summary>
		public string ScopeKey { get; set; }

		public int? NextSequence { get; set; }
	}

	public class DocumentNumberingUpdate
	{
		/// <summary>Null leaves the saved year start as it is (Records kinds take theirs from setting 72 and ignore these).</summary>
		public int? YearStartMonth { get; set; }

		public int? YearStartDay { get; set; }

		public int? YearLabel { get; set; }

		public List<DocumentNumberPatternUpdate> Patterns { get; set; } = new List<DocumentNumberPatternUpdate>();
	}

	public class DocumentNumberingSaveResult
	{
		/// <summary>The year start is not a day every year has; nothing was saved.</summary>
		public bool YearStartRejected { get; set; }

		/// <summary>Kinds whose pattern did not validate or does not fit the kind's column; those kinds kept what they had.</summary>
		public List<string> PatternsRejected { get; set; } = new List<string>();

		/// <summary>Raised next numbers below what the sequence already issues next; left unchanged.</summary>
		public List<DocumentNumberStatus> BelowCurrent { get; set; } = new List<DocumentNumberStatus>();

		/// <summary>Raised next numbers typed against a sequence the saved settings no longer produce (pattern or year start changed).</summary>
		public int NotApplied { get; set; }
	}
}
