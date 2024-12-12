﻿using System;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using System;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class IsInRoleQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public IsInRoleQuery(SqlConfiguration sqlConfiguration)
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

            var query = _sqlConfiguration.IsInRoleQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 _sqlConfiguration.UserTable,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] {
                                                                                        "%ROLENAME%",
                                                                                        "%USERID%"
                                                                              },
                                                                 new string[] {
                                                                                        "RoleName",
                                                                                        "UserId"
                                                                              },
                                                                 new string[] {
                                                                                        "%USERTABLE%",
                                                                                        "%USERROLETABLE%",
                                                                                        "%ROLETABLE%"
                                                                              },
                                                                 new string[] {
                                                                                        _sqlConfiguration.UserTable,
                                                                                        _sqlConfiguration.UserRoleTable,
                                                                                        _sqlConfiguration.RoleTable
                                                                              }
                                                                 );

            return query;
        }
    }
}
