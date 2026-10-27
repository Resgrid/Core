using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.IncidentLogistics
{
	/// <summary>Pick lists for the Incident Back Office desks. Table, key and label names come from IncidentBackOfficeTables only.</summary>
	public class IncidentLookupRepository : RmsRepositoryBase<IncidentActivation>, IIncidentLookupRepository
	{
		public const string VendorTable = "InventoryVendors";

		public IncidentLookupRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static (string Key, string Label, bool HasActivation, bool SoftDelete) Shape(string table)
		{
			var entry = IncidentBackOfficeTables.All.FirstOrDefault(t => string.Equals(t.Table, table, StringComparison.OrdinalIgnoreCase));
			if (entry.Table == null)
				throw new ArgumentException($"'{table}' is not an Incident Back Office table.", nameof(table));
			var label = IncidentBackOfficeTables.Labels.TryGetValue(entry.Table, out var column) ? column : entry.Key;
			return (entry.Key, label, entry.HasActivation, IncidentBackOfficeTables.SoftDeletable.Contains(entry.Table));
		}

		private string False => IsPostgres ? "FALSE" : "0";

		private string LabelExpression(string label) => IsPostgres ? $"CAST({Col(label)} AS text)" : $"CAST({Col(label)} AS nvarchar(400))";

		public async Task<IReadOnlyList<IncidentLookupRow>> GetOptionsAsync(string table, int departmentId, IReadOnlyList<string> activationIds, bool includeDepartmentCatalog, int take, CancellationToken cancellationToken = default)
		{
			if (string.Equals(table, VendorTable, StringComparison.OrdinalIgnoreCase))
				return (await QueryAsync<IncidentLookupRow>(
					$"SELECT v.{Col("Id")} AS Id, COALESCE(NULLIF(c.{Col("CompanyName")}, ''), {Concat($"COALESCE(c.{Col("FirstName")}, '')", "' '", $"COALESCE(c.{Col("LastName")}, '')")}) AS Label " +
					$"FROM {Tbl(VendorTable)} v JOIN {Tbl("Contacts")} c ON c.{Col("ContactId")} = v.{Col("ContactId")} AND c.{Col("DepartmentId")} = v.{Col("DepartmentId")} " +
					$"WHERE v.{Col("DepartmentId")} = {P}DepartmentId AND v.{Col("IsDeleted")} = {False} ORDER BY 2 {Paging()}",
					new { DepartmentId = departmentId, Skip = 0, Take = Math.Clamp(take, 1, 2000) }, cancellationToken)).ToList();

			var shape = Shape(table);
			var where = $"{Col("DepartmentId")} = {P}DepartmentId";
			if (shape.SoftDelete) where += $" AND {Col("IsDeleted")} = {False}";
			var ids = activationIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? Array.Empty<string>();
			if (shape.HasActivation && ids.Length > 0)
				where += includeDepartmentCatalog ? $" AND ({CaseInsensitiveInList("IncidentActivationId", "Ids")} OR {Col("IncidentActivationId")} IS NULL)" : $" AND {CaseInsensitiveInList("IncidentActivationId", "Ids")}";
			return (await QueryAsync<IncidentLookupRow>(
				$"SELECT {Col(shape.Key)} AS Id, {LabelExpression(shape.Label)} AS Label FROM {Tbl(table)} WHERE {where} ORDER BY 2 {Paging()}",
				new { DepartmentId = departmentId, Ids = ids, Skip = 0, Take = Math.Clamp(take, 1, 2000) }, cancellationToken)).ToList();
		}

		public async Task<IReadOnlyList<IncidentActivationRef>> GetOpenActivationsAsync(CancellationToken cancellationToken = default)
			=> (await QueryAsync<IncidentActivationRef>(
				$"SELECT {Col("DepartmentId")} AS DepartmentId, {Col("IncidentActivationId")} AS IncidentActivationId, {Col("Status")} AS Status, {Col("ClosedOn")} AS ClosedOn " +
				$"FROM {Tbl("IncidentActivations")} WHERE {Col("IsDeleted")} = {False} AND {Col("Status")} < {P}Closed ORDER BY {Col("DepartmentId")}",
				new { Closed = (int)IncidentActivationStatuses.Closed }, cancellationToken)).ToList();

		public async Task<IReadOnlyList<IncidentActivationRef>> GetClosedActivationsAsync(DateTime closedBefore, CancellationToken cancellationToken = default)
			=> (await QueryAsync<IncidentActivationRef>(
				$"SELECT {Col("DepartmentId")} AS DepartmentId, {Col("IncidentActivationId")} AS IncidentActivationId, {Col("Status")} AS Status, {Col("ClosedOn")} AS ClosedOn " +
				$"FROM {Tbl("IncidentActivations")} WHERE {Col("IsDeleted")} = {False} AND {Col("Status")} >= {P}Closed AND {Col("ClosedOn")} < {P}Before ORDER BY {Col("DepartmentId")}",
				new { Closed = (int)IncidentActivationStatuses.Closed, Before = closedBefore }, cancellationToken)).ToList();

		public async Task<(int DepartmentId, string LinkId)?> FindSelfServiceLinkAsync(string tokenHash, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(tokenHash))
				return null;
			var row = (await QueryAsync<IncidentLookupLinkRow>(
				$"SELECT {Col("DepartmentId")} AS DepartmentId, {Col("IncidentSelfServiceLinkId")} AS LinkId FROM {Tbl("IncidentSelfServiceLinks")} " +
				$"WHERE {Col("TokenHash")} = {P}Hash AND {Col("IsDeleted")} = {False} AND {Col("RevokedOn")} IS NULL",
				new { Hash = tokenHash }, cancellationToken)).FirstOrDefault();
			return row == null ? null : (row.DepartmentId, row.LinkId);
		}

		private sealed class IncidentLookupLinkRow
		{
			public int DepartmentId { get; set; }
			public string LinkId { get; set; }
		}

		public async Task<IReadOnlyList<int>> GetDepartmentsWithRowsAsync(string table, CancellationToken cancellationToken = default)
		{
			var shape = Shape(table);
			var live = shape.SoftDelete ? $" WHERE {Col("IsDeleted")} = {False}" : string.Empty;
			return (await QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl(table)}{live}", null, cancellationToken)).ToList();
		}

		public async Task<IReadOnlyList<IncidentLookupRow>> GetLabelsAsync(string table, int departmentId, IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
		{
			var list = ids?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
			if (list.Length == 0)
				return Array.Empty<IncidentLookupRow>();
			if (string.Equals(table, VendorTable, StringComparison.OrdinalIgnoreCase))
				return (await QueryAsync<IncidentLookupRow>(
					$"SELECT v.{Col("Id")} AS Id, COALESCE(NULLIF(c.{Col("CompanyName")}, ''), {Concat($"COALESCE(c.{Col("FirstName")}, '')", "' '", $"COALESCE(c.{Col("LastName")}, '')")}) AS Label " +
					$"FROM {Tbl(VendorTable)} v JOIN {Tbl("Contacts")} c ON c.{Col("ContactId")} = v.{Col("ContactId")} AND c.{Col("DepartmentId")} = v.{Col("DepartmentId")} " +
					$"WHERE v.{Col("DepartmentId")} = {P}DepartmentId AND {CaseInsensitiveInList("Id", "Ids", "v")}",
					new { DepartmentId = departmentId, Ids = list }, cancellationToken)).ToList();

			var shape = Shape(table);
			return (await QueryAsync<IncidentLookupRow>(
				$"SELECT {Col(shape.Key)} AS Id, {LabelExpression(shape.Label)} AS Label FROM {Tbl(table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {CaseInsensitiveInList(shape.Key, "Ids")}",
				new { DepartmentId = departmentId, Ids = list }, cancellationToken)).ToList();
		}
	}
}
