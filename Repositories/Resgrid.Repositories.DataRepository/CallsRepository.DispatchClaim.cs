using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	public partial class CallsRepository
	{
		public async Task<bool> TryClaimCallForDispatchAsync(int callId, int departmentId, DateTime claimedOn, DateTime staleBefore,
			CancellationToken cancellationToken = default)
		{
			// The stored row must still be waiting, by the same rule as PendingCallsService.IsWaitingForDispatch: pending, or
			// active with a scheduled dispatch not yet sent, or marked sent by a claim whose lease ran out (its process died
			// mid-dispatch). Only one caller sees the row change.
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var sql = postgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calls SET hasbeendispatched = true, dispatchclaimedon = @ClaimedOn " +
				  "WHERE callid = @CallId AND departmentid = @DepartmentId AND isdeleted = false AND " +
				  "((state = 8 AND hasbeendispatched IS NOT TRUE) OR (state = 0 AND dispatchon IS NOT NULL AND hasbeendispatched = false) OR " +
				  "(state IN (0, 8) AND hasbeendispatched = true AND dispatchclaimedon < @StaleBefore))"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[Calls] SET [HasBeenDispatched] = 1, [DispatchClaimedOn] = @ClaimedOn " +
				  "WHERE [CallId] = @CallId AND [DepartmentId] = @DepartmentId AND [IsDeleted] = 0 AND " +
				  "(([State] = 8 AND ([HasBeenDispatched] IS NULL OR [HasBeenDispatched] = 0)) OR ([State] = 0 AND [DispatchOn] IS NOT NULL AND [HasBeenDispatched] = 0) OR " +
				  "([State] IN (0, 8) AND [HasBeenDispatched] = 1 AND [DispatchClaimedOn] < @StaleBefore))";

			var parameters = new DynamicParameters(new { CallId = callId, DepartmentId = departmentId });
			AddTimestamp(parameters, "ClaimedOn", claimedOn, postgres);
			AddTimestamp(parameters, "StaleBefore", staleBefore, postgres);

			return await ExecuteDispatchClaimAsync(sql, parameters, cancellationToken) == 1;
		}

		public async Task<bool> ReleaseCallDispatchClaimAsync(int callId, int departmentId, DateTime claimedOn, int state, DateTime? dispatchOn,
			bool? hasBeenDispatched, CancellationToken cancellationToken = default)
		{
			// Only this claim is given back: a row another sender has since claimed, or that was closed or cancelled meanwhile,
			// is left as it is.
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var sql = postgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calls SET state = @State, dispatchon = @DispatchOn, hasbeendispatched = @HasBeenDispatched, dispatchclaimedon = NULL " +
				  "WHERE callid = @CallId AND departmentid = @DepartmentId AND isdeleted = false AND hasbeendispatched = true AND state IN (0, 8) AND dispatchclaimedon = @ClaimedOn"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[Calls] SET [State] = @State, [DispatchOn] = @DispatchOn, [HasBeenDispatched] = @HasBeenDispatched, [DispatchClaimedOn] = NULL " +
				  "WHERE [CallId] = @CallId AND [DepartmentId] = @DepartmentId AND [IsDeleted] = 0 AND [HasBeenDispatched] = 1 AND [State] IN (0, 8) AND [DispatchClaimedOn] = @ClaimedOn";

			var parameters = new DynamicParameters(new { CallId = callId, DepartmentId = departmentId, State = state, HasBeenDispatched = hasBeenDispatched });
			AddTimestamp(parameters, "ClaimedOn", claimedOn, postgres);
			AddTimestamp(parameters, "DispatchOn", dispatchOn, postgres);

			return await ExecuteDispatchClaimAsync(sql, parameters, cancellationToken) == 1;
		}

		public async Task<bool> CompleteCallDispatchClaimAsync(int callId, int departmentId, DateTime claimedOn, CancellationToken cancellationToken = default)
		{
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var sql = postgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calls SET dispatchclaimedon = NULL WHERE callid = @CallId AND departmentid = @DepartmentId AND dispatchclaimedon = @ClaimedOn"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[Calls] SET [DispatchClaimedOn] = NULL WHERE [CallId] = @CallId AND [DepartmentId] = @DepartmentId AND [DispatchClaimedOn] = @ClaimedOn";

			var parameters = new DynamicParameters(new { CallId = callId, DepartmentId = departmentId });
			AddTimestamp(parameters, "ClaimedOn", claimedOn, postgres);

			return await ExecuteDispatchClaimAsync(sql, parameters, cancellationToken) == 1;
		}

		/// <summary>
		/// The claim time identifies a claim, so it must read back exactly as written. SQL Server gets datetime2, not Dapper's
		/// default datetime: compared with the datetime2 column, a datetime value is widened past the precision the column
		/// stored (compatibility level 130 and up) and never matches. Postgres stores timestamp without time zone, which takes
		/// an unspecified-kind value.
		/// </summary>
		private static void AddTimestamp(DynamicParameters parameters, string name, DateTime? value, bool postgres)
		{
			if (postgres)
				parameters.Add(name, value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : (DateTime?)null);
			else
				parameters.Add(name, value, DbType.DateTime2);
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
