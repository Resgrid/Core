using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Units
{
	public class SelectUnitsByGroupIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectUnitsByGroupIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectUnitsByGroupIdQuery
						.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
												 string.Empty,
												 _sqlConfiguration.ParameterNotation,
												new string[] {
																"%GROUPID%"
															  },
												 new string[] {
																"GroupId",
															  },
												 new string[] {
																"%UNITSTABLE%",
																"%DEPARTMENTGROUPSTABLE%"
												 },
												 new string[] {
																_sqlConfiguration.UnitsTable,
																_sqlConfiguration.DepartmentGroupsTable
												 }
						);

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
