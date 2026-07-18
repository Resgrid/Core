using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Answers "what calls am I on?" (intent <see cref="ChatbotIntentType.MyCalls"/>) and "what calls is
	/// Rescue 6 on?" (intent <see cref="ChatbotIntentType.UnitCalls"/>): the ACTIVE calls whose dispatch
	/// lists include the requesting user (direct personnel dispatch) or the named unit.
	/// </summary>
	public class MyCallsActionHandler : IChatbotActionHandler
	{
		private const int MaxConcurrentCallPopulations = 5;
		private const int MaxCallsToList = 10;

		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;

		public MyCallsActionHandler(ICallsService callsService, IUnitsService unitsService)
		{
			_callsService = callsService;
			_unitsService = unitsService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.MyCalls;

		public bool CanHandle(ChatbotIntentType intentType)
			=> intentType == ChatbotIntentType.MyCalls || intentType == ChatbotIntentType.UnitCalls;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				if (intent.Type == ChatbotIntentType.UnitCalls)
					return await HandleUnitCallsAsync(intent, session, culture);

				return await HandleMyCallsAsync(session, culture);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("MyCalls_Error", culture), Processed = false };
			}
		}

		private async Task<ChatbotResponse> HandleMyCallsAsync(ChatbotSession session, string culture)
		{
			var myCalls = new List<Call>();
			foreach (var call in await GetActiveCallsWithDispatchesAsync(session.DepartmentId, forUnits: false))
			{
				if (call.Dispatches != null && call.Dispatches.Any(d => d.UserId == session.UserId))
					myCalls.Add(call);
			}

			if (myCalls.Count == 0)
				return new ChatbotResponse { Text = ChatbotResources.Get("MyCalls_None", culture), Processed = true };

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("MyCalls_Header", culture));
			sb.AppendLine("----------------------");
			foreach (var call in myCalls.Take(MaxCallsToList))
				sb.AppendLine(ChatbotResources.Get("Calls_Line", culture, call.CallId, call.Name?.Truncate(25), call.NatureOfCall?.Truncate(40)));

			return new ChatbotResponse { Text = sb.ToString(), Processed = true };
		}

		private async Task<ChatbotResponse> HandleUnitCallsAsync(ChatbotIntent intent, ChatbotSession session, string culture)
		{
			intent.Parameters.TryGetValue("unitName", out var unitName);
			if (string.IsNullOrWhiteSpace(unitName))
				return new ChatbotResponse { Text = ChatbotResources.Get("UnitCalls_Specify", culture), Processed = false };

			var units = await _unitsService.GetUnitsForDepartmentAsync(session.DepartmentId);
			var query = unitName.Trim().ToLowerInvariant();
			Unit unit = null;

			if (int.TryParse(query, out var unitId))
				unit = units?.FirstOrDefault(u => u.UnitId == unitId);

			unit ??= units?.FirstOrDefault(u => u.Name != null && u.Name.ToLowerInvariant() == query)
				?? units?.FirstOrDefault(u => u.Name != null && u.Name.ToLowerInvariant().Contains(query));

			if (unit == null)
				return new ChatbotResponse { Text = ChatbotResources.Get("Unit_NotFound", culture, unitName), Processed = true };

			var unitCalls = new List<Call>();
			foreach (var call in await GetActiveCallsWithDispatchesAsync(session.DepartmentId, forUnits: true))
			{
				if (call.UnitDispatches != null && call.UnitDispatches.Any(d => d.UnitId == unit.UnitId))
					unitCalls.Add(call);
			}

			if (unitCalls.Count == 0)
				return new ChatbotResponse { Text = ChatbotResources.Get("UnitCalls_None", culture, unit.Name), Processed = true };

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("UnitCalls_Header", culture, unit.Name));
			sb.AppendLine("----------------------");
			foreach (var call in unitCalls.Take(MaxCallsToList))
				sb.AppendLine(ChatbotResources.Get("Calls_Line", culture, call.CallId, call.Name?.Truncate(25), call.NatureOfCall?.Truncate(40)));

			return new ChatbotResponse { Text = sb.ToString(), Processed = true };
		}

		private async Task<List<Call>> GetActiveCallsWithDispatchesAsync(int departmentId, bool forUnits)
		{
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(departmentId);
			var populated = new List<Call>();

			var orderedCalls = (activeCalls ?? new List<Call>()).OrderByDescending(c => c.LoggedOn);
			foreach (var batch in orderedCalls.Chunk(MaxConcurrentCallPopulations))
			{
				var populatedBatch = await Task.WhenAll(batch.Select(call => _callsService.PopulateCallData(call,
					getDispatches: !forUnits,
					getAttachments: false,
					getNotes: false,
					getGroupDispatches: false,
					getUnitDispatches: forUnits,
					getRoleDispatches: false,
					getProtocols: false,
					getReferences: false,
					getContacts: false)));
				populated.AddRange(populatedBatch);
			}

			return populated;
		}
	}
}
