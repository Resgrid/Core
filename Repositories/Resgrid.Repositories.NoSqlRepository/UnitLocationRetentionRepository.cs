using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model.Repositories;

namespace Resgrid.Repositories.NoSqlRepository
{
	public sealed class UnitLocationRetentionRepository :
		IUnitLocationRetentionRepository
	{
		private readonly Lazy<IUnitLocationsDocRepository>
			_postgresRepository;
		private readonly Lazy<IUnitLocationsMongoRepository>
			_mongoRepository;

		public UnitLocationRetentionRepository(
			Lazy<IUnitLocationsDocRepository> postgresRepository,
			Lazy<IUnitLocationsMongoRepository> mongoRepository)
		{
			_postgresRepository =
				postgresRepository ??
				throw new ArgumentNullException(
					nameof(postgresRepository));
			_mongoRepository =
				mongoRepository ??
				throw new ArgumentNullException(
					nameof(mongoRepository));
		}

		public Task<int> DeleteHardwareLocationsBeforeAsync(
			int departmentId,
			DateTime cutoffUtc,
			int batchSize,
			CancellationToken cancellationToken = default)
		{
			if (DataConfig.DocDatabaseType ==
			    DatabaseTypes.Postgres)
			{
				return _postgresRepository.Value
					.DeleteHardwareLocationsBeforeAsync(
						departmentId,
						cutoffUtc,
						batchSize,
						cancellationToken);
			}

			if (DataConfig.DocDatabaseType ==
			    DatabaseTypes.MongoDb)
			{
				return _mongoRepository.Value
					.DeleteHardwareLocationsBeforeAsync(
						departmentId,
						cutoffUtc,
						batchSize,
						cancellationToken);
			}

			throw new InvalidOperationException(
				$"Document database type '{DataConfig.DocDatabaseType}' does not support unit-location retention.");
		}
	}
}
