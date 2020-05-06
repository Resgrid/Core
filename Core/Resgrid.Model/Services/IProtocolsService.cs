using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IProtocolsService
	{
		List<DispatchProtocol> GetAllProtocolsForDepartment(int departmentId);
		DispatchProtocol SaveProtocol(DispatchProtocol protocol);
		DispatchProtocol GetProcotolById(int id);
		void DeleteProtocol(int id);
		List<DispatchProtocolTrigger> DetermineActiveTriggers(DispatchProtocol protocol, Call call);
		List<DispatchProtocol> ProcessTriggers(List<DispatchProtocol> protocols, Call call);
		DispatchProtocolAttachment GetAttachmentById(int protocolAttachmentId);
	}
}
