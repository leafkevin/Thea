#### 介绍
Thea框架是插拔式的集成框架，基于.NET CORE 6.0版本开发的，可以根据需要自由选择组件。
主要功能包括：
1.  序列化
2.  Logging日志
3.  告警
4.  日志告警
5.  JwtToken组件
6.  Web组件
7.  全球化组件
8.  Orm组件
9.  Job组件
10.  消息驱动组件

#### 序列化组件
TheaJsonSerializer类，是json序列化类，使用原生System.Text.Json完成的。
6.0版本之前不支持循环引用，要支持循环引用，需要配置`ReferenceHandler = ReferenceHandler.IgnoreCycles`
.NET CORE Json序列化类，不支持非同类型范反序列化，要想支持，需要增加自定义类型转换器，继承于`JsonConverter<int>`类。
Thea框架提供了多种类型转换器，能够完成类似于从字符串"123"到123整数、长整型的转换功能。
同时增加两个扩展方法ToJson，JsonTo方法。

#### Logging日志组件
.NET CORE中的LogScope，允许在该日志上下文中共享数据，其上下文可以对其进行更改。
TheaLogScope包含了TraceId，Tag，Sequence三个关键的属性，TraceId：跟踪ID，Tag:特定场景值，如：订单ID等
同时，扩展了.NET CORE日志接口，增加了Tag参数。
可以通过调用ILogger.AddTag方法，在当前的LogScope中添加Tag值，比如：订单ID等，方便后续通过Tag(订单ID)来查询相关请求日志。

Thea日志组件要配合Web组件一起使用，日志组件的工作流程如下：
当前请求线程，组装好日志数据，推送到一个后台线程的消息队列中，这个后台线程把收集到的所有日志，并每隔10秒钟或是超过指定条数(默认100)，再批量推送到es中。
在推送完成后，如果有注册日志处理器，将执行日志处理器的内容，比如：日志分析，日志告警，自定义模板日志记录等等，可以扩展各种日志处理器。

整体日志架构图
![日志架构](https://github.com/leafkevin/Thea/assets/12764314/82e0861e-3de2-4255-9535-4cbe6fa48134)


#### Alarm告警组件
目前使用的是钉钉告警，在钉钉中创建一个告警群，或是已有的工作群也可以。
创建一个自定义机器人，把token和secret都保存下来，配置到告警组件中，就可以了，配置如下：
``` json
{
  "Alarm": {
    "Token": "uhNwLZeYHoAOUPCxb5QelhXEnBNPFuOj",
	"Secret": "uhNwLZeYHoAOUPCxb5QelhXEnBNPFuOj"
  },
}
```
#### 日志告警组件
对接日志和告警组件，如果Warnning以上级别的日志，将会产生告警。
每隔10分钟，进行报警一次，最多报警2次，开头一次，结尾一次。

#### JwtToken组件
提供了生成、读取、验证JWT token的服务方法，生成jwt token的加密算法推荐RSA。
UserToken类包装了所有用于生成token的元素，没有值的属性将不生成到token中。
jwt token的验证，是通过设置TokenValidationParameters类实例完成的。
``` csharp
var rsaPublicKey = builder.Configuration["JwtAuth:RsaPublicKey"];
options.TokenValidationParameters = new TokenValidationParameters
{
    NameClaimType = JwtClaimTypes.Name,
    ValidateLifetime = true,
    RoleClaimType = JwtClaimTypes.Role,
    ValidateIssuer = true,
    ValidIssuer = "https://xxx.com",
    ValidateAudience = true,
    ValidAudiences = new string[] { "https://xxx.com" },
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new RsaSecurityKey(RSA.Create().FromPemKey(rsaPublicKey))
};
```

#### Web组件
TheaWebMiddleware中间件：拦截所有的http请求，收集输入参数、输出参数、token、用户信息，并组装成日志，调用日志组件记录日志，后续工作有日志组件接管。
日志内容，包含：TraceId，Sequence，Tag等信息，TraceId是跟踪ID,多个请求之间，只要传递了相同的TraceId，在ES中查询，就能查询到相关关联的请求日志，Sequence标识着他们的调用顺序。

TheaHttpClientFactory类，IHttpClientFactory接口的实现，增加了代理请求支持。
TheaHttpMessageHandler类，实现了DelegatingHandler抽象类，在方法中调用其他http请求时，在Headers中，增加了TraceId，Sequence，Tag三个值，确保了跟踪ID的透传，从而生成调用链条。

#### 全球化组件
Thea.Globalization全球化组件，主要提供一个服务，从数据库中，把对应的标签Tag对应的多语言值，获取过来，并缓存到redis，返回给前端，进行显示。
同时也提供了一个自定义标签，用在mvc的view中，用起来会更优雅，直接调用服务也是可以的。

全球化服务类，提供GetGlossary方法，如果没有传cultureName参数，先会从当前的QueryString中，取hl的值作为语言，如果没有值，则继续取Cookie中hl的值，如果都没有值，则取Globalization:DefaultCulture配置的默认语言。

js文件的多语言处理
只有JS文件中的多语言是走中间件的，JsFileGlobalizationMiddleware类拦截了指定过滤条件结尾的js文件，将里面的多语言标签内容[[xxxx]],替换为对用的多语言内容，类似这种的[[{xxxx}]]，会被替换为对应的配置文件中appsetting.json中的内容。
配置如下：
``` json
{
  "Globalization": {
    "ConnectionString": "Server=172.18.50.8\\developer;Database=xxx;Uid=xxx;password=xxx;TrustServerCertificate=true",
    "DefaultLanguage": "zh-CN",
    "FilterFileExtensions": ".lang.js", //拦截所有文件名以.lang.js结尾的js文件
    "Redis": {
      "Url": "172.18.50.8:6379",
      "Prefix": "Development"
    }
  }
}
```

#### Orm组件
使用Trolley组件，把接口和实现分离出来，具体使用方法参见Trolley。

#### Job组件

#### 消息驱动组件


工具类ObjectMethodExecutor：可以使用它来实现动态方法调用，摘自ASP.NET COREY源码，支持同步异步方法调用。
工具类TheaActivator：类似于Activator,构造各种类实例，依赖从IServiceProvider中获取。

