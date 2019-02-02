using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.EmailProvider
{
	public class CallEmailProvider : ICallEmailProvider
	{
		public CallEmailsResult GetAllCallEmailsFromServer(DepartmentCallEmail emailSettings)
		{
			var result = new CallEmailsResult();

			// TODO: Remove me

			return result;
		}
	}
}
