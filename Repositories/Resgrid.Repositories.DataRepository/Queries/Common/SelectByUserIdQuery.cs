using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Common
{
    public class SelectByUserIdQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public SelectByUserIdQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
	        throw new System.NotImplementedException();
        }

        public string GetQuery<TEntity>() where TEntity : class, IEntity
        {
	        var entity = Activator.CreateInstance(typeof(TEntity));
	        var query = _sqlConfiguration.SelectByUserIdQuery
		        .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
			        ((IEntity)entity).TableName,
			        _sqlConfiguration.ParameterNotation,
			        new string[] { "%ID%" },
			        new string[] { "UserId" });

	        entity = null;
	        return query;
        }
    }
}
