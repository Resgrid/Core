using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
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
	public class TrainingRepository : RmsRepositoryBase<Training>, ITrainingRepository
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

		private sealed class QuestionCountRow
		{
			public int TrainingId { get; set; }
			public int QuestionCount { get; set; }
		}

		private static string True => IsPostgres ? "true" : "1";

		private static string False => IsPostgres ? "false" : "0";

		public async Task<Dictionary<int, int>> GetQuestionCountsForDepartmentAsync(int departmentId)
		{
			var rows = await QueryAsync<QuestionCountRow>(
				$"SELECT q.{Col("TrainingId")} AS TrainingId, CAST(COUNT(*) AS INT) AS QuestionCount FROM {Tbl("TrainingQuestions")} q " +
				$"JOIN {Tbl("Trainings")} t ON t.{Col("TrainingId")} = q.{Col("TrainingId")} WHERE t.{Col("DepartmentId")} = {P}DepartmentId GROUP BY q.{Col("TrainingId")}",
				new { DepartmentId = departmentId });

			return rows.ToDictionary(r => r.TrainingId, r => r.QuestionCount);
		}

		public async Task<List<int>> GetTrainingIdsWithNoticeWorkAsync(DateTime utcNow, int maxLeadDays)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("Horizon", DatabaseTimestamp(utcNow.AddDays(Math.Max(1, maxLeadDays) + 1)), System.Data.DbType.DateTime2);
			parameters.Add("Yesterday", DatabaseTimestamp(utcNow.Date.AddDays(-1)), System.Data.DbType.DateTime2);

			// Open assignments that were never announced, or whose due moment is inside the widest lead (or past, when the
			// training sends overdue notices). Rows from before T0 have no DueOn and fall back to the training's local date.
			var sql = $@"SELECT DISTINCT t.{Col("TrainingId")} FROM {Tbl("Trainings")} t
JOIN {Tbl("TrainingUsers")} tu ON tu.{Col("TrainingId")} = t.{Col("TrainingId")}
WHERE t.{Col("ArchivedOn")} IS NULL AND (t.{Col("StartsOn")} IS NULL OR t.{Col("StartsOn")} <= {P}Now)
AND tu.{Col("Complete")} = {False} AND tu.{Col("RemovedOn")} IS NULL
AND (tu.{Col("ExemptOn")} IS NULL OR (tu.{Col("ExemptUntil")} IS NOT NULL AND tu.{Col("ExemptUntil")} <= {P}Now))
AND (tu.{Col("NotifiedOn")} IS NULL
	OR (tu.{Col("DueOn")} IS NOT NULL AND tu.{Col("DueOn")} <= {P}Horizon AND (tu.{Col("DueOn")} >= {P}Now OR t.{Col("SendOverdueNotices")} = {True}))
	OR (tu.{Col("DueOn")} IS NULL AND t.{Col("ToBeCompletedBy")} IS NOT NULL AND t.{Col("ToBeCompletedBy")} <= {P}Horizon AND (t.{Col("ToBeCompletedBy")} >= {P}Yesterday OR t.{Col("SendOverdueNotices")} = {True})))";

			return (await QueryAsync<int>(sql, parameters)).ToList();
		}

		public async Task<List<Training>> GetTrainingsDueForRepeatAsync(DateTime utcNow, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("Take", Math.Max(1, take));
			var where = $"{Col("ArchivedOn")} IS NULL AND {Col("RepeatMonths")} > 0 AND {Col("NextRepeatOn")} IS NOT NULL AND {Col("NextRepeatOn")} <= {P}Now ORDER BY {Col("NextRepeatOn")}";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("Trainings")} WHERE {where} LIMIT {P}Take"
				: $"SELECT TOP ({P}Take) * FROM {Tbl("Trainings")} WHERE {where}";
			return (await QueryAsync<Training>(sql, parameters)).ToList();
		}

		public async Task<bool> SetArchivedAsync(int departmentId, int trainingId, DateTime? archivedOn, string userId, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("TrainingId", trainingId);
			parameters.Add("ArchivedOn", archivedOn.HasValue ? DatabaseTimestamp(archivedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("UserId", archivedOn.HasValue ? userId : null, System.Data.DbType.String);
			parameters.Add("UpdatedOn", DatabaseTimestamp(DateTime.UtcNow), System.Data.DbType.DateTime2);
			parameters.Add("UpdatedBy", userId, System.Data.DbType.String);
			return await ExecuteAsync($"UPDATE {Tbl("Trainings")} SET {Col("ArchivedOn")} = {P}ArchivedOn, {Col("ArchivedByUserId")} = {P}UserId, {Col("UpdatedOn")} = {P}UpdatedOn, {Col("UpdatedByUserId")} = {P}UpdatedBy " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId", parameters, cancellationToken) == 1;
		}

		public async Task<bool> SetNextRepeatOnAsync(int trainingId, DateTime? nextRepeatOn, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("TrainingId", trainingId);
			parameters.Add("NextRepeatOn", nextRepeatOn.HasValue ? DatabaseTimestamp(nextRepeatOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			return await ExecuteAsync($"UPDATE {Tbl("Trainings")} SET {Col("NextRepeatOn")} = {P}NextRepeatOn WHERE {Col("TrainingId")} = {P}TrainingId", parameters, cancellationToken) == 1;
		}

		public Task<int> CountCompletionsAsync(int trainingId)
		{
			return ScalarAsync<int>(
				$"SELECT CAST((SELECT COUNT(*) FROM {Tbl("TrainingUsers")} WHERE {Col("TrainingId")} = {P}TrainingId AND {Col("Complete")} = {True}) + " +
				$"(SELECT COUNT(*) FROM {Tbl("TrainingCompletionHistories")} WHERE {Col("TrainingId")} = {P}TrainingId) AS INT)",
				new { TrainingId = trainingId });
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
