using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class RemoveClaimsQuery : IDeleteQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public RemoveClaimsQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.RemoveClaimsQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 _sqlConfiguration.UserClaimTable,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] {
                                                                                 "%ID%",
                                                                                 "%CLAIMVALUE%",
                                                                                 "%CLAIMTYPE%"
                                                                              },
                                                                 new string[] {
                                                                                 "UserId",
                                                                                 "ClaimValue",
                                                                                 "ClaimType"
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
