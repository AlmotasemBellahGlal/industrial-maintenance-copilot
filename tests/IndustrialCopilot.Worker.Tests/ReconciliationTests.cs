using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialCopilot.Worker.Tests;

public class ReconciliationTests
{
    private sealed class Backend : IReconciliationDiscovery,IDispatchAttemptStore,IExternalDispatch,IActionAuthorization
    {
        public Guid[] Ids=Enumerable.Range(0,6).Select(_=>Guid.NewGuid()).ToArray();public Guid? FailId;public int Active;public int Peak;public int Sends;public int Queries;
        public readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,DispatchAttemptState> States=new();
        public readonly System.Collections.Concurrent.ConcurrentBag<Guid> Reconciled=[];
        public Task<IReadOnlyList<Guid>> DiscoverAsync(int size,TimeSpan delay,CancellationToken ct){ct.ThrowIfCancellationRequested();Queries++;return Task.FromResult<IReadOnlyList<Guid>>(Ids.Take(size).ToArray());}
        public Task<bool> AuthorizeAsync(string a,TrustedAction action,Guid id,CancellationToken ct)=>Task.FromResult(true);
        private DispatchAttempt Attempt(Guid id)=>new(id,id,2,"original","reserved",null,null,"host",DateTimeOffset.UtcNow,States.GetOrAdd(id,DispatchAttemptState.Uncertain),true,null,null,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"noise","repair",[new(1,"inspect")]));
        public Task<DispatchAttempt?> GetAsync(Guid id,CancellationToken ct)=>id==FailId?throw new IOException("injected"):Task.FromResult<DispatchAttempt?>(Attempt(id));
        public Task<DispatchAttemptSession?> OpenAsync(Guid id,CancellationToken ct)=>Task.FromResult<DispatchAttemptSession?>(new Session(this,Attempt(id)));
        public Task<DispatchReservation> ReserveAsync(DispatchCommand c,CancellationToken ct)=>throw new Exception("Worker must never reserve new delivery");
        public Task<ExternalDispatchResult> SendAsync(DispatchAttempt a,CancellationToken ct){Sends++;throw new Exception("Worker must never send");}
        public async Task<ExternalDispatchResult> ReconcileAsync(Guid id,CancellationToken ct)
        {
            Reconciled.Add(id);var active=Interlocked.Increment(ref Active);int old;
            do {old=Peak;if(old>=active)break;}while(Interlocked.CompareExchange(ref Peak,active,old)!=old);
            try {await Task.Delay(50,ct);return (Array.IndexOf(Ids,id)%3) switch{0=>ExternalDispatchResult.Accepted("ticket"),1=>ExternalDispatchResult.Failed(),_=>ExternalDispatchResult.Uncertain()};}
            finally{Interlocked.Decrement(ref Active);}
        }
        private sealed class Session(Backend owner,DispatchAttempt initial) : DispatchAttemptSession
        {
            private DispatchAttempt current=initial;public override DispatchAttempt Attempt=>current;
            public override Task MarkInvocationStartedAsync(CancellationToken ct)=>throw new Exception("Not a send path");
            public override Task<bool> RestartAsync(string a,CancellationToken ct)=>throw new Exception("No automatic retry");
            public override Task RecordAsync(ExternalDispatchResult result,string a,CancellationToken ct)
            {var state=result.Outcome switch{ExternalDispatchOutcome.Accepted=>DispatchAttemptState.Confirmed,ExternalDispatchOutcome.DefinitivelyFailed=>DispatchAttemptState.DefinitivelyFailed,_=>DispatchAttemptState.Uncertain};owner.States[current.Id]=state;current=current with{State=state};return Task.CompletedTask;}
            public override ValueTask DisposeAsync()=>ValueTask.CompletedTask;
        }
    }
    [Fact]
    public async Task BatchReconcilesSameKeysWithBoundedConcurrencyAndIsolatesFailure()
    {
        var b=new Backend();b.FailId=b.Ids[5];var batch=new ReconciliationBatch(b,new(b,b,b));
        var result=await batch.ExecuteAsync("worker",6,2,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(1),default);
        Assert.Equal(6,result.Discovered);Assert.Equal(5,result.Inspected);Assert.Equal(1,result.Failed);Assert.InRange(b.Peak,1,2);Assert.Equal(0,b.Sends);
        Assert.Equal(DispatchAttemptState.Confirmed,b.States[b.Ids[0]]);Assert.Equal(DispatchAttemptState.DefinitivelyFailed,b.States[b.Ids[1]]);Assert.Equal(DispatchAttemptState.Uncertain,b.States[b.Ids[2]]);
        Assert.All(b.Reconciled,id=>Assert.Contains(id,b.Ids));
    }
    [Fact]
    public async Task CancellationStopsBatchAndWorkerTimerShutsDownPromptly()
    {
        var b=new Backend();var batch=new ReconciliationBatch(b,new(b,b,b));using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>batch.ExecuteAsync("worker",3,2,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(1),cancellation.Token));Assert.Equal(0,b.Queries);
        using var worker=new global::IndustrialCopilot.Worker.Worker(batch,new(5),NullLogger<global::IndustrialCopilot.Worker.Worker>.Instance);
        await worker.StartAsync(default);using var shutdown=new CancellationTokenSource(TimeSpan.FromSeconds(2));await worker.StopAsync(shutdown.Token);Assert.Equal(0,b.Queries);
    }
    [Theory]
    [InlineData(0,20,4,30)] [InlineData(30,101,4,30)] [InlineData(30,20,17,30)] [InlineData(30,20,4,0)]
    public void InvalidSchedulingBoundsAreRejected(int interval,int size,int concurrency,int timeout)=>Assert.Throws<ArgumentException>(()=>new ReconciliationSchedule(interval,size,concurrency,timeout));
}
