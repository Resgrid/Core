using Npgsql;
using System;

namespace Quidjibo.Postgres.Extensions
{
    public static class NpgsqlParameterCollectionExtensions
    {
        public static NpgsqlParameter AddParameter(this NpgsqlCommand cmd, string name, object value)
        {
            return cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
