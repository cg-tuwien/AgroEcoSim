using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgroServer.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

const string Origins = "_AgroEcoSim";
#if DEBUG
builder.Services.AddCors(o => o.AddPolicy(name: Origins, p =>
    p.WithOrigins("http://localhost:8080", "https://localhost:7215")
    //.SetIsOriginAllowedToAllowWildcardSubdomains()
    //.WithMethods("GET", "POST", "OPTIONS").AllowAnyHeader().AllowCredentials().Build()
    .WithMethods("GET", "POST").AllowAnyHeader().AllowCredentials()
));
#endif

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var loaded = new HashSet<string>();
    foreach(var asm in AppDomain.CurrentDomain.GetAssemblies())
        loaded.Add(asm.GetName().Name);
    foreach (var xmlFile in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.xml"))
    {
        var assemblyName = Path.GetFileNameWithoutExtension(xmlFile);
        if (loaded.Contains(assemblyName))
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
    }
});

builder.Services.AddSignalR(options => {
    options.MaximumParallelInvocationsPerClient = 3;
});

builder.Configuration.AddEnvironmentVariables(prefix: "AGRO_");
var app = builder.Build();

// Configure the HTTP request pipeline.
//
//if (app.Environment.IsDevelopment())
if (true)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Environment.IsDevelopment())
    // app.UseCors(options => options
    //     .SetIsOriginAllowed(s => s.Contains("localhost"))
    //     .AllowAnyHeader()
    //     .AllowCredentials()
    //     .AllowAnyMethod()
    // );
    app.UseCors(Origins);
else
{
    var host = app.Configuration["AGRO_HOSTNAME"];
    app.UseCors(options => options
        .SetIsOriginAllowed(s => s.Contains(host))
        .AllowAnyHeader()
        .AllowCredentials()
        .AllowAnyMethod()
    );
}



//app.UseHttpsRedirection();

//app.UseAuthorization();
app.MapHub<SimulationHub>("/SimSocket");

app.MapControllers();

app.Run();