using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Reporting
{
	public class SelectReportDepartmentsTotalQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectReportDepartmentsTotalQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			return _sqlConfiguration.SelectReportDepartmentsTotalQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.DepartmentsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { },
					new string[] { });
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
