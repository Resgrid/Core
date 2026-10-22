using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Uploaded unit, call and POI type icons (M0285) and personnel role icons (M0286).</summary>
	public class TypeIconsRepository : RmsRepositoryBase<TypeIcon>, ITypeIconsRepository
	{
		public TypeIconsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<TypeIcon> GetByIconKeyAsync(string iconKey)
		{
			return QueryFirstOrDefaultAsync<TypeIcon>(
				$"SELECT * FROM {Tbl("TypeIcons")} WHERE {Col("IconKey")} = {P}IconKey",
				new { IconKey = iconKey });
		}

		public Task<int> DeleteForTypeAsync(int departmentId, TypeIconKinds kind, int typeId, string keepIconKey = null, CancellationToken cancellationToken = default)
		{
			var keep = keepIconKey == null ? string.Empty : $" AND {Col("IconKey")} <> {P}KeepIconKey";

			return ExecuteAsync(
				$"DELETE FROM {Tbl("TypeIcons")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TypeKind")} = {P}TypeKind AND {Col("TypeId")} = {P}TypeId{keep}",
				new { DepartmentId = departmentId, TypeKind = (int)kind, TypeId = typeId, KeepIconKey = keepIconKey }, cancellationToken);
		}

		public Task<int> SetTypeIconKeyAsync(int departmentId, TypeIconKinds kind, int typeId, string iconKey, CancellationToken cancellationToken = default)
		{
			// The table and key column come from this fixed map, never from input.
			var (table, idColumn) = kind switch
			{
				TypeIconKinds.UnitType => ("UnitTypes", "UnitTypeId"),
				TypeIconKinds.CallType => ("CallTypes", "CallTypeId"),
				TypeIconKinds.PoiType => ("POITypes", "PoiTypeId"),
				TypeIconKinds.PersonnelRole => ("PersonnelRoles", "PersonnelRoleId"),
				_ => throw new ArgumentOutOfRangeException(nameof(kind))
			};

			return ExecuteAsync(
				$"UPDATE {Tbl(table)} SET {Col("IconKey")} = {P}IconKey WHERE {Col(idColumn)} = {P}TypeId AND {Col("DepartmentId")} = {P}DepartmentId",
				new { IconKey = iconKey, TypeId = typeId, DepartmentId = departmentId }, cancellationToken);
		}
	}
}
