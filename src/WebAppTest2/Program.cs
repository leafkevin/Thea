using Thea.Cache;
using Thea.MessageDriven;
using Thea.MessageDriven.MySqlRepository;
using Trolley;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
// Add services to the container.
builder.Services.AddSingleton(f =>
{
    var connString = configuration["ConnectionStrings:default"];
    return new OrmDbFactoryBuilder()
        .Register(OrmProviderType.MySql, "default", connString, true)
        .Build();
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
    f.UseTrolleyRepository("default")
    .UseProducer("cache.refresh", "award.issue")
    .UseProducer("award.take", true);
});
app.UseAuthorization();

app.MapControllers();

app.Run(configuration["Urls"]);
