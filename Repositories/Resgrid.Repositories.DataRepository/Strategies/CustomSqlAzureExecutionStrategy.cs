using System;
using System.Data.Entity.SqlServer;
using System.Data.SqlClient;

namespace Resgrid.Repositories.DataRepository.Strategies
{
	public class CustomSqlAzureExecutionStrategy: SqlAzureExecutionStrategy
	{
		protected override bool ShouldRetryOn(Exception exception)
		{
			bool shouldRetry = false;

			SqlException sqlException = exception as SqlException;
			if (sqlException != null)
			{
				foreach (SqlError error in sqlException.Errors)
				{
					if (error.Number == -2)
						shouldRetry = true;
				}
			}

			shouldRetry = shouldRetry || base.ShouldRetryOn(exception);

			return shouldRetry;
		}
	}
}