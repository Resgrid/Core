using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Quidjibo.SqlServer.Utils
{
    public static class SqlRunner
    {
        public static Task ExecuteAsync(Func<SqlCommand, Task> func, string connectionString, CancellationToken cancellationToken)
        {
            return ExecuteAsync(func, connectionString, true, cancellationToken);
        }

        public static async Task ExecuteAsync(Func<SqlCommand, Task> func, string connectionString, bool inTransaction, CancellationToken cancellationToken)
        {
            using (var conn = new SqlConnection(connectionString))
            using (var cmd = conn.CreateCommand())
            {
                {
                    await conn.OpenAsync(cancellationToken);
                    if (inTransaction)
                    {
                        using (var tran = conn.BeginTransaction())
                        {
                            try
                            {
                                cmd.Transaction = tran;
                                await func(cmd);
                                tran.Commit();
                            }
                            catch
                            {
                                tran.Rollback();
                                throw;
                            }
                        }

                        return;
                    }

                    await func(cmd);
                }
            }
        }


        public static T Map<T>(this SqlDataReader rdr, string name)
        {
            var val = rdr[name];
            if (val == DBNull.Value)
            {
                return default(T);
            }

            return (T)val;
        }
    }
}