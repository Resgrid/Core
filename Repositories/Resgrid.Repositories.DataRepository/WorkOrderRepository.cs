using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.WorkOrders;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class WorkOrderRepository : RmsRepositoryBase<WorkOrder>, IWorkOrderRepository, IWorkOrderMaintenanceRepository
	{
		public WorkOrderRepository(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries) : base(connection, config, uow, queries) { }
		private static string Table<T>() where T : WorkOrderRow => WorkOrderTables.All[typeof(T)];
		private static string[] Columns<T>(bool data = true) where T : WorkOrderRow => typeof(T).GetProperties().Where(p => p.CanWrite && !Attribute.IsDefined(p, typeof(NotMappedAttribute)) && (data || p.Name != "Data")).Select(p => p.Name).ToArray();
		public async Task<int> ClaimNotificationAsync(WorkOrderNotification row, DateTime now)
		{
			Transaction();
			var key = new { row.DepartmentId, row.EventId, row.UserId };
			var existing = await QueryFirstOrDefaultAsync<WorkOrderNotification>($"SELECT * FROM {Tbl("WorkOrderNotifications")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("EventId")}={P}EventId AND {Col("UserId")}={P}UserId", key, default);
			if (existing?.State is 2 or 3) return existing.State;
			if (existing?.State == 1 && existing.LeaseExpiresOn > now) return -1;
			row.State = 1; row.LeaseExpiresOn = now.AddMinutes(30); row.UpdatedOn = now;
			if (existing == null)
				await ExecuteAsync($"INSERT INTO {Tbl("WorkOrderNotifications")} ({Cols("DepartmentId","WorkOrderId","EventId","UserId","State","LeaseOwner","LeaseExpiresOn","UpdatedOn")}) VALUES ({P}DepartmentId,{P}WorkOrderId,{P}EventId,{P}UserId,{P}State,{P}LeaseOwner,{P}LeaseExpiresOn,{P}UpdatedOn)", row, default);
			else await ExecuteAsync($"UPDATE {Tbl("WorkOrderNotifications")} SET {Col("State")}=1,{Col("LeaseOwner")}={P}LeaseOwner,{Col("LeaseExpiresOn")}={P}LeaseExpiresOn,{Col("UpdatedOn")}={P}UpdatedOn WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("EventId")}={P}EventId AND {Col("UserId")}={P}UserId", row, default);
			return 1;
		}
		public async Task<bool> FinishNotificationAsync(WorkOrderNotification row, int state, DateTime now)
		{
			Transaction();
			return await ExecuteAsync($"UPDATE {Tbl("WorkOrderNotifications")} SET {Col("State")}={P}State,{Col("LeaseExpiresOn")}=NULL,{Col("UpdatedOn")}={P}Now WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("EventId")}={P}EventId AND {Col("UserId")}={P}UserId AND {Col("State")}=1 AND {Col("LeaseOwner")}={P}LeaseOwner AND {Col("LeaseExpiresOn")}>{P}Now", new { row.DepartmentId, row.EventId, row.UserId, row.LeaseOwner, State = state, Now = now }, default) == 1;
		}
		public Task LockDepartmentAsync(int departmentId) => LockRecordsDepartmentAsync(departmentId, default);
		private void Transaction() { if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Work-order writes require a transaction."); }
		public Task<T> GetAsync<T>(int departmentId, int id, bool includeData = true) where T : WorkOrderRow => QueryFirstOrDefaultAsync<T>($"SELECT {Cols(Columns<T>(includeData))} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", new { DepartmentId = departmentId, Id = id }, default);
		public Task<WorkOrder> RequestAsync(int departmentId, string requestId) => QueryFirstOrDefaultAsync<WorkOrder>($"SELECT {Cols(Columns<WorkOrder>())} FROM {Tbl("WorkOrders")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("RequestId")}={P}RequestId", new { DepartmentId = departmentId, RequestId = requestId }, default);
		public async Task<List<WorkOrder>> ListAsync(int departmentId, WorkOrderReadScope scope, WorkOrderFilter filter)
		{
			if (scope == null || string.IsNullOrWhiteSpace(scope.UserId) || filter.Page < 0 || filter.Page > 10000) throw new ArgumentException("Invalid work-order scope.");
			var parameters = new DynamicParameters(new { DepartmentId = departmentId, UserId = scope.UserId, AllowedGroup = scope.GroupId, Status = (int?)filter.Status, Priority = (int?)filter.Priority, UnitId = filter.UnitId, GroupId = filter.GroupId, AssetId = filter.AssetId, ChecklistCompletionId = filter.ChecklistCompletionId, Skip = filter.Page * 50, Take = 51 });
			parameters.Add("Roles", InListValue(scope.RoleIds == null || scope.RoleIds.Length == 0 ? new[] { -1 } : scope.RoleIds));
			var own = $"({Col("CreatedBy")}={P}UserId OR {Col("AssignedToUserId")}={P}UserId OR {InList("AssignedToRoleId", "Roles")} OR {Col("TargetGroupId")}={P}AllowedGroup)";
			var conditions = new List<string> { $"{Col("DepartmentId")}={P}DepartmentId" };
			if (!scope.All) conditions.Add(own);
			if (filter.AssignedToMe) conditions.Add($"({Col("AssignedToUserId")}={P}UserId OR {InList("AssignedToRoleId", "Roles")})");
			foreach (var item in new[] { (filter.Status.HasValue, "Status", "Status"), (filter.Priority.HasValue, "Priority", "Priority"), (filter.UnitId.HasValue, "TargetUnitId", "UnitId"), (filter.GroupId.HasValue, "TargetGroupId", "GroupId"), (filter.AssetId != null, "InventoryAssetId", "AssetId"), (filter.ChecklistCompletionId != null, "SourceChecklistCompletionId", "ChecklistCompletionId") })
				if (item.Item1) conditions.Add(Col(item.Item2) + "=" + P + item.Item3);
			return (await QueryAsync<WorkOrder>($"SELECT {Cols(Columns<WorkOrder>())} FROM {Tbl("WorkOrders")} WHERE {string.Join(" AND ", conditions)} ORDER BY {Col("Id")} DESC {Paging()}", parameters, default)).ToList();
		}
		public async Task<List<T>> ChildrenAsync<T>(int departmentId, int orderId, int skip = 0) where T : WorkOrderRow
		{
			if (skip < 0 || typeof(T) == typeof(WorkOrder)) throw new ArgumentException("Invalid child page.");
			return (await QueryAsync<T>($"SELECT {Cols(Columns<T>(false))} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("WorkOrderId")}={P}OrderId ORDER BY {Col("Id")} {Paging()}", new { DepartmentId = departmentId, OrderId = orderId, Skip = skip, Take = 500 }, default)).ToList();
		}
		public Task<int> NextNumberAsync(int departmentId, int year)
		{
			Transaction();
			return ScalarAsync<int>($"SELECT COALESCE(MAX({Col("NumberSequence")}),0)+1 FROM {Tbl("WorkOrders")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("NumberYear")}={P}Year", new { DepartmentId = departmentId, Year = year }, default);
		}
		public async Task AllocateAsync<T>(T row) where T : WorkOrderRow
		{
			Transaction();
			if (row.Id != 0 || row.Content != null || row is WorkOrderFile file && file.Data != null) throw new InvalidOperationException("Allocate identities without sensitive content.");
			var columns = Columns<T>().Where(c => c != "Id").ToArray();
			var sql = $"INSERT INTO {Tbl(Table<T>())} ({Cols(columns)}) {(IsPostgres ? "" : "OUTPUT INSERTED.[Id]")} VALUES ({string.Join(",", columns.Select(c => P + c))}) {(IsPostgres ? "RETURNING id" : "")}";
			row.Id = await ScalarAsync<int>(sql, row, default);
		}
		public async Task WriteAsync<T>(T row) where T : WorkOrderRow
		{
			Transaction();
			var columns = Columns<T>().Where(c => c != "Id" && c != "DepartmentId");
			var immutable = typeof(T) == typeof(WorkOrderActivity) || typeof(T) == typeof(WorkOrderLabor) || typeof(T) == typeof(WorkOrderRecurrenceVersion) || typeof(T) == typeof(WorkOrderMeterReading) || typeof(T) == typeof(WorkOrderRecurrenceChange) ? $" AND {Col("Content")} IS NULL" : "";
			if (await ExecuteAsync($"UPDATE {Tbl(Table<T>())} SET {string.Join(",", columns.Select(c => Col(c) + "=" + P + c))} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id{immutable}", row, default) != 1) throw new InvalidOperationException("Work-order evidence could not be saved.");
		}
	}
}
