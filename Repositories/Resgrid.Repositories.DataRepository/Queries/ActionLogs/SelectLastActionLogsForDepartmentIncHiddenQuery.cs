using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.ActionLogs
{
    public class SelectLastActionLogsForDepartmentIncHiddenQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public SelectLastActionLogsForDepartmentIncHiddenQuery(SqlConfiguration sqlConfiguration)
        {
            // Guarded here so every SqlConfiguration access in GetQuery is provably safe. A missing
            // configuration is a container misregistration, fail at construction rather than handing
            // back a query string that would reach the database malformed.
            _sqlConfiguration = sqlConfiguration ?? throw new ArgumentNullException(nameof(sqlConfiguration));
        }

        public string GetQuery()
        {
	        var queryTemplate = _sqlConfiguration.SelectLastActionLogsForDepartmentIncHiddenQuery;

	        if (string.IsNullOrWhiteSpace(queryTemplate))
		        throw new InvalidOperationException(
			        $"{nameof(SqlConfiguration.SelectLastActionLogsForDepartmentIncHiddenQuery)} is not set on {_sqlConfiguration.GetType().Name}.");

	        var query = queryTemplate
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
