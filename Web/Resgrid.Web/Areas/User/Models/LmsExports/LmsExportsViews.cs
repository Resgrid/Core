using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.LmsExports
{
	/// <summary>What every data export and CE reporting page shows (Trainings → Programs, T5 round 6D).</summary>
	public abstract class LmsExportsPageView
	{
		public Department Department { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public bool IsOfficer { get; set; }

		/// <summary>The LMS add-on is active (settings may change, lapse rule 3).</summary>
		public bool CanAuthor { get; set; }

		public bool IsLapsed { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }

		/// <summary>A stored UTC time in the department's zone.</summary>
		public Func<DateTime?, string> Time { get; set; } = d => d?.ToString("g");
	}

	/// <summary>Trainings → Programs → Data export.</summary>
	public class LmsDataExportsView : LmsExportsPageView
	{
		public List<LmsDataExport> Exports { get; set; } = new List<LmsDataExport>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public int LinkDays { get; set; }
		public bool CanRequest { get; set; }
		public DateTime UtcNow { get; set; }

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		/// <summary>"12.4 MB" style size.</summary>
		public static string Size(long bytes)
		{
			string[] units = { "B", "KB", "MB", "GB" };
			double value = bytes;
			var unit = 0;
			while (value >= 1024 && unit < units.Length - 1)
			{
				value /= 1024;
				unit++;
			}

			return value.ToString(unit == 0 ? "0" : "0.0", System.Globalization.CultureInfo.CurrentCulture) + " " + units[unit];
		}
	}

	/// <summary>Trainings → Programs → CE reporting: the exporters, the settings, and a preview of one file.</summary>
	public class LmsCeReportingView : LmsExportsPageView
	{
		/// <summary>
		/// ADP: the department's protection is enforced, so licence, NREMT/NEMSID and member numbers leave a plain download REDACTED. The page then
		/// downloads through the reveal module (step-up, the grant in the request header) so an officer gets them as recorded.
		/// </summary>
		public bool ProtectionEnforced { get; set; }

		public List<LmsCeExporter> Exporters { get; set; } = new List<LmsCeExporter>();
		public LmsCeReportingSettings Settings { get; set; } = new LmsCeReportingSettings();
		public bool CanConfigure { get; set; }
		public List<DepartmentCertificationType> CertificationTypes { get; set; } = new List<DepartmentCertificationType>();
		public List<TrainingCategory> Categories { get; set; } = new List<TrainingCategory>();
		public LmsCeExportRequest Request { get; set; } = new LmsCeExportRequest();
		public LmsCeExportFile Preview { get; set; }
		public int PreviewRows { get; set; }

		public string CertificationTypeName(int id) => id <= 0 ? null : CertificationTypes.FirstOrDefault(t => t.DepartmentCertificationTypeId == id)?.Type;
	}

	/// <summary>Trainings → Programs → CE reporting → Columns of one exporter.</summary>
	public class LmsCeColumnsView : LmsExportsPageView
	{
		/// <summary>Blank rows offered under the columns for new ones.</summary>
		public const int BlankRows = 3;

		public LmsCeExporter Exporter { get; set; }
		public List<LmsCeColumn> Columns { get; set; } = new List<LmsCeColumn>();
		public bool IsCustom { get; set; }
		public bool CanConfigure { get; set; }

		public static IEnumerable<LmsCeColumnSources> Sources => Enum.GetValues(typeof(LmsCeColumnSources)).Cast<LmsCeColumnSources>();
	}
}
