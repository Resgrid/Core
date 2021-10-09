using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Web.Helpers;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3.Models.BigBoard
{
	namespace BigBoardX
	{
		public class BigBoardMapModel
		{
			public BigBoardMapModel()
			{
				MapMakerInfos = new List<MapMakerInfo>();
			}

			public double CenterLat { get; set; }
			public double CenterLon { get; set; }
			public int ZoomLevel { get; set; }
			public List<MapMakerInfo> MapMakerInfos { get; set; }
		}

		public class BigBoardModel
		{
			public ICollection<PersonnelViewModel> Personnel { get; set; }
			public ICollection<UnitViewModel> Units { get; set; }
			public ICollection<CallViewModel> Calls { get; set; }
			public BigBoardMapModel MapModel { get; set; }
			public int RefreshTime { get; set; }
			public string WeatherUnit { get; set; }
			public ICollection<GroupViewModel> Groups { get; set; }
		}

		public class UnitViewModel
		{
			public int UnitId { get; set; }
			public string Name { get; set; }
			public string Type { get; set; }
			public string State { get; set; }
			public string StateCss { get; set; }
			public string StateStyle { get; set; }
			public DateTime? Timestamp { get; set; }
			public int? DestinationId { get; set; }
			public string Note { get; set; }
			public decimal? Latitude { get; set; }
			public decimal? Longitude { get; set; }
			public string GroupName { get; set; }
			public int GroupId { get; set; }
			public string DestinationName { get; set; }
		}

		public class CallViewModel
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public string LoggingUser { get; set; }
			public string Priority { get; set; }
			public string PriorityCss { get; set; }
			public string State { get; set; }
			public string StateCss { get; set; }
			public DateTime Timestamp { get; set; }
			public string Address { get; set; }
		}

		public class GroupViewModel
		{
			public int GroupId { get; set; }
			public string Name { get; set; }
			public string Type { get; set; }
		}

		public class PersonnelViewModel
		{
			public static async Task<PersonnelViewModel> Create(string name, ActionLog actionLog, UserState userState, Department department, DepartmentGroup respondingToDepartment, DepartmentGroup group, List<PersonnelRole> roles, string callNumber)
			{
				DateTime updateDate = TimeConverterHelper.TimeConverter(DateTime.UtcNow, department);

				string status = "";
				string statusCss = "";
				string state = "";
				string stateCss = "";
				string stateStyle = "";
				string statusStyle = "";
				double? latitude = null;
				double? longitude = null;
				int statusValue = 0;
				double eta = 0;
				int destinationType = 0;

				if (userState != null)
				{
					if (userState.State <= 25)
					{
						if (userState.State == 0)
						{
							state = "Available";
							stateCss = "label-default";
						}
						else if (userState.State == 1)
						{
							state = "Delayed";
							stateCss = "label-warning";
						}
						else if (userState.State == 2)
						{
							state = "Unavailable";
							stateCss = "label-danger";
						}
						else if (userState.State == 3)
						{
							state = "Committed";
							stateCss = "label-info";
						}
						else if (userState.State == 4)
						{
							state = "On Shift";
							stateCss = "label-info";
						}
					}
					else
					{
						var customState = await CustomStatesHelper.GetCustomState(department.DepartmentId, userState.State);

						if (customState != null)
						{
							state = customState.ButtonText;
							stateCss = "label-default";
							stateStyle = string.Format("color:{0};background-color:{1};", customState.TextColor, customState.ButtonColor);
						}
						else
						{
							state = "Unknown";
							stateCss = "label-default";
						}
					}
				}
				else
				{
					state = "Available";
					stateCss = "label-default";
				}


				if (actionLog == null)
				{
					status = "Standing By";
					statusCss = "label-default";
				}
				else
				{
					updateDate = TimeConverterHelper.TimeConverter(actionLog.Timestamp, department);
					eta = actionLog.Eta;
					destinationType = actionLog.DestinationType.GetValueOrDefault();

					statusValue = actionLog.ActionTypeId;

					if (actionLog.ActionTypeId <= 25)
					{
						if (actionLog.ActionTypeId == 1)
						{
							status = "Not Responding";
							statusCss = "label-danger";
						}
						else if (actionLog.ActionTypeId == 2)
						{
							status = "Responding";
							statusCss = "label-success";

							if (!String.IsNullOrWhiteSpace(actionLog.GeoLocationData))
							{
								var cords = actionLog.GetCoordinates();

								latitude = cords.Latitude;
								longitude = cords.Longitude;
							}
						}
						else if (actionLog.ActionTypeId == 0)
						{
							status = "Standing By";
							statusCss = "label-default";
						}
						else if (actionLog.ActionTypeId == 3)
						{
							status = "On Scene";
							statusCss = "label-inverse";
						}
						else if (actionLog.ActionTypeId == 4)
						{
							if (respondingToDepartment == null)
							{
								status = "Available Station";
							}
							else
							{
								status = string.Format("Available at {0}", respondingToDepartment.Name);
							}

							statusCss = "label-default";
						}
						else if (actionLog.ActionTypeId == 5)
						{
							statusCss = "label-success";

							if (!String.IsNullOrWhiteSpace(actionLog.GeoLocationData))
							{
								var cords = actionLog.GetCoordinates();

								latitude = cords.Latitude;
								longitude = cords.Longitude;
							}

							if (respondingToDepartment == null)
							{
								status = "Responding to Station";
							}
							else
							{
								status = string.Format("Responding to {0}", respondingToDepartment.Name);
							}
						}
						else if (actionLog.ActionTypeId == 6)
						{
							statusCss = "label-success";

							if (!String.IsNullOrWhiteSpace(actionLog.GeoLocationData))
							{
								var cords = actionLog.GetCoordinates();

								latitude = cords.Latitude;
								longitude = cords.Longitude;
							}

							if (!actionLog.DestinationId.HasValue)
							{
								status = "Responding to Call";
							}
							else
							{
								if (!String.IsNullOrWhiteSpace(callNumber))
									status = string.Format("Responding to Call {0}", callNumber);
								else
									status = string.Format("Responding to Call {0}", actionLog.DestinationId);
							}
						}
					}
					else
					{
						var customStatus = await CustomStatesHelper.GetCustomState(department.DepartmentId, actionLog.ActionTypeId);

						if (customStatus != null)
						{
							status = customStatus.ButtonText;
							statusCss = "label-default";
							statusStyle = string.Format("color:{0};background-color:{1};", customStatus.TextColor, customStatus.ButtonColor);

							if (!String.IsNullOrWhiteSpace(actionLog.GeoLocationData))
							{
								var cords = actionLog.GetCoordinates();

								latitude = cords.Latitude;
								longitude = cords.Longitude;
							}
						}
						else
						{
							status = "Unknown";
							statusCss = "label-default";
						}
					}
				}

				string groupName = "";
				int groupId = 0;
				if (group != null)
				{
					groupName = group.Name;
					groupId = group.DepartmentGroupId;
				}

				var newRoles = new StringBuilder();

				foreach (var role in roles)
				{
					if (newRoles.Length > 0)
						newRoles.Append(", " + role.Name);
					else
						newRoles.Append(role.Name);
				}

				return new PersonnelViewModel
				{
					Name = name,
					Status = status,
					StatusCss = statusCss,
					State = state,
					StateCss = stateCss,
					UpdatedDate = updateDate,
					Group = groupName,
					Roles = newRoles.ToString(),
					GroupId = groupId,
					StateStyle = stateStyle,
					StatusStyle = statusStyle,
					Latitude = latitude,
					Longitude = longitude,
					StatusValue = statusValue,
					Eta = eta,
					DestinationType = destinationType
				};
			}
			public string Name { get; set; }
			public string Status { get; set; }
			public string StatusCss { get; set; }
			public string StatusStyle { get; set; }
			public string State { get; set; }
			public string StateCss { get; set; }
			public string StateStyle { get; set; }
			public DateTime UpdatedDate { get; set; }
			public string Group { get; set; }
			public string Roles { get; set; }
			public int GroupId { get; set; }
			public double? Latitude { get; set; }
			public double? Longitude { get; set; }
			public int StatusValue { get; set; }
			public double Eta { get; set; }
			public int DestinationType { get; set; }
			public string CallNumber { get; set; }
		}

		public static class CssExtensions
		{

			public static string ToCallPriorityDisplayText(this Call call)
			{
				if (call == null) return "";

				if (call.Priority == (int)CallPriority.Low)
					return "Low";
				if (call.Priority == (int)CallPriority.Medium)
					return "Medium";
				if (call.Priority == (int)CallPriority.High)
					return "High";
				if (call.Priority == (int)CallPriority.Emergency)
					return "Emergency";

				return "";
			}

			public static string ToCallPriorityCss(this Call call)
			{
				if (call == null) return "";

				if (call.Priority == (int)CallPriority.Low)
					return "call-priority-low";
				if (call.Priority == (int)CallPriority.Medium)
					return "call-priority-medium";
				if (call.Priority == (int)CallPriority.High)
					return "call-priority-high";
				if (call.Priority == (int)CallPriority.Emergency)
					return "call-priority-emergency";

				return "";
			}


			public static string ToCallStateDisplayText(this Call call)
			{
				if (call == null) return "";

				if (call.State == (int)CallStates.Active)
					return "Active";
				if (call.State == (int)CallStates.Cancelled)
					return "Cancelled";
				if (call.State == (int)CallStates.Closed)
					return "Closed";

				return "";
			}

			public static string ToCallStateCss(this Call call)
			{
				if (call == null) return "";

				if (call.State == (int)CallStates.Active)
					return "call-state-active";
				if (call.State == (int)CallStates.Cancelled)
					return "call-state-cancelled";
				if (call.State == (int)CallStates.Closed)
					return "call-state-closed";

				return "";
			}


			public static string ToStateDisplayText(this UnitState state)
			{
				if (state == null) return "Unknown";

				if (state.State == (int)UnitStateTypes.Available)
					return "Available";
				if (state.State == (int)UnitStateTypes.Unavailable)
					return "Unavailable";
				if (state.State == (int)UnitStateTypes.OutOfService)
					return "Out of Service";
				if (state.State == (int)UnitStateTypes.Committed)
					return "Committed";
				if (state.State == (int)UnitStateTypes.Delayed)
					return "Delayed";
				if (state.State == (int)UnitStateTypes.Responding)
					return "Responding";
				if (state.State == (int)UnitStateTypes.OnScene)
					return "On Scene";
				if (state.State == (int)UnitStateTypes.Staging)
					return "Staging";
				if (state.State == (int)UnitStateTypes.Returning)
					return "Returning";
				if (state.State == (int)UnitStateTypes.Cancelled)
					return "Cancelled";
				if (state.State == (int)UnitStateTypes.Released)
					return "Released";
				if (state.State == (int)UnitStateTypes.Manual)
					return "Manual";
				if (state.State == (int)UnitStateTypes.Enroute)
					return "Enroute";

				return "Unknown";
			}

			public static string ToStateCss(this UnitState state)
			{
				if (state == null)
					return "";

				if (state.State == (int)UnitStateTypes.Available)
					return "";
				if (state.State == (int)UnitStateTypes.Unavailable)
					return "label-danger";
				if (state.State == (int)UnitStateTypes.OutOfService)
					return "label-danger";
				if (state.State == (int)UnitStateTypes.Committed)
					return "label-info";
				if (state.State == (int)UnitStateTypes.Delayed)
					return "label-warning";
				if (state.State == (int)UnitStateTypes.Responding)
					return "label-success";
				if (state.State == (int)UnitStateTypes.OnScene)
					return "label-onscene";
				if (state.State == (int)UnitStateTypes.Staging)
					return "label-primary";
				if (state.State == (int)UnitStateTypes.Returning)
					return "label-returning";
				if (state.State == (int)UnitStateTypes.Cancelled)
					return "label-default";
				if (state.State == (int)UnitStateTypes.Released)
					return "label-default";
				if (state.State == (int)UnitStateTypes.Manual)
					return "label-default";
				if (state.State == (int)UnitStateTypes.Enroute)
					return "label-enroute";

				return "";
			}

			public static string ToCss(this UserStateTypes type)
			{
				switch (type)
				{
					case UserStateTypes.Available: return "";
					case UserStateTypes.Delayed: return "label-warning";
					case UserStateTypes.Unavailable: return "label-important";
					case UserStateTypes.Committed: return "label-info";
					default: return "";
				}
			}

			public static string ToCss(this ActionTypes type)
			{
				switch (type)
				{
					case ActionTypes.StandingBy: return "";
					case ActionTypes.NotResponding: return "label-important";
					case ActionTypes.Responding: return "label-success";
					case ActionTypes.OnScene: return "label-inverse";
					case ActionTypes.AvailableStation: return "";
					case ActionTypes.RespondingToStation: return "label-success";
					case ActionTypes.RespondingToScene: return "label-success";
					default: return "";
				}
			}
		}
	}
}
