using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using MaghrebButusAPI.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Firebase Admin SDK ────────────────────────────────────────────────────
// En production (Render), le JSON du compte de service est dans la variable FIREBASE_SERVICE_ACCOUNT
var firebaseJson = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT");
if (!string.IsNullOrEmpty(firebaseJson))
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromJson(firebaseJson)
    });
}
else
{
    // En local : lire depuis le fichier
    var path = builder.Configuration["Firebase:ServiceAccountPath"] ?? "firebase-service-account.json";
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromJson(File.ReadAllText(path))
    });
}

// ── JWT Bearer — valide les tokens Firebase ───────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://securetoken.google.com/butus-52a53";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://securetoken.google.com/butus-52a53",
            ValidateAudience = true,
            ValidAudience = "butus-52a53",
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 5;
    });
    options.RejectionStatusCode = 429;
});

// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<FirebaseAuthService>();
builder.Services.AddHttpClient("Maptiler");
builder.Services.AddSingleton<MntService>();
builder.Services.AddSingleton<CalageService>();
builder.Services.AddTransient<TopoService>();
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true);

// ── CORS — autoriser le plugin AutoCAD (localhost) et le dashboard ────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("api");

app.Run();
