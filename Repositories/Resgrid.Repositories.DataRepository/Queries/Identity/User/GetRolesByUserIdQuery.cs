using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using System;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class GetRolesByUserIdQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public GetRolesByUserIdQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.GetRolesByUserIdQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 string.Empty,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] {
                                                                                  "%ID%"
                                                                              },
                                                                 new string[] {
                                                                                  "UserId"
                                                                              },
                                                                 new string[] {
                                                                                  "%ROLETABLE%",
                                                                                  "%USERROLETABLE%"
                                                                              },
                                                                 new string[] {
                                                                                  _sqlConfiguration.RoleTable,
                                                                                  _sqlConfiguration.UserRoleTable
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
