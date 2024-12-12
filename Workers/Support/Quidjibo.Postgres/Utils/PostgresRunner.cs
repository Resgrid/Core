using Npgsql;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Quidjibo.Postgres.Utils
{
    public static class PostgresRunner
    {
        public static Task ExecuteAsync(Func<NpgsqlCommand, Task> func, string connectionString, CancellationToken cancellationToken)
        {
            return ExecuteAsync(func, connectionString, true, cancellationToken);
        }

        public static async Task ExecuteAsync(Func<NpgsqlCommand, Task> func, string connectionString, bool inTransaction, CancellationToken cancellationToken)
        {
            using (var conn = new NpgsqlConnection(connectionString))
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
                                await tran.CommitAsync();
                            }
                            catch (Exception ex)
                            {
                                await tran.RollbackAsync();
                                throw;
                            }
                        }

                        return;
                    }

                    await func(cmd);
                }
            }
        }


        public static T Map<T>(this NpgsqlDataReader rdr, string name)
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
