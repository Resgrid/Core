using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	public partial class CallsRepository
	{
		public async Task<bool> TryClaimCallForDispatchAsync(int callId, int departmentId, CancellationToken cancellationToken = default)
		{
			// The stored row must still be waiting, by the same rule as PendingCallsService.IsWaitingForDispatch: pending, or
			// active with a scheduled dispatch not yet sent. Only one caller sees the row change.
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var sql = postgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calls SET hasbeendispatched = true " +
				  "WHERE callid = @CallId AND departmentid = @DepartmentId AND isdeleted = false AND " +
				  "((state = 8 AND hasbeendispatched IS NOT TRUE) OR (state = 0 AND dispatchon IS NOT NULL AND hasbeendispatched = false))"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[Calls] SET [HasBeenDispatched] = 1 " +
				  "WHERE [CallId] = @CallId AND [DepartmentId] = @DepartmentId AND [IsDeleted] = 0 AND " +
				  "(([State] = 8 AND ([HasBeenDispatched] IS NULL OR [HasBeenDispatched] = 0)) OR ([State] = 0 AND [DispatchOn] IS NOT NULL AND [HasBeenDispatched] = 0))";

			return await ExecuteDispatchClaimAsync(sql, new { CallId = callId, DepartmentId = departmentId }, cancellationToken) == 1;
		}

		public async Task<bool> ReleaseCallDispatchClaimAsync(int callId, int departmentId, int state, DateTime? dispatchOn, bool? hasBeenDispatched,
			CancellationToken cancellationToken = default)
		{
			// Only a row still claimed and not moved on (closed, cancelled) by someone else is put back.
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var sql = postgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calls SET state = @State, dispatchon = @DispatchOn, hasbeendispatched = @HasBeenDispatched " +
				  "WHERE callid = @CallId AND departmentid = @DepartmentId AND isdeleted = false AND hasbeendispatched = true AND state IN (0, 8)"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[Calls] SET [State] = @State, [DispatchOn] = @DispatchOn, [HasBeenDispatched] = @HasBeenDispatched " +
				  "WHERE [CallId] = @CallId AND [DepartmentId] = @DepartmentId AND [IsDeleted] = 0 AND [HasBeenDispatched] = 1 AND [State] IN (0, 8)";

			var parameters = new
			{
				CallId = callId,
				DepartmentId = departmentId,
				State = state,
				DispatchOn = dispatchOn.HasValue && postgres ? DateTime.SpecifyKind(dispatchOn.Value, DateTimeKind.Unspecified) : dispatchOn,
				HasBeenDispatched = hasBeenDispatched
			};

			return await ExecuteDispatchClaimAsync(sql, parameters, cancellationToken) == 1;
		}

		private async Task<int> ExecuteDispatchClaimAsync(string sql, object parameters, CancellationToken cancellationToken)
		{
			if (_unitOfWork?.Connection != null)
				return await _unitOfWork.CreateOrGetConnection().ExecuteAsync(new CommandDefinition(sql, parameters, _unitOfWork.Transaction,
					cancellationToken: cancellationToken));

			await using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
		}
	}
}
