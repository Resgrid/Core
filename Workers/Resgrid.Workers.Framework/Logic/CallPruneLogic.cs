using Resgrid.Model;
using Resgrid.Model.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	public class CallPruneLogic
	{
		private readonly ICallsService _callsService;

		public CallPruneLogic()
		{
			_callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
		}

		public async Task<Tuple<bool, string>> Process(CallPruneQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.PruneSettings != null)
			{
				try
				{
					var calls = await _callsService.GetActiveCallsByDepartmentAsync(item.PruneSettings.DepartmentId);

					if (calls != null && calls.Count > 0)
					{
						var emailImportCalls = calls.Where(x => x.CallSource == (int)CallSources.EmailImport || x.CallSource == (int)CallSources.AudioImport);
						var userCalls = calls.Where(x => x.CallSource == (int)CallSources.User);

						if (item.PruneSettings.PruneEmailImportedCalls.HasValue &&
								item.PruneSettings.PruneEmailImportedCalls.Value & item.PruneSettings.EmailImportCallPruneInterval.HasValue)
						{
							if (emailImportCalls.Any())
							{
								foreach (var call in emailImportCalls)
								{
									if (call.LoggedOn.AddMinutes(item.PruneSettings.EmailImportCallPruneInterval.Value) < DateTime.UtcNow)
									{
										call.State = (int)CallStates.Closed;
										call.ClosedOn = DateTime.UtcNow;
										call.CompletedNotes = "Call automatically closed by the system.";
										call.ClosedByUserId = item.PruneSettings.Department.ManagingUserId;

										await _callsService.SaveCallAsync(call);
									}
								}
							}
						}

						if (item.PruneSettings.PruneUserEnteredCalls.HasValue &&
								item.PruneSettings.PruneUserEnteredCalls.Value & item.PruneSettings.UserCallPruneInterval.HasValue)
						{
							if (userCalls.Any())
							{
								foreach (var call in userCalls)
								{
									if (call.LoggedOn.AddMinutes(item.PruneSettings.UserCallPruneInterval.Value) < DateTime.UtcNow)
									{
										call.State = (int)CallStates.Closed;
										call.ClosedOn = DateTime.UtcNow;
										call.CompletedNotes = "Call automatically closed by the system.";
										call.ClosedByUserId = item.PruneSettings.Department.ManagingUserId;

										await _callsService.SaveCallAsync(call);
									}
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					success = false;
					result = ex.ToString();
				}
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
