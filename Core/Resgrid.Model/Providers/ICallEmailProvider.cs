using System.Collections.Generic;

namespace Resgrid.Model.Providers
{
	public interface ICallEmailProvider
	{
	    CallEmailsResult GetAllCallEmailsFromServer(DepartmentCallEmail emailSettings);
			//string TestEmailSettings(DepartmentCallEmail emailSettings);
	}
}