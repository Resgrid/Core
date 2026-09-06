using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class RmsCommandReceiptsRepository : RmsRepositoryBase<RmsRevision>, IRmsCommandReceiptsRepository
	{
		public RmsCommandReceiptsRepository(IConnectionProvider connections, SqlConfiguration configuration, IUnitOfWork unitOfWork, IQueryFactory queries)
			: base(connections, configuration, unitOfWork, queries) { }

		public Task<RecordCommandReceipt> GetAsync(int departmentId, string keyHash) => QueryFirstOrDefaultAsync<RecordCommandReceipt>(
			$"SELECT {Cols("RecordId", "RequestChecksum")}, CASE WHEN {Col("CompletedOn")} IS NULL THEN 1 ELSE 0 END AS {Col("IsPending")} FROM {Tbl("RmsCommandReceipts")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("KeyHash")}={P}KeyHash",
			new { DepartmentId = departmentId, KeyHash = keyHash });

		public async Task<bool> ReserveAsync(int departmentId, string keyHash, string recordId, string requestChecksum, string reservationId)
		{
			if (UnitOfWork.Transaction != null) throw new InvalidOperationException("A command reservation must commit before executing the command.");
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await LockLiveContentParentAsync(departmentId, recordId, CancellationToken.None);
				if (await GetAsync(departmentId, keyHash) != null) { UnitOfWork.CommitChanges(); return false; }
				await ExecuteAsync($"INSERT INTO {Tbl("RmsCommandReceipts")} ({Cols("DepartmentId", "KeyHash", "RecordId", "RequestChecksum", "ReservationId", "CreatedOn")}) VALUES ({P}DepartmentId,{P}KeyHash,{P}RecordId,{P}RequestChecksum,{P}ReservationId,{UtcNowSql})",
					new { DepartmentId = departmentId, KeyHash = keyHash, RecordId = recordId, RequestChecksum = requestChecksum, ReservationId = reservationId });
				UnitOfWork.CommitChanges(); return true;
			}
			catch { UnitOfWork.DiscardChanges(); throw; }
		}

		public async Task<bool> CompleteAsync(int departmentId, string keyHash, string recordId, string requestChecksum, string reservationId)
		{
			if (UnitOfWork.Transaction != null) throw new InvalidOperationException("A command receipt cannot complete before the command transaction commits.");
			return await ExecuteAsync($"UPDATE {Tbl("RmsCommandReceipts")} SET {Col("CompletedOn")}={UtcNowSql} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("KeyHash")}={P}KeyHash AND {Col("RecordId")}={P}RecordId AND {Col("RequestChecksum")}={P}RequestChecksum AND {Col("ReservationId")}={P}ReservationId AND {Col("CompletedOn")} IS NULL",
				new { DepartmentId = departmentId, KeyHash = keyHash, RecordId = recordId, RequestChecksum = requestChecksum, ReservationId = reservationId }) == 1;
		}
	}
}
