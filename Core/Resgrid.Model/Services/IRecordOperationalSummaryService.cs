using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Builds <see cref="RecordOperationalSummaryV1"/> from official revisions (RMS plan sections 5.1 and 4.7).
	/// Department-scoped; a draft, a purged record and a deleted record never produce a summary. The feed is
	/// unauthorized by design so the API boundary can apply the member rule or a system-principal grant per
	/// row, exactly as the delta cursor does.
	/// </summary>
	public interface IRecordOperationalSummaryService
	{
		/// <summary>Member path: null when the record has no official revision, is gone, or the viewer cannot see it.</summary>
		Task<RecordOperationalSummaryV1> GetAsync(int departmentId, string viewerUserId, string recordId, RmsRecordKind kind, string revisionId = null);

		/// <summary>Unauthorized build for a caller that has already applied its own visibility rule (system principals).</summary>
		Task<RecordOperationalSummaryV1> BuildAsync(int departmentId, string recordId, RmsRecordKind kind, string revisionId = null);

		/// <summary>Records with an official revision whose projection changed after the query point, oldest first; the caller filters visibility.</summary>
		Task<RecordOperationalSummaryPage> QueryAsync(int departmentId, RecordOperationalSummaryQuery query);
	}
}
