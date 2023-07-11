using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
        var context = this.contextAccessor.HttpContext;
        if (context == null) return base.SendAsync(request, cancellationToken);
        if (!request.Headers.Contains("TraceId"))
        {
            if (TheaLogScope.Current != null)
            {
                TheaLogScope.Current.State.Sequence++;

                request.Headers.Add("TraceId", TheaLogScope.Current.State.TraceId);
                request.Headers.Add("Sequence", TheaLogScope.Current.State.Sequence.ToString());
                if (!string.IsNullOrEmpty(TheaLogScope.Current.State.Tag))
                    request.Headers.Add("Tag", TheaLogScope.Current.State.Tag);
            }
            else
            {
                request.Headers.Add("TraceId", context.TraceIdentifier.Replace(":", "-"));
                int sequence = 2;
                if (context.Request.Headers.TryGetValue("Sequence", out var sequences))
                    sequence = int.Parse(sequences.ToString()) + 1;
                request.Headers.Add("Sequence", sequence.ToString());
                if (context.Request.Headers.TryGetValue("Tag", out var tag))
                    request.Headers.Add("Tag", tag.ToString());
            }
        }
        return base.SendAsync(request, cancellationToken);
    }
}
