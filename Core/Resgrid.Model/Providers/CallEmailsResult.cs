using System.Collections.Generic;

namespace Resgrid.Model.Providers
{
    public class CallEmailsResult
    {
        public DepartmentCallEmail EmailSettings { get; set; }
        public List<CallEmail> Emails { get; set; }
    }
}