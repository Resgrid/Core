using System;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class RemoveUserFromRoleQuery : IDeleteQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public RemoveUserFromRoleQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.RemoveUserFromRoleQuery
                                         .ReplaceQueryParameters(_sqlConfiguration.SchemaName,
                                                                 _sqlConfiguration.UserRoleTable,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] {
                                                                                "%USERID%",
                                                                                "%ROLENAME%"
                                                                              },
                                                                 new string[] {
                                                                                "UserId",
                                                                                "RoleName"
                                                                              },
                                                                 new string[] {
                                                                                "%USERROLETABLE%",
                                                                                "%ROLETABLE%"
                                                                              },
                                                                 new string[] {
                                                                                _sqlConfiguration.UserRoleTable,
                                                                                _sqlConfiguration.RoleTable
                                                                              }
                                                                 );

            return query;
        }
    }
}
