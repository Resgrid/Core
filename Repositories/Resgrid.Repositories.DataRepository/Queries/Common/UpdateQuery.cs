using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using System.Collections.Generic;

namespace Resgrid.Repositories.DataRepository.Queries.Common
{
    public class UpdateQuery : IUpdateQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public UpdateQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }
    
        public string GetQuery<TEntity>(TEntity entity)
        {

			List<string> ignoredProperties = new List<string>(((IEntity)entity).IgnoredProperties);
			ignoredProperties.Add(((IEntity)entity).IdName);

			var columns = entity.GetColumns(_sqlConfiguration, ignoreProperties: ignoredProperties);
            var roleProperties = entity.GetColumns(_sqlConfiguration, ignoreIdProperty: true, ignoreProperties: ignoredProperties);

            var setFragment = roleProperties.UpdateQuerySetFragment(_sqlConfiguration.ParameterNotation);
			var tableName = ((IEntity)entity).TableName;
			var idName = ((IEntity)entity).IdName;

            var query = _sqlConfiguration.UpdateQuery
                                         .ReplaceUpdateQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
	                                         tableName,
	                                         setFragment,
	                                         idName,
	                                         $"{_sqlConfiguration.ParameterNotation}{idName}");

            return query;
        }
    }
}
