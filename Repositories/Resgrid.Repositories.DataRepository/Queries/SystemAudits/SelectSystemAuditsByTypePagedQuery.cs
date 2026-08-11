using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.SystemAudits
{
	public class SelectSystemAuditsByTypePagedQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectSystemAuditsByTypePagedQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.systemaudits WHERE type = {_sqlConfiguration.ParameterNotation}Type AND loggedon >= {_sqlConfiguration.ParameterNotation}StartDate AND loggedon < {_sqlConfiguration.ParameterNotation}EndDate ORDER BY loggedon DESC, systemauditid DESC LIMIT {_sqlConfiguration.ParameterNotation}PageSize OFFSET {_sqlConfiguration.ParameterNotation}Offset";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[SystemAudits] WHERE [Type] = {_sqlConfiguration.ParameterNotation}Type AND [LoggedOn] >= {_sqlConfiguration.ParameterNotation}StartDate AND [LoggedOn] < {_sqlConfiguration.ParameterNotation}EndDate ORDER BY [LoggedOn] DESC, [SystemAuditId] DESC OFFSET {_sqlConfiguration.ParameterNotation}Offset ROWS FETCH NEXT {_sqlConfiguration.ParameterNotation}PageSize ROWS ONLY";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}
