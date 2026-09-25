using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository
	{
		private sealed class AdministrativeDocument
		{
			public int DocumentId { get; set; }
			public DateTime? RemoveOn { get; set; }
		}
		public async Task<AdministrativeReferenceCounts> ReadAdministrativeReferencesAsync(int departmentId, int[] documentIds, int[] groupIds, DateTime asOfUtc, CancellationToken ct)
		{
			if (departmentId <= 0 || documentIds == null || groupIds == null || documentIds.Length > 75 || groupIds.Length > 25 || documentIds.Concat(groupIds).Any(id => id <= 0)) throw new ArgumentException("Invalid reference projection.");
			documentIds = documentIds.Distinct().ToArray(); groupIds = groupIds.Distinct().ToArray();
			var documents = documentIds.Length == 0 ? Array.Empty<AdministrativeDocument>() :
				(await QueryAsync<AdministrativeDocument>($"SELECT {Cols("DocumentId", "RemoveOn")} FROM {Tbl("Documents")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("DocumentId")} IN ({string.Join(",", documentIds.Select((_, i) => P + "D" + i))})", Parameters(departmentId, "D", documentIds), ct)).ToArray();
			var groups = groupIds.Length == 0 ? Array.Empty<int>() :
				(await QueryAsync<int>($"SELECT {Col("DepartmentGroupId")} FROM {Tbl("DepartmentGroups")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("DepartmentGroupId")} IN ({string.Join(",", groupIds.Select((_, i) => P + "G" + i))})", Parameters(departmentId, "G", groupIds), ct)).ToArray();
			return new(documentIds.Length, documentIds.Length - documents.Count(d => !d.RemoveOn.HasValue || d.RemoveOn > asOfUtc),
				documents.Count(d => d.RemoveOn > asOfUtc && d.RemoveOn <= asOfUtc.AddDays(30)), groupIds.Length, groupIds.Length - groups.Distinct().Count());
		}
		private static Dapper.DynamicParameters Parameters(int departmentId, string prefix, int[] ids)
		{
			var parameters = new Dapper.DynamicParameters(); parameters.Add("DepartmentId", departmentId);
			for (int i = 0; i < ids.Length; i++) parameters.Add(prefix + i, ids[i]);
			return parameters;
		}
	}
}
