using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Calendar;

namespace Resgrid.Repositories.DataRepository
{
	public class CalendarItemsRepository : RepositoryBase<CalendarItem>, ICalendarItemsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CalendarItemsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CalendarItem>> GetCalendarItemsByRecurrenceIdAsync(int id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CalendarItem>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CalendarItemId", id);

					var query = _queryFactory.GetQuery<SelectCalendarItemByRecurrenceIdQuery>();

					return await x.QueryAsync<CalendarItem>(sql: query,
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

		public async Task<bool> DeleteCalendarItemAndRecurrencesAsync(int id, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var dynamicParameters = new DynamicParametersExtension();
						dynamicParameters.Add("CalendarItemId", id);

						var query = _queryFactory.GetDeleteQuery<DeleteCalendarItemQuery>();

						var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await removeFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await removeFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public async Task<IEnumerable<CalendarItem>> GetAllCalendarItemsToNotifyAsync(DateTime startDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CalendarItem>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("StartDate", startDate);

					var query = _queryFactory.GetQuery<SelectCalendarItemsByDateQuery>();

					return await x.QueryAsync<CalendarItem>(sql: query,
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

		public async Task<CalendarItem> GetCalendarItemByIdAsync(int calendarItemId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<CalendarItem>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CalendarItemId", calendarItemId);

					var query = _queryFactory.GetQuery<SelectCalendarItemByIdQuery>();

					var messageDictionary = new Dictionary<int, CalendarItem>();
					var result = await x.QueryAsync<CalendarItem, CalendarItemAttendee, CalendarItem>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: CalendarItemRecipientMapping(messageDictionary),
						splitOn: "CalendarItemAttendeeId");

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

		public async Task<IEnumerable<CalendarItem>> GetAllCalendarItemsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CalendarItem>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectCalendarItemByDIdQuery>();

					var dictionary = new Dictionary<int, CalendarItem>();
					var result = await x.QueryAsync<CalendarItem, CalendarItemAttendee, CalendarItem>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: CalendarItemRecipientMapping(dictionary),
						splitOn: "CalendarItemAttendeeId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value);

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

		private static Func<CalendarItem, CalendarItemAttendee, CalendarItem> CalendarItemRecipientMapping(Dictionary<int, CalendarItem> dictionary)
		{
			return new Func<CalendarItem, CalendarItemAttendee, CalendarItem>((calendarItem, calendarItemAttendee) =>
			{
				var dictionaryItem = default(CalendarItem);

				if (calendarItemAttendee != null)
				{
					if (dictionary.TryGetValue(calendarItem.CalendarItemId, out dictionaryItem))
					{
						if (dictionaryItem.Attendees.All(x => x.CalendarItemAttendeeId != calendarItemAttendee.CalendarItemAttendeeId))
							dictionaryItem.Attendees.Add(calendarItemAttendee);
					}
					else
					{
						if (calendarItem.Attendees == null)
							calendarItem.Attendees = new List<CalendarItemAttendee>();

						calendarItem.Attendees.Add(calendarItemAttendee);
						dictionary.Add(calendarItem.CalendarItemId, calendarItem);

						dictionaryItem = calendarItem;
					}
				}
				else
				{
					calendarItem.Attendees = new List<CalendarItemAttendee>();
					dictionaryItem = calendarItem;
					dictionary.Add(calendarItem.CalendarItemId, calendarItem);
				}

				return dictionaryItem;
			});
		}
	}
}
