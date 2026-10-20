using Microsoft.AspNetCore.Mvc.Filters;

namespace Crooked.Services
{
    // Marks an endpoint that changes data: after a successful (2xx) response, open pages showing
    // these topics are told to reload. Usage: [LiveUpdate(LiveTopics.Products)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class LiveUpdateAttribute : ActionFilterAttribute
    {
        private readonly string[] _topics;

        public LiveUpdateAttribute(params string[] topics)
        {
            _topics = topics;
        }

        public override void OnResultExecuted(ResultExecutedContext context)
        {
            var status = context.HttpContext.Response.StatusCode;
            if (context.Exception == null && status is >= 200 and < 300)
            {
                context.HttpContext.RequestServices.GetRequiredService<LiveUpdates>().Publish(_topics);
            }
        }
    }
}
