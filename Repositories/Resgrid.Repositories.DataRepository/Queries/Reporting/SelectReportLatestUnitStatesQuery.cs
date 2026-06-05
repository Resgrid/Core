using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Reporting
{
	public class SelectReportLatestUnitStatesQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectReportLatestUnitStatesQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			// UnitState has no DepartmentId; scope is applied by joining Units (%UNITSTABLE%).
			return _sqlConfiguration.SelectReportLatestUnitStatesQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.UnitStatesTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%ALLDEPTS%", "%DID%" },
					new string[] { "AllDepts", "DepartmentId" },
					new string[] { "%UNITSTABLE%" },
					new string[] { _sqlConfiguration.UnitsTable });
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
