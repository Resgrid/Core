using System;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class InsertUserLoginQuery : IInsertQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public InsertUserLoginQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery<TEntity>(TEntity entity)
        {
            var columns = entity.GetColumns(_sqlConfiguration);

            var valuesArray = new List<string>(columns.Count());
            valuesArray = valuesArray.InsertQueryValuesFragment(_sqlConfiguration.ParameterNotation, columns);

            var query = _sqlConfiguration.InsertUserLoginQuery
                                         .ReplaceInsertQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                       _sqlConfiguration.UserLoginTable,
                                                                       _sqlConfiguration.InsertGetReturnIdCommand,
                                                                       columns.GetCommaSeparatedColumns(),
                                                                       string.Join(", ", valuesArray));

            return query;
        }
    }
}
