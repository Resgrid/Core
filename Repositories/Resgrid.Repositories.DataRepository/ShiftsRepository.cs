using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class ShiftsRepository : RepositoryBase<Shift>, IShiftsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public ShiftsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }


		public List<Shift> GetAllShiftsAndDays()
		{
			Dictionary<int, Shift> lookup = new Dictionary<int, Shift>();

			List<ShiftDay> days;

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT * FROM Shifts s INNER JOIN Departments d ON d.DepartmentId = s.DepartmentId";

				db.Query<Shift, Department, Shift>(query, (s, d) =>
				{
					Shift shift;

					if (!lookup.TryGetValue(s.ShiftId, out shift))
					{
						lookup.Add(s.ShiftId, s);
						shift = s;
					}

					if (s != null && s.Department == null)
					{
						s.Department = d;
					}

					return s;

				}, splitOn: "DepartmentId").ToList();

				days = db.Query<ShiftDay>($"SELECT * FROM ShiftDays").ToList();
			}

			var shifts = lookup.Values.ToList();
			foreach (var shift in shifts)
			{
				shift.Days = days.Where(x => x.ShiftId == shift.ShiftId).ToList();
			}

			return shifts;
		}
	}
}
