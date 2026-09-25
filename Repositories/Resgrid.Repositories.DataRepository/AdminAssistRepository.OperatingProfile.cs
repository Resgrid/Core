using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository
	{
		public async Task<bool> ValidateOperatingProfileReferencesAsync(int departmentId, DepartmentOperatingProfile profile, DateTime asOfUtc, CancellationToken ct)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Profile validation requires a transaction.");
			var groups = profile.SiteGroupReferences;
			var documents = profile.StaffingPolicyReferences.Concat(profile.QualificationPolicyReferences).Concat(profile.ContinuityProcedureReferences).ToArray();
			if (groups.Count > 25 || documents.Length > 75) return false;
			foreach (var reference in groups.Distinct(StringComparer.Ordinal))
			{
				if (!int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) return false;
				if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("DepartmentGroups")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("DepartmentGroupId")}={P}Id",
					new { DepartmentId = departmentId, Id = id }, ct) != 1) return false;
			}
			foreach (var reference in documents.Distinct(StringComparer.Ordinal))
			{
				if (!int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) return false;
				if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("Documents")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("DocumentId")}={P}Id AND ({Col("RemoveOn")} IS NULL OR {Col("RemoveOn")}>{P}AsOfUtc)",
					new { DepartmentId = departmentId, Id = id, AsOfUtc = DatabaseTimestamp(asOfUtc) }, ct) != 1) return false;
			}
			return true;
		}
	}
}
