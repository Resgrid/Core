using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// The call's determinant for call-context answers (K3 over the P4 case flow): the determinant of the most recent protocol case on the
	/// call that is not removed. Only the code is read (never the case answers or an override reason), so the answer prompt carries no
	/// more than the dispatcher saw on the call's header. The caller hands over a call it already loaded for the department.
	/// </summary>
	public sealed class CallProtocolDeterminantSource : IKnowledgeCallDeterminantSource
	{
		private readonly ICallProtocolsRepository _callProtocols;

		public CallProtocolDeterminantSource(ICallProtocolsRepository callProtocols)
		{
			_callProtocols = callProtocols;
		}

		public async Task<string> GetDeterminantAsync(int departmentId, int callId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || callId <= 0)
				return null;

			try
			{
				var rows = await _callProtocols.GetCallProtocolsByCallIdAsync(callId) ?? Enumerable.Empty<CallProtocol>();
				return rows
					.Where(r => r != null && r.CallId == callId && !r.IsRemoved && !string.IsNullOrWhiteSpace(r.Determinant))
					.OrderByDescending(r => r.CallProtocolId)
					.Select(r => r.Determinant.Trim())
					.FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The protocol determinant of call {callId} could not be read for department {departmentId}.");
				return null;
			}
		}
	}
}
