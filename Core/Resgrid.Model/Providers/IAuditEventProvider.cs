using Resgrid.Model.Events;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IAuditEventProvider
	{
		Task<bool> EnqueueAuditEventAsync(AuditEvent auditEvent);
	}
}
