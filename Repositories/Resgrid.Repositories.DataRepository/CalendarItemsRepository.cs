using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using Dapper;
using System;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class CalendarItemsRepository : RepositoryBase<CalendarItem>, ICalendarItemsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public CalendarItemsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<CalendarItem> GetAllCalendarItemsToNotify()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<CalendarItem>($"SELECT * FROM CalendarItems WHERE ReminderSent = 0 AND Reminder > 0 AND IsV2Schedule = 0").ToList();
			}
		}

		public async Task<List<CalendarItem>> GetAllCalendarItemsToNotifyAsync()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var items = await db.QueryAsync<CalendarItem>($"SELECT * FROM CalendarItems WHERE ReminderSent = 0 AND Reminder > 0 AND IsV2Schedule = 0");

				return items.ToList();
			}
		}

		public List<CalendarItem> GetAllV2CalendarItemsForDepartment(int departmentId, DateTime startDate)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<CalendarItem>(@"SELECT * FROM CalendarItems
					WHERE DepartmentId = @departmentId
					AND IsV2Schedule = 1
					AND (Start >= @startDate OR (RecurrenceType > 0 AND (RecurrenceEnd IS NULL OR RecurrenceEnd > @startDate)))",
					new { departmentId = departmentId, startDate = startDate }).ToList();
			}
		}

		public async Task<List<CalendarItem>> GetAllV2CalendarItemsForDepartmentAsync(int departmentId, DateTime startDate)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var items = await db.QueryAsync<CalendarItem>(@"SELECT * FROM CalendarItems
					WHERE DepartmentId = @departmentId
					AND IsV2Schedule = 1
					AND (Start >= @startDate OR (RecurrenceType > 0 AND (RecurrenceEnd IS NULL OR RecurrenceEnd > @startDate)))",
					new { departmentId = departmentId, startDate = startDate });

				return items.ToList();
			}
		}

		public List<CalendarItem> GetAllV2CalendarItemRecurrences(string calendarItemId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<CalendarItem>(@"SELECT * FROM CalendarItems
					WHERE RecurrenceId = @calendarItemId",
					new { calendarItemId = calendarItemId }).ToList();
			}
		}

		public async Task<List<CalendarItem>> GetAllV2CalendarItemRecurrencesAsync(string calendarItemId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var items = await db.QueryAsync<CalendarItem>(@"SELECT * FROM CalendarItems
					WHERE RecurrenceId = @calendarItemId",
					new { calendarItemId = calendarItemId });

				return items.ToList();
			}
		}

		public List<CalendarItem> GetAllV2CalendarItemsToNotify(DateTime startDate)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<CalendarItem>(@"SELECT * FROM CalendarItems
					WHERE IsV2Schedule = 1 AND ReminderSent = 0 AND Reminder > 0 
					AND (Start >= @startDate OR (RecurrenceType > 0 AND (RecurrenceEnd IS NULL OR RecurrenceEnd > @startDate)))",
					new { startDate = startDate }).ToList();
			}
		}

		public async Task<List<CalendarItem>> GetAllV2CalendarItemsToNotifyAsync(DateTime startDate)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var items = await db.QueryAsync<CalendarItem>(@"SELECT * FROM CalendarItems
					WHERE IsV2Schedule = 1 AND ReminderSent = 0 AND Reminder > 0 
					AND (Start >= @startDate OR (RecurrenceType > 0 AND (RecurrenceEnd IS NULL OR RecurrenceEnd > @startDate)))",
					new { startDate = startDate });

				return items.ToList();
			}
		}

		public bool DeleteCalendarItemAndRecurrences(int calendarItemId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				db.Query($"DELETE FROM CalendarItems WHERE CalendarItemId = @itemId OR RecurrenceId = @itemId",
					new { itemId = calendarItemId });
			}

			return true;
		}

		public async Task<bool> DeleteCalendarItemAndRecurrencesAsync(int calendarItemId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				await db.QueryAsync($"DELETE FROM CalendarItems WHERE CalendarItemId = @itemId OR RecurrenceId = @itemId",
					new { itemId = calendarItemId });
			}

			return true;
		}
	}
}
