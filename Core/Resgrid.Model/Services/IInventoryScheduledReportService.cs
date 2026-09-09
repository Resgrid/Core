using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IInventoryScheduledReportService
	{
		Task<EmailNotification> BuildAsync(ScheduledTask queuedTask);
		/// <summary>Revalidates the successfully built delivery for this same queued task immediately before email handoff.</summary>
		Task ValidateAsync(ScheduledTask queuedTask);
	}
}
