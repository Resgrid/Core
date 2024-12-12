using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.Role
{
    public class InsertQuery : IInsertQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public InsertQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery<TEntity>(TEntity entity)
        {
            var columns = entity.GetColumns(_sqlConfiguration);

            var valuesArray = new List<string>(columns.Count());
            valuesArray = valuesArray.InsertQueryValuesFragment(_sqlConfiguration.ParameterNotation, columns);

            var query = _sqlConfiguration.InsertRoleQuery
                                         .ReplaceInsertQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                       _sqlConfiguration.RoleTable,
                                                                       _sqlConfiguration.InsertGetReturnIdCommand,
                                                                       columns.GetCommaSeparatedColumns(),
                                                                       string.Join(", ", valuesArray));

            return query;
        }
    }
}
