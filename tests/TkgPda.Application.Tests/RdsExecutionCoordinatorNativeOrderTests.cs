using System.Text.Json;
using TkgPda.Application;
using TkgPda.Domain;
using Xunit;

namespace TkgPda.Application.Tests;

public sealed class RdsExecutionCoordinatorNativeOrderTests
{
    private static readonly Guid RequestId = Guid.Parse("44444444-5555-6666-7777-888888888888");

    [Fact]
    public async Task WaitingPreparation_readiness_state_never_dispatches_native_order()
    {
        var fixture = Fixture(readinessState: TransportReadinessState.WaitingPreparation);
        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("READINESS_WAITING_PREPARATION", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
        Assert.Equal(0, fixture.ExecutionClient.CreateCount);
        Assert.Empty(fixture.WorkflowStore.Transitions);
    }

    [Fact]
    public async Task Duplicate_worker_attempt_aborts_when_lease_cannot_be_acquired()
    {
        var fixture = Fixture();
        fixture.Store.LeaseAvailable = false;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.Null(result);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
        Assert.Equal(0, fixture.ExecutionClient.CreateCount);
    }

    [Fact]
    public async Task Native_dispatch_uses_deterministic_order_and_block_identities()
    {
        var fixture = Fixture();
        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Equal(1, fixture.NativeClient.SetOrderCount);
        var order = fixture.NativeClient.LastOrder;
        Assert.NotNull(order);
        Assert.Equal(RequestId.ToString("D"), order.Id);
        Assert.Equal($"{RequestId:D}:pickup", order.Blocks[0].BlockId);
        Assert.Equal($"{RequestId:D}:dropoff", order.Blocks[1].BlockId);
        Assert.Contains(TransportRequestStatus.SentToRds, fixture.WorkflowStore.Transitions);
    }

    [Fact]
    public async Task Ambiguous_setOrder_network_timeout_recovers_via_details_query()
    {
        var fixture = Fixture();
        fixture.NativeClient.SetOrderError = new RdsCoreNativeOrderException("TIMEOUT", "Ambiguous network timeout");
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Running;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Contains(TransportRequestStatus.SentToRds, fixture.WorkflowStore.Transitions);
        Assert.Contains(TransportRequestStatus.Running, fixture.WorkflowStore.Transitions);
    }

    [Fact]
    public async Task Finished_native_order_reconciles_to_Completed()
    {
        var fixture = Fixture(activeTaskStatus: TransportRequestStatus.SentToRds);
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Finished;

        await fixture.Coordinator.ReconcileAsync(10, CancellationToken.None);

        Assert.Contains(TransportRequestStatus.Completed, fixture.WorkflowStore.Transitions);
    }

    [Fact]
    public async Task Stopped_native_order_reconciles_to_Cancelled_when_cancellation_requested()
    {
        var fixture = Fixture(activeTaskStatus: TransportRequestStatus.Running, cancellationRequested: true);
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Stopped;

        await fixture.Coordinator.ReconcileAsync(10, CancellationToken.None);

        Assert.Contains(fixture.WorkflowStore.Outcomes, o => o.Status == TransportRequestStatus.Cancelled);
    }

    [Fact]
    public async Task Failed_native_order_reconciles_to_Failed_safely()
    {
        var fixture = Fixture(activeTaskStatus: TransportRequestStatus.Running);
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Failed;

        await fixture.Coordinator.ReconcileAsync(10, CancellationToken.None);

        Assert.Contains(fixture.WorkflowStore.Outcomes, o => o.Status == TransportRequestStatus.Failed);
    }

    [Fact]
    public async Task Unknown_RDS_state_does_not_mutate_request()
    {
        var fixture = Fixture(activeTaskStatus: TransportRequestStatus.SentToRds);
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Unknown;

        await fixture.Coordinator.ReconcileAsync(10, CancellationToken.None);

        Assert.Empty(fixture.WorkflowStore.Transitions);
        Assert.Empty(fixture.WorkflowStore.Outcomes);
    }

    [Fact]
    public async Task Native_and_Wind_mode_are_mutually_exclusive()
    {
        // Native mode: executionClient (WindTask) must NOT be invoked
        var nativeFixture = Fixture(mode: RdsExecutionMode.RdsCoreNativeOrder);
        await nativeFixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());
        Assert.Equal(1, nativeFixture.NativeClient.SetOrderCount);
        Assert.Equal(0, nativeFixture.ExecutionClient.CreateCount);

        // WindTask mode: nativeClient must NOT be invoked
        var windFixture = Fixture(mode: RdsExecutionMode.WindTask);
        await windFixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());
        Assert.Equal(0, windFixture.NativeClient.SetOrderCount);
        Assert.Equal(1, windFixture.ExecutionClient.CreateCount);
    }

    [Fact]
    public async Task Repeated_cancel_is_idempotent()
    {
        var fixture = Fixture(requestStatus: TransportRequestStatus.Cancelled);
        var first = await fixture.Coordinator.CancelAsync(RequestId, null, Guid.NewGuid());
        Assert.True(first.WasIdempotentReplay);

        var second = await fixture.Coordinator.CancelAsync(RequestId, null, Guid.NewGuid());
        Assert.True(second.WasIdempotentReplay);
        Assert.Equal(0, fixture.NativeClient.TerminateCount);
    }

    private static NativeTestFixture Fixture(
        TransportRequestStatus requestStatus = TransportRequestStatus.Queued,
        TransportReadinessState readinessState = TransportReadinessState.Ready,
        TransportRequestStatus? activeTaskStatus = null,
        bool cancellationRequested = false,
        RdsExecutionMode mode = RdsExecutionMode.RdsCoreNativeOrder)
    {
        var context = Context(readinessState: readinessState);
        var store = new FakeNativeStore
        {
            Context = context,
            RequestStatus = requestStatus,
            CancellationRequested = cancellationRequested
        };

        if (activeTaskStatus is { } activeStatus)
        {
            var proj = new RdsTaskProjection(
                RequestId, RequestId.ToString("D"), "TKG_TRANSPORT", 1000, null, null, null,
                JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement,
                cancellationRequested, activeStatus);
            store.Active = [proj];
            store.TaskProjection = proj;
        }

        var workflowStore = new FakeNativeWorkflowStore(store.Request);
        var executionClient = new FakeNativeExecutionClient();
        var readClient = new FakeNativeReadClient();
        var reconciler = new FakeNativeWorkSiteReconciler();
        var nativeClient = new FakeNativeOrderClient(mode);
        var settings = new RdsExecutionSettings(true, TimeSpan.FromMinutes(5), "", "");

        var coordinator = new RdsExecutionCoordinator(
            store,
            executionClient,
            readClient,
            reconciler,
            new TransportWorkflowService(workflowStore),
            settings,
            TimeProvider.System,
            nativeOrderClient: nativeClient);

        return new(coordinator, store, workflowStore, executionClient, nativeClient);
    }

    private static RdsDispatchContext Context(TransportReadinessState readinessState) => new(
        new TransportRequestSnapshot(
            RequestId, 1, "test-key", TransportRequestStatus.Queued, TransportTaskType.BringEmpty,
            RequestOrigin.Api, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100,
            readinessState, null),
        "SH-01-A", "V1", "71550", "715A2", "KCK", Guid.NewGuid(),
        "TKG_TRANSPORT", 100,
        JsonSerializer.SerializeToElement(new
        {
            rdsInputBindings = new[]
            {
                new { param = "fromSite", source = "SOURCE_RDS_SITE_ID", required = true, value = (string?)null },
                new { param = "toSite", source = "DESTINATION_RDS_SITE_ID", required = true, value = (string?)null }
            }
        }),
        new RdsDispatchEndpoint(Guid.NewGuid(), "SOURCE_STATION", "GRP", "STAGING", "SOURCE_STATION", 1, 0, 0, 0, 0, DateTimeOffset.UtcNow, true),
        new RdsDispatchEndpoint(Guid.NewGuid(), "DEST_STATION", "GRP", "STAGING", "DEST_STATION", 1, 0, 0, 0, 0, DateTimeOffset.UtcNow, true));

    private sealed record NativeTestFixture(
        RdsExecutionCoordinator Coordinator,
        FakeNativeStore Store,
        FakeNativeWorkflowStore WorkflowStore,
        FakeNativeExecutionClient ExecutionClient,
        FakeNativeOrderClient NativeClient);

    private sealed class FakeNativeExecutionLease : IRdsExecutionLease
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeNativeStore : IRdsExecutionStore
    {
        public RdsDispatchContext Context { get; set; } = Context(TransportReadinessState.Ready);
        public TransportRequestStatus RequestStatus { get; set; }
        public bool CancellationRequested { get; set; }
        public bool LeaseAvailable { get; set; } = true;
        public IReadOnlyList<RdsTaskProjection> Active { get; set; } = [];
        public RdsTaskProjection? TaskProjection { get; set; }
        public TransportRequestSnapshot Request => Context.Request with { Status = RequestStatus };

        public Task<IReadOnlyList<Guid>> ListQueuedRequestIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([RequestId]);
        public Task<IReadOnlyList<RdsTaskProjection>> ListActiveTasksAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active);
        public Task<IReadOnlyList<Guid>> ListPendingWorkSiteRefreshRequestIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IRdsExecutionLease?> TryAcquireRequestLeaseAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IRdsExecutionLease?>(LeaseAvailable ? new FakeNativeExecutionLease() : null);
        public Task<RdsDispatchContext?> GetDispatchContextAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RdsDispatchContext?>(Context with { Request = Request });
        public Task<TransportRequestSnapshot?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TransportRequestSnapshot?>(Request);
        public Task<RdsTaskProjection?> FindTaskByRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TaskProjection);
        public Task<RdsTaskProjection?> FindTaskByRecordIdAsync(string taskRecordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TaskProjection);
        public Task EnsureTaskRecordAsync(Guid requestId, string taskRecordId, string taskLabel, JsonElement requestPayload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RecordDispatchResponseAsync(Guid requestId, JsonElement responsePayload, DateTimeOffset syncedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RecordTaskStatusAsync(Guid requestId, RdsWindTaskSnapshot task, DateTimeOffset syncedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task MarkCancellationRequestedAsync(Guid requestId, JsonElement responsePayload, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task WriteExecutionAuditAsync(Guid requestId, string action, string code, string message, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<RdsMappedWorkSite?> FindMappedWorkSiteAsync(Guid slotId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RdsMappedWorkSite?>(null);
        public Task<IReadOnlyList<RdsTaskTemplateSummary>> ListTaskTemplatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RdsTaskTemplateSummary>>([]);
        public Task EnrichRemainingTimesAsync(IReadOnlyDictionary<string, double> agvRemainingTimes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeNativeWorkflowStore(TransportRequestSnapshot initial) : ITransportWorkflowStore
    {
        public List<TransportRequestStatus> Transitions { get; } = [];
        public List<ExecutionOutcome> Outcomes { get; } = [];

        public Task<CreateTransportResult> CreateAndQueueAsync(CreateTransportRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransportBatchResult> CreateBatchAndQueueAsync(CreateTransportBatchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CancelTransportResult> CancelAsync(Guid requestId, Guid? actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelTransportResult(initial with { Status = TransportRequestStatus.Cancelled }, initial.Status == TransportRequestStatus.Cancelled));
        public Task<TransportRequestSnapshot> MarkReadyAsync(Guid requestId, Guid? actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransportRequestSnapshot> RecordExecutionTransitionAsync(Guid requestId, TransportRequestStatus nextStatus, CancellationToken cancellationToken = default)
        {
            Transitions.Add(nextStatus);
            return Task.FromResult(initial with { Status = nextStatus });
        }
        public Task<TransportRequestSnapshot> RecordExecutionOutcomeAsync(Guid requestId, ExecutionOutcome outcome, CancellationToken cancellationToken = default)
        {
            Outcomes.Add(outcome);
            return Task.FromResult(initial with { Status = outcome.Status });
        }
        public Task<LoadStateUpdateResult> UpdateLoadStateAsync(LoadStateUpdateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SwitchVariantAsync(VariantSelection selection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeNativeExecutionClient : IRdsExecutionClient
    {
        public bool DispatchEnabled => true;
        public int CreateCount { get; private set; }
        public Task<RdsValidationResult> ValidateTransportAsync(RdsValidateTransportRequest request, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCreateTransportResult> CreateTransportAsync(RdsCreateTransportRequest request, Guid correlationId, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return Task.FromResult(new RdsCreateTransportResult(true, false, 200, "ok", JsonDocument.Parse("{}").RootElement));
        }
        public Task<RdsStopTaskResult> StopTaskAsync(string taskRecordId, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsStopTaskResult(200, "ok", JsonDocument.Parse("{}").RootElement));
        public Task<RdsContentUpdateResult> UpdateContentAsync(string siteId, string content, Guid correlationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeNativeReadClient : IRdsReadClient
    {
        public bool Enabled => true;
        public string? SafeBaseAddress => "http://127.0.0.1:8080";
        public DateTimeOffset? LastSuccessfulRead => DateTimeOffset.UtcNow;
        public Task<RdsReadResult<IReadOnlyList<RdsWorkSiteSnapshot>>> GetWorkSitesAsync(Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsReadResult<RdsWindTaskPage>> QueryWindTasksAsync(RdsWindTaskQuery query, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsReadResult<RdsWindTaskPage>(new(1, 1, 100, 1, [new RdsWindTaskSnapshot(RequestId.ToString(), "TKG", 1000, null, null, JsonDocument.Parse("{}").RootElement, null)]), 200, "ok", "/api/queryWindTask", correlationId, DateTimeOffset.UtcNow));
        public Task<RdsReadResult<RdsRobotAlarmReport>> GetRobotAlarmsAsync(Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeNativeWorkSiteReconciler : IRdsWorkSiteReconciler
    {
        public Task<RdsWorkSiteSyncRecord> SyncWorkSitesAsync(Guid navigationProfileId, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsWorkSiteSyncRecord(navigationProfileId, 1, 0, 1, DateTimeOffset.UtcNow, 0, "ok"));
    }

    private sealed class FakeNativeOrderClient(RdsExecutionMode mode) : IRdsCoreNativeOrderClient
    {
        public bool MutationEnabled => true;
        public RdsExecutionMode Mode => mode;
        public int SetOrderCount { get; private set; }
        public int TerminateCount { get; private set; }
        public RdsCoreNormalOrder? LastOrder { get; private set; }
        public Exception? SetOrderError { get; set; }
        public RdsCoreKnownOrderState DetailsState { get; set; } = RdsCoreKnownOrderState.Waiting;

        public Task<RdsCoreSetOrderResult> SetOrderAsync(RdsCoreNormalOrder order, Guid correlationId, CancellationToken cancellationToken = default)
        {
            SetOrderCount++;
            LastOrder = order;
            if (SetOrderError is not null) throw SetOrderError;
            return Task.FromResult(new RdsCoreSetOrderResult(
                true, true, true, 200, 0, "success", false, RdsCoreOrderDiagnostics.Empty, JsonDocument.Parse("{}").RootElement));
        }

        public Task<RdsCoreOrderDetails> GetOrderDetailsAsync(string orderId, Guid correlationId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RdsCoreOrderDetails(
                orderId,
                null,
                new(DetailsState, DetailsState.ToString().ToUpperInvariant()),
                [],
                RdsCoreOrderDiagnostics.Empty,
                JsonDocument.Parse("{}").RootElement));
        }

        public Task<RdsCoreTerminateResult> TerminateAsync(string orderId, Guid correlationId, CancellationToken cancellationToken = default)
        {
            TerminateCount++;
            return Task.FromResult(new RdsCoreTerminateResult(true, true, 200, 0, "success", JsonDocument.Parse("{}").RootElement));
        }

        public Task<RdsCoreOrderList> GetOrdersAsync(RdsCoreOrderQuery query, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RdsCoreOrderDetails>> GetOrderDetailsByExternalIdAsync(string externalId, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreOrderDetails> GetOrderDetailsByBlockIdAsync(string blockId, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreBlockDetails> GetBlockDetailsByIdAsync(string blockId, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> SetPriorityAsync(string orderId, int priority, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> ClearRobotAllErrorAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> GotoSitePauseAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> GotoSiteResumeAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> ManualFinishedAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> RedoFailedOrderAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> SetDispatchableAsync(IReadOnlyList<string> vehicles, string type, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RdsCoreRobotTelemetry>> GetRobotsStatusAsync(Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
