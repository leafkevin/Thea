using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Thea.Web;

public class ProxyHttpMessageHandlerBuilder : HttpMessageHandlerBuilder
{
    public ProxyHttpMessageHandlerBuilder(IServiceProvider serviceProvider)
    {
        this.Services = serviceProvider;
    }
    public virtual bool IsIgnoreSslError { get; set; }
    public virtual string ProxyAddress { get; set; }
    public virtual string User { get; set; }
    public virtual string Password { get; set; }

    public override string Name { get; set; }
    public override HttpMessageHandler PrimaryHandler { get; set; }
    public override IList<DelegatingHandler> AdditionalHandlers => new List<DelegatingHandler>();
    public override IServiceProvider Services { get; }

    public override HttpMessageHandler Build()
    {
        if (this.PrimaryHandler == null)
        {
            if (string.IsNullOrEmpty(this.ProxyAddress))
                throw new ArgumentNullException(nameof(this.ProxyAddress));

            var wbProxy = new WebProxy(this.ProxyAddress);
            if (!string.IsNullOrEmpty(this.User) && !string.IsNullOrEmpty(this.Password))
                wbProxy.Credentials = new NetworkCredential(this.User, this.Password);

            var proxyHandler = new HttpClientHandler
            {
                UseCookies = true,
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                Proxy = wbProxy
            };
            var hostBuilderContext = this.Services.GetService<HostBuilderContext>();
            if (this.IsIgnoreSslError)
                proxyHandler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;

            this.PrimaryHandler = proxyHandler;
        }
        return CreateHandlerPipeline(this.PrimaryHandler, this.AdditionalHandlers);
    }
}
