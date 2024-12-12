﻿using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using System;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class GetUsersByClaimQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public GetUsersByClaimQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            throw new NotImplementedException();
        }

        public string GetQuery<TEntity>() where TEntity : class, IEntity
        {
	        var entity = Activator.CreateInstance(typeof(TEntity));
            var userProperties = entity.GetColumns(_sqlConfiguration, ignoreIdProperty: true, ignoreProperties: new string[] { "ConcurrencyStamp" });

            var query = _sqlConfiguration.GetUsersByClaimQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 _sqlConfiguration.UserTable,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] {
                                                                                        "%CLAIMVALUE%",
                                                                                        "%CLAIMTYPE%"
                                                                              },
                                                                 new string[] {
                                                                                        "ClaimValue",
                                                                                        "ClaimType"
                                                                              },
                                                                 new string[] {
                                                                                        "%USERFILTER%",
                                                                                        "%USERTABLE%",
                                                                                        "%USERCLAIMTABLE%",
                                                                              },
                                                                 new string[] {
                                                                                        userProperties.SelectFilterWithTableName(_sqlConfiguration.UserTable),
                                                                                        _sqlConfiguration.UserTable,
                                                                                        _sqlConfiguration.UserClaimTable,
                                                                              }
                                                                 );

            return query;
        }
    }
}
