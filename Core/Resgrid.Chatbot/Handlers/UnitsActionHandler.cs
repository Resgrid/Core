using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class UnitsActionHandler : IChatbotActionHandler
	{
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuthorizationService _authorizationService;

		public UnitsActionHandler(IUnitsService unitsService, ICustomStateService customStateService,
			IDepartmentsService departmentsService, IAuthorizationService authorizationService)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ListUnits;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				var departmentName = department?.Name ?? ChatbotResources.Get("Common_YourDepartment", culture);
				var unitStatuses = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(session.DepartmentId);

				if (unitStatuses == null || !unitStatuses.Any())
					return new ChatbotResponse { Text = ChatbotResources.Get("Units_None", culture), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("Units_Header", culture, departmentName));
				sb.AppendLine("----------------------");

				var listedCount = 0;
				foreach (var unitState in unitStatuses)
				{
					var unit = unitState?.Unit;
					if (unit == null || unit.DepartmentId != session.DepartmentId
						|| !await _authorizationService.CanUserViewUnitAsync(session.UserId, unit.UnitId))
						continue;

					var status = await _customStateService.GetCustomUnitStateAsync(unitState);
					var statusText = status?.ButtonText ?? ChatbotResources.Get("Personnel_Unknown", culture);
					sb.AppendLine(ChatbotResources.Get("Units_Line", culture, unit.Name, statusText));
					if (++listedCount == 15)
						break;
				}

				if (listedCount == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Units_None", culture), Processed = true };

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Units_Error", culture), Processed = false };
			}
		}
	}
}
