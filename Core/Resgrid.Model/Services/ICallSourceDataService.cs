using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Reads everything Resgrid holds about a call that a report needs — the call's times, each unit's and person's
	/// statuses (linked and inferred, with who set them and from where), crews, dispatches, check-ins and the Incident
	/// Command it ran under — into one <see cref="CallSourceData"/>. Incident and run reports prefill from it and report
	/// editors look times up in it. It does no authorization: callers check the reader may see the call first.
	/// </summary>
	public interface ICallSourceDataService
	{
		/// <summary>
		/// The call's source data, or null when the call is not in the department. A source that cannot be read is left
		/// out and named in <see cref="CallSourceData.Warnings"/>; it never fails the whole read.
		/// </summary>
		Task<CallSourceData> GetForCallAsync(int departmentId, int callId);

		/// <summary>As <see cref="GetForCallAsync(int, int)"/> for a call already loaded (its dispatch lists are read if missing).</summary>
		Task<CallSourceData> GetForCallAsync(int departmentId, Call call);
	}
}
