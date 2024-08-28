using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using WebAppTest.Domain;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
builder.Services.AddDomainServices(builder.Configuration);

//var frontendUrl = builder.Configuration["FrontendUrl"];
//string[] urls = new[] { frontendUrl };
//builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
//    policy.WithOrigins(urls).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Authorization format : Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        BearerFormat = "JWT",
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme{
                Reference = new OpenApiReference
                {
                    Id = "Bearer",
                    Type = ReferenceType.SecurityScheme}
                },
                new string[] { }
        }
    });
    options.OrderActionsBy(o => o.RelativePath);
});

var app = builder.Build();
app.UseForwardedHeaders();
//app.UseCors();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.UseDomainServices(builder.Configuration);

app.MapControllers();

app.Run();
