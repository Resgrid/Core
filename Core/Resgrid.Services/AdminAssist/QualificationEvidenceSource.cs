using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class QualificationEvidenceSource(ICertificationService certifications, IAuthorizationService authorization,
		IDepartmentsService departments) : IAdminAssistEvidenceSource
	{
		public string SourceId => "Qualifications";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "qualificationsExpiring30Days", "qualificationsExpiring60Days", "qualificationsExpiring90Days", "qualificationsExpired", "qualificationsPending", "qualificationsSuspended", "uncoveredQualificationCount" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var department = await departments.GetDepartmentByIdAsync(actor.DepartmentId, true).WaitAsync(ct)
				?? throw new InvalidOperationException("Department unavailable.");
			var zone = TimeZoneInfo.FindSystemTimeZoneById(department.TimeZone);
			var localDate = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;
			var dashboard = await certifications.GetExpiryDashboardAsync(actor.DepartmentId, localDate).WaitAsync(ct)
				?? throw new InvalidOperationException("Qualification metadata unavailable.");
			var limit = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			if (dashboard.PersonCells.Count + dashboard.UnitCells.Count > limit) throw new InvalidOperationException("Row bound exceeded.");
			var visible = new List<CertificationDashboardCell>();
			foreach (var cell in dashboard.PersonCells)
			{
				ct.ThrowIfCancellationRequested();
				if (await authorization.CanUserViewPersonAsync(actor.UserId, cell.SubjectId, actor.DepartmentId)) visible.Add(cell);
				else throw new UnauthorizedAccessException();
			}
			foreach (var cell in dashboard.UnitCells)
			{
				ct.ThrowIfCancellationRequested();
				if (int.TryParse(cell.SubjectId, out var id) && await authorization.CanUserViewUnitAsync(actor.UserId, id)) visible.Add(cell);
				else throw new UnauthorizedAccessException();
			}
			ConfigurationEvidence Count(string id, int value) => new(id, EvidenceState.Known, SourceId, "1", now, Number: value);
			var result = new List<ConfigurationEvidence>();
			foreach (var days in new[] { 30, 60, 90 })
				result.Add(Count($"qualificationsExpiring{days}Days", visible.Count(c => c.Status == (int)PersonnelCertificationStatuses.Active && c.DaysUntilExpiry >= 0 && c.DaysUntilExpiry <= days)));
			result.Add(Count("qualificationsExpired", visible.Count(c => c.Status == (int)PersonnelCertificationStatuses.Expired || c.DaysUntilExpiry < 0)));
			result.Add(Count("qualificationsPending", visible.Count(c => c.Status == (int)PersonnelCertificationStatuses.PendingVerification)));
			result.Add(Count("qualificationsSuspended", visible.Count(c => c.Status == (int)PersonnelCertificationStatuses.Suspended || c.Status == (int)PersonnelCertificationStatuses.Revoked)));
			var requirements = await certifications.GetAllRoleRequirementsAsync(actor.DepartmentId).WaitAsync(ct);
			if (requirements == null || requirements.Count > limit) throw new InvalidOperationException("Role requirements unavailable.");
			var uncovered = 0;
			foreach (var role in requirements.Where(r => r.IsMandatory).Select(r => r.PersonnelRoleId).Distinct())
			{
				var evaluations = await certifications.EvaluateRoleRequirementsAsync(actor.DepartmentId, role, localDate).WaitAsync(ct)
					?? throw new InvalidOperationException("Role evidence unavailable.");
				if (evaluations.Count > limit) throw new InvalidOperationException("Row bound exceeded.");
				foreach (var evaluation in evaluations)
					if (!await authorization.CanUserViewPersonAsync(actor.UserId, evaluation.UserId, actor.DepartmentId)) throw new UnauthorizedAccessException();
				// A role nobody holds yet (common while roles are created before people are assigned) has no one to be
				// uncovered; only a staffed role whose holders all lack the mandatory qualification is a coverage gap.
				if (evaluations.Count > 0 && !evaluations.Any(e => e.Qualified)) uncovered++;
			}
			result.Add(Count("uncoveredQualificationCount", uncovered));
			return result;
		}
	}
}
