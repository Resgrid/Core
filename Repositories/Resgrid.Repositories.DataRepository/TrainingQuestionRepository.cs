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
	public class TrainingQuestionRepository : RepositoryBase<TrainingQuestion>, ITrainingQuestionRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public TrainingQuestionRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<TrainingQuestion>> GetTrainingQuestionsByTrainingIdAsync(int trainingId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<TrainingQuestion>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("TrainingId", trainingId);

					var query = _queryFactory.GetQuery<SelectTrainingQuestionsByTrainIdQuery>();

					var messageDictionary = new Dictionary<int, TrainingQuestion>();
					var result = await x.QueryAsync<TrainingQuestion, TrainingQuestionAnswer, TrainingQuestion>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: TrainingQuestionAnswerMapping(messageDictionary),
						splitOn: "TrainingQuestionAnswerId");

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

		private static Func<TrainingQuestion, TrainingQuestionAnswer, TrainingQuestion> TrainingQuestionAnswerMapping(Dictionary<int, TrainingQuestion> dictionary)
		{
			return new Func<TrainingQuestion, TrainingQuestionAnswer, TrainingQuestion>((trainingQuestion, trainingQuestionAnswer) =>
			{
				var dictionaryTrainingQuestion = default(TrainingQuestion);

				if (trainingQuestionAnswer != null)
				{
					if (dictionary.TryGetValue(trainingQuestion.TrainingQuestionId, out dictionaryTrainingQuestion))
					{
						if (dictionaryTrainingQuestion.Answers.All(x => x.TrainingQuestionAnswerId != trainingQuestionAnswer.TrainingQuestionAnswerId))
							dictionaryTrainingQuestion.Answers.Add(trainingQuestionAnswer);
					}
					else
					{
						if (trainingQuestion.Answers == null)
							trainingQuestion.Answers = new List<TrainingQuestionAnswer>();

						trainingQuestion.Answers.Add(trainingQuestionAnswer);
						dictionary.Add(trainingQuestion.TrainingQuestionId, trainingQuestion);

						dictionaryTrainingQuestion = trainingQuestion;
					}
				}
				else
				{
					trainingQuestion.Answers = new List<TrainingQuestionAnswer>();
					dictionaryTrainingQuestion = trainingQuestion;
					dictionary.Add(trainingQuestion.TrainingQuestionId, trainingQuestion);
				}

				return dictionaryTrainingQuestion;
			});
		}
	}
}
