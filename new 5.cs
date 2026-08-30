using Microsoft.AspNetCore.SignalR;
using ignis.Api.Modules.FireDetection;
using ignis.Api.Modules.Weather;
using ignis.Api.Modules.PredictionEngine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR(); // real-time push ke client (lihat bagian 3 arsitektur)

// Registrasi tiap modul sebagai service — modular monolith:
// satu proses, tapi tiap modul independen & gampang dipisah nanti
builder.Services.AddScoped<IFireDetectionService, FireDetectionService>();
builder.Services.AddScoped<IWeatherService, WeatherService>();

// Prediction Engine: semua strategi terdaftar, engine yang pilih
// strategi mana dipakai berdasarkan jenis lahan (lihat PredictionEngine.cs)
builder.Services.AddScoped<IPredictionStrategy, RothermelPredictionStrategy>();
builder.Services.AddScoped<IPredictionStrategy, PeatFirePredictionStrategy>();
builder.Services.AddScoped<PredictionEngine>();

var app = builder.Build();

app.MapControllers();
app.MapHub<FireUpdatesHub>("/hubs/fire-updates");

app.Run();

public class FireUpdatesHub : Hub { }