using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Data.Common;
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
	public class TrainingUserRepository : RmsRepositoryBase<TrainingUser>, ITrainingUserRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public TrainingUserRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<TrainingUser> GetTrainingUserByTrainingIdAndUserIdAsync(int trainingId, string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<TrainingUser>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("TrainingId", trainingId);
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectTrainingUserByTandUIdQuery>();

					return await x.QueryFirstOrDefaultAsync<TrainingUser>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
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

				throw;
			}
		}

		private static string True => IsPostgres ? "true" : "1";

		/// <summary>Columns an assignment update may write (UpdateColumnsAsync); views and quiz results have their own methods.</summary>
		private static readonly HashSet<string> UpdatableColumns = new HashSet<string>(StringComparer.Ordinal)
		{
			"Viewed", "ViewedOn", "Complete", "CompletedOn", "Score", "Passed", "AssignedOn", "DueOn", "AssignmentSource", "NotifiedOn", "RemindedOn",
			"LastReminderLeadDays", "OverdueNotifiedOn", "RemovedOn", "RemovedByUserId", "OriginalDueOn", "ExtensionReason", "ExtendedByUserId",
			"ExtendedOn", "ExemptOn", "ExemptReason", "ExemptByUserId", "ExemptUntil", "AttemptCount", "Cycle",
			// Round 7B (M0400): a completion recorded by an instructor (permission 135)
			"CompletionMethod", "RecordedCompletionId"
		};

		public async Task<List<TrainingUser>> GetByTrainingIdAsync(int trainingId)
		{
			return (await QueryAsync<TrainingUser>($"SELECT * FROM {Tbl("TrainingUsers")} WHERE {Col("TrainingId")} = {P}TrainingId ORDER BY {Col("TrainingUserId")}",
				new { TrainingId = trainingId })).ToList();
		}

		public async Task<TrainingUser> InsertAssignmentAsync(TrainingUser assignment, CancellationToken cancellationToken = default)
		{
			if (assignment == null || assignment.TrainingId <= 0 || string.IsNullOrWhiteSpace(assignment.UserId))
				return null;

			var columns = new[] { "TrainingId", "UserId", "Viewed", "Complete", "Score", "AssignedOn", "DueOn", "AssignmentSource", "NotifiedOn", "Cycle", "AttemptCount" };
			var parameters = BuildParameters(assignment, columns);
			var values = string.Join(", ", columns.Select(c => P + c));

			// One row per member and training (UX_TrainingUsers_TrainingId_UserId): an existing row wins and nothing is inserted,
			// so a double submit or a racing worker never fails or duplicates.
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("TrainingUsers")} ({Cols(columns)}) VALUES ({values}) ON CONFLICT ({Col("TrainingId")}, {Col("UserId")}) DO NOTHING RETURNING {Col("TrainingUserId")}"
				: $"INSERT INTO {Tbl("TrainingUsers")} ({Cols(columns)}) OUTPUT INSERTED.{Col("TrainingUserId")} SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("TrainingUsers")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("TrainingId")} = {P}TrainingId AND {Col("UserId")} = {P}UserId)";

			var id = await ScalarAsync<int?>(sql, parameters, cancellationToken);
			if (!id.HasValue || id.Value <= 0)
				return null;

			assignment.TrainingUserId = id.Value;
			return assignment;
		}

		public Task<int> UpdateColumnsAsync(TrainingUser assignment, IReadOnlyCollection<string> columns, CancellationToken cancellationToken = default)
		{
			if (assignment == null || assignment.TrainingUserId <= 0 || columns == null || columns.Count == 0)
				return Task.FromResult(0);

			var list = columns.Distinct(StringComparer.Ordinal).ToList();
			foreach (var column in list)
				if (!UpdatableColumns.Contains(column))
					throw new ArgumentException("Not an updatable assignment column: " + column, nameof(columns));

			var parameters = BuildParameters(assignment, list);
			parameters.Add("TrainingUserId", assignment.TrainingUserId);
			var sets = string.Join(", ", list.Select(c => $"{Col(c)} = {P}{c}"));
			return ExecuteAsync($"UPDATE {Tbl("TrainingUsers")} SET {sets} WHERE {Col("TrainingUserId")} = {P}TrainingUserId", parameters, cancellationToken);
		}

		public Task<int> MarkViewedAsync(int trainingUserId, DateTime utcNow, bool completeWithoutQuiz, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", trainingUserId);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);

			// ViewedOn and CompletedOn are written once (T-D13): later opens keep the first view and the completion date.
			var complete = completeWithoutQuiz ? $", {Col("Complete")} = {True}, {Col("CompletedOn")} = COALESCE({Col("CompletedOn")}, {P}Now)" : string.Empty;
			return ExecuteAsync($"UPDATE {Tbl("TrainingUsers")} SET {Col("Viewed")} = {True}, {Col("ViewedOn")} = COALESCE({Col("ViewedOn")}, {P}Now){complete} " +
				$"WHERE {Col("TrainingUserId")} = {P}Id AND {Col("RemovedOn")} IS NULL", parameters, cancellationToken);
		}

		public Task<int> RecordQuizResultAsync(int trainingUserId, double score, bool passed, int attemptCount, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", trainingUserId);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("Score", score, System.Data.DbType.Double);
			parameters.Add("Passed", passed, System.Data.DbType.Boolean);
			parameters.Add("AttemptCount", attemptCount);

			// The score is always written (T-D1); Passed is stored apart from Complete (T-D2): only a pass completes.
			var complete = passed ? $", {Col("Complete")} = {True}, {Col("CompletedOn")} = COALESCE({Col("CompletedOn")}, {P}Now)" : string.Empty;
			return ExecuteAsync($"UPDATE {Tbl("TrainingUsers")} SET {Col("Score")} = {P}Score, {Col("Passed")} = {P}Passed, {Col("AttemptCount")} = {P}AttemptCount, " +
				$"{Col("Viewed")} = {True}, {Col("ViewedOn")} = COALESCE({Col("ViewedOn")}, {P}Now){complete} WHERE {Col("TrainingUserId")} = {P}Id", parameters, cancellationToken);
		}

		public Task<int> DeleteAssignmentAsync(int trainingId, int trainingUserId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl("TrainingUsers")} WHERE {Col("TrainingId")} = {P}TrainingId AND {Col("TrainingUserId")} = {P}Id",
				new { TrainingId = trainingId, Id = trainingUserId }, cancellationToken);
		}

		private static DynamicParameters BuildParameters(TrainingUser assignment, IEnumerable<string> columns)
		{
			var parameters = new DynamicParameters();
			foreach (var column in columns)
			{
				var property = typeof(TrainingUser).GetProperty(column);
				var value = property.GetValue(assignment);
				var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

				if (type == typeof(DateTime))
					parameters.Add(column, value == null ? (DateTime?)null : DatabaseTimestamp((DateTime)value), System.Data.DbType.DateTime2);
				else if (type == typeof(bool))
					parameters.Add(column, value, System.Data.DbType.Boolean);
				else if (type == typeof(int))
					parameters.Add(column, value, System.Data.DbType.Int32);
				else if (type == typeof(double))
					parameters.Add(column, value, System.Data.DbType.Double);
				else
					parameters.Add(column, value, System.Data.DbType.String);
			}

			return parameters;
		}
	}
}
