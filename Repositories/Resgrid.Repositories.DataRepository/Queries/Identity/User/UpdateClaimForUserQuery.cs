using System;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class UpdateClaimForUserQuery : IUpdateQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public UpdateClaimForUserQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery<TEntity>(TEntity entity)
        {
            var query = _sqlConfiguration.UpdateClaimForUserQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 _sqlConfiguration.UserClaimTable,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] {
                                                                                 "%NEWCLAIMTYPE%",
                                                                                 "%NEWCLAIMVALUE%",
                                                                                 "%USERID%",
                                                                                 "%CLAIMTYPE%",
                                                                                 "%CLAIMVALUE%"
                                                                              },
                                                                 new string[] {
                                                                                 "NewClaimType",
                                                                                 "NewClaimValue",
                                                                                 "UserId",
                                                                                 "ClaimType",
                                                                                 "ClaimValue"
                                                                              }
                                                                 );

            return query;
        }
    }
}
