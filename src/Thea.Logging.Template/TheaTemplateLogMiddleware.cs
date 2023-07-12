using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Thea.JwtTokens;
using Thea.Orm;

namespace Thea.Logging.Template;

public class TheaTemplateLogMiddleware
{
    private readonly IOrmDbFactory dbFactory;
    private readonly IJwtTokenService tokenService;
    private readonly LoggerHandlerDelegate next;
    private DateTime lastTime = DateTime.MinValue;
    private readonly Dictionary<string, LogTemplate> logTemplates = new();
    private readonly List<TemplateLog> logs = new();

    public TheaTemplateLogMiddleware(LoggerHandlerDelegate next, IOrmDbFactory dbFactory, IJwtTokenService tokenService, ILogger<TheaTemplateLogMiddleware> logger)
    {
        this.next = next;
        this.dbFactory = dbFactory;
        this.tokenService = tokenService;
    }
    public async Task Invoke(LoggerHandlerContext context)
    {
        try
        {
            if (context.LogEntity != null)
            {
                var logEntityInfo = context.LogEntity;
                foreach (var logTemplate in logTemplates.Values)
                {
                    if (!string.IsNullOrEmpty(logEntityInfo.ApiUrl)
                        && logEntityInfo.ApiUrl.ToLower().Contains(logTemplate.ApiUrl.ToLower())
                        && (string.IsNullOrEmpty(logTemplate.TenantId) || logTemplate.TenantId == logEntityInfo.TenantId))
                    {
                        string tagValue = null;
                        var tenantId = logEntityInfo.TenantId;
                        string userId = logEntityInfo.UserId;
                        string userName = logEntityInfo.UserName;
                        switch (logTemplate.TagFrom)
                        {
                            case "AuthToken":
                                if (!string.IsNullOrEmpty(logTemplate.TagRegex))
                                {
                                    tagValue = Regex.Replace(logEntityInfo.Response, logTemplate.TagRegex, "${data}");
                                    if (this.tokenService.ReadToken(tagValue, out var claims))
                                    {
                                        userId = claims.Find(f => f.Type == "sub")?.Value;
                                        userName = claims.Find(f => f.Type == "name")?.Value;
                                        tenantId = claims.Find(f => f.Type == "tenant")?.Value;
                                    }
                                }
                                tagValue = userId;
                                break;
                            case "Request":
                                tagValue = Regex.Replace(logEntityInfo.Parameters, logTemplate.TagRegex, "${data}");
                                break;
                            case "Response":
                                tagValue = Regex.Replace(logEntityInfo.Response, logTemplate.TagRegex, "${data}");
                                break;
                        }
                        if (string.IsNullOrEmpty(tagValue))
                            continue;

                        var body = logTemplate.Template.Replace("{USERID}", userId)
                            .Replace("{USERNAME}", userName);
                        this.logs.Add(new TemplateLog
                        {
                            Id = logEntityInfo.Id,
                            UserId = userId,
                            ApiUrl = logEntityInfo.ApiUrl,
                            Category = logTemplate.Category,
                            TenantId = tenantId,
                            Tag = tagValue,
                            Body = body,
                            ClientIp = logEntityInfo.ClientIp,
                            CreatedAt = logEntityInfo.CreatedAt
                        });
                    }
                }
            }
            else
            {
                if (this.logs.Count > 0)
                {
                    using var repository = this.dbFactory.Create();
                    await repository.CreateAsync<TemplateLog>(this.logs);
                    this.logs.Clear();
                }
                if (DateTime.Now.Subtract(this.lastTime) > TimeSpan.FromMinutes(5))
                {
                    await this.Initialize();
                    this.lastTime = DateTime.Now;
                }
            }
            await this.next(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
    private async Task Initialize()
    {
        using var repository = this.dbFactory.Create();
        var logTemplates = await repository.QueryAsync<LogTemplate>(f => f.IsEnabled);
        var removedTemplates = this.logTemplates.Keys.Where(f => !logTemplates.Exists(t => t.Id == f)).ToList();
        if (removedTemplates != null && removedTemplates.Count > 0)
            removedTemplates.ForEach(f => this.logTemplates.Remove(f));

        foreach (var logTemplate in logTemplates)
        {
            if (!this.logTemplates.TryGetValue(logTemplate.Id, out var template))
                this.logTemplates.TryAdd(logTemplate.Id, template = logTemplate);
            if (template.ReviseTime > logTemplate.ReviseTime)
                this.logTemplates[logTemplate.Id] = logTemplate;
        }
    }
}