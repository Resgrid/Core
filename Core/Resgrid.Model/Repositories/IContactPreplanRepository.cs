using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Contact pre-plans (Contacts plan Phase A, A3).</summary>
	public interface IContactPreplanRepository : IRepository<ContactPreplan>
	{
		/// <summary>The live (non-deleted) pre-plan for a contact, or null.</summary>
		Task<ContactPreplan> GetPreplanByContactIdAsync(string contactId, int departmentId);

		/// <summary>Live pre-plans for a set of contacts in one query.</summary>
		Task<IEnumerable<ContactPreplan>> GetPreplansByContactIdsAsync(int departmentId, IEnumerable<string> contactIds);

		/// <summary>All live pre-plans for a department (review-currency index badge).</summary>
		Task<IEnumerable<ContactPreplan>> GetPreplansByDepartmentIdAsync(int departmentId);
	}

	/// <summary>Repeating premise hazards on a contact pre-plan (Contacts plan Phase A, A3).</summary>
	public interface IContactPreplanHazardRepository : IRepository<ContactPreplanHazard>
	{
		Task<IEnumerable<ContactPreplanHazard>> GetHazardsByPreplanIdAsync(string contactPreplanId, int departmentId);

		Task<IEnumerable<ContactPreplanHazard>> GetHazardsByContactIdAsync(string contactId, int departmentId);

		/// <summary>Live hazards for a set of contacts in one query.</summary>
		Task<IEnumerable<ContactPreplanHazard>> GetHazardsByContactIdsAsync(int departmentId, IEnumerable<string> contactIds);
	}

	/// <summary>Contact site attachments (Contacts plan Phase A, A3). Metadata reads never load the blob.</summary>
	public interface IContactAttachmentRepository : IRepository<ContactAttachment>
	{
		/// <summary>Live attachment rows for a contact WITHOUT the Data column.</summary>
		Task<IEnumerable<ContactAttachment>> GetAttachmentMetaByContactIdAsync(string contactId, int departmentId);

		/// <summary>Live attachment rows for a set of contacts WITHOUT the Data column.</summary>
		Task<IEnumerable<ContactAttachment>> GetAttachmentMetaByContactIdsAsync(int departmentId, IEnumerable<string> contactIds);

		/// <summary>One attachment including its blob.</summary>
		Task<ContactAttachment> GetAttachmentByIdAsync(int contactAttachmentId);
	}
}
