using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	public static class ReferencedCallsHelper
	{
		/// <summary>
		/// Adds the department's closed calls that the given status rows point at to the active call list, so an events
		/// report still names the call a status was set against after the call closed.
		/// </summary>
		public static async Task<List<Call>> AddReferencedCallsAsync(ICallsService callsService, int departmentId, List<Call> calls, IEnumerable<int> destinationCallIds)
		{
			var result = calls != null ? new List<Call>(calls) : new List<Call>();
			var known = new HashSet<int>(result.Select(x => x.CallId));

			foreach (var callId in destinationCallIds.Where(x => x > 0).Distinct())
			{
				if (known.Contains(callId))
					continue;

				var call = await callsService.GetCallByIdAsync(callId);
				if (call != null && call.DepartmentId == departmentId)
				{
					result.Add(call);
					known.Add(callId);
				}
			}

			return result;
		}
	}
}
