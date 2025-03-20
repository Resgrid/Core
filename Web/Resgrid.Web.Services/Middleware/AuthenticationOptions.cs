using Microsoft.AspNetCore.Authentication;

namespace Resgrid.Web.Services.Middleware
{
	public class ResgridAuthenticationOptions : AuthenticationSchemeOptions
    {
	    public ResgridAuthenticationOptions()
        {
        }

        public bool AllowInsecureProtocol
        {
            get; set;
        }

        public new ResgridAuthenticationEvents Events

        {
            get { return (ResgridAuthenticationEvents)base.Events; }

            set { base.Events = value; }
        }
    }
}
