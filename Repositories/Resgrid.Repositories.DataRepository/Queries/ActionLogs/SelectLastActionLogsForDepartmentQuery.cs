using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.ActionLogs
{
    public class SelectLastActionLogsForDepartmentQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public SelectLastActionLogsForDepartmentQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
	        var query = _sqlConfiguration.SelectLastActionLogsForDepartmentQuery
		        .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
			        string.Empty,
			        _sqlConfiguration.ParameterNotation,
			        new string[] {
				        "%DID%",
				        "%DAA%",
				        "%LTS%",
				        "%TS%"
			        },
			        new string[] {
				        "DepartmentId",
				        "DisableAutoAvailable",
				        "LatestTimestamp",
				        "Timestamp"
			        },
			        new string[] {
				        "%ACTIONLOGSTABLE%",
				        "%ASPNETUSERSTABLE%",
						"%DEPARTMENTMEMBERSTABLE%"
			        },
			        new string[] {
				        _sqlConfiguration.ActionLogsTable,
				        _sqlConfiguration.UserTable,
						_sqlConfiguration.DepartmentMembersTable
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
