using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Thea.Logging;

namespace Thea.Web;

public class TheaHttpMessageHandlerBuilder : HttpMessageHandlerBuilder
{
    private string _name;
    private readonly IServiceProvider serviceProvider;
    public TheaHttpMessageHandlerBuilder(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        var messageHandler = serviceProvider.GetService<TheaHttpMessageHandler>();
        this.AdditionalHandlers.Add(messageHandler);
    }
    public override string Name
    {
        get => _name;
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            _name = value;
        }
    }
    public override IServiceProvider Services => this.serviceProvider;
    public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
    public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();
    public override HttpMessageHandler Build()
    {
        if (PrimaryHandler == null)
        {
            string message = $"The '{nameof(PrimaryHandler)}' must not be null.";
            throw new InvalidOperationException(message);
        }
        return CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }
}

public sealed class TheaHttpMessageHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor contextAccessor;
    public TheaHttpMessageHandler(IHttpContextAccessor contextAccessor) => this.contextAccessor = contextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains("TraceId"))
        {
            var context = this.contextAccessor.HttpContext;
            var traceId = StateScope.State?.TraceId ?? context?.TraceIdentifier;
            if (!string.IsNullOrEmpty(traceId))
                request.Headers.Add("TraceId", traceId);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
