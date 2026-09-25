using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public sealed class AdpAccessStore : IAdpAccessStore
	{
		private readonly IConnectionProvider _connections;
		private readonly string _table;
		public AdpAccessStore(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_table = configuration.SchemaName + (DataConfig.DatabaseType == DatabaseTypes.Postgres ? ".adpaccessstates" : ".[AdpAccessStates]");
		}
		public async Task<AdpAccessState> GetAsync(string id, CancellationToken cancellationToken = default)
		{
			using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<AdpAccessState>(new CommandDefinition(
				$"SELECT * FROM {_table} WHERE StateId=@id", new { id }, cancellationToken: cancellationToken));
		}
		public async Task<bool> SaveAsync(string id, string json, long expectedVersion, CancellationToken cancellationToken = default)
		{
			using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			// First creation can race; a unique-key failure is a failed CAS, never a replacement.
			if (expectedVersion == 0)
			{
				try
				{
					return await connection.ExecuteAsync(new CommandDefinition(
						$"INSERT INTO {_table}(StateId,Json,Version) VALUES(@id,@json,1)", new { id, json }, cancellationToken: cancellationToken)) == 1;
				}
				catch (System.Data.Common.DbException)
				{
					if (await GetAsync(id, cancellationToken) == null) throw;
					return false;
				}
			}
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET Json=@json, Version=Version+1 WHERE StateId=@id AND Version=@expectedVersion",
				new { id, json, expectedVersion }, cancellationToken: cancellationToken)) == 1;
		}
	}
}
