using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using MaghrebButusAPI.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Firebase Admin SDK (vérifie les JWT Firebase) ─────────────────────────
FirebaseApp.Create(new AppOptions
{
    Credential = GoogleCredential.FromJson(
        File.ReadAllText(builder.Configuration["Firebase:ServiceAccountPath"]
            ?? "firebase-service-account.json"))
});

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
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.RejectionStatusCode = 429;
});

// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<FirebaseAuthService>();
builder.Services.AddSingleton<MntService>();
builder.Services.AddSingleton<CalageService>();
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
