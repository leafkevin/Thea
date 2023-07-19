using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using Thea.Orm;

namespace Thea.Globalization;

class GlobalizationResource : IGlobalizationResource
{
    private readonly string dbKey;
    private readonly IConfiguration configuration;
    private readonly IHttpContextAccessor contextAccessor;
    private readonly ConnectionMultiplexer connection;
    private readonly IOrmDbFactory dbFactory;
    private readonly int databaseIndex = -1;
    private readonly string prefix;

    public GlobalizationResource(IConfiguration configuration, IHttpContextAccessor contextAccessor, IOrmDbFactory dbFactory)
    {
        this.configuration = configuration;
        this.contextAccessor = contextAccessor;
        this.dbFactory = dbFactory;

        this.dbKey = configuration["Globalization:DbKey"];
        if (string.IsNullOrEmpty(this.dbKey))
            throw new ArgumentNullException("appsettings.json中缺少Globalization:ConnectionString配置项");
        var redisUrl = configuration["Globalization:Redis:Url"];
        if (string.IsNullOrEmpty(redisUrl))
            throw new ArgumentNullException("appsettings.json中缺少Globalization:Redis:Url配置项");
        this.prefix = configuration["Globalization:Redis:Prefix"] ?? string.Empty;
        var database = configuration["Globalization:Redis:Database"];
        if (!string.IsNullOrEmpty(database) && int.TryParse(database, out var dbIndex))
            this.databaseIndex = dbIndex;
        this.connection = ConnectionMultiplexer.Connect(redisUrl);
    }
    public string GetGlossary(string tagName, string language = null, int lifetimeMinutes = 5)
    {
        if (string.IsNullOrEmpty(tagName))
            throw new ArgumentNullException(tagName);
        language ??= this.GetCulture();
        if (!this.TryGetCache(tagName, language, out var result) && !string.IsNullOrEmpty(result))
            this.SetCache(tagName, language, result, lifetimeMinutes);
        if (string.IsNullOrEmpty(result))
            return string.Format("Unkown Tag : [{0}]", tagName);
        return result;
    }
    public async Task<string> GetGlossaryAsync(string tagName, string language = null, int lifetimeMinutes = 5)
    {
        if (string.IsNullOrEmpty(tagName))
            throw new ArgumentNullException(tagName);
        language ??= this.GetCulture();
        (var hasCache, var tagValue) = await this.GetCacheAsync(tagName, language);
        if (!hasCache && !string.IsNullOrEmpty(tagValue))
            await this.SetCacheAsync(tagName, language, tagValue, lifetimeMinutes);
        if (string.IsNullOrEmpty(tagValue))
            return string.Format("Unkown Tag : [{0}]", tagName);
        return tagValue;
    }
    public string GetCulture()
    {
        var context = this.contextAccessor.HttpContext;
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
    private bool TryGetCache(string tagName, string language, out string result)
    {
        result = null;
        var cacheKey = $"gr.{tagName}.{language}";
        var database = this.connection.GetDatabase(this.databaseIndex);
        var redisValue = database.StringGet(this.prefix + cacheKey);
        if (redisValue.IsNull)
        {
            using var repository = this.dbFactory.Create(this.dbKey);
            result = repository.From<Glossary>()
                .Where(f => f.Language == language && f.Tag == tagName)
                .Select(f => f.Text)
                .First();
            return false;
        }
        result = redisValue.ToString().JsonTo<string>();
        return true;
    }
    private async Task<(bool, string)> GetCacheAsync(string tagName, string language)
    {
        var cacheKey = $"gr.{tagName}.{language}";
        var database = this.connection.GetDatabase(this.databaseIndex);
        var redisValue = await database.StringGetAsync(this.prefix + cacheKey);
        if (redisValue.IsNull)
        {
            using var repository = this.dbFactory.Create(this.dbKey);
            var result = await repository.From<Glossary>()
                .Where(f => f.Language == language && f.Tag == tagName)
                .Select(f => f.Text)
                .FirstAsync();
            return (false, result);
        }
        return (true, redisValue.ToString().JsonTo<string>());
    }
    private void SetCache(string tagName, string language, string tagValue, int lifetimeMinutes = 5)
    {
        var cacheKey = $"gr.{tagName}.{language}";
        var database = this.connection.GetDatabase(this.databaseIndex);
        database.StringSet(this.prefix + cacheKey, tagValue.ToJson(), TimeSpan.FromMinutes(lifetimeMinutes));
    }
    private async Task SetCacheAsync(string tagName, string language, string tagValue, int lifetimeMinutes = 5)
    {
        var cacheKey = $"gr.{tagName}.{language}";
        var database = this.connection.GetDatabase(this.databaseIndex);
        await database.StringSetAsync(this.prefix + cacheKey, tagValue.ToJson(), TimeSpan.FromMinutes(lifetimeMinutes));
    }
}
