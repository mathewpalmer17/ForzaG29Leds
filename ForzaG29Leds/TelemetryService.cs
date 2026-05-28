using System.Net;
using System.Net.Sockets;

namespace ForzaG29Leds;

public sealed class TelemetryService : IDisposable
{
    public event Action<bool>?   WheelStatusChanged;     // true = connected
    public event Action<bool>?   TelemetryStatusChanged; // true = receiving packets
    public event Action<string>? ServiceError;           // e.g. port already in use
    public event Action<ForzaTelemetryPacket>? PacketReceived; // throttled ~4 Hz

    private Settings _settings = new();

    // _leds is always accessed through _ledsLock to prevent the flash loop
    // from calling into a handle that the reconnect loop is simultaneously
    // disposing and replacing.
    private readonly object _ledsLock = new();
    private LogitechWheelLeds? _leds;

    private CancellationTokenSource? _cts;
    private Task? _udpTask;
    private Task? _flashTask;
    private Task? _reconnectTask;

    private volatile bool _atLimiter;
    private long _lastFlashTick;
    private bool _flashState;
    private long _lastPacketTick;
    private long _lastDumpTick;
    private bool _telemetryActive;   // tracks last-fired state; prevents 60 Hz dispatches
    private bool _disposed;

    public bool IsWheelConnected     => _leds?.IsOpen ?? false;
    public bool IsReceivingTelemetry => Environment.TickCount64 - _lastPacketTick < 3000;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start(Settings settings)
    {
        _settings = settings;
        // Start loops first so _cts is never null when ApplySettings is called.
        // HID discovery runs in background and is independent of the loops.
        StartLoops();
        Task.Run(InitHid);
    }

    public void ApplySettings(Settings settings)
    {
        bool portChanged = settings.Port != _settings.Port;
        _settings = settings;
        if (portChanged) RestartLoops();
    }

    public void TestLeds() => Task.Run(FlashTest);

    private void InitHid()
    {
        var leds = LogitechWheelLeds.Open();
        lock (_ledsLock) { _leds = leds; }
        WheelStatusChanged?.Invoke(leds?.IsOpen ?? false);

        // Run flash test concurrently — does not block loop startup
        if (leds?.IsOpen == true)
            Task.Run(FlashTest);
    }

    private void StartLoops()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _udpTask       = Task.Run(() => UdpLoop(ct),       ct);
        _flashTask     = Task.Run(() => FlashLoop(ct),     ct);
        _reconnectTask = Task.Run(() => ReconnectLoop(ct), ct);
    }

    private void StopLoops()
    {
        _cts?.Cancel();
        try
        {
            Task.WhenAll(_udpTask       ?? Task.CompletedTask,
                         _flashTask     ?? Task.CompletedTask,
                         _reconnectTask ?? Task.CompletedTask).Wait(1500);
        }
        catch (AggregateException) { }   // expected on cancellation
        _cts?.Dispose();
        _cts = null;
    }

    private void RestartLoops()
    {
        StopLoops();
        StartLoops();
    }

    // ── Startup flash test ────────────────────────────────────────────────────

    private void FlashTest()
    {
        for (int i = 0; i < 3; i++)
        {
            LogitechWheelLeds? leds;
            lock (_ledsLock) { leds = _leds; }
            leds?.AllOn();
            Thread.Sleep(200);
            lock (_ledsLock) { leds = _leds; }
            leds?.TurnOff();
            Thread.Sleep(200);
        }
    }

    // ── UDP loop ──────────────────────────────────────────────────────────────

    private void UdpLoop(CancellationToken ct)
    {
        FireTelemetry(false);
        UdpClient? udp = null;
        try
        {
            try
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Any, _settings.Port));
            }
            catch (SocketException ex) when (!ct.IsCancellationRequested)
            {
                ServiceError?.Invoke(
                    $"Cannot bind UDP port {_settings.Port}: {ex.Message}\n" +
                    "Check that no other app is using the same port.");
                return;
            }

            ct.Register(() => { try { udp.Close(); } catch { } });

            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                byte[] data;
                try { data = udp.Receive(ref remote); }
                catch (SocketException) when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }

                if (data.Length < 324) continue;

                _lastPacketTick = Environment.TickCount64;
                FireTelemetry(true);
                ProcessPacket(data);
            }
        }
        catch (SocketException) when (ct.IsCancellationRequested) { }
        finally
        {
            udp?.Dispose();
            FireTelemetry(false);
        }
    }

    // Only fires TelemetryStatusChanged when the state actually changes
    // — avoids ~60 cross-thread dispatches per second while in a race.
    private void FireTelemetry(bool active)
    {
        if (active == _telemetryActive) return;
        _telemetryActive = active;
        TelemetryStatusChanged?.Invoke(active);
    }

    // ── LED stage logic (pure, testable) ─────────────────────────────────────

    internal enum LedStage { Off, Progressive, Solid, Flash }

    internal static (LedStage stage, float progressRatio) ComputeLedState(
        float ratio, float idleRatio, float solidRatio, float flashRatio)
    {
        if (ratio >= flashRatio) return (LedStage.Flash,       0f);
        if (ratio >= solidRatio) return (LedStage.Solid,       0f);
        if (ratio <  idleRatio)  return (LedStage.Off,         0f);

        float range  = solidRatio - idleRatio;
        float scaled = range > 0f ? (ratio - idleRatio) / range : 0f;
        return (LedStage.Progressive, Math.Clamp(scaled, 0f, 1f));
    }

    // ── Packet processing ─────────────────────────────────────────────────────

    private unsafe void ProcessPacket(byte[] data)
    {
        ForzaTelemetryPacket pkt;
        fixed (byte* ptr = data) { pkt = *(ForzaTelemetryPacket*)ptr; }

        LogitechWheelLeds? leds;
        lock (_ledsLock) { leds = _leds; }

        if (pkt.IsRaceOn == 0) { leds?.TurnOff(); return; }

        float current   = pkt.CurrentEngineRpm;
        float max       = pkt.EngineMaxRpm;
        float idle      = pkt.EngineIdleRpm;
        if (max <= 0f) return;

        float ratio     = current / max;
        float idleRatio = idle / max;

        var (stage, progressRatio) = ComputeLedState(
            ratio, idleRatio, _settings.SolidRatio, _settings.FlashRatio);

        _atLimiter = stage == LedStage.Flash;

        if (leds is not null && !_atLimiter)
        {
            switch (stage)
            {
                case LedStage.Solid:       leds.AllOn();                     break;
                case LedStage.Progressive: leds.SetFromRatio(progressRatio); break;
                case LedStage.Off:         leds.TurnOff();                   break;
            }
        }

        long tick = Environment.TickCount64;
        if (tick - _lastDumpTick >= 250)
        {
            _lastDumpTick = tick;
            PacketReceived?.Invoke(pkt);
        }
    }

    // ── Reconnect loop ────────────────────────────────────────────────────────

    private void ReconnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            for (int i = 0; i < 30 && !ct.IsCancellationRequested; i++)
                Thread.Sleep(100);
            if (ct.IsCancellationRequested) break;

            LogitechWheelLeds? current;
            lock (_ledsLock) { current = _leds; }
            if (current?.IsOpen == true) continue;

            bool hadDevice = current != null;
            current?.Dispose();

            var newLeds = LogitechWheelLeds.Open();
            lock (_ledsLock) { _leds = newLeds; }

            bool connected = newLeds?.IsOpen == true;
            if (hadDevice || connected)
                WheelStatusChanged?.Invoke(connected);

            if (connected) Task.Run(FlashTest);
        }
    }

    // ── Flash loop ────────────────────────────────────────────────────────────

    private void FlashLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_atLimiter)
            {
                LogitechWheelLeds? leds;
                lock (_ledsLock) { leds = _leds; }
                if (leds is not null)
                {
                    long now = Environment.TickCount64;
                    if (now - _lastFlashTick >= _settings.FlashIntervalMs)
                    {
                        _lastFlashTick = now;
                        _flashState    = !_flashState;
                        if (_flashState) leds.AllOn();
                        else             leds.TurnOff();
                    }
                }
            }
            Thread.Sleep(16);
        }
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopLoops();
        LogitechWheelLeds? leds;
        lock (_ledsLock) { leds = _leds; _leds = null; }
        leds?.TurnOff();
        leds?.Dispose();
    }
}
