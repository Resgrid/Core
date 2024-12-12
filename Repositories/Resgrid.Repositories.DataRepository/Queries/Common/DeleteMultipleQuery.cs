using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Common
{
	public class DeleteMultipleQuery : IDeleteQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public DeleteMultipleQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
	        throw new System.NotImplementedException();
        }

        public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
        {
	        var query = _sqlConfiguration.DeleteMultipleQuery.
				ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
			        string.Empty,
			        _sqlConfiguration.ParameterNotation,
			        new string[] {
						"%PARENTID%"
			        },
			        new string[] {
						"ParentId"
			        },
			        new string[] {
				        "%BASETABLENAME%",
				        "%IDCOLUMN%"
			        },
			        new string[] {
				        ((IEntity)entity).TableName,
				        ((IEntity)entity).IdName
			        }
		        );

	        return query;
        }
    }
}
