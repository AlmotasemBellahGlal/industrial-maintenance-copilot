using IndustrialCopilot.Domain.MaintenanceRuns;

namespace IndustrialCopilot.Domain.Tests.MaintenanceRuns;

public class MaintenanceRunTests
{
    private static MaintenanceRun Create() => new(Guid.NewGuid(), Guid.NewGuid(), "Pump vibrates excessively");

    [Fact]
    public void NewRunPreservesIdentityAndSymptomAndStartsQueued()
    {
        var id = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var run = new MaintenanceRun(id, equipmentId, "Pump vibrates excessively");
        Assert.Equal(id, run.Id);
        Assert.Equal(equipmentId, run.EquipmentId);
        Assert.Equal("Pump vibrates excessively", run.ReportedSymptom);
        Assert.Equal(MaintenanceRunStatus.Queued, run.Status);
    }

    [Fact]
    public void RejectsEmptyIdentities()
    {
        Assert.Throws<ArgumentException>(() => new MaintenanceRun(Guid.Empty, Guid.NewGuid(), "Vibration"));
        Assert.Throws<ArgumentException>(() => new MaintenanceRun(Guid.NewGuid(), Guid.Empty, "Vibration"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingSymptom(string? symptom) =>
        Assert.ThrowsAny<ArgumentException>(() => new MaintenanceRun(Guid.NewGuid(), Guid.NewGuid(), symptom!));

    [Fact]
    public void CanStartWaitResumeAndCompleteWithoutChangingRunDetails()
    {
        var run = Create();
        var id = run.Id;
        var equipmentId = run.EquipmentId;
        var symptom = run.ReportedSymptom;
        run.Start();
        Assert.Equal(MaintenanceRunStatus.Running, run.Status);
        run.WaitForApproval();
        Assert.Equal(MaintenanceRunStatus.WaitingForApproval, run.Status);
        run.Resume();
        Assert.Equal(MaintenanceRunStatus.Running, run.Status);
        run.Complete();
        Assert.Equal(MaintenanceRunStatus.Completed, run.Status);
        Assert.Equal(id, run.Id);
        Assert.Equal(equipmentId, run.EquipmentId);
        Assert.Equal(symptom, run.ReportedSymptom);
    }

    [Theory]
    [InlineData(MaintenanceRunStatus.Queued)]
    [InlineData(MaintenanceRunStatus.Running)]
    [InlineData(MaintenanceRunStatus.WaitingForApproval)]
    public void CanCancelAnyActiveRun(MaintenanceRunStatus status)
    {
        var run = CreateInState(status);
        run.Cancel();
        Assert.Equal(MaintenanceRunStatus.Cancelled, run.Status);
    }

    [Fact]
    public void RunningRunCanFail()
    {
        var run = CreateInState(MaintenanceRunStatus.Running);
        run.Fail();
        Assert.Equal(MaintenanceRunStatus.Failed, run.Status);
    }

    [Theory]
    [InlineData(MaintenanceRunStatus.Queued, nameof(MaintenanceRun.Resume))]
    [InlineData(MaintenanceRunStatus.Queued, nameof(MaintenanceRun.WaitForApproval))]
    [InlineData(MaintenanceRunStatus.Queued, nameof(MaintenanceRun.Complete))]
    [InlineData(MaintenanceRunStatus.Queued, nameof(MaintenanceRun.Fail))]
    [InlineData(MaintenanceRunStatus.Running, nameof(MaintenanceRun.Start))]
    [InlineData(MaintenanceRunStatus.Running, nameof(MaintenanceRun.Resume))]
    [InlineData(MaintenanceRunStatus.WaitingForApproval, nameof(MaintenanceRun.Start))]
    [InlineData(MaintenanceRunStatus.WaitingForApproval, nameof(MaintenanceRun.WaitForApproval))]
    [InlineData(MaintenanceRunStatus.WaitingForApproval, nameof(MaintenanceRun.Complete))]
    [InlineData(MaintenanceRunStatus.WaitingForApproval, nameof(MaintenanceRun.Fail))]
    public void InvalidActiveTransitionsThrowRepeatedlyWithoutMutation(MaintenanceRunStatus status, string operation)
    {
        var run = CreateInState(status);
        Action action = operation switch
        {
            nameof(MaintenanceRun.Start) => run.Start,
            nameof(MaintenanceRun.Resume) => run.Resume,
            nameof(MaintenanceRun.WaitForApproval) => run.WaitForApproval,
            nameof(MaintenanceRun.Complete) => run.Complete,
            nameof(MaintenanceRun.Fail) => run.Fail,
            _ => throw new ArgumentException("Unknown test operation.", nameof(operation))
        };
        AssertRejectedWithoutMutation(run, action);
    }

    [Theory]
    [InlineData(MaintenanceRunStatus.Completed)]
    [InlineData(MaintenanceRunStatus.Cancelled)]
    [InlineData(MaintenanceRunStatus.Failed)]
    public void TerminalStatesRejectEveryOperationIncludingRepeatedTerminalTransitions(MaintenanceRunStatus status)
    {
        var run = CreateInState(status);
        Action[] actions = [run.Start, run.WaitForApproval, run.Resume, run.Complete, run.Cancel, run.Fail];
        foreach (var action in actions) AssertRejectedWithoutMutation(run, action);
    }

    private static void AssertRejectedWithoutMutation(MaintenanceRun run, Action action)
    {
        var id = run.Id;
        var equipmentId = run.EquipmentId;
        var symptom = run.ReportedSymptom;
        var status = run.Status;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Throws<InvalidOperationException>(action);
            Assert.Equal(status, run.Status);
            Assert.Equal(id, run.Id);
            Assert.Equal(equipmentId, run.EquipmentId);
            Assert.Equal(symptom, run.ReportedSymptom);
        }
    }

    private static MaintenanceRun CreateInState(MaintenanceRunStatus status)
    {
        var run = Create();
        if (status == MaintenanceRunStatus.Queued) return run;
        run.Start();
        switch (status)
        {
            case MaintenanceRunStatus.Running: break;
            case MaintenanceRunStatus.WaitingForApproval: run.WaitForApproval(); break;
            case MaintenanceRunStatus.Cancelled: run.Cancel(); break;
            case MaintenanceRunStatus.Failed: run.Fail(); break;
            case MaintenanceRunStatus.Completed: run.Complete(); break;
            default: throw new ArgumentOutOfRangeException(nameof(status));
        }
        return run;
    }
}
