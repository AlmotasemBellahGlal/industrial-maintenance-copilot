using System.Text.Json;
using IndustrialCopilot.Evaluation;
using Npgsql;

try
{
    var datasetPath="evaluation/golden-v1.json"; var fixturePath="evaluation/fixtures-v1.json";
    var dataset=GoldenDataset.Load(datasetPath);
    var datasetHash=DatasetFingerprint.FromFile(datasetPath);
    if(datasetHash!=File.ReadAllText("evaluation/golden-v1.sha256").Trim())throw new ArgumentException("Dataset version hash changed.");
    Console.WriteLine($"Validated {dataset.Cases.Count} cases, {dataset.Cases.Count(c=>c.Category!=CaseCategory.Normal)} adversarial; SHA256 {datasetHash}");
    if(args.Contains("--validate"))return;
    var connection=Environment.GetEnvironmentVariable("EVALUATION_POSTGRES")??throw new ArgumentException("Set EVALUATION_POSTGRES.");
    var parsed=new NpgsqlConnectionStringBuilder(connection);
    if(parsed.Database!="maintenance_evaluation" || parsed.Host is not ("localhost" or "127.0.0.1"))throw new ArgumentException("Use isolated loopback maintenance_evaluation database.");
    var fixtures=JsonSerializer.Deserialize<EvaluationFixture[]>(await File.ReadAllTextAsync(fixturePath),GoldenDataset.Json)??throw new ArgumentException("Missing fixtures.");
    var fixtureHash=DatasetFingerprint.FromFile(fixturePath);
    using var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
    await using var source=NpgsqlDataSource.Create(connection);
    var report=await EvaluationRunner.RunAsync(dataset,datasetHash,fixtures,fixtureHash,source,connection,cancellation.Token);
    var json=JsonSerializer.Serialize(report,GoldenDataset.Json)+"\n";
    Directory.CreateDirectory("artifacts/evaluation");
    await File.WriteAllTextAsync("artifacts/evaluation/results.json",json);
    await File.WriteAllTextAsync("artifacts/evaluation/summary.md",EvaluationRunner.Markdown(report));
    Console.WriteLine(EvaluationRunner.Markdown(report));
    if(report.Summary.SystemErrorCases>0){Environment.ExitCode=2;return;}
    if(args.Contains("--repeat"))
    {
        var again=await EvaluationRunner.RunAsync(dataset,datasetHash,fixtures,fixtureHash,source,connection,cancellation.Token);
        if(json!=JsonSerializer.Serialize(again,GoldenDataset.Json)+"\n")throw new InvalidOperationException("Repeatability failure.");
        Console.WriteLine("PASS: deterministic rerun is byte-identical, including evidence and outcomes.");
    }
}
catch(ArgumentException){Console.Error.WriteLine("Invalid evaluation dataset/configuration. No quality score was inferred.");Environment.ExitCode=2;}
catch(Exception){Console.Error.WriteLine("Evaluation harness/storage failure. No successful refusal inferred; inspect safe per-case results if written.");Environment.ExitCode=2;}
