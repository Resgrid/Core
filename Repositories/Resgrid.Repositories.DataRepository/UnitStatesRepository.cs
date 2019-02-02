using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitStatesRepository : RepositoryBase<UnitState>, IUnitStatesRepository
	{
		public UnitStatesRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<UnitState> GetLatestUnitStatesForDepartment(int departmentId)
		{
			var units = db.SqlQuery<UnitState>(@"SELECT  q.*
												 FROM    (
												 		SELECT *, ROW_NUMBER() OVER (PARTITION BY UnitId ORDER BY UnitStateId DESC) us
												 		FROM UnitStates
												 		) q
												 INNER JOIN Units u ON u.UnitId = q.UnitId
												 WHERE u.DepartmentId = @departmentId AND us = 1",
												new SqlParameter("@departmentId", departmentId));

			return units.ToList();
		}

		public List<UnitStateRole> GetCurrentRolesForUnit(int unitId)
		{
			var units = db.SqlQuery<UnitStateRole>(@"SELECT * FROM UnitStates us
												INNER JOIN UnitStateRoles usr ON us.UnitStateId = usr.UnitStateId
												WHERE us.UnitId = @unitId AND us.Timestamp >= DATEADD(day,-2,GETUTCDATE())",
												new SqlParameter("@unitId", unitId));

			return units.ToList();
		}
	}
}
