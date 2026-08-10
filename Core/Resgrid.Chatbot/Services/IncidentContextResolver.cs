using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	/// <inheritdoc />
	public class IncidentContextResolver : IIncidentContextResolver
	{
		/// <summary>
		/// Session-context key the ingress copies the client's open incident into. The IC app's command
		/// board sends it with every question so "PAR" means "PAR on the board I'm looking at".
		/// </summary>
		public const string IncidentCallIdContextKey = "incidentCallId";

		private readonly ICallsService _callsService;
		private readonly IIncidentCommandService _incidentCommandService;
		private readonly IIncidentResourcesService _incidentResourcesService;
		private readonly IAuthorizationService _authorizationService;

		public IncidentContextResolver(
			ICallsService callsService,
			IIncidentCommandService incidentCommandService,
			IIncidentResourcesService incidentResourcesService,
			IAuthorizationService authorizationService)
		{
			_callsService = callsService;
			_incidentCommandService = incidentCommandService;
			_incidentResourcesService = incidentResourcesService;
			_authorizationService = authorizationService;
		}

		public async Task<IncidentContext> ResolveAsync(ChatbotIntent intent, ChatbotSession session, bool includeAdHocResources = false)
		{
			var context = new IncidentContext();

			if (session == null)
				return context;

			var departmentId = session.DepartmentId;

			// 1. An explicit reference in the question always wins ("PAR for 26-1", "status of c1445").
			//    CallReferenceResolver enforces department scoping, so a foreign call reads as not-found.
			var reference = GetParameter(intent, "callRef") ?? GetParameter(intent, "callId");
			if (!string.IsNullOrWhiteSpace(reference))
			{
				var referenced = await CallReferenceResolver.ResolveAsync(_callsService, departmentId, reference);
				if (referenced == null)
					return context;

				return await LoadAsync(context, referenced, session, includeAdHocResources);
			}

			// 2. The incident the client has open. Trusted only as a hint: it still goes through the same
			//    department + view-permission checks as anything the user typed.
			if (session.Context != null
				&& session.Context.TryGetValue(IncidentCallIdContextKey, out var contextCallId)
				&& int.TryParse(contextCallId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var openCallId)
				&& openCallId > 0)
			{
				var openCall = await _callsService.GetCallByIdAsync(openCallId);
				if (openCall != null && openCall.DepartmentId == departmentId)
					return await LoadAsync(context, openCall, session, includeAdHocResources);
			}

			// 3. Fall back to what the department is actually running. One active command is unambiguous;
			//    with several, an incident the caller commands wins, and anything else asks which one.
			var activeCommands = await _incidentCommandService.GetActiveCommandsForDepartmentAsync(departmentId)
				?? new List<Resgrid.Model.IncidentCommand>();

			if (activeCommands.Count == 0)
				return context;

			var commanded = activeCommands
				.Where(c => string.Equals(c.CurrentCommanderUserId, session.UserId, StringComparison.OrdinalIgnoreCase))
				.ToList();

			var candidates = commanded.Count > 0 ? commanded : activeCommands;

			if (candidates.Count == 1)
			{
				var single = await _callsService.GetCallByIdAsync(candidates[0].CallId);
				if (single == null || single.DepartmentId != departmentId)
					return context;

				return await LoadAsync(context, single, session, includeAdHocResources);
			}

			// Several in play — surface only the ones the caller may actually see.
			foreach (var command in candidates)
			{
				var call = await _callsService.GetCallByIdAsync(command.CallId);
				if (call == null || call.DepartmentId != departmentId)
					continue;

				if (await _authorizationService.CanUserViewCallAsync(session.UserId, call.CallId))
					context.Candidates.Add(call);
			}

			if (context.Candidates.Count == 1)
				return await LoadAsync(context, context.Candidates[0], session, includeAdHocResources);

			context.IsAmbiguous = context.Candidates.Count > 1;
			return context;
		}

		private async Task<IncidentContext> LoadAsync(IncidentContext context, Resgrid.Model.Call call, ChatbotSession session, bool includeAdHocResources)
		{
			if (!await _authorizationService.CanUserViewCallAsync(session.UserId, call.CallId))
			{
				context.IsUnauthorized = true;
				return context;
			}

			context.Call = call;

			try
			{
				context.Board = await _incidentCommandService.GetCommandBoardAsync(session.DepartmentId, call.CallId);
			}
			catch (Exception ex)
			{
				// A board read failure must not take the whole answer down — the caller degrades to
				// "I couldn't read the board for that incident".
				Logging.LogException(ex, $"Chatbot incident context: board read failed for call {call.CallId}.");
				return context;
			}

			if (includeAdHocResources && context.Board?.Command != null)
			{
				try
				{
					context.AdHocUnits = await _incidentResourcesService.GetAdHocUnitsForCallAsync(session.DepartmentId, call.CallId)
						?? new List<Resgrid.Model.IncidentAdHocUnit>();
					context.AdHocPersonnel = await _incidentResourcesService.GetAdHocPersonnelForCallAsync(session.DepartmentId, call.CallId)
						?? new List<Resgrid.Model.IncidentAdHocPersonnel>();
				}
				catch (Exception ex)
				{
					// External resources are supplementary; the department's own resources still answer.
					Logging.LogException(ex, $"Chatbot incident context: ad-hoc resource read failed for call {call.CallId}.");
				}
			}

			return context;
		}

		private static string GetParameter(ChatbotIntent intent, string key)
		{
			if (intent?.Parameters == null)
				return null;

			return intent.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
				? value.Trim()
				: null;
		}
	}
}
