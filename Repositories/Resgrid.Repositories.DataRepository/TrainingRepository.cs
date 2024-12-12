using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Trainings;

namespace Resgrid.Repositories.DataRepository
{
	public class TrainingRepository : RepositoryBase<Training>, ITrainingRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public TrainingRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public List<Training> GetAllTrainings()
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	return db.Query<Training>($"SELECT * FROM Trainings").ToList();
			//}

			return null;
		}

		public async Task<IEnumerable<Training>> GetTrainingsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Training>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectTrainingsByDIdQuery>();

					var messageDictionary = new Dictionary<int, Training>();
					var result = await x.QueryAsync<Training, TrainingUser, Training>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: TrainingUsersMapping(messageDictionary),
						splitOn: "TrainingUserId");

					if (messageDictionary.Count > 0)
						return messageDictionary.Select(y => y.Value);

					return result;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<Training> GetTrainingByTrainingIdAsync(int trainingId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Training>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("TrainingId", trainingId);

					var query = _queryFactory.GetQuery<SelectTrainingByIdQuery>();

					var messageDictionary = new Dictionary<int, Training>();
					var result = await x.QueryAsync<Training, TrainingUser, Training>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: TrainingUsersMapping(messageDictionary),
						splitOn: "TrainingUserId");

					if (messageDictionary.Count > 0)
						return messageDictionary.Select(y => y.Value).FirstOrDefault();

					return result.FirstOrDefault();
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		private static Func<Training, TrainingUser, Training> TrainingUsersMapping(Dictionary<int, Training> dictionary)
		{
			return new Func<Training, TrainingUser, Training>((training, trainingUser) =>
			{
				var dictionaryTraining = default(Training);

				if (trainingUser != null)
				{
					if (dictionary.TryGetValue(training.TrainingId, out dictionaryTraining))
					{
						if (dictionaryTraining.Users.All(x => x.TrainingUserId != trainingUser.TrainingUserId))
							dictionaryTraining.Users.Add(trainingUser);
					}
					else
					{
						if (training.Users == null)
							training.Users = new List<TrainingUser>();

						training.Users.Add(trainingUser);
						dictionary.Add(training.TrainingId, training);

						dictionaryTraining = training;
					}
				}
				else
				{
					training.Users = new List<TrainingUser>();
					dictionaryTraining = training;
					dictionary.Add(training.TrainingId, training);
				}

				return dictionaryTraining;
			});
		}
	}
}
