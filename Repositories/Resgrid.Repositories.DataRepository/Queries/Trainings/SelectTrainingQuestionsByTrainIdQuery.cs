using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Trainings
{
	public class SelectTrainingQuestionsByTrainIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectTrainingQuestionsByTrainIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectTrainingQuestionsByTrainIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%TRAININGID%"
					},
					new string[] {
						"TrainingId"
					},
					new string[] {
						"%TRAININGQUESTIONSTABLE%",
						"%TRAININGQUESTIONANSWERSTABLE%"
					},
					new string[] {
						_sqlConfiguration.TrainingQuestionsTable,
						_sqlConfiguration.TrainingQuestionAnswersTable
					}
				);

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
