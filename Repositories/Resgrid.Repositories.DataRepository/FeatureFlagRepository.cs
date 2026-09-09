using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class FeatureFlagRepository : RepositoryBase<FeatureFlag>, IFeatureFlagRepository
	{
		private readonly IConnectionProvider _connections;
		private readonly IUnitOfWork _unit;
		public FeatureFlagRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connections = connectionProvider; _unit = unitOfWork;
		}
		public async System.Threading.Tasks.Task TouchEvaluationAsync(int flagId, System.DateTime evaluatedOn, System.Threading.CancellationToken ct = default)
		{
			// Never re-save a cached definition for telemetry: that could restore an old flag state.
			const string sql = "UPDATE FeatureFlags SET LastEvaluatedOn=@evaluatedOn WHERE FeatureFlagId=@flagId AND (LastEvaluatedOn IS NULL OR LastEvaluatedOn<@evaluatedOn)";
			if (_unit.Connection != null)
				await Dapper.SqlMapper.ExecuteAsync(_unit.Connection, new Dapper.CommandDefinition(sql, new { flagId, evaluatedOn }, _unit.Transaction, cancellationToken: ct));
			else
			{
				using var connection = _connections.Create(); await connection.OpenAsync(ct);
				await Dapper.SqlMapper.ExecuteAsync(connection, new Dapper.CommandDefinition(sql, new { flagId, evaluatedOn }, cancellationToken: ct));
			}
		}
	}
}
