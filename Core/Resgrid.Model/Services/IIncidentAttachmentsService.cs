using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IIncidentAttachmentsService
	{
		Task<bool> RemoveAsync(int departmentId, string userId, string reportId, string attachmentId, long expectedVersion, CancellationToken cancellationToken = default);
		Task<RmsRecordAttachment> AddAsync(int departmentId, string userId, string reportId, long expectedVersion, string fileName, string contentType, byte[] data, string description, CancellationToken cancellationToken = default, int classification = 1);
		Task<RmsRecordAttachment> GetAsync(int departmentId, string userId, string reportId, string attachmentId, string revisionId = null);
	}
}
