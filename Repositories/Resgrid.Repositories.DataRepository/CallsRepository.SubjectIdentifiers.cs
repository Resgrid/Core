using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	public partial class CallsRepository
	{
		public async Task<bool> TryUpdateSubjectIdentifiersAsync(int callId, int departmentId, string expectedValue, string newValue,
			CancellationToken cancellationToken = default)
		{
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var sql = postgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calls SET subjectidentifiers = @NewValue " +
				  "WHERE callid = @CallId AND departmentid = @DepartmentId AND subjectidentifiers IS NOT DISTINCT FROM @ExpectedValue::citext"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[Calls] SET [SubjectIdentifiers] = @NewValue " +
				  "WHERE [CallId] = @CallId AND [DepartmentId] = @DepartmentId AND " +
				  "(([SubjectIdentifiers] IS NULL AND @ExpectedValue IS NULL) OR [SubjectIdentifiers] = @ExpectedValue COLLATE Latin1_General_BIN2)";

			var parameters = new { CallId = callId, DepartmentId = departmentId, ExpectedValue = expectedValue, NewValue = newValue };

			if (_unitOfWork?.Connection != null)
				return await _unitOfWork.CreateOrGetConnection().ExecuteAsync(new CommandDefinition(sql, parameters, _unitOfWork.Transaction,
					cancellationToken: cancellationToken)) == 1;

			await using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)) == 1;
		}
	}
}
