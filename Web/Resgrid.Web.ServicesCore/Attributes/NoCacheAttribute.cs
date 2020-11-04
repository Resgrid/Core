//using System;
//using System.Web;
//using Microsoft.AspNetCore.Mvc.Filters;

//namespace Resgrid.Web.Services.Attributes
//{
//	public class NoCacheAttribute : ActionFilterAttribute
//	{
//		public override void OnResultExecuting(ResultExecutingContext filterContext)
//		{
//			filterContext.HttpContext.Response..Cache.SetExpires(DateTime.UtcNow.AddDays(-1));
//			filterContext.HttpContext.Response.Cache.SetValidUntilExpires(false);
//			filterContext.HttpContext.Response.Cache.SetRevalidation(HttpCacheRevalidation.AllCaches);
//			filterContext.HttpContext.Response.Cache.SetCacheability(HttpCacheability.NoCache);
//			filterContext.HttpContext.Response.Cache.SetNoStore();

//			base.OnResultExecuting(filterContext);
//		}
//	}
//}
