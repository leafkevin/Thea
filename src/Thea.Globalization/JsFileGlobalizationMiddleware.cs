﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Thea.Globalization;

public class JsFileGlobalizationMiddleware
{
    private readonly RequestDelegate next;
    private readonly IGlobalizationResource grService;
    private readonly IConfiguration configuration;
    private readonly string filterFileExtensions;

    public JsFileGlobalizationMiddleware(RequestDelegate next, IGlobalizationResource grService, IConfiguration configuration)
    {
        this.next = next;
        this.grService = grService;
        this.configuration = configuration;
        this.filterFileExtensions = configuration["Globalization:FilterFileExtensions"];
    }

    public async Task Invoke(HttpContext context)
    {
        var urlPath = context.Request.Path.ToString();
        //只拦截所有js请求
        if (!urlPath.EndsWith(this.filterFileExtensions))
        {
            await next(context);
            return;
        }
        var language = this.GetCulture(context);
        using var reader = File.OpenText("wwwroot/" + urlPath);
        var respBody = await reader.ReadToEndAsync();
        respBody = this.ReplaceConfigurations(respBody, @"\[\[\{(\w+(:\w+)+)\}\]\]");
        respBody = this.ReplaceConfigurations(respBody, @"\[\[\{(\w+)\}\]\]");
        respBody = this.ReplaceTags(respBody, language, @"\[\[([\w\-]+)\]\]");
        context.Response.ContentType = "text/javascript";
        await context.Response.WriteAsync(respBody);
    }
    private string ReplaceConfigurations(string respBody, string pattern)
    {
        //[[{PA_SHOP_SITE}]]                @"\[\[\{(\w+)\}\]\]"
        //[[{PA_SHOP_SITE:PA_SHOP_SITE}]]   @"\[\[\{(\w+(:\w+)+)\}\]\]"
        return Regex.Replace(respBody, pattern, match =>
        {
            var tagName = match.Groups[1].Value;
            return this.configuration[tagName];
        });
    }
    private string ReplaceTags(string respBody, string cultureName, string pattern)
    {
        //[[PA_SHOP_SITE]]      @"\[\[\([\w\-]+)\]\]"
        var content = Regex.Replace(respBody, pattern, match =>
        {
            var tagName = match.Groups[1].Value;
            return this.grService.GetGlossary(tagName, cultureName);
        });
        return content;
    }
    private string GetCulture(HttpContext context)
    {
        string language = context.Request.Query["lang"];
        if (!string.IsNullOrEmpty(language))
        {
            context.Response.Cookies.Append("lang", language);
            return language;
        }
        language = context.Request.Cookies["lang"];
        if (!string.IsNullOrEmpty(language))
            return language;
        return this.configuration["Globalization:DefaultLanguage"];
    }
}