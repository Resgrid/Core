using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>Finalize/resubmit refused because local validation found errors; the issues are stored on the report for the author.</summary>
	public class IncidentReportValidationException : InvalidOperationException
	{
		public IncidentReportValidationException(string reportId, IReadOnlyList<RmsValidationIssue> issues)
			: base($"Incident report {reportId} has {issues?.Count ?? 0} validation error(s) and cannot be signed: " +
				   string.Join("; ", (issues ?? Array.Empty<RmsValidationIssue>()).Take(5).Select(i => $"{i.FieldPath ?? i.RuleKey}: {i.Message}")))
		{
			ReportId = reportId;
			Issues = issues ?? Array.Empty<RmsValidationIssue>();
		}

		public string ReportId { get; }
		public IReadOnlyList<RmsValidationIssue> Issues { get; }
	}
}
