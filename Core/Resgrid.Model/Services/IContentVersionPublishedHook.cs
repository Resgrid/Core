using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>Which content core a newly published version belongs to (the shared lifecycle of protocols and courses, §12.1).</summary>
	public enum ContentVersionKinds
	{
		/// <summary>A protocol version (P1) put in force, immediately or on its scheduled date.</summary>
		ProtocolVersion = 0,

		/// <summary>An LMS course version (T2) published.</summary>
		CourseVersion = 1
	}

	/// <summary>A version that just went into force; the one in force before it (if any) is now superseded.</summary>
	public sealed class ContentVersionPublished
	{
		public int DepartmentId { get; set; }

		public ContentVersionKinds Kind { get; set; }

		/// <summary>DispatchProtocolId or LmsCourseId.</summary>
		public int ItemId { get; set; }

		/// <summary>ProtocolVersionId or LmsCourseVersionId of the version now in force.</summary>
		public int VersionId { get; set; }

		public string VersionLabel { get; set; }

		public DateTime PublishedOnUtc { get; set; }
	}

	/// <summary>
	/// Work that follows a version going into force (T5 item 5: content drafted from a source version is flagged for review when that version is
	/// superseded). The publishing service calls every registered hook after its commit; a hook never fails the publish (errors are logged) and
	/// must be cheap (a single update by department and item). Baseline services hold this seam only; the LMS layer implements it.
	/// </summary>
	public interface IContentVersionPublishedHook
	{
		Task OnVersionPublishedAsync(ContentVersionPublished published, CancellationToken cancellationToken = default);
	}
}
