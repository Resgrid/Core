using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Resgrid.Web.Areas.User.Controllers
{
    // HTML number/date controls submit invariant values regardless of the display language.
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class WorkOrderFormCultureAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context) => context.ValueProviderFactories.Insert(0,new InvariantFormFactory());
        public void OnResourceExecuted(ResourceExecutedContext context) { }
        private sealed class InvariantFormFactory : IValueProviderFactory
        {
            public async Task CreateValueProviderAsync(ValueProviderFactoryContext context)
            {
                var request=context.ActionContext.HttpContext.Request;
                if(request.HasFormContentType) context.ValueProviders.Add(new FormValueProvider(BindingSource.Form,await request.ReadFormAsync(),CultureInfo.InvariantCulture));
            }
        }
    }
}
