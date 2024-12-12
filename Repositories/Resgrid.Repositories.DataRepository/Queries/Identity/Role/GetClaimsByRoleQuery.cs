using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.Role
{
    public class GetClaimsByRoleQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public GetClaimsByRoleQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.SelectClaimByRoleQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 string.Empty,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] {
                                                                                  "%ROLEID%"
                                                                              },
                                                                 new string[] {
                                                                                  "RoleId"
                                                                              },
                                                                 new string[] {
                                                                                  "%ROLETABLE%",
                                                                                  "%ROLECLAIMTABLE%"
                                                                              },
                                                                 new string[] {
                                                                                  _sqlConfiguration.RoleTable,
                                                                                  _sqlConfiguration.RoleClaimTable
                                                                              }
                                                                );

            return query;
        }

        public string GetQuery<TEntity>() where TEntity : class, IEntity
        {
	        throw new NotImplementedException();
        }
    }
}
