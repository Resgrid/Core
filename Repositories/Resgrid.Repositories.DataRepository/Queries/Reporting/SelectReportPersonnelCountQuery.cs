using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Reporting
{
	public class SelectReportPersonnelCountQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectReportPersonnelCountQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			return _sqlConfiguration.SelectReportPersonnelCountQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.DepartmentMembersTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%ALLDEPTS%", "%DID%" },
					new string[] { "AllDepts", "DepartmentId" });
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
