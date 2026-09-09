using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Only the closed checklist table map can form identifiers. Every read/write is tenant scoped.</summary>
	public class ChecklistRepository : RmsRepositoryBase<ChecklistDefinition>, IChecklistRepository
	{
		public ChecklistRepository(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries) : base(connection, config, uow, queries) { }
		public Task LockDepartmentAsync(int departmentId, CancellationToken ct = default) => LockRecordsDepartmentAsync(departmentId, ct);
		private static string Table<T>() where T : ChecklistRow => ChecklistTables.All[typeof(T)];
		private static string[] Columns<T>(bool includeData = true) where T : ChecklistRow => typeof(T).GetProperties()
			.Where(p => p.CanWrite && !Attribute.IsDefined(p, typeof(NotMappedAttribute)) && (includeData || p.Name != "Data")).Select(p => p.Name).ToArray();
		public Task<T> GetAsync<T>(int departmentId, string id, CancellationToken ct = default) where T : ChecklistRow =>
			QueryFirstOrDefaultAsync<T>($"SELECT {Cols(Columns<T>())} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", new { DepartmentId = departmentId, Id = id }, ct);
		public async Task<List<T>> ListAsync<T>(int departmentId, string parentId = null, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow
		{
			if (skip < 0 || take < 1 || take > 500) throw new ArgumentOutOfRangeException(nameof(take));
			var parent = parentId == null ? "" : $" AND {Col("ParentId")}={P}ParentId";
			return (await QueryAsync<T>($"SELECT {Cols(Columns<T>(false))} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId{parent} ORDER BY {Col("CreatedOn")} DESC, {Col("Id")} {Paging()}", new { DepartmentId = departmentId, ParentId = parentId, Skip = skip, Take = take }, ct)).ToList();
		}
		public async Task WriteAsync<T>(T row, bool insert, CancellationToken ct = default) where T : ChecklistRow
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist writes require a transaction.");
			if (!insert && typeof(T) == typeof(ChecklistDefinitionVersion)) throw new InvalidOperationException("Published versions are immutable.");
			var columns = Columns<T>();
			var sql = insert ? $"INSERT INTO {Tbl(Table<T>())} ({Cols(columns)}) VALUES ({string.Join(",", columns.Select(c => P + c))})"
				: $"UPDATE {Tbl(Table<T>())} SET {string.Join(",", columns.Where(c => c != "Id" && c != "DepartmentId").Select(c => Col(c) + "=" + P + c))} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id";
			if (await ExecuteAsync(sql, row, ct) != 1) throw new InvalidOperationException("Checklist row could not be saved.");
		}
		public async Task ReplaceAnswersAsync(int departmentId, string completionId, IEnumerable<ChecklistCompletionItem> items, CancellationToken ct = default)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist writes require a transaction.");
			await ExecuteAsync($"DELETE FROM {Tbl("ChecklistCompletionItems")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ParentId")}={P}Id", new { DepartmentId = departmentId, Id = completionId }, ct);
			foreach (var item in items)
			{
				if (item.DepartmentId != departmentId || item.ParentId != completionId) throw new InvalidOperationException("Answer ownership mismatch.");
				await WriteAsync(item, true, ct);
			}
		}
		public Task DeleteFileAsync(int departmentId, string id, CancellationToken ct = default) => ExecuteAsync($"DELETE FROM {Tbl("ChecklistCompletionFiles")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", new { DepartmentId = departmentId, Id = id }, ct);
		public Task<ChecklistCompletionFile> GetFileMetadataAsync(int departmentId, string id) => QueryFirstOrDefaultAsync<ChecklistCompletionFile>(
			$"SELECT {Cols(Columns<ChecklistCompletionFile>(false))} FROM {Tbl("ChecklistCompletionFiles")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", new { DepartmentId = departmentId, Id = id });
	}
}
