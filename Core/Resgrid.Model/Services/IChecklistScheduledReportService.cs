using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IChecklistScheduledReportService
	{
		Task<EmailNotification> BuildAsync(ScheduledTask queuedTask);
	}
}
