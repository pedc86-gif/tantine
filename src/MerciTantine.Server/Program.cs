using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using MerciTantine.GameCore;
using MerciTantine.Network;
using MerciTantine.Server;
using Microsoft.AspNetCore.RateLimiting;

var builder=WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<RoomService>();
builder.Services.AddRateLimiter(o=>
{
    o.RejectionStatusCode=429;
    o.AddPolicy("api",c=>RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=600,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=16384);
var app=builder.Build();
app.Use(async(context,next)=>
{
    context.Response.Headers.CacheControl="no-store";
    try { await next(context); }
    catch(UnauthorizedAccessException) { context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new{error="Jeton de joueur invalide."}); }
    catch(RevisionException ex) { context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error=ex.Message}); }
    catch(RuleException ex) { context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error=ex.Message}); }
});
app.UseRateLimiter();
string Token(HttpRequest request) { string h=request.Headers.Authorization.ToString();return h.StartsWith("Bearer ",StringComparison.Ordinal)?h[7..]:""; }
app.MapGet("/health",()=>Results.Ok(new{status="ok",protocol=1}));
app.MapPost("/rooms",(CreateRoomRequest request,RoomService service)=>service.Create(request)).RequireRateLimiting("api");
app.MapPost("/rooms/{code}/join",(string code,JoinRoomRequest request,RoomService service)=>service.Join(code,request)).RequireRateLimiting("api");
app.MapGet("/rooms/{code}",(string code,HttpRequest request,RoomService service)=>service.Read(code,Token(request))).RequireRateLimiting("api");
app.MapPost("/rooms/{code}/commands",(string code,HttpRequest request,NetworkCommand command,RoomService service)=>service.Command(code,Token(request),command)).RequireRateLimiting("api");
app.Run();
