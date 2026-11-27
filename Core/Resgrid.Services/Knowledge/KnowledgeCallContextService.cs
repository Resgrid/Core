using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// The call's operational fields for "Ask about this call" (K3), and nothing else: the type (already a name on the call), the priority's
	/// name (the department's priorities, else the built-in names), the determinant (P4's reader, when registered) and the type of the point
	/// of interest the location was picked from (the department's POI types; never the POI's name, note or address). It reads no other call
	/// field, so a patient's name, the nature, notes, the address, the caller and any form answer can never reach a prompt (guard tested).
	/// A lookup that fails leaves its field empty.
	/// </summary>
	public sealed class KnowledgeCallContextService : IKnowledgeCallContextService
	{
		private readonly Lazy<ICallsService> _calls;
		private readonly IMappingService _mapping;
		private readonly IKnowledgeCallDeterminantSource _determinants;

		public KnowledgeCallContextService(Lazy<ICallsService> calls, IMappingService mapping, IKnowledgeCallDeterminantSource determinants)
		{
			_calls = calls;
			_mapping = mapping;
			_determinants = determinants;
		}

		public async Task<KnowledgeCallContext> BuildAsync(Call call, CancellationToken cancellationToken = default)
		{
			var context = new KnowledgeCallContext();
			if (call == null || call.DepartmentId <= 0)
				return context;

			context.CallType = KnowledgeCallContext.Clean(call.Type);
			context.Priority = await PriorityNameAsync(call.DepartmentId, call.Priority);
			context.Determinant = await DeterminantAsync(call.DepartmentId, call.CallId, cancellationToken);
			context.LocationType = await LocationTypeAsync(call.DepartmentId, call.LocationPoiId);
			return context;
		}

		private async Task<string> PriorityNameAsync(int departmentId, int priority)
		{
			try
			{
				var match = (await _calls.Value.GetCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>())
					.FirstOrDefault(p => p != null && p.DepartmentCallPriorityId == priority && (p.DepartmentId == departmentId || p.DepartmentId == 0));
				if (match != null && !string.IsNullOrWhiteSpace(match.Name))
					return KnowledgeCallContext.Clean(match.Name);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Call priorities could not be read for department {departmentId}.");
			}
			return Enum.IsDefined(typeof(CallPriority), priority) ? ((CallPriority)priority).ToString() : null;
		}

		private async Task<string> DeterminantAsync(int departmentId, int callId, CancellationToken cancellationToken)
		{
			if (_determinants == null || callId <= 0)
				return null;
			try
			{
				return KnowledgeCallContext.Clean(await _determinants.GetDeterminantAsync(departmentId, callId, cancellationToken));
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, $"The determinant of call {callId} could not be read.");
				return null;
			}
		}

		private async Task<string> LocationTypeAsync(int departmentId, int? locationPoiId)
		{
			if (!locationPoiId.HasValue || locationPoiId.Value <= 0)
				return null;
			try
			{
				var poi = await _mapping.GetPOIByIdAsync(locationPoiId.Value);
				if (poi == null)
					return null;
				// The type's name only, and only one of the department's own types.
				var type = (await _mapping.GetPOITypesForDepartmentAsync(departmentId) ?? new List<PoiType>())
					.FirstOrDefault(t => t != null && t.PoiTypeId == poi.PoiTypeId && t.DepartmentId == departmentId);
				return KnowledgeCallContext.Clean(type?.Name);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The location type of a call could not be read for department {departmentId}.");
				return null;
			}
		}
	}

	/// <summary>The determinant reader until P4 registers its own: no determinant.</summary>
	public sealed class NullKnowledgeCallDeterminantSource : IKnowledgeCallDeterminantSource
	{
		public Task<string> GetDeterminantAsync(int departmentId, int callId, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);
	}
}
