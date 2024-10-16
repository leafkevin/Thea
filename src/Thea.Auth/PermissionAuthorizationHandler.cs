using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Trolley;

namespace Thea.Auth;

public class RoledResourceAuthorizationRequirement : IAuthorizationRequirement { }

public class PermissionAuthorizationHandler : AuthorizationHandler<RoledResourceAuthorizationRequirement>
{
    private readonly IDistributedCache redisCache;
    private readonly HttpContextAccessor contextAccessor;
    private readonly IOrmDbFactory dbFactory;
    private readonly IConfiguration configuration;
    public PermissionAuthorizationHandler(IServiceProvider serviceProvider)
    {
        this.redisCache = serviceProvider.GetService<IDistributedCache>();
        this.contextAccessor = serviceProvider.GetService<HttpContextAccessor>();
        this.dbFactory = serviceProvider.GetService<IOrmDbFactory>();
        this.configuration = serviceProvider.GetService<IConfiguration>();
    }
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RoledResourceAuthorizationRequirement requirement)
    {
        if (!context.User.HasClaim(c => c.Type == ClaimTypes.Role) && !context.User.HasClaim(c => c.Type == "role"))
            context.Fail();
        var roles = context.User.FindFirst("role").Value;
        var roleIds = roles.Split(',');
        var requestUrl = this.contextAccessor.HttpContext.Request.Path;
        var dbKey = this.configuration.GetValue<string>("Authorization:DbKey");
        if (string.IsNullOrEmpty(dbKey)) throw new ArgumentNullException("");
        foreach (var roleId in roleIds)
        {
            var actionUrls = await this.redisCache.GetOrCreateAsync($"sys.role.{roleId}", async () =>
            {
                var repository = this.dbFactory.CreateRepository(dbKey);
                return await repository.From<Permission, Resource>()
                    .InnerJoin((x, y) => x.ResourceId == y.ResourceId)
                    .Where((x, y) => x.RoleId == roleId && x.IsEnabled && y.IsEnabled)
                    .Select((x, y) => y.ActionUrl)
                    .ToListAsync();
            });
            if (this.IsSatisfied(requestUrl, actionUrls))
            {
                context.Succeed(requirement);
                break;
            }
        }
        context.Fail();
    }
    private bool IsSatisfied(string requestUrl, List<string> actionUrls)
    {
        foreach (var actionUrl in actionUrls)
        {
            if (actionUrl.Contains(requestUrl))
                return true;
        }
        return false;
    }
}