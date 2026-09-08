using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Contact pre-plans (Contacts plan Phase A, registry M0183). Dapper over the shared dialect helpers.</summary>
	public class ContactPreplanRepository : RmsRepositoryBase<ContactPreplan>, IContactPreplanRepository
	{
		public ContactPreplanRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ContactPreplan> GetPreplanByContactIdAsync(string contactId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<ContactPreplan>(
				$"SELECT * FROM {Tbl("ContactPreplans")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId, ContactId = contactId });
		}

		public Task<IEnumerable<ContactPreplan>> GetPreplansByContactIdsAsync(int departmentId, IEnumerable<string> contactIds)
		{
			var ids = InListValue(contactIds);
			if (ids.Length == 0)
				return Task.FromResult<IEnumerable<ContactPreplan>>(new List<ContactPreplan>());

			return QueryAsync<ContactPreplan>(
				$"SELECT * FROM {Tbl("ContactPreplans")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ContactId", "ContactIds")} AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, ContactIds = ids });
		}

		public Task<IEnumerable<ContactPreplan>> GetPreplansByDepartmentIdAsync(int departmentId)
		{
			return QueryAsync<ContactPreplan>(
				$"SELECT * FROM {Tbl("ContactPreplans")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId });
		}

		private static string False => IsPostgres ? "FALSE" : "0";
	}

	/// <summary>Repeating premise hazards on a contact pre-plan (registry M0183).</summary>
	public class ContactPreplanHazardRepository : RmsRepositoryBase<ContactPreplanHazard>, IContactPreplanHazardRepository
	{
		public ContactPreplanHazardRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<ContactPreplanHazard>> GetHazardsByPreplanIdAsync(string contactPreplanId, int departmentId)
		{
			return QueryAsync<ContactPreplanHazard>(
				$"SELECT * FROM {Tbl("ContactPreplanHazards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactPreplanId")} = {P}PreplanId AND {Col("IsDeleted")} = {False} ORDER BY {Col("Severity")} DESC, {Col("AddedOn")}",
				new { DepartmentId = departmentId, PreplanId = contactPreplanId });
		}

		public Task<IEnumerable<ContactPreplanHazard>> GetHazardsByContactIdAsync(string contactId, int departmentId)
		{
			return QueryAsync<ContactPreplanHazard>(
				$"SELECT * FROM {Tbl("ContactPreplanHazards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("IsDeleted")} = {False} ORDER BY {Col("Severity")} DESC, {Col("AddedOn")}",
				new { DepartmentId = departmentId, ContactId = contactId });
		}

		public Task<IEnumerable<ContactPreplanHazard>> GetHazardsByContactIdsAsync(int departmentId, IEnumerable<string> contactIds)
		{
			var ids = InListValue(contactIds);
			if (ids.Length == 0)
				return Task.FromResult<IEnumerable<ContactPreplanHazard>>(new List<ContactPreplanHazard>());

			return QueryAsync<ContactPreplanHazard>(
				$"SELECT * FROM {Tbl("ContactPreplanHazards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ContactId", "ContactIds")} AND {Col("IsDeleted")} = {False} ORDER BY {Col("Severity")} DESC, {Col("AddedOn")}",
				new { DepartmentId = departmentId, ContactIds = ids });
		}

		private static string False => IsPostgres ? "FALSE" : "0";
	}

	/// <summary>Contact site attachments (registry M0184). Metadata reads exclude the blob column by construction.</summary>
	public class ContactAttachmentRepository : RmsRepositoryBase<ContactAttachment>, IContactAttachmentRepository
	{
		public ContactAttachmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string MetadataColumns()
		{
			return Cols("ContactAttachmentId", "ContactId", "DepartmentId", "ContactAttachmentType", "Name", "FileName", "FileType", "Size", "IsDeleted", "AddedOn", "AddedByUserId", "IsProtected");
		}

		public Task<IEnumerable<ContactAttachment>> GetAttachmentMetaByContactIdAsync(string contactId, int departmentId)
		{
			return QueryAsync<ContactAttachment>(
				$"SELECT {MetadataColumns()} FROM {Tbl("ContactAttachments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId, ContactId = contactId });
		}

		public Task<IEnumerable<ContactAttachment>> GetAttachmentMetaByContactIdsAsync(int departmentId, IEnumerable<string> contactIds)
		{
			var ids = InListValue(contactIds);
			if (ids.Length == 0)
				return Task.FromResult<IEnumerable<ContactAttachment>>(new List<ContactAttachment>());

			return QueryAsync<ContactAttachment>(
				$"SELECT {MetadataColumns()} FROM {Tbl("ContactAttachments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ContactId", "ContactIds")} AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId, ContactIds = ids });
		}

		public Task<ContactAttachment> GetAttachmentByIdAsync(int contactAttachmentId)
		{
			return QueryFirstOrDefaultAsync<ContactAttachment>(
				$"SELECT * FROM {Tbl("ContactAttachments")} WHERE {Col("ContactAttachmentId")} = {P}Id",
				new { Id = contactAttachmentId });
		}

		private static string False => IsPostgres ? "FALSE" : "0";
	}
}
