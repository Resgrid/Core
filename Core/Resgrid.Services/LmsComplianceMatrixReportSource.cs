using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round-4B's compliance matrix (person × requirement) as a report of round-4C's LMS catalog (key "compliance-matrix", LMS tier), so it can
	/// be saved in a folder and scheduled like the others. Department-wide and active members only (the matrix's own rules); the catalog's
	/// callers are already officers or records managers. Group by member (a row per member and requirement) or by requirement (counts per state).
	/// While the add-on is lapsed the matrix is paused, and the report says so.
	/// </summary>
	public class LmsComplianceMatrixReportSource : ILmsReportSource
	{
		public const string Key = "compliance-matrix";

		/// <summary>The matrix's state names (the LmsPrograms resources the matrix page uses).</summary>
		private static readonly ResourceManager StateNames = new ResourceManager("Resgrid.Localization.Areas.User.LmsPrograms.LmsPrograms", typeof(Resgrid.Localization.SupportedLocales).Assembly);

		private static readonly LmsRequirementStates[] States =
		{
			LmsRequirementStates.Compliant, LmsRequirementStates.Pending, LmsRequirementStates.AtRisk, LmsRequirementStates.Overdue,
			LmsRequirementStates.Exempt, LmsRequirementStates.OnLeave, LmsRequirementStates.NotLicensed
		};

		private readonly ILmsRequirementService _requirements;

		public LmsComplianceMatrixReportSource(ILmsRequirementService requirements)
		{
			_requirements = requirements;
		}

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = Key, NameKey = "Prg.Report.Matrix", DescriptionKey = "Prg.Report.MatrixHelp", Tier = LmsReportTiers.Lms,
			Groupings = new Dictionary<int, string> { { 0, "Prg.GroupBy.Member" }, { 1, "Prg.GroupBy.Requirement" } }
		};

		public static string StateName(LmsRequirementStates state, CultureInfo culture) => LmsText.Lookup(StateNames, "State." + state, culture?.Name);

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var matrix = await _requirements.GetDepartmentMatrixAsync(request.DepartmentId, new LmsMatrixFilter(), request.UtcNow) ?? new LmsComplianceMatrix();
			var culture = request.Culture ?? CultureInfo.InvariantCulture;
			var table = new LmsReportTable { Title = text(Descriptor.NameKey) };
			if (matrix.Error != LmsErrors.None)
			{
				table.Problem = text("Prg.AddonRequired");
				return table;
			}

			string Name(LmsRequirement requirement) =>
				matrix.TargetNames.TryGetValue(requirement.LmsRequirementId, out var target) && !string.IsNullOrWhiteSpace(target) ? requirement.Name + " (" + target + ")" : requirement.Name;
			string Date(DateTime? utc) => utc.HasValue
				? (request.Department == null ? utc.Value : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).TimeConverter(request.Department)).ToString("d", culture)
				: string.Empty;

			if (request.Parameters?.GroupBy == 1)
			{
				table.Columns.Add(text("Prg.Col.Requirement"));
				table.Columns.Add(text("Prg.Col.Members"));
				table.Columns.AddRange(States.Select(s => StateName(s, culture)));
				foreach (var requirement in matrix.Requirements)
				{
					var cells = matrix.Rows.Where(r => r.Cells.ContainsKey(requirement.LmsRequirementId)).Select(r => r.Cells[requirement.LmsRequirementId]).ToList();
					var row = new List<string> { Name(requirement), cells.Count.ToString(culture) };
					row.AddRange(States.Select(s => cells.Count(c => c.State == s).ToString(culture)));
					table.Rows.Add(row);
				}
			}
			else
			{
				table.Columns.AddRange(new[] { text("Prg.Col.Member"), text("Prg.Col.Requirement"), text("Prg.Col.Status"), text("Prg.Col.Due"), text("Prg.Col.LastCompleted"), text("Prg.Col.Progress") });
				var requirements = matrix.Requirements.ToDictionary(r => r.LmsRequirementId);
				foreach (var member in matrix.Rows)
					foreach (var cell in member.Cells.Values.Where(c => requirements.ContainsKey(c.LmsRequirementId)).OrderBy(c => requirements[c.LmsRequirementId].Name, StringComparer.CurrentCultureIgnoreCase))
						table.Rows.Add(new List<string>
						{
							member.Name,
							Name(requirements[cell.LmsRequirementId]),
							StateName(cell.State, culture),
							Date(cell.ExtendedDueUtc ?? cell.DueUtc),
							Date(cell.LastCompletedUtc),
							cell.Counted ? LmsReportCells.Hours(cell.Progress, culture) + " / " + LmsReportCells.Hours(cell.Required, culture) : string.Empty
						});
			}

			foreach (var state in States.Where(s => matrix.Totals.ContainsKey(s)))
				table.Footer.Add(StateName(state, culture) + ": " + matrix.Totals[state].ToString(culture));
			if (matrix.Paused)
				table.Footer.Add(text("Prg.Footer.MatrixPaused"));
			return table;
		}
	}
}
