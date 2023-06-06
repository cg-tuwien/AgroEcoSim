#if !GODOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
//
//if (app.Environment.IsDevelopment())
if (true)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(options => options
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()
);

//app.UseHttpsRedirection();

//app.UseAuthorization();

app.MapControllers();

app.Run();
#endif