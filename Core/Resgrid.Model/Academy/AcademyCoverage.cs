using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Model.Academy
{
	/// <summary>What the Academy coverage guard found: modules and capabilities with no lesson and no exemption, and stale references.</summary>
	public sealed class AcademyCoverageReport
	{
		public string CatalogVersion { get; set; }
		public int Lessons { get; set; }
		public int Areas { get; set; }
		public int Capabilities { get; set; }
		public List<string> UncoveredAreas { get; set; } = new List<string>();
		public List<string> UncoveredCapabilities { get; set; } = new List<string>();

		/// <summary>Area or capability ids a lesson names that the catalog does not have ("lesson: id").</summary>
		public List<string> UnknownReferences { get; set; } = new List<string>();

		/// <summary>Exemptions for ids the catalog no longer has, or that a lesson now covers.</summary>
		public List<string> StaleExemptions { get; set; } = new List<string>();

		public bool HasFindings => UncoveredAreas.Count > 0 || UncoveredCapabilities.Count > 0 || UnknownReferences.Count > 0 || StaleExemptions.Count > 0;

		public string Summary =>
			$"Academy coverage: {Lessons} lessons; {Areas - UncoveredAreas.Count}/{Areas} modules and {Capabilities - UncoveredCapabilities.Count}/{Capabilities} capabilities " +
			$"covered or exempt; {UnknownReferences.Count} unknown reference(s), {StaleExemptions.Count} stale exemption(s).";

		/// <summary>The report the per-release docs/Academy pass works from (decision 13).</summary>
		public string ToMarkdown()
		{
			var text = new StringBuilder();
			text.Append("# Academy coverage (Admin Assist catalog ").Append(CatalogVersion).Append(")\n\n").Append(Summary).Append("\n");
			void Section(string title, IReadOnlyCollection<string> items)
			{
				text.Append("\n## ").Append(title).Append(" (").Append(items.Count).Append(")\n\n");
				if (items.Count == 0)
					text.Append("None.\n");
				foreach (var item in items)
					text.Append("- ").Append(item).Append('\n');
			}
			Section("Modules without a lesson or exemption", UncoveredAreas);
			Section("Capabilities without a lesson or exemption", UncoveredCapabilities);
			Section("Lesson references the catalog does not have", UnknownReferences);
			Section("Stale exemptions", StaleExemptions);
			return text.ToString();
		}
	}

	/// <summary>
	/// The A2 coverage guard (plan §15, decision 13): every Admin Assist module (area) and capability should map to at least one lesson
	/// manifest or to an explicit exemption (<c>coverage-exemptions.json</c>). It reports; the test that runs it warns and never fails CI.
	/// </summary>
	public static class AcademyCoverage
	{
		public static AcademyCoverageReport Evaluate(IAdminAssistCatalog catalog, AcademyManifestSet set)
		{
			var report = new AcademyCoverageReport { CatalogVersion = catalog?.Version };
			if (catalog == null || set == null)
				return report;

			var areaIds = catalog.Areas.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
			var capabilityIds = catalog.Capabilities.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
			var coveredAreas = new HashSet<string>(StringComparer.Ordinal);
			var coveredCapabilities = new HashSet<string>(StringComparer.Ordinal);
			report.Lessons = set.Lessons.Count;
			report.Areas = areaIds.Count;
			report.Capabilities = capabilityIds.Count;

			foreach (var lesson in set.Lessons)
			{
				foreach (var area in lesson.AdminAssist?.Areas ?? new List<string>())
				{
					if (areaIds.Contains(area))
						coveredAreas.Add(area);
					else
						report.UnknownReferences.Add(lesson.Id + ": module " + area);
				}
				foreach (var capability in lesson.AdminAssist?.Capabilities ?? new List<string>())
				{
					if (capabilityIds.Contains(capability))
						coveredCapabilities.Add(capability);
					else
						report.UnknownReferences.Add(lesson.Id + ": capability " + capability);
				}
			}

			var exemptAreas = (set.Exemptions?.Areas ?? new List<AcademyCoverageExemption>()).Select(e => e.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
			var exemptCapabilities = (set.Exemptions?.Capabilities ?? new List<AcademyCoverageExemption>()).Select(e => e.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);

			report.UncoveredAreas = catalog.Areas.Where(a => !coveredAreas.Contains(a.Id) && !exemptAreas.Contains(a.Id)).Select(a => a.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
			report.UncoveredCapabilities = catalog.Capabilities.Where(c => !coveredCapabilities.Contains(c.Id) && !exemptCapabilities.Contains(c.Id))
				.Select(c => c.AreaId + "/" + c.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
			report.StaleExemptions = exemptAreas.Where(id => !areaIds.Contains(id) || coveredAreas.Contains(id)).Select(id => "module " + id)
				.Concat(exemptCapabilities.Where(id => !capabilityIds.Contains(id) || coveredCapabilities.Contains(id)).Select(id => "capability " + id))
				.OrderBy(i => i, StringComparer.Ordinal).ToList();
			return report;
		}
	}
}
