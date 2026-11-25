using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// One entry of a member's GDPR export that a module outside the export service contributes (round 5F). The export service writes every
	/// registered section into the archive, so a module the baseline must not reference (the LMS, §17.3) or a stream merged later (classes,
	/// skill evaluations) adds its rows by registering a section, without the export service knowing its types.
	/// </summary>
	public interface IGdprExportSection
	{
		/// <summary>The archive entry, e.g. "lms_learning.json". Unique across sections.</summary>
		string EntryName { get; }

		/// <summary>
		/// The member's own rows in the department, as plain JSON-ready objects: never another member's rows, never answer keys or anything
		/// else the member could not see in the product.
		/// </summary>
		Task<object> BuildAsync(string userId, int departmentId);
	}
}
