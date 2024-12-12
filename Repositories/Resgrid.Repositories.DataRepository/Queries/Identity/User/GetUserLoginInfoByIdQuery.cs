using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using System;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
    public class GetUserLoginInfoByIdQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public GetUserLoginInfoByIdQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.GetUserLoginInfoByIdQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 _sqlConfiguration.UserLoginTable,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] { "%ID%" },
                                                                 new string[] { "UserId" });

            return query;
        }

        public string GetQuery<TEntity>() where TEntity : class, IEntity
        {
            throw new NotImplementedException();
        }
    }
}
