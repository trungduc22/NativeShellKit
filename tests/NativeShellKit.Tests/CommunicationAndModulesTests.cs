using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Communication;
using NativeShellKit.Communication.InProcess;
using NativeShellKit.Communication.Ipc;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Security;
using Xunit;

namespace NativeShellKit.Tests;

public sealed class CommunicationAndModulesTests
{
    private sealed record PingEvent(string Tag);

    private sealed class TestModule(string id, int order, List<string> trace, string triggerTag) : IShellModule
    {
        private IDisposable? _sub;
        public List<string> ObservedTags { get; } = [];
        public ModuleDescriptor Descriptor { get; } = new(id, id, "1.0.0");

        public void RegisterServices(IServiceRegistry services) { }

        public IReadOnlyList<ModuleNavigationItem> GetNavigationItems() =>
            [new ModuleNavigationItem($"route-{id}", id, "\uE80F", id, Order: order)];

        public IReadOnlyList<ModuleCommandDescriptor> GetCommands() =>
            [new ModuleCommandDescriptor($"cmd-{id}", id, id, (sp, ct) =>
                sp.GetRequiredService<IInProcessEventBus>().PublishAsync(new PingEvent(triggerTag), ct))];

        public Task InitializeAsync(IModuleContext context, CancellationToken ct = default)
        {
            trace.Add($"init:{id}");
            Directory.CreateDirectory(context.ModuleDataDirectory);
            _sub = context.Services.GetRequiredService<IInProcessEventBus>()
                .Subscribe<PingEvent>((msg, _) =>
                {
                    ObservedTags.Add(msg.Tag);
                    return Task.CompletedTask;
                });
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken ct = default)
        {
            _sub?.Dispose();
            trace.Add($"stop:{id}");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task InProcessEventBus_DeliversMessages_AndStopsAfterDispose()
    {
        var bus = new InProcessEventBus();
        int count = 0;

        var sub = bus.Subscribe<PingEvent>((_, _) => { count++; return Task.CompletedTask; });
        await bus.PublishAsync(new PingEvent("one"));
        Assert.Equal(1, count);

        sub.Dispose();
        await bus.PublishAsync(new PingEvent("two"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task NamedPipeIpc_AuthenticatesValidClients_AndRejectsTamperedOrReplayedRequests()
    {
        using var tmp = new TempRootScope("nsk-ipc");
        var identity = new AppIdentity("ipcapp", "IPC App", baseDataRoot: tmp.RootPath);
        var sharedKey = new byte[32];
        Random.Shared.NextBytes(sharedKey);

        var auth = new IpcAuthManager(identity, sharedKey);
        var wrongAuth = new IpcAuthManager(identity, Enumerable.Repeat((byte)0x42, 32).ToArray());

        string pipeName = $"nsk_test_ipc_{Guid.NewGuid():N}";
        await using var server = new NamedPipeIpcServer(identity, auth, pipeName);
        server.RegisterAllowedHandler("echo", (req, _) =>
            Task.FromResult(auth.CreateSignedEnvelope("ECHO_OK", req.PayloadJson)));

        await server.StartAsync();

        var client = new NamedPipeIpcClient(identity, auth, pipeName);
        var caps = await client.HandshakeAsync();
        Assert.Equal(IpcProtocolVersion.Current, caps.ProtocolVersion);
        Assert.Contains("echo", caps.SupportedOperations);

        var echoResp = await client.SendAsync("echo", "{\"hello\":\"world\"}");
        Assert.Equal("ECHO_OK", echoResp.Operation);
        Assert.Equal("{\"hello\":\"world\"}", echoResp.PayloadJson);

        var forbiddenResp = await client.SendAsync("unregistered-op", "{}");
        Assert.Equal("ERROR", forbiddenResp.Operation);
        Assert.Contains("FORBIDDEN", forbiddenResp.PayloadJson);

        // Unauthorized signature rejected
        var untrustedClient = new NamedPipeIpcClient(identity, wrongAuth, pipeName);
        var unauthResp = await untrustedClient.SendAsync("echo", "{}");
        Assert.Equal("ERROR", unauthResp.Operation);
        Assert.Contains("UNAUTHORIZED", unauthResp.PayloadJson);

        // Replay nonce check rejected on second verification
        var signed = auth.CreateSignedEnvelope("echo", "{}");
        Assert.True(auth.VerifyEnvelope(signed));
        Assert.False(auth.VerifyEnvelope(signed));

        await server.StopAsync();
    }

    [Fact]
    public async Task LocalPeerGrant_AuthenticatesAndCorrelates_ThenRevokesAndRotates()
    {
        using var tmp = new TempRootScope("nsk-peer");
        var identity = new AppIdentity($"peer-{Guid.NewGuid():N}", "Peer Test", baseDataRoot: tmp.RootPath);
        var grant = new LocalPeerGrant(identity);
        Assert.Null(await grant.OpenClientAsync());
        await grant.GrantAsync();
        await using var originalClient = Assert.IsType<NamedPipeIpcClient>(await grant.OpenClientAsync());
        await using var first = Assert.IsType<NamedPipeIpcServer>(await grant.OpenServerAsync());
        first.RegisterAllowedHandler("status", (request, _) => Task.FromResult(request with { Operation = "ok" }));
        await first.StartAsync();
        Assert.Equal("ok", (await originalClient.SendAsync("status")).Operation);
        var ecoCaps = await originalClient.GetEcosystemCapabilitiesAsync();
        Assert.Equal(identity.AppId, ecoCaps.AppId);
        Assert.Equal(identity.Version, ecoCaps.Version);
        Assert.Equal(IpcProtocolVersion.Current, ecoCaps.ProtocolVersion);
        Assert.Contains("status", ecoCaps.SupportedOperations);
        Assert.Contains(EcosystemIpcOperations.Capabilities, ecoCaps.SupportedOperations);
        Assert.Contains("FORBIDDEN", (await originalClient.SendAsync("unlisted")).PayloadJson);
        await first.StopAsync();
        await grant.RevokeAsync();
        Assert.Null(await grant.OpenServerAsync());
        Assert.Null(await grant.OpenClientAsync());

        await grant.GrantAsync();
        await using var second = Assert.IsType<NamedPipeIpcServer>(await grant.OpenServerAsync());
        await second.StartAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => originalClient.SendAsync("status"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => originalClient.GetEcosystemCapabilitiesAsync());
    }

    [Fact]
    public async Task ModularHost_InitializesAndStopsTwoModules_WithBusDecoupling()
    {
        using var tmp = new TempRootScope();
        var identity = new AppIdentity("modhost", "Modular Host", baseDataRoot: tmp.RootPath);
        var trace = new List<string>();
        var modA = new TestModule("alpha", 0, trace, "from-alpha");
        var modB = new TestModule("beta", 1, trace, "from-beta");

        await using var host = NativeShellHostBuilder.Create(identity)
            .ConfigureServices(s => s.AddCommunicationServices())
            .AddModule(modA)
            .AddModule(modB)
            .Build();

        await host.StartAsync();
        Assert.Equal(["init:alpha", "init:beta"], trace);
        Assert.True(Directory.Exists(identity.GetModuleDataDirectory("alpha")));
        Assert.True(Directory.Exists(identity.GetModuleDataDirectory("beta")));

        var catalog = host.Services.GetRequiredService<IModuleCatalog>();
        Assert.Equal(2, catalog.GetAllNavigationItems().Count);
        Assert.True(await catalog.ExecuteCommandAsync("cmd-alpha", host.Services));
        Assert.Contains("from-alpha", modB.ObservedTags);

        await host.StopAsync();
        Assert.Equal(["init:alpha", "init:beta", "stop:beta", "stop:alpha"], trace);
    }

    [Fact]
    public async Task SecurityAndIpc_BoundsNonceCache_Enforces256KiBLimit_AndSupportsILocalPeerGrant()
    {
        using var tmp = new TempRootScope("nsk-sec-v2");
        var identity = new AppIdentity("secv2", "Sec V2", baseDataRoot: tmp.RootPath);
        var key = Enumerable.Repeat((byte)0x5A, 32).ToArray();
        var auth = new IpcAuthManager(identity, key);

        var first = auth.CreateSignedEnvelope("op", "{}");
        Assert.True(auth.VerifyEnvelope(first));
        for (int i = 0; i < 1200; i++)
        {
            var env = auth.CreateSignedEnvelope("op", "{}");
            Assert.True(auth.VerifyEnvelope(env));
        }

        // Most recent nonce is still tracked against replay even after pruning
        var latest = auth.CreateSignedEnvelope("op", "{}");
        Assert.True(auth.VerifyEnvelope(latest));
        Assert.False(auth.VerifyEnvelope(latest));

        ILocalPeerGrant grant = new LocalPeerGrant(identity);
        await grant.GrantAsync();
        await using var server = await grant.OpenServerAsync();
        Assert.NotNull(server);
        server!.RegisterAllowedHandler("echo", (req, _) => Task.FromResult(req with { Operation = "ECHO_OK" }));
        await server.StartAsync();

        await using var client = await grant.OpenClientAsync();
        Assert.NotNull(client);
        var oversized = new string('x', 260 * 1024);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client!.SendAsync("echo", oversized));
        await grant.RevokeAsync();
    }
}
