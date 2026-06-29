using System;
using Microsoft.Data.SqlClient;

namespace Quidjibo.SqlServer.Extensions
{
    public static class SqlParameterCollectionExtensions
    {
        public static SqlParameter AddParameter(this SqlCommand cmd, string name, object value)
        {
            return cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}