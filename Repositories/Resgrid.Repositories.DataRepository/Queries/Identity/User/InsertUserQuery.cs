using System;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class InsertUserQuery : IInsertQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public InsertUserQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery<TEntity>(TEntity entity)
        {
            var columns = entity.GetColumns(_sqlConfiguration, ignoreProperties: new string[] { "ConcurrencyStamp", "SecurityQuestion", "SecurityAnswer", "SecurityAnswerSalt", "CreateDate", "UserId" });


			var valuesArray = new List<string>(columns.Count());
            valuesArray = valuesArray.InsertQueryValuesFragment(_sqlConfiguration.ParameterNotation, columns);

            var query = _sqlConfiguration.InsertUserQuery
                                         .ReplaceInsertQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                       _sqlConfiguration.UserTable,
                                                                       _sqlConfiguration.InsertGetReturnIdCommand,
                                                                       columns.GetCommaSeparatedColumns(),
                                                                       string.Join(", ", valuesArray));

            return query;
        }
    }
}
