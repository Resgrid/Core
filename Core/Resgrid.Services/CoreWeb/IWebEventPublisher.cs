using Resgrid.Model.Events;

namespace Resgrid.Services.CoreWeb
{
	public interface IWebEventPublisher
	{
		void Publish(IDepartmentEvent message);
	}
}
