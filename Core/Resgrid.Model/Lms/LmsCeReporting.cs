using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model.Lms
{
	// Training T5 CE reporting (round 6D, plan §13a rows "CE and state reporting"): a CAPCE completion file for departments that are
	// CAPCE-accredited providers themselves, state CE files (one exporter per state, a CSV fallback for every state) and a state-ready
	// roster CSV (the Acadis investigation's answer). Files only: nothing is ever submitted to CAPCE or a state system. Resgrid is not an
	// accreditor (§14): the provider and course numbers are what the department entered on its courses and records.
	//
	// Layout verification (2026-11-25, public sources only):
	// - CAPCE: the 2026 Organizational Accreditation Manual §1.13.3 lists the data every completion upload needs (activity number,
	//   completion date, name, e-mail, state of licensure, state license number, license level and expiry, NEMSID, NREMT number and
	//   re-registration date, CEH and CEH category) and says CSV upload is one way to submit. The CSV column order, header names and date
	//   format are in the AMS Operations Manual, which is not public. The default columns follow the manual's list in its order and every
	//   column is configurable per department.
	// - Florida EMS: CE Broker's "Easy Excel" provider upload (columns A–H) is publicly documented and is the default layout; CE Broker
	//   asks for an .xls workbook, so the department opens the CSV in a spreadsheet and saves it as Excel before uploading.
	// - Virginia OEMS (CE Scanner application XML, no third-party file), New Jersey OEMS (per-student entry in NJLincs) and Florida fire
	//   (FCDICE/FireTRAQ) publish no file layout: they get the configurable CSV fallback. Acadis publishes no import template; the roster
	//   CSV follows the Illinois OSFM PS&E course completion roster fields.
	//
	// Round 9C (region and sector neutrality): a training hours / CE / CPD file for any reporting body, employer or regulator, anywhere -
	// Canadian provincial regulators and colleges, EU and UK CPD schemes, a business's own compliance records. Its default columns carry no US
	// identifiers, its dates are ISO 8601 (yyyy-MM-dd), its hours use a full stop as the decimal separator, and its headings are written in the
	// language of the person building the file. Every column stays configurable, like the other exporters. The US files keep their mandated
	// English headings and US date formats.

	/// <summary>Which value a CE file column carries (LmsCeColumn.Source). Persisted in setting 148: append only.</summary>
	public enum LmsCeColumnSources
	{
		/// <summary>The column's Argument as typed (e.g. "State=FL"), or empty.</summary>
		Constant = 0,
		FirstName = 1,
		LastName = 2,
		FullName = 3,
		Email = 4,

		/// <summary>The Resgrid user id.</summary>
		MemberId = 5,

		/// <summary>The department's own member number (profile identification number).</summary>
		IdentificationNumber = 6,

		CompletedOn = 7,
		StartedOn = 8,
		EndedOn = 9,
		Title = 10,
		CourseCode = 11,
		CeProvider = 12,
		CeCourseNumber = 13,

		/// <summary>The CE course number without a leading "NN-" prefix (CE Broker wants "123" for "20-123").</summary>
		CeCourseNumberSuffix = 14,

		/// <summary>CE hours, or the record's hours when no CE hours were entered.</summary>
		CeHours = 15,

		Hours = 16,
		CategoryName = 17,

		/// <summary>The category's external code (an NCCP or ISO code when the category came from a template).</summary>
		CategoryCode = 18,

		Method = 19,
		Location = 20,
		Instructors = 21,

		/// <summary>The number on the member's state license (the certification type chosen in the settings).</summary>
		LicenseNumber = 22,

		/// <summary>The state license's area (or issuer when no area was entered).</summary>
		LicenseState = 23,

		/// <summary>The state license's type (or name): the license level.</summary>
		LicenseLevel = 24,

		LicenseExpiresOn = 25,
		NremtNumber = 26,

		/// <summary>The NREMT certification's expiry: the next re-registration date.</summary>
		NremtExpiresOn = 27,

		/// <summary>The National EMS ID (the certification type chosen in the settings).</summary>
		Nemsid = 28,

		CapceProviderNumber = 29,

		/// <summary>The CE Broker provider tracking number without its "50-" prefix.</summary>
		CeBrokerProviderNumber = 30,

		RecordId = 31,
		VerificationCode = 32,
		SourceKind = 33
	}

	/// <summary>What an exporter produces.</summary>
	public enum LmsCeExporterKinds
	{
		/// <summary>CAPCE completion records (the department is the accredited provider).</summary>
		Capce = 0,

		/// <summary>A state EMS or fire CE file.</summary>
		StateCe = 1,

		/// <summary>A state-ready roster / completion file (Acadis and other state systems that import rosters).</summary>
		Roster = 2,

		/// <summary>A training hours / CE / CPD file for any reporting body or employer, in any country (round 9C).</summary>
		TrainingHours = 3
	}

	/// <summary>One column of a CE file: the header, where its value comes from and the argument (a date or number format, or constant text).</summary>
	[ProtoContract]
	public class LmsCeColumn
	{
		public const int MaxHeaderLength = 80;
		public const int MaxArgumentLength = 80;

		[ProtoMember(1)]
		public string Header { get; set; }

		/// <summary><see cref="LmsCeColumnSources"/>.</summary>
		[ProtoMember(2)]
		public int Source { get; set; }

		[ProtoMember(3)]
		public string Argument { get; set; }

		/// <summary>
		/// LmsExports resource key of a default column's heading, for exporters whose headings follow the reader's language (never stored:
		/// a department's own column list keeps the heading it typed).
		/// </summary>
		[JsonIgnore]
		public string HeaderKey { get; set; }

		public LmsCeColumn()
		{
		}

		public LmsCeColumn(string header, LmsCeColumnSources source, string argument = null)
		{
			Header = header;
			Source = (int)source;
			Argument = argument;
		}

		/// <summary>A default column whose heading is the source's name in the reader's language ("Ce.Source.{source}"); English until localized.</summary>
		public static LmsCeColumn Localized(string englishHeader, LmsCeColumnSources source, string argument = null) =>
			new LmsCeColumn(englishHeader, source, argument) { HeaderKey = "Ce.Source." + source };
	}

	/// <summary>A department's own column list for one exporter (replaces the exporter's default columns).</summary>
	[ProtoContract]
	public class LmsCeColumnMap
	{
		[ProtoMember(1)]
		public string ExporterKey { get; set; }

		[ProtoMember(2)]
		public List<LmsCeColumn> Columns { get; set; } = new List<LmsCeColumn>();
	}

	/// <summary>
	/// Trainings → Programs → CE reporting (DepartmentSettingTypes.LmsCeReportingSettings, 148): the CAPCE provider number, which
	/// certification types hold the member identifiers CE files need, the CE Broker provider number, and per-exporter column maps.
	/// Missing = no CAPCE provider (the CAPCE file is refused), no identifier types (those columns stay empty), default columns.
	/// </summary>
	[ProtoContract]
	public class LmsCeReportingSettings
	{
		public const int MaxColumns = 40;

		/// <summary>The department's CAPCE provider number (it is a CAPCE-accredited provider). Empty: no CAPCE file.</summary>
		[ProtoMember(1)]
		public string CapceProviderNumber { get; set; }

		/// <summary>The provider name as course versions and records carry it (CE provider); records naming it count as the department's own.</summary>
		[ProtoMember(2)]
		public string CapceProviderName { get; set; }

		/// <summary>The certification type that holds the member's state license (number, area = state, type = level, expiry).</summary>
		[ProtoMember(3)]
		public int LicenseCertificationTypeId { get; set; }

		/// <summary>The certification type that holds the NREMT registration (number, expiry = next re-registration).</summary>
		[ProtoMember(4)]
		public int NremtCertificationTypeId { get; set; }

		/// <summary>The certification type whose number is the member's National EMS ID (NEMSID).</summary>
		[ProtoMember(5)]
		public int NemsidCertificationTypeId { get; set; }

		/// <summary>The CE Broker provider tracking number (Florida), e.g. "50-2486".</summary>
		[ProtoMember(6)]
		public string CeBrokerProviderNumber { get; set; }

		/// <summary>The department's column lists, per exporter; an exporter without one uses its default columns.</summary>
		[ProtoMember(7)]
		public List<LmsCeColumnMap> ColumnMaps { get; set; } = new List<LmsCeColumnMap>();

		public LmsCeReportingSettings Normalized()
		{
			static string Clean(string value, int max)
			{
				var text = (value ?? string.Empty).Trim();
				return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
			}

			var maps = new List<LmsCeColumnMap>();
			foreach (var map in (ColumnMaps ?? new List<LmsCeColumnMap>()).Where(m => m != null && LmsCeExporterCatalog.Find(m.ExporterKey) != null))
			{
				if (maps.Any(m => string.Equals(m.ExporterKey, map.ExporterKey, StringComparison.OrdinalIgnoreCase)))
					continue;
				var columns = LmsCeExportRules.NormalizeColumns(map.Columns);
				if (columns.Count > 0)
					maps.Add(new LmsCeColumnMap { ExporterKey = LmsCeExporterCatalog.Find(map.ExporterKey).Key, Columns = columns });
			}

			return new LmsCeReportingSettings
			{
				CapceProviderNumber = Clean(CapceProviderNumber, 40),
				CapceProviderName = Clean(CapceProviderName, 200),
				LicenseCertificationTypeId = Math.Max(0, LicenseCertificationTypeId),
				NremtCertificationTypeId = Math.Max(0, NremtCertificationTypeId),
				NemsidCertificationTypeId = Math.Max(0, NemsidCertificationTypeId),
				CeBrokerProviderNumber = Clean(CeBrokerProviderNumber, 40),
				ColumnMaps = maps
			};
		}

		/// <summary>The columns an exporter writes for this department: its own map, or the exporter's defaults.</summary>
		public List<LmsCeColumn> ColumnsFor(LmsCeExporter exporter)
		{
			if (exporter == null)
				return new List<LmsCeColumn>();
			var map = (ColumnMaps ?? new List<LmsCeColumnMap>()).FirstOrDefault(m => m != null && string.Equals(m.ExporterKey, exporter.Key, StringComparison.OrdinalIgnoreCase));
			return map != null && map.Columns != null && map.Columns.Count > 0
				? map.Columns.Select(c => new LmsCeColumn { Header = c.Header, Source = c.Source, Argument = c.Argument }).ToList()
				: exporter.DefaultColumns.Select(c => new LmsCeColumn { Header = c.Header, Source = c.Source, Argument = c.Argument, HeaderKey = exporter.LocalizedHeaders ? c.HeaderKey : null })
					.ToList();
		}

		public bool HasCustomColumns(string exporterKey) =>
			(ColumnMaps ?? new List<LmsCeColumnMap>()).Any(m => m != null && string.Equals(m.ExporterKey, exporterKey, StringComparison.OrdinalIgnoreCase) && m.Columns?.Count > 0);
	}

	/// <summary>One CE file exporter: a state (or CAPCE, or the roster), its default layout and whether that layout was verified.</summary>
	public sealed class LmsCeExporter
	{
		public string Key { get; set; }
		public LmsCeExporterKinds Kind { get; set; }

		/// <summary>Two-letter state code for a state exporter; null for CAPCE, the roster and the generic fallback.</summary>
		public string StateCode { get; set; }

		/// <summary>LmsExports resource keys.</summary>
		public string NameKey { get; set; }
		public string NoteKey { get; set; }

		/// <summary>The default columns follow a publicly documented layout (false: the configurable CSV fallback).</summary>
		public bool LayoutVerified { get; set; }

		/// <summary>The file starts with a header row (CE Broker's upload has none: a non-number in column A is an error).</summary>
		public bool HeaderRow { get; set; } = true;

		/// <summary>Only records with a CE course number go into the file.</summary>
		public bool RequiresCeNumber { get; set; }

		/// <summary>Only the department's own CE records (provider blank on its own courses and classes, or the CAPCE provider number/name).</summary>
		public bool OwnProviderOnly { get; set; }

		/// <summary>The default headings are written in the language of the person building the file (round 9C; the US files keep English).</summary>
		public bool LocalizedHeaders { get; set; }

		public List<LmsCeColumn> DefaultColumns { get; set; } = new List<LmsCeColumn>();

		public string FilePrefix { get; set; }
	}

	/// <summary>The exporters, in the order the page shows them. Data only: a state is added by adding an entry here.</summary>
	public static class LmsCeExporterCatalog
	{
		public const string Capce = "capce";
		public const string FloridaEms = "fl-ems";
		public const string VirginiaEms = "va-ems";
		public const string NewJerseyEms = "nj-ems";
		public const string FloridaFire = "fl-fire";
		public const string GenericState = "state-ce";
		public const string Roster = "state-roster";
		public const string TrainingHours = "training-hours";

		private static List<LmsCeColumn> FallbackColumns() => new List<LmsCeColumn>
		{
			new LmsCeColumn("Last Name", LmsCeColumnSources.LastName),
			new LmsCeColumn("First Name", LmsCeColumnSources.FirstName),
			new LmsCeColumn("State License Number", LmsCeColumnSources.LicenseNumber),
			new LmsCeColumn("Course Number", LmsCeColumnSources.CeCourseNumber),
			new LmsCeColumn("Course Title", LmsCeColumnSources.Title),
			new LmsCeColumn("Completion Date", LmsCeColumnSources.CompletedOn, "yyyy-MM-dd"),
			new LmsCeColumn("CE Hours", LmsCeColumnSources.CeHours, "0.##"),
			new LmsCeColumn("Category", LmsCeColumnSources.CategoryCode),
			new LmsCeColumn("CE Provider", LmsCeColumnSources.CeProvider),
			new LmsCeColumn("Instructor", LmsCeColumnSources.Instructors)
		};

		/// <summary>The training hours / CE / CPD file's default columns: no US identifiers, ISO dates, invariant decimal hours.</summary>
		private static List<LmsCeColumn> TrainingHoursColumns() => new List<LmsCeColumn>
		{
			LmsCeColumn.Localized("Last name", LmsCeColumnSources.LastName),
			LmsCeColumn.Localized("First name", LmsCeColumnSources.FirstName),
			LmsCeColumn.Localized("Member number", LmsCeColumnSources.IdentificationNumber),
			LmsCeColumn.Localized("Email", LmsCeColumnSources.Email),
			LmsCeColumn.Localized("Course title", LmsCeColumnSources.Title),
			LmsCeColumn.Localized("Course code", LmsCeColumnSources.CourseCode),
			LmsCeColumn.Localized("Category name", LmsCeColumnSources.CategoryName),
			LmsCeColumn.Localized("Category code", LmsCeColumnSources.CategoryCode),
			LmsCeColumn.Localized("Completion date", LmsCeColumnSources.CompletedOn, "yyyy-MM-dd"),
			LmsCeColumn.Localized("Hours", LmsCeColumnSources.Hours, "0.##"),
			LmsCeColumn.Localized("CE / CPD hours", LmsCeColumnSources.CeHours, "0.##"),
			LmsCeColumn.Localized("Method of training", LmsCeColumnSources.Method),
			LmsCeColumn.Localized("Training provider", LmsCeColumnSources.CeProvider),
			LmsCeColumn.Localized("Instructors", LmsCeColumnSources.Instructors),
			LmsCeColumn.Localized("Certificate verification code", LmsCeColumnSources.VerificationCode)
		};

		private static readonly List<LmsCeExporter> Exporters = new List<LmsCeExporter>
		{
			new LmsCeExporter
			{
				Key = TrainingHours, Kind = LmsCeExporterKinds.TrainingHours, NameKey = "Ce.Exporter.training-hours", NoteKey = "Ce.Note.training-hours",
				LayoutVerified = false, LocalizedHeaders = true, RequiresCeNumber = false, OwnProviderOnly = false, FilePrefix = "training-hours",
				DefaultColumns = TrainingHoursColumns()
			},
			new LmsCeExporter
			{
				Key = Capce, Kind = LmsCeExporterKinds.Capce, NameKey = "Ce.Exporter.capce", NoteKey = "Ce.Note.capce", LayoutVerified = false,
				RequiresCeNumber = true, OwnProviderOnly = true, FilePrefix = "capce-completions",
				DefaultColumns = new List<LmsCeColumn>
				{
					new LmsCeColumn("CAPCE Activity Number", LmsCeColumnSources.CeCourseNumber),
					new LmsCeColumn("Date of Completion", LmsCeColumnSources.CompletedOn, "MM/dd/yyyy"),
					new LmsCeColumn("First Name", LmsCeColumnSources.FirstName),
					new LmsCeColumn("Last Name", LmsCeColumnSources.LastName),
					new LmsCeColumn("Email", LmsCeColumnSources.Email),
					new LmsCeColumn("State of Licensure", LmsCeColumnSources.LicenseState),
					new LmsCeColumn("State License Number", LmsCeColumnSources.LicenseNumber),
					new LmsCeColumn("License Level", LmsCeColumnSources.LicenseLevel),
					new LmsCeColumn("License Expiration Date", LmsCeColumnSources.LicenseExpiresOn, "MM/dd/yyyy"),
					new LmsCeColumn("NEMSID", LmsCeColumnSources.Nemsid),
					new LmsCeColumn("NREMT Number", LmsCeColumnSources.NremtNumber),
					new LmsCeColumn("NREMT Re-registration Date", LmsCeColumnSources.NremtExpiresOn, "MM/dd/yyyy"),
					new LmsCeColumn("CEH", LmsCeColumnSources.CeHours, "0.##"),
					new LmsCeColumn("CEH Category", LmsCeColumnSources.CategoryCode)
				}
			},
			new LmsCeExporter
			{
				Key = FloridaEms, Kind = LmsCeExporterKinds.StateCe, StateCode = "FL", NameKey = "Ce.Exporter.fl-ems", NoteKey = "Ce.Note.fl-ems", LayoutVerified = true,
				HeaderRow = false, RequiresCeNumber = true, OwnProviderOnly = true, FilePrefix = "fl-cebroker",
				DefaultColumns = new List<LmsCeColumn>
				{
					new LmsCeColumn("A: CE Provider Tracking Number", LmsCeColumnSources.CeBrokerProviderNumber),
					new LmsCeColumn("B: CE Broker Course Number", LmsCeColumnSources.CeCourseNumberSuffix),
					new LmsCeColumn("C: Your Course ID", LmsCeColumnSources.Constant),
					new LmsCeColumn("D: Publishing Number", LmsCeColumnSources.Constant),
					new LmsCeColumn("E: License Number", LmsCeColumnSources.LicenseNumber),
					new LmsCeColumn("F: Date Completed", LmsCeColumnSources.CompletedOn, "M/d/yyyy"),
					new LmsCeColumn("G: State", LmsCeColumnSources.Constant, "State=FL"),
					new LmsCeColumn("H: Profession", LmsCeColumnSources.Constant)
				}
			},
			new LmsCeExporter
			{
				Key = VirginiaEms, Kind = LmsCeExporterKinds.StateCe, StateCode = "VA", NameKey = "Ce.Exporter.va-ems", NoteKey = "Ce.Note.va-ems", LayoutVerified = false,
				RequiresCeNumber = true, FilePrefix = "va-ems-ce", DefaultColumns = FallbackColumns()
			},
			new LmsCeExporter
			{
				Key = NewJerseyEms, Kind = LmsCeExporterKinds.StateCe, StateCode = "NJ", NameKey = "Ce.Exporter.nj-ems", NoteKey = "Ce.Note.nj-ems", LayoutVerified = false,
				RequiresCeNumber = true, FilePrefix = "nj-ems-ce", DefaultColumns = FallbackColumns()
			},
			new LmsCeExporter
			{
				Key = FloridaFire, Kind = LmsCeExporterKinds.StateCe, StateCode = "FL", NameKey = "Ce.Exporter.fl-fire", NoteKey = "Ce.Note.fl-fire", LayoutVerified = false,
				RequiresCeNumber = false, FilePrefix = "fl-fire-training", DefaultColumns = FallbackColumns()
			},
			new LmsCeExporter
			{
				Key = GenericState, Kind = LmsCeExporterKinds.StateCe, NameKey = "Ce.Exporter.state-ce", NoteKey = "Ce.Note.state-ce", LayoutVerified = false,
				RequiresCeNumber = true, FilePrefix = "state-ce", DefaultColumns = FallbackColumns()
			},
			new LmsCeExporter
			{
				Key = Roster, Kind = LmsCeExporterKinds.Roster, NameKey = "Ce.Exporter.state-roster", NoteKey = "Ce.Note.state-roster", LayoutVerified = false,
				RequiresCeNumber = false, FilePrefix = "state-roster",
				DefaultColumns = new List<LmsCeColumn>
				{
					new LmsCeColumn("Course Code", LmsCeColumnSources.CourseCode),
					new LmsCeColumn("Course Title", LmsCeColumnSources.Title),
					new LmsCeColumn("Training Facility", LmsCeColumnSources.Location),
					new LmsCeColumn("Program Hours", LmsCeColumnSources.Hours, "0.##"),
					new LmsCeColumn("Date Started", LmsCeColumnSources.StartedOn, "MM/dd/yyyy"),
					new LmsCeColumn("Date Ended", LmsCeColumnSources.EndedOn, "MM/dd/yyyy"),
					new LmsCeColumn("Last Name", LmsCeColumnSources.LastName),
					new LmsCeColumn("First Name", LmsCeColumnSources.FirstName),
					new LmsCeColumn("Member Number", LmsCeColumnSources.IdentificationNumber),
					new LmsCeColumn("State Certification Number", LmsCeColumnSources.LicenseNumber),
					new LmsCeColumn("Hours Attended", LmsCeColumnSources.Hours, "0.##"),
					new LmsCeColumn("Method", LmsCeColumnSources.Method),
					new LmsCeColumn("Instructors", LmsCeColumnSources.Instructors)
				}
			}
		};

		public static IReadOnlyList<LmsCeExporter> All => Exporters;

		public static LmsCeExporter Find(string key) =>
			string.IsNullOrWhiteSpace(key) ? null : Exporters.FirstOrDefault(e => string.Equals(e.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>A request for one CE file: the exporter, the department-local date range and who is in it.</summary>
	public sealed class LmsCeExportRequest
	{
		public string ExporterKey { get; set; }

		/// <summary>Department-local first and last day (inclusive).</summary>
		public DateTime FromLocal { get; set; }
		public DateTime ToLocal { get; set; }

		/// <summary>Removed, disabled and hidden members' completions too (the explicit switch, §12.2/§14).</summary>
		public bool IncludeFormerMembers { get; set; }

		/// <summary>Only this training category; null for all.</summary>
		public int? CategoryId { get; set; }

		/// <summary>The language of the person building the file: headings of exporters with localized headings are written in it (English when null).</summary>
		[JsonIgnore]
		public CultureInfo Culture { get; set; }
	}

	/// <summary>A built CE file.</summary>
	public sealed class LmsCeExportFile
	{
		/// <summary>LmsExports resource key when the file cannot be built (no CAPCE provider number, unknown exporter).</summary>
		public string Problem { get; set; }

		public LmsCeExporter Exporter { get; set; }
		public List<string> Headers { get; set; } = new List<string>();
		public List<List<string>> Rows { get; set; } = new List<List<string>>();

		/// <summary>Per column header: how many rows have it empty while the column's source should fill it (a license number, a CE number).</summary>
		public Dictionary<string, int> MissingValues { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

		public string FileName { get; set; }
		public string ContentType { get; set; } = "text/csv";
		public byte[] Bytes { get; set; }
	}

	/// <summary>One member's identifiers a CE file reads from their certifications.</summary>
	public sealed class LmsCeMemberIdentity
	{
		public string LicenseNumber { get; set; }
		public string LicenseState { get; set; }
		public string LicenseLevel { get; set; }
		public DateTime? LicenseExpiresOn { get; set; }
		public string NremtNumber { get; set; }
		public DateTime? NremtExpiresOn { get; set; }
		public string Nemsid { get; set; }
	}

	/// <summary>The CE file rules, pure so they can be tested: which records go in, and what each column says.</summary>
	public static class LmsCeExportRules
	{
		/// <summary>Sources a reporting body needs filled; an empty cell of one of these is counted as missing.</summary>
		private static readonly HashSet<int> IdentifyingSources = new HashSet<int>
		{
			(int)LmsCeColumnSources.LicenseNumber, (int)LmsCeColumnSources.CeCourseNumber, (int)LmsCeColumnSources.CeCourseNumberSuffix,
			(int)LmsCeColumnSources.CeBrokerProviderNumber, (int)LmsCeColumnSources.CapceProviderNumber, (int)LmsCeColumnSources.Nemsid,
			(int)LmsCeColumnSources.NremtNumber
		};

		/// <summary>Department-made sources: a blank CE provider on one of these means the department itself is the provider.</summary>
		private static readonly HashSet<int> OwnSources = new HashSet<int>
		{
			(int)LearningRecordSourceKinds.CourseCompletion, (int)LearningRecordSourceKinds.ClassSession, (int)LearningRecordSourceKinds.TrainingRecord,
			(int)LearningRecordSourceKinds.LmsCourseCompletion, (int)LearningRecordSourceKinds.SkillEvaluation, (int)LearningRecordSourceKinds.CalendarAttendance
		};

		/// <summary>Trims, drops empty and unknown columns, caps the list and the text lengths.</summary>
		public static List<LmsCeColumn> NormalizeColumns(IEnumerable<LmsCeColumn> columns)
		{
			var result = new List<LmsCeColumn>();
			foreach (var column in columns ?? Enumerable.Empty<LmsCeColumn>())
			{
				if (column == null || !Enum.IsDefined(typeof(LmsCeColumnSources), column.Source))
					continue;
				var header = (column.Header ?? string.Empty).Trim();
				if (header.Length == 0)
					continue;
				var argument = (column.Argument ?? string.Empty).Trim();
				result.Add(new LmsCeColumn
				{
					Header = header.Length > LmsCeColumn.MaxHeaderLength ? header.Substring(0, LmsCeColumn.MaxHeaderLength) : header,
					Source = column.Source,
					Argument = argument.Length == 0 ? null : argument.Length > LmsCeColumn.MaxArgumentLength ? argument.Substring(0, LmsCeColumn.MaxArgumentLength) : argument
				});
				if (result.Count >= LmsCeReportingSettings.MaxColumns)
					break;
			}

			return result;
		}

		/// <summary>
		/// Writes each default column's heading in the reader's language (columns with a <see cref="LmsCeColumn.HeaderKey"/>); a heading the
		/// label lookup does not know stays as it is. A department's own columns have no key and keep what it typed.
		/// </summary>
		public static List<LmsCeColumn> LocalizeHeaders(List<LmsCeColumn> columns, Func<string, string> label)
		{
			if (columns == null || label == null)
				return columns ?? new List<LmsCeColumn>();
			foreach (var column in columns.Where(c => c != null && !string.IsNullOrWhiteSpace(c.HeaderKey)))
			{
				var text = label(column.HeaderKey);
				if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, column.HeaderKey, StringComparison.Ordinal))
					column.Header = text.Length > LmsCeColumn.MaxHeaderLength ? text.Substring(0, LmsCeColumn.MaxHeaderLength) : text;
			}

			return columns;
		}

		/// <summary>The text with a leading "NN-" prefix removed (CE Broker numbers are reported as the suffix only).</summary>
		public static string Suffix(string number)
		{
			var text = (number ?? string.Empty).Trim();
			var dash = text.IndexOf('-');
			return dash > 0 && dash <= 3 && text.Substring(0, dash).All(char.IsDigit) && dash < text.Length - 1 ? text.Substring(dash + 1).Trim() : text;
		}

		private static bool Same(string a, string b) =>
			!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

		/// <summary>Whether the record is the department's own CE (it is the provider): its provider is blank on a department-made source, or names the provider.</summary>
		public static bool IsOwnProvider(LearningRecord record, LmsCeReportingSettings settings)
		{
			if (record == null)
				return false;
			if (string.IsNullOrWhiteSpace(record.CeProvider))
				return OwnSources.Contains(record.SourceKind);
			return Same(record.CeProvider, settings?.CapceProviderNumber) || Same(record.CeProvider, settings?.CapceProviderName) ||
			       Same(record.CeProvider, settings?.CeBrokerProviderNumber) || Same(Suffix(record.CeProvider), Suffix(settings?.CeBrokerProviderNumber));
		}

		/// <summary>
		/// The records a file holds: verified (never pending, rejected or voided), completed in [fromUtc, toUtc), the exporter's CE number and
		/// provider rules, the category filter, and only active members unless former members were asked for. Oldest first, then by member.
		/// </summary>
		public static List<LearningRecord> Select(LmsCeExporter exporter, IEnumerable<LearningRecord> records, LmsCeReportingSettings settings, DateTime fromUtc, DateTime toUtc,
			IReadOnlyDictionary<string, LmsExportMember> members, bool includeFormerMembers, int? categoryId, int departmentId)
		{
			return (records ?? Enumerable.Empty<LearningRecord>())
				.Where(r => r != null && r.DepartmentId == departmentId && r.Status == (int)LearningRecordStatuses.Verified)
				.Where(r => r.CompletedOn >= fromUtc && r.CompletedOn < toUtc)
				.Where(r => !exporter.RequiresCeNumber || !string.IsNullOrWhiteSpace(r.CeCourseNumber))
				.Where(r => !exporter.OwnProviderOnly || IsOwnProvider(r, settings))
				.Where(r => !categoryId.HasValue || r.CategoryId == categoryId)
				.Where(r => includeFormerMembers || (r.UserId != null && members != null && members.TryGetValue(r.UserId, out var member) && member.IsActive))
				.OrderBy(r => r.CompletedOn).ThenBy(r => r.UserId, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.LearningRecordId)
				.ToList();
		}

		private static string FormatDate(DateTime? value, string format, Func<DateTime, DateTime> toLocal)
		{
			if (!value.HasValue)
				return string.Empty;
			var local = toLocal == null ? value.Value : toLocal(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
			try
			{
				return local.ToString(string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd" : format, CultureInfo.InvariantCulture);
			}
			catch (FormatException)
			{
				return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			}
		}

		/// <summary>A date the member holds (a certification expiry): a calendar date, never shifted by time zone.</summary>
		private static string FormatCalendarDate(DateTime? value, string format) => FormatDate(value, format, null);

		private static string FormatNumber(decimal value, string format)
		{
			try
			{
				return value.ToString(string.IsNullOrWhiteSpace(format) ? "0.##" : format, CultureInfo.InvariantCulture);
			}
			catch (FormatException)
			{
				return value.ToString("0.##", CultureInfo.InvariantCulture);
			}
		}

		/// <summary>One cell. <paramref name="toLocal"/> turns stored UTC times into the department's local time (completion dates are local days).</summary>
		public static string Value(LmsCeColumn column, LearningRecord record, LmsExportMember member, LmsCeMemberIdentity identity, LmsCeReportingSettings settings,
			IReadOnlyDictionary<int, TrainingCategory> categories, Func<DateTime, DateTime> toLocal)
		{
			TrainingCategory category = null;
			if (record?.CategoryId != null && categories != null)
				categories.TryGetValue(record.CategoryId.Value, out category);
			identity ??= new LmsCeMemberIdentity();

			switch ((LmsCeColumnSources)column.Source)
			{
				case LmsCeColumnSources.Constant: return column.Argument ?? string.Empty;
				case LmsCeColumnSources.FirstName: return member?.FirstName ?? string.Empty;
				case LmsCeColumnSources.LastName: return member?.LastName ?? string.Empty;
				case LmsCeColumnSources.FullName: return member?.Name ?? string.Empty;
				case LmsCeColumnSources.Email: return member?.Email ?? string.Empty;
				case LmsCeColumnSources.MemberId: return record?.UserId ?? string.Empty;
				case LmsCeColumnSources.IdentificationNumber: return member?.IdentificationNumber ?? string.Empty;
				case LmsCeColumnSources.CompletedOn: return FormatDate(record?.CompletedOn, column.Argument, toLocal);
				case LmsCeColumnSources.StartedOn: return FormatDate(record?.StartedOn ?? record?.CompletedOn, column.Argument, toLocal);
				case LmsCeColumnSources.EndedOn: return FormatDate(record?.EndedOn ?? record?.CompletedOn, column.Argument, toLocal);
				case LmsCeColumnSources.Title: return record?.Title ?? string.Empty;
				case LmsCeColumnSources.CourseCode: return record?.CourseCode ?? string.Empty;
				case LmsCeColumnSources.CeProvider: return record?.CeProvider ?? string.Empty;
				case LmsCeColumnSources.CeCourseNumber: return record?.CeCourseNumber?.Trim() ?? string.Empty;
				case LmsCeColumnSources.CeCourseNumberSuffix: return Suffix(record?.CeCourseNumber);
				case LmsCeColumnSources.CeHours: return record == null ? string.Empty : FormatNumber(record.CeHours ?? record.Hours, column.Argument);
				case LmsCeColumnSources.Hours: return record == null ? string.Empty : FormatNumber(record.Hours, column.Argument);
				case LmsCeColumnSources.CategoryName: return category?.Name ?? string.Empty;
				case LmsCeColumnSources.CategoryCode: return category == null ? string.Empty : string.IsNullOrWhiteSpace(category.ExternalCode) ? category.Name ?? string.Empty : category.ExternalCode;
				case LmsCeColumnSources.Method:
					return record?.MethodOfTraining != null && Enum.IsDefined(typeof(MethodsOfTraining), record.MethodOfTraining.Value) ? ((MethodsOfTraining)record.MethodOfTraining.Value).ToString() : string.Empty;
				case LmsCeColumnSources.Location: return record?.Location ?? string.Empty;
				case LmsCeColumnSources.Instructors: return record?.InstructorNames ?? string.Empty;
				case LmsCeColumnSources.LicenseNumber: return identity.LicenseNumber ?? string.Empty;
				case LmsCeColumnSources.LicenseState: return identity.LicenseState ?? string.Empty;
				case LmsCeColumnSources.LicenseLevel: return identity.LicenseLevel ?? string.Empty;
				case LmsCeColumnSources.LicenseExpiresOn: return FormatCalendarDate(identity.LicenseExpiresOn, column.Argument);
				case LmsCeColumnSources.NremtNumber: return identity.NremtNumber ?? string.Empty;
				case LmsCeColumnSources.NremtExpiresOn: return FormatCalendarDate(identity.NremtExpiresOn, column.Argument);
				case LmsCeColumnSources.Nemsid: return identity.Nemsid ?? string.Empty;
				case LmsCeColumnSources.CapceProviderNumber: return settings?.CapceProviderNumber ?? string.Empty;
				case LmsCeColumnSources.CeBrokerProviderNumber: return Suffix(settings?.CeBrokerProviderNumber);
				case LmsCeColumnSources.RecordId: return record == null ? string.Empty : record.LearningRecordId.ToString(CultureInfo.InvariantCulture);
				case LmsCeColumnSources.VerificationCode: return record?.VerificationCode ?? string.Empty;
				case LmsCeColumnSources.SourceKind:
					return record != null && Enum.IsDefined(typeof(LearningRecordSourceKinds), record.SourceKind) ? ((LearningRecordSourceKinds)record.SourceKind).ToString() : string.Empty;
				default: return string.Empty;
			}
		}

		/// <summary>The member's identifiers from their certifications: per configured type, the newest certification that is not deleted.</summary>
		public static LmsCeMemberIdentity Identity(IEnumerable<PersonnelCertification> certifications, LmsCeReportingSettings settings)
		{
			PersonnelCertification Pick(int typeId) => typeId <= 0
				? null
				: (certifications ?? Enumerable.Empty<PersonnelCertification>())
					.Where(c => c != null && !c.IsDeleted && c.DepartmentCertificationTypeId == typeId)
					.OrderByDescending(c => c.ExpiresOn ?? DateTime.MaxValue).ThenByDescending(c => c.RecievedOn ?? DateTime.MinValue).ThenByDescending(c => c.PersonnelCertificationId)
					.FirstOrDefault();

			var license = Pick(settings?.LicenseCertificationTypeId ?? 0);
			var nremt = Pick(settings?.NremtCertificationTypeId ?? 0);
			var nemsid = Pick(settings?.NemsidCertificationTypeId ?? 0);
			return new LmsCeMemberIdentity
			{
				LicenseNumber = license?.Number?.Trim(),
				LicenseState = string.IsNullOrWhiteSpace(license?.Area) ? license?.IssuedBy?.Trim() : license.Area.Trim(),
				LicenseLevel = string.IsNullOrWhiteSpace(license?.Type) ? license?.Name?.Trim() : license.Type.Trim(),
				LicenseExpiresOn = license?.ExpiresOn,
				NremtNumber = nremt?.Number?.Trim(),
				NremtExpiresOn = nremt?.ExpiresOn,
				Nemsid = nemsid?.Number?.Trim()
			};
		}

		/// <summary>Builds the file's rows (and its missing-value counts) for the selected records.</summary>
		public static LmsCeExportFile Build(LmsCeExporter exporter, IReadOnlyList<LmsCeColumn> columns, IEnumerable<LearningRecord> selected,
			IReadOnlyDictionary<string, LmsExportMember> members, IReadOnlyDictionary<string, LmsCeMemberIdentity> identities, LmsCeReportingSettings settings,
			IReadOnlyDictionary<int, TrainingCategory> categories, Func<DateTime, DateTime> toLocal)
		{
			var file = new LmsCeExportFile { Exporter = exporter, Headers = (columns ?? new List<LmsCeColumn>()).Select(c => c.Header).ToList() };
			foreach (var record in selected ?? Enumerable.Empty<LearningRecord>())
			{
				LmsExportMember member = null;
				LmsCeMemberIdentity identity = null;
				if (record.UserId != null)
				{
					members?.TryGetValue(record.UserId, out member);
					identities?.TryGetValue(record.UserId, out identity);
				}

				var row = new List<string>();
				foreach (var column in columns ?? new List<LmsCeColumn>())
				{
					var value = Value(column, record, member, identity, settings, categories, toLocal);
					row.Add(value);
					if (string.IsNullOrEmpty(value) && IdentifyingSources.Contains(column.Source))
						file.MissingValues[column.Header] = (file.MissingValues.TryGetValue(column.Header, out var count) ? count : 0) + 1;
				}

				file.Rows.Add(row);
			}

			return file;
		}

		/// <summary>The file's bytes: a header row when the exporter has one, then the rows (formula-guarded, UTF-8, no byte order mark).</summary>
		public static byte[] ToBytes(LmsCeExportFile file) =>
			new UTF8Encoding(false).GetBytes(LmsExportCsv.Write(file.Exporter?.HeaderRow == false ? null : file.Headers, file.Rows));
	}
}
