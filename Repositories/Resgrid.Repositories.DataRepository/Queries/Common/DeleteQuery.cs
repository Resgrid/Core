using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Common
{
	public class DeleteQuery : IDeleteQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public DeleteQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
	        throw new System.NotImplementedException();
        }

        public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
        {
	        var query = _sqlConfiguration.DeleteQuery
		        .ReplaceDeleteQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					((IEntity)entity).TableName,
			        ((IEntity)entity).IdName,
			        $"{_sqlConfiguration.ParameterNotation}Id");

	        return query;
        }
    }
}
