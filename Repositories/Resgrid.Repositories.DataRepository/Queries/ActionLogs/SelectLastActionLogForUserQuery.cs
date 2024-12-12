using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.ActionLogs
{
    public class SelectLastActionLogForUserQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public SelectLastActionLogForUserQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
	        var query = _sqlConfiguration.SelectLastActionLogForUserQuery
		        .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
			        string.Empty,
			        _sqlConfiguration.ParameterNotation,
			        new string[] {
				        "%USERID%",
				        "%DAA%",
				        "%LTS%",
				        "%TS%"
			        },
			        new string[] {
				        "UserId",
				        "DisableAutoAvailable",
				        "LatestTimestamp",
				        "Timestamp"
			        },
			        new string[] {
				        "%ACTIONLOGSTABLE%",
				        "%ASPNETUSERSTABLE%"
			        },
			        new string[] {
				        _sqlConfiguration.ActionLogsTable,
				        _sqlConfiguration.UserTable
			        }
		        );

	        return query;
        }

        public string GetQuery<TEntity>() where TEntity : class, IEntity
        {
	        throw new System.NotImplementedException();
        }
    }
}
