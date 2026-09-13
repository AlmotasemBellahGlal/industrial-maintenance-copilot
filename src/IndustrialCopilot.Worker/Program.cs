using IndustrialCopilot.Worker;
using IndustrialCopilot.Infrastructure.Operations;

var builder=Host.CreateApplicationBuilder(args);
if(Environment.GetEnvironmentVariable("MAINTENANCE_CONFIG") is {} configFile)builder.Configuration.AddJsonFile(configFile,false,false).AddEnvironmentVariables();
var equipment=(builder.Configuration["Worker:EquipmentIds"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToHashSet();
if(equipment.Count==0 || equipment.Contains(Guid.Empty)) throw new InvalidOperationException("Worker equipment permissions are required.");
var identity=new HostIdentity("reconciliation-worker",new HashSet<string>{"dispatch"},equipment);
builder.Services.AddMaintenanceHost(builder.Configuration,()=>identity,false);
int Number(string key,int fallback)=>builder.Configuration[key] is {} text?int.Parse(text):fallback;
builder.Services.AddSingleton(new ReconciliationSchedule(Number("Worker:IntervalSeconds",30),Number("Worker:BatchSize",20),Number("Worker:Concurrency",4),Number("Worker:AttemptTimeoutSeconds",30)));
builder.Services.AddHostedService<Worker>();
using var host=builder.Build();
if(args.Contains("--migrate")) { await MaintenanceHostRegistration.MigrateAsync(host.Services,false,default); return; }
await host.RunAsync();
