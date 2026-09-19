using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Column lists shared by the Phase D certification repositories (plan D3): reads never pull the file bytes unless asked.</summary>
	internal static class CertificationColumns
	{
		public static readonly string[] PersonnelMeta =
		{
			"PersonnelCertificationId", "DepartmentId", "UserId", "Name", "Number", "Type", "Area", "IssuedBy", "ExpiresOn", "RecievedOn",
			"Filetype", "Filename", "IsProtected", "DepartmentCertificationTypeId", "Status", "StatusChangedOn", "StatusChangedByUserId",
			"StatusReason", "VerifiedByUserId", "VerifiedOn", "IsDeleted"
		};

		public static readonly string[] UnitMeta =
		{
			"UnitCertificationId", "UnitId", "DepartmentId", "DepartmentCertificationTypeId", "Number", "IssuedBy", "IssuedOn", "ExpiresOn",
			"Status", "StatusChangedOn", "StatusChangedByUserId", "StatusReason", "Notes", "FileName", "FileType", "FileSize", "IsDeleted",
			"AddedOn", "AddedByUserId", "EditedOn", "EditedByUserId", "IsProtected", "ProtectedCatalogVersion"
		};

		public static readonly string[] CreditMeta =
		{
			"PersonnelCertificationCreditId", "PersonnelCertificationId", "DepartmentId", "CreditDate", "Hours", "Category", "Description",
			"FileName", "FileType", "AddedByUserId", "AddedOn", "IsProtected", "ProtectedCatalogVersion"
		};
	}

	/// <summary>Role certification requirements (M0213).</summary>
	public class PersonnelRoleCertificationRequirementRepository : RmsRepositoryBase<PersonnelRoleCertificationRequirement>, IPersonnelRoleCertificationRequirementRepository
	{
		public PersonnelRoleCertificationRequirementRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<PersonnelRoleCertificationRequirement>> GetByRoleIdAsync(int personnelRoleId) =>
			QueryAsync<PersonnelRoleCertificationRequirement>(
				$"SELECT * FROM {Tbl("PersonnelRoleCertificationRequirements")} WHERE {Col("PersonnelRoleId")} = {P}RoleId ORDER BY {Col("AnyOfGroup")}, {Col("PersonnelRoleCertificationRequirementId")}",
				new { RoleId = personnelRoleId });

		public Task<IEnumerable<PersonnelRoleCertificationRequirement>> GetAllForDepartmentAsync(int departmentId) =>
			QueryAsync<PersonnelRoleCertificationRequirement>(
				$"SELECT * FROM {Tbl("PersonnelRoleCertificationRequirements")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("PersonnelRoleId")}, {Col("AnyOfGroup")}, {Col("PersonnelRoleCertificationRequirementId")}",
				new { DepartmentId = departmentId });

		public Task<int> CountByTypeIdAsync(int departmentCertificationTypeId) =>
			ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("PersonnelRoleCertificationRequirements")} WHERE {Col("DepartmentCertificationTypeId")} = {P}TypeId", new { TypeId = departmentCertificationTypeId });

		public Task<int> DeleteByRoleIdAsync(int personnelRoleId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("PersonnelRoleCertificationRequirements")} WHERE {Col("PersonnelRoleId")} = {P}RoleId", new { RoleId = personnelRoleId }, cancellationToken);

		public Task<IEnumerable<int>> GetDepartmentIdsAsync() =>
			QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("PersonnelRoleCertificationRequirements")}", new { });
	}

	/// <summary>Department certification settings, keyed by DepartmentId (M0213); the generic insert would drop the key, so this is explicit SQL.</summary>
	public class DepartmentCertificationSettingsRepository : RmsRepositoryBase<DepartmentCertificationSettings>, IDepartmentCertificationSettingsRepository
	{
		public DepartmentCertificationSettingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<DepartmentCertificationSettings> GetAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<DepartmentCertificationSettings>($"SELECT * FROM {Tbl("DepartmentCertificationSettings")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });

		public async Task<DepartmentCertificationSettings> SaveAsync(DepartmentCertificationSettings settings, CancellationToken cancellationToken = default)
		{
			if (settings == null || settings.DepartmentId <= 0)
				throw new ArgumentException("Department certification settings need a department.", nameof(settings));

			var columns = new[] { "DepartmentId", "EnforcementMode", "RoleRemovalGraceDays", "NotifyLeadDaysCsv", "NotifyCertificationHolder", "TreatPendingVerificationAsValid", "SendAdminDigest", "UpdatedOn", "UpdatedByUserId" };
			var parameters = new
			{
				settings.DepartmentId, settings.EnforcementMode, settings.RoleRemovalGraceDays, NotifyLeadDaysCsv = settings.NotifyLeadDaysCsv ?? DepartmentCertificationSettings.DefaultNotifyLeadDaysCsv,
				settings.NotifyCertificationHolder, settings.TreatPendingVerificationAsValid, settings.SendAdminDigest, UpdatedOn = DatabaseTimestamp(settings.UpdatedOn), settings.UpdatedByUserId
			};
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("DepartmentCertificationSettings")} SET {string.Join(", ", columns.Skip(1).Select(c => Col(c) + " = " + P + c))} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				parameters, cancellationToken);
			if (updated == 0)
				await ExecuteAsync(
					$"INSERT INTO {Tbl("DepartmentCertificationSettings")} ({Cols(columns)}) VALUES ({string.Join(", ", columns.Select(c => P + c))})",
					parameters, cancellationToken);
			return await GetAsync(settings.DepartmentId);
		}
	}

	/// <summary>Continuing-education credits (M0214).</summary>
	public class PersonnelCertificationCreditsRepository : RmsRepositoryBase<PersonnelCertificationCredit>, IPersonnelCertificationCreditsRepository
	{
		public PersonnelCertificationCreditsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<PersonnelCertificationCredit>> GetByCertificationIdAsync(int personnelCertificationId) =>
			QueryAsync<PersonnelCertificationCredit>(
				$"SELECT {Cols(CertificationColumns.CreditMeta)} FROM {Tbl("PersonnelCertificationCredits")} WHERE {Col("PersonnelCertificationId")} = {P}CertId ORDER BY {Col("CreditDate")} DESC, {Col("PersonnelCertificationCreditId")} DESC",
				new { CertId = personnelCertificationId });

		public Task<PersonnelCertificationCredit> GetByIdWithDataAsync(int personnelCertificationCreditId) =>
			QueryFirstOrDefaultAsync<PersonnelCertificationCredit>($"SELECT * FROM {Tbl("PersonnelCertificationCredits")} WHERE {Col("PersonnelCertificationCreditId")} = {P}Id", new { Id = personnelCertificationCreditId });

		public async Task<IReadOnlyDictionary<int, decimal>> GetHourTotalsAsync(IEnumerable<int> personnelCertificationIds)
		{
			var ids = InListValue(personnelCertificationIds);
			if (ids.Length == 0)
				return new Dictionary<int, decimal>();
			var rows = await QueryAsync<(int PersonnelCertificationId, decimal Hours)>(
				$"SELECT {Col("PersonnelCertificationId")} AS PersonnelCertificationId, SUM({Col("Hours")}) AS Hours FROM {Tbl("PersonnelCertificationCredits")} WHERE {InList("PersonnelCertificationId", "Ids")} GROUP BY {Col("PersonnelCertificationId")}",
				new { Ids = ids });
			return rows.ToDictionary(r => r.PersonnelCertificationId, r => r.Hours);
		}

		public Task<int> DeleteByCertificationIdAsync(int personnelCertificationId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("PersonnelCertificationCredits")} WHERE {Col("PersonnelCertificationId")} = {P}CertId", new { CertId = personnelCertificationId }, cancellationToken);
	}

	/// <summary>Unit-scoped certification records (M0213).</summary>
	public class UnitCertificationRepository : RmsRepositoryBase<UnitCertification>, IUnitCertificationRepository
	{
		public UnitCertificationRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";

		public Task<IEnumerable<UnitCertification>> GetByUnitIdAsync(int unitId) =>
			QueryAsync<UnitCertification>(
				$"SELECT {Cols(CertificationColumns.UnitMeta)} FROM {Tbl("UnitCertifications")} WHERE {Col("UnitId")} = {P}UnitId AND {Col("IsDeleted")} = {False} ORDER BY {Col("ExpiresOn")}, {Col("UnitCertificationId")}",
				new { UnitId = unitId });

		public Task<IEnumerable<UnitCertification>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<UnitCertification>(
				$"SELECT {Cols(CertificationColumns.UnitMeta)} FROM {Tbl("UnitCertifications")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} ORDER BY {Col("UnitId")}, {Col("ExpiresOn")}",
				new { DepartmentId = departmentId });

		public Task<UnitCertification> GetByIdWithDataAsync(int unitCertificationId) =>
			QueryFirstOrDefaultAsync<UnitCertification>($"SELECT * FROM {Tbl("UnitCertifications")} WHERE {Col("UnitCertificationId")} = {P}Id", new { Id = unitCertificationId });

		public Task<IEnumerable<UnitCertification>> GetExpiringAsync(int departmentId, DateTime onOrBefore) =>
			QueryAsync<UnitCertification>(
				$"SELECT {Cols(CertificationColumns.UnitMeta)} FROM {Tbl("UnitCertifications")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {Col("ExpiresOn")} IS NOT NULL AND {Col("ExpiresOn")} <= {P}OnOrBefore ORDER BY {Col("ExpiresOn")}",
				new { DepartmentId = departmentId, OnOrBefore = DatabaseTimestamp(onOrBefore) });

		public Task<int> CountByTypeIdAsync(int departmentCertificationTypeId) =>
			ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("UnitCertifications")} WHERE {Col("DepartmentCertificationTypeId")} = {P}TypeId AND {Col("IsDeleted")} = {False}", new { TypeId = departmentCertificationTypeId });

		public Task<IEnumerable<int>> GetDepartmentIdsAsync() =>
			QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("UnitCertifications")} WHERE {Col("IsDeleted")} = {False}", new { });
	}
}
