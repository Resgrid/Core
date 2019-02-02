using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace DeleteServiceTests
	{
		public class with_the_delete_service : TestBase
		{
			protected IDeleteService _deleteService;

			protected with_the_delete_service()
			{
				_deleteService = Resolve<IDeleteService>();
			}
		}
	}
}