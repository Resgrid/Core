using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Departments
{
	public class SelectDepartmentByLinkCodeQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectDepartmentByLinkCodeQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectDepartmentByLinkCodeQuery
				.ReplaceQueryParameters(_sqlConfiguration.SchemaName,
					_sqlConfiguration.DepartmentsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%CODE%" },
					new string[] { "Code" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
