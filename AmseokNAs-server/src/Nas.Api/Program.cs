//--------------------------//
//--------API 入口只装配控制面 HTTP 管道---------//
//--------The API entry point only composes the control-plane HTTP pipeline--------//
//-------------------------//
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthorization();
app.MapControllers();

app.Run();
