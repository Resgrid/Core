using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Resgrid.Web.Areas.User.Controllers
{
    // HTML number/date controls submit invariant values even when their display is localized.
    // Keep this at the work-order form boundary; API JSON and other controllers retain their binders.
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class WorkOrderFormValuesAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            if (!context.HttpContext.Request.HasFormContentType) return;
            for (var i = 0; i < context.ValueProviderFactories.Count; i++)
                if (context.ValueProviderFactories[i] is FormValueProviderFactory)
                    context.ValueProviderFactories[i] = new InvariantFormFactory();
        }
        public void OnResourceExecuted(ResourceExecutedContext context) { }
        private sealed class InvariantFormFactory : IValueProviderFactory
        {
            public async Task CreateValueProviderAsync(ValueProviderFactoryContext context)
            {
                // Reuse the framework's size/error handling before replacing only the form culture.
                var start = context.ValueProviders.Count;
                await new FormValueProviderFactory().CreateValueProviderAsync(context);
                for (var i = context.ValueProviders.Count - 1; i >= start; i--)
                    if (context.ValueProviders[i] is FormValueProvider)
                        context.ValueProviders[i] = new FormValueProvider(BindingSource.Form, context.ActionContext.HttpContext.Request.Form, CultureInfo.InvariantCulture);
            }
        }
    }
}
