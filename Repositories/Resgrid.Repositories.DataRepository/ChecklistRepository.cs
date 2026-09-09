using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Only the closed checklist table map can form identifiers. Every read/write is tenant scoped.</summary>
	public partial class ChecklistRepository : RmsRepositoryBase<ChecklistDefinition>, IChecklistRepository
	{
		public ChecklistRepository(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries) : base(connection, config, uow, queries) { }
		public Task LockDepartmentAsync(int departmentId, CancellationToken ct = default) => LockRecordsDepartmentAsync(departmentId, ct);
		public async Task<List<ChecklistOccurrence>> ReportOccurrencesAsync(int departmentId, DateTime fromUtc, DateTime untilUtc, int skip, CancellationToken ct = default) => (await QueryAsync<ChecklistOccurrence>(
			$"SELECT {Cols(Columns<ChecklistOccurrence>())} FROM {Tbl("ChecklistOccurrences")} WHERE {Col("DepartmentId")}={P}DepartmentId AND COALESCE({Col("PeriodStartUtc")},{Col("CreatedOn")})>={P}From AND COALESCE({Col("PeriodStartUtc")},{Col("CreatedOn")})<{P}Until ORDER BY {Col("CreatedOn")},{Col("Id")} {Paging()}", new { DepartmentId = departmentId, From = fromUtc, Until = untilUtc, Skip = skip, Take = 500 }, ct)).ToList();
		private static string Table<T>() where T : ChecklistRow => ChecklistTables.All[typeof(T)];
		private static class ColumnCache<T> where T : ChecklistRow
		{
			public static readonly System.Reflection.PropertyInfo[] Properties = typeof(T).GetProperties()
				.Where(p => p.CanWrite && !Attribute.IsDefined(p, typeof(NotMappedAttribute))).ToArray();
			public static readonly string[] WithData = Properties.Select(p => p.Name).ToArray();
			public static readonly string[] WithoutData = WithData.Where(name => name != "Data").ToArray();
		}
		private static string[] Columns<T>(bool includeData = true) where T : ChecklistRow => includeData ? ColumnCache<T>.WithData : ColumnCache<T>.WithoutData;
		public Task<T> GetAsync<T>(int departmentId, string id, CancellationToken ct = default) where T : ChecklistRow =>
			QueryFirstOrDefaultAsync<T>($"SELECT {Cols(Columns<T>())} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", new { DepartmentId = departmentId, Id = id }, ct);
		public async Task<List<T>> ListAsync<T>(int departmentId, string parentId = null, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow
		{
			if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
			if (take < 1 || take > 500) throw new ArgumentOutOfRangeException(nameof(take));
			var parent = parentId == null ? "" : $" AND {Col("ParentId")}={P}ParentId";
			return (await QueryAsync<T>($"SELECT {Cols(Columns<T>(false))} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId{parent} ORDER BY {Col("CreatedOn")} DESC, {Col("Id")} {Paging()}", new { DepartmentId = departmentId, ParentId = parentId, Skip = skip, Take = take }, ct)).ToList();
		}
		public async Task<List<T>> ListForMemberAsync<T>(int departmentId, string userId, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow
		{
			if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A member is required.", nameof(userId));
			if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
			if (take < 1 || take > 500) throw new ArgumentOutOfRangeException(nameof(take));
			var target = $"({Col("TargetType")}={P}Personnel AND {Col("TargetId")}={P}UserId)";
			var owned = $"({Col("CreatedBy")}={P}UserId OR {target})";
			string member;
			if (typeof(T) == typeof(ChecklistCompletion)) member = $"({owned} OR {Col("WitnessUserId")}={P}UserId)";
			else if (typeof(T) == typeof(ChecklistSchedule)) member = owned;
			else if (typeof(T) == typeof(ChecklistOccurrence))
				// Witnessing alone does not grant an export of another member's occurrence metadata.
				member = $"({target} OR {Col("Id")} IN (SELECT {Col("OccurrenceId")} FROM {Tbl("ChecklistCompletions")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {owned}) OR {Col("ScheduleId")} IN (SELECT {Col("Id")} FROM {Tbl("ChecklistSchedules")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {owned}))";
			else throw new InvalidOperationException("This checklist table does not support member exports.");
			return (await QueryAsync<T>($"SELECT {Cols(Columns<T>(false))} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId AND {member} ORDER BY {Col("CreatedOn")} DESC, {Col("Id")} {Paging()}",
				new { DepartmentId = departmentId, UserId = userId, Personnel = (int)ChecklistTargetType.Personnel, Skip = skip, Take = take }, ct)).ToList();
		}
		public async Task<List<T>> ListChildrenAsync<T>(int departmentId, IReadOnlyCollection<string> parentIds, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow
		{
			if (parentIds == null || parentIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(parentIds));
			if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
			if (take < 1 || take > 500) throw new ArgumentOutOfRangeException(nameof(take));
			if (parentIds.Count == 0) return new List<T>();
			return (await QueryAsync<T>($"SELECT {Cols(Columns<T>(false))} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId AND {InList("ParentId", "ParentIds")} ORDER BY {Col("CreatedOn")} DESC, {Col("Id")} {Paging()}",
				new { DepartmentId = departmentId, ParentIds = InListValue(parentIds), Skip = skip, Take = take }, ct)).ToList();
		}
		public async Task WriteAsync<T>(T row, bool insert, CancellationToken ct = default) where T : ChecklistRow
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist writes require a transaction.");
			if (!insert && typeof(T) == typeof(ChecklistDefinitionVersion)) throw new InvalidOperationException("Published versions are immutable.");
			var columns = Columns<T>();
			var sql = insert ? $"INSERT INTO {Tbl(Table<T>())} ({Cols(columns)}) VALUES ({string.Join(",", columns.Select(c => P + c))})"
				: $"UPDATE {Tbl(Table<T>())} SET {string.Join(",", columns.Where(c => c != "Id" && c != "DepartmentId").Select(c => Col(c) + "=" + P + c))} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id";
			try
			{
				if (await ExecuteAsync(sql, row, ct) != 1) throw new InvalidOperationException("Checklist row could not be saved.");
			}
			catch (Microsoft.Data.SqlClient.SqlException ex) when (insert && typeof(T) == typeof(ChecklistCompletion) && ex.Number is 2601 or 2627)
			{ throw new ChecklistException(409, "Run identifier is already in use."); }
			catch (Npgsql.PostgresException ex) when (insert && typeof(T) == typeof(ChecklistCompletion) && ex.SqlState == "23505")
			{ throw new ChecklistException(409, "Run identifier is already in use."); }
		}
		public async Task ReplaceAnswersAsync(int departmentId, string completionId, IEnumerable<ChecklistCompletionItem> items, CancellationToken ct = default)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist writes require a transaction.");
			var answers = items.ToList();
			if (answers.Any(item => item == null || item.DepartmentId != departmentId || item.ParentId != completionId))
				throw new InvalidOperationException("Answer ownership mismatch.");
			await ExecuteAsync($"DELETE FROM {Tbl("ChecklistCompletionItems")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ParentId")}={P}Id", new { DepartmentId = departmentId, Id = completionId }, ct);
			// Stay below SQL Server's parameter limit; each VALUES batch is a single round trip.
			var properties = ColumnCache<ChecklistCompletionItem>.Properties;
			foreach (var batch in answers.Chunk(Math.Min(100, 2000 / properties.Length)))
			{
				var parameters = new DynamicParameters();
				var values = new List<string>();
				for (var i = 0; i < batch.Length; i++)
				{
					var names = properties.Select(property =>
					{
						var name = property.Name + i;
						parameters.Add(name, property.GetValue(batch[i]), property.PropertyType == typeof(bool?) ? System.Data.DbType.Boolean : null);
						return P + name;
					});
					values.Add("(" + string.Join(",", names) + ")");
				}
				var count = await ExecuteAsync($"INSERT INTO {Tbl("ChecklistCompletionItems")} ({Cols(Columns<ChecklistCompletionItem>())}) VALUES {string.Join(",", values)}", parameters, ct);
				if (count != batch.Length) throw new InvalidOperationException("Checklist answers could not be saved.");
			}
		}
		public Task DeleteFileAsync(int departmentId, string id, CancellationToken ct = default) => ExecuteAsync($"DELETE FROM {Tbl("ChecklistCompletionFiles")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", new { DepartmentId = departmentId, Id = id }, ct);
		public Task<ChecklistCompletionFile> GetFileMetadataAsync(int departmentId, string id) => QueryFirstOrDefaultAsync<ChecklistCompletionFile>(
			$"SELECT {Cols(Columns<ChecklistCompletionFile>(false))} FROM {Tbl("ChecklistCompletionFiles")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", new { DepartmentId = departmentId, Id = id });
		public async Task<List<int>> SchedulingDepartmentsAsync(int afterDepartmentId, CancellationToken ct = default) => (await QueryAsync<int>(
			$"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("ChecklistSchedules")} WHERE {Col("IsActive")}={P}Active AND {Col("DepartmentId")}>{P}After ORDER BY {Col("DepartmentId")} {Paging()}", new { Active = true, After = afterDepartmentId, Skip = 0, Take = 100 }, ct)).ToList();
		public async Task<List<ChecklistSchedule>> ActiveSchedulesAsync(int departmentId, string afterId, CancellationToken ct = default) => (await QueryAsync<ChecklistSchedule>(
			$"SELECT {Cols(Columns<ChecklistSchedule>())} FROM {Tbl("ChecklistSchedules")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("IsActive")}={P}Active AND {Col("Id")}>{P}After ORDER BY {Col("Id")} {Paging()}", new { DepartmentId = departmentId, Active = true, After = afterId ?? "", Skip = 0, Take = 100 }, ct)).ToList();
		public async Task<List<ChecklistOccurrence>> ScheduledOccurrencesAsync(int departmentId, string scheduleId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) => (await QueryAsync<ChecklistOccurrence>(
			$"SELECT {Cols(Columns<ChecklistOccurrence>())} FROM {Tbl("ChecklistOccurrences")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ScheduleId")}={P}ScheduleId AND {Col("State")} IN (0,1,3) AND {Col("WindowEndUtc")}>={P}From AND {Col("WindowEndUtc")}<{P}Until ORDER BY {Col("PeriodStartUtc")},{Col("Id")} {Paging()}", new { DepartmentId = departmentId, ScheduleId = scheduleId, From = fromUtc, Until = untilUtc, Skip = 0, Take = 500 }, ct)).ToList();
		public async Task<List<ChecklistOccurrence>> DueOccurrencesAsync(int departmentId, DateTime untilUtc, int skip, CancellationToken ct = default) => (await QueryAsync<ChecklistOccurrence>(
			$"SELECT {Cols(Columns<ChecklistOccurrence>())} FROM {Tbl("ChecklistOccurrences")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ScheduleId")} IS NOT NULL AND {Col("State")} IN (0,1,3,4) AND {Col("PeriodStartUtc")}<={P}Until ORDER BY {Col("PeriodStartUtc")},{Col("Id")} {Paging()}", new { DepartmentId = departmentId, Until = untilUtc, Skip = skip, Take = 50 }, ct)).ToList();
		public async Task<List<ChecklistOccurrence>> OccurrencesInWindowAsync(int departmentId, string scheduleId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) => (await QueryAsync<ChecklistOccurrence>(
			$"SELECT {Cols(Columns<ChecklistOccurrence>())} FROM {Tbl("ChecklistOccurrences")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ScheduleId")}={P}ScheduleId AND {Col("PeriodStartUtc")}>={P}From AND {Col("PeriodStartUtc")}<{P}Until", new { DepartmentId = departmentId, ScheduleId = scheduleId, From = fromUtc, Until = untilUtc }, ct)).ToList();
		public Task CancelUnstartedOccurrencesAsync(int departmentId, string scheduleId, DateTime? fromUtc, DateTime nowUtc, CancellationToken ct = default)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist writes require a transaction.");
			return ExecuteAsync($"UPDATE {Tbl("ChecklistOccurrences")} SET {Col("State")}=6,{Col("Revision")}={Col("Revision")}+1,{Col("UpdatedOn")}={P}Now WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ScheduleId")}={P}ScheduleId AND {Col("State")}=3{(fromUtc.HasValue ? $" AND {Col("PeriodStartUtc")}>={P}From" : "")}", new { DepartmentId = departmentId, ScheduleId = scheduleId, From = fromUtc, Now = nowUtc }, ct);
		}
		public async Task<bool> WorkshiftExistsAsync(int departmentId, string workshiftId, CancellationToken ct = default) => await ScalarAsync<int>(
			$"SELECT COUNT(*) FROM {Tbl("Workshifts")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("WorkshiftId")}={P}Id AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Id = workshiftId }, ct) == 1;
		public async Task<List<DateTime>> WorkshiftStartsAsync(int departmentId, string workshiftId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) => (await QueryAsync<DateTime>(
			$"SELECT {Col("Day")} FROM {Tbl("WorkshiftDays")} WHERE {Col("WorkshiftId")} IN (SELECT {Col("WorkshiftId")} FROM {Tbl("Workshifts")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("WorkshiftId")}={P}Id AND {Col("DeletedOn")} IS NULL) AND {Col("Day")}>={P}From AND {Col("Day")}<{P}Until ORDER BY {Col("Day")}", new { DepartmentId = departmentId, Id = workshiftId, From = fromUtc, Until = untilUtc }, ct)).ToList();
	}
}
