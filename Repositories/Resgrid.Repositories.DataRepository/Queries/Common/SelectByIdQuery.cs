using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Common
{
    public class SelectByIdQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public SelectByIdQuery(SqlConfiguration sqlConfiguration)
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


	        var query = _sqlConfiguration.SelectByIdQuery
		        .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
			        string.Empty,
			        _sqlConfiguration.ParameterNotation,
			        new string[] {
				        "%ID%"
			        },
			        new string[] {
				        "Id",
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

	        entity = null;
	        return query;
        }
    }
}
