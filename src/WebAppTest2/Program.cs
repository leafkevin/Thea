using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Thea.Cache;
using Thea.MessageDriven;
using Thea.MessageDriven.MySqlRepository;
using Trolley;
using WebAppTest2.Domain;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
// Add services to the container.
builder.Services.AddSingleton(f =>
{
    var connString = configuration["ConnectionStrings:default"];
    return new OrmDbFactoryBuilder()
        .Register("default", OrmProviderType.MySql, f => f.Use(connString), true)
        .UseMapping<ModelConfiguration>(OrmProviderType.MySql)
        .UseInterceptor<MyDbInterceptor>().Build();
});
builder.Services.AddMessageDriven();
builder.Services.AddRedisCache();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
app.UseSwagger();
app.UseSwaggerUI();
//}
app.UseMessageDriven(f =>
{
    f.UseTrolleyRepository()
     .UseProducer().UseRpcConsumer();
});
app.UseAuthorization();

app.MapControllers();

app.Run(configuration["Urls"]);
