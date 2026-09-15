using IndustrialCopilot.Worker;

namespace IndustrialCopilot.Worker.Tests;

public class ReasoningScheduleTests
{
    [Theory]
    [InlineData(0,30,1)]
    [InlineData(5,30,1)]
    [InlineData(2,3,1)]
    [InlineData(2,301,1)]
    [InlineData(2,30,0)]
    public void RejectsUnboundedCapacityUnsafeLeaseAndPollingStorm(int capacity,int lease,int poll)
    {Assert.Throws<ArgumentException>(()=>new ReasoningSchedule([Guid.NewGuid()],capacity,lease,poll));}
    [Fact]
    public void WorkerScopeIsRequiredAndDefensivelyOwned()
    {
        Assert.Throws<ArgumentException>(()=>new ReasoningSchedule([]));
        Assert.Throws<ArgumentException>(()=>new ReasoningSchedule([Guid.Empty]));
        var ids=new[]{Guid.NewGuid()};var schedule=new ReasoningSchedule(ids);var original=ids[0];ids[0]=Guid.NewGuid();
        Assert.Equal(original,Assert.Single(schedule.EquipmentIds));
    }
}
