using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets
{
	/// <summary>
	/// The units the Units module lists for a viewer: the department's units with their latest status
	/// (IUnitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync — a unit that never set one reads as Available with
	/// UnitStateId 0), soft-deleted units left out, filtered by the unit visibility matrix (UnitsController.Index).
	/// </summary>
	public static class DashboardUnitReads
	{
		public static string UnitUrl(int unitId) => "/User/Units/ViewEvents?unitId=" + unitId.ToString(CultureInfo.InvariantCulture);

		public static async Task<List<UnitState>> GetVisibleLatestStatesAsync(IUnitsService unitsService, IAuthorizationService authorizationService,
			int departmentId, string userId)
		{
			var states = (await unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(departmentId) ?? new List<UnitState>())
				.Where(s => s?.Unit != null && s.Unit.DepartmentId == departmentId && !s.Unit.IsDeleted && s.UnitId == s.Unit.UnitId)
				.GroupBy(s => s.UnitId)
				.Select(g => g.First())
				.ToList();

			var visible = new List<UnitState>();
			foreach (var state in states)
			{
				if (await authorizationService.CanUserViewUnitViaMatrixAsync(state.UnitId, userId, departmentId))
					visible.Add(state);
			}

			return visible;
		}

		/// <summary>
		/// A note as a widget may carry it (contract §2.4): null for no note; a shield for a redacted or still-enveloped value
		/// (never the REDACTED literal or an rgdp: envelope); the text otherwise.
		/// </summary>
		public static DashboardProtectedText ProtectedNote(string note, DashboardViewer viewer)
		{
			if (string.IsNullOrEmpty(note))
				return null;

			if (string.Equals(note, ProtectedDataEnvelope.RedactionValue, System.StringComparison.Ordinal) || ProtectedDataEnvelope.HasEnvelopePrefix(note))
				return DashboardProtectedText.Hidden(viewer != null && viewer.CanRevealAdp(PermissionTypes.ViewProtectedOperationalData));

			return DashboardProtectedText.Plain(note);
		}
	}
}
