using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class SelectUserByIdQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public SelectUserByIdQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.SelectUserByIdQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 string.Empty,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                new string[] {
                                                                                "%ID%"
                                                                              },
                                                                 new string[] {
                                                                                "Id",
                                                                              },
                                                                 new string[] {
                                                                                "%USERTABLE%",
                                                                                "%ROLETABLE%",
                                                                                "%USERROLETABLE%",
                                                                              },
                                                                 new string[] {
                                                                                _sqlConfiguration.UserTable,
                                                                                _sqlConfiguration.RoleTable,
                                                                                _sqlConfiguration.UserRoleTable,
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
