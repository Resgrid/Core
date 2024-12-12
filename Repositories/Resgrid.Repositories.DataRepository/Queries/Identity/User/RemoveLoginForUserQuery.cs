using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class RemoveLoginForUserQuery : IDeleteQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public RemoveLoginForUserQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.RemoveLoginForUserQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 _sqlConfiguration.UserLoginTable,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] {
                                                                                "%USERID%",
                                                                                "%LOGINPROVIDER%",
                                                                                "%PROVIDERKEY%"
                                                                              },
                                                                 new string[] {
                                                                                "UserId",
                                                                                "LoginProvider",
                                                                                "ProviderKey"
                                                                              }
                                                                 );

            return query;
        }

        public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
        {
	        throw new System.NotImplementedException();
        }
    }
}
