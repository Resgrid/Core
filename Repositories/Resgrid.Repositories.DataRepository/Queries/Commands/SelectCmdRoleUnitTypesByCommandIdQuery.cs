using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Commands
{
	public class SelectCmdRoleUnitTypesByCommandIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectCmdRoleUnitTypesByCommandIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectCmdRoleUnitTypesByCommandIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CommandDefinitionRoleUnitTypesTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%COMMANDDEFINITIONID%" },
					new string[] { "CommandDefinitionId" },
					new string[] { "%ROLESTABLE%" },
					new string[] { _sqlConfiguration.CommandDefinitionRolesTable });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
