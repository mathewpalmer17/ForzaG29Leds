using System.Net;
using System.Net.Sockets;

namespace ForzaG29Leds;

public sealed class TelemetryService : IDisposable
{
    public event Action<bool>? G29StatusChanged;       // true = connected
    public event Action<bool>? TelemetryStatusChanged; // true = receiving packets
    public event Action<ForzaTelemetryPacket>? PacketReceived;         // throttled ~4 Hz

    private Settings _settings = new();
    private volatile G29HidLeds? _leds;
    private CancellationTokenSource? _cts;
    private Task? _udpTask;
    private Task? _flashTask;
    private Task? _reconnectTask;

    private volatile bool _atLimiter;
    private long _lastFlashTick;
    private bool _flashState;
    private long _lastPacketTick;
    private long _lastDumpTick;
    private bool _disposed;

    public bool IsG29Connected => _leds?.IsOpen ?? false;
    public bool IsReceivingTelemetry => Environment.TickCount64 - _lastPacketTick < 3000;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start(Settings settings)
    {
        _settings = settings;
        Task.Run(InitHid);
    }

    public void ApplySettings(Settings settings)
    {
        bool portChanged = settings.Port != _settings.Port;
        _settings = settings;
        if (portChanged) RestartLoops();
    }

    private void InitHid()
    {
        _leds = G29HidLeds.Open();
        G29StatusChanged?.Invoke(_leds?.IsOpen ?? false);

        if (_leds?.IsOpen == true)
            FlashTest();

        StartLoops();
    }

    private void StartLoops()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _udpTask      = Task.Run(() => UdpLoop(ct),       ct);
        _flashTask    = Task.Run(() => FlashLoop(ct),     ct);
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
        catch { }
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
            _leds!.AllOn();
            Thread.Sleep(200);
            _leds.TurnOff();
            Thread.Sleep(200);
        }
    }

    // ── UDP loop ──────────────────────────────────────────────────────────────

    private void UdpLoop(CancellationToken ct)
    {
        TelemetryStatusChanged?.Invoke(false);
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, _settings.Port));
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
                TelemetryStatusChanged?.Invoke(true);
                ProcessPacket(data);
            }
        }
        catch (SocketException) when (ct.IsCancellationRequested) { }
        finally
        {
            udp?.Dispose();
            TelemetryStatusChanged?.Invoke(false);
        }
    }

    // ── LED stage logic (pure, testable) ─────────────────────────────────────

    internal enum LedStage { Off, Progressive, Solid, Flash }

    internal static (LedStage stage, float progressRatio) ComputeLedState(
        float ratio, float idleRatio, float solidRatio, float flashRatio)
    {
        if (ratio >= flashRatio) return (LedStage.Flash, 0f);
        if (ratio >= solidRatio) return (LedStage.Solid, 0f);
        if (ratio < idleRatio) return (LedStage.Off, 0f);

        float range = solidRatio - idleRatio;
        float scaled = range > 0f ? (ratio - idleRatio) / range : 0f;
        return (LedStage.Progressive, Math.Clamp(scaled, 0f, 1f));
    }

    // ── Packet processing ─────────────────────────────────────────────────────

    private unsafe void ProcessPacket(byte[] data)
    {
        ForzaTelemetryPacket pkt;
        fixed (byte* ptr = data) { pkt = *(ForzaTelemetryPacket*)ptr; }

        if (pkt.IsRaceOn == 0) { _leds?.TurnOff(); return; }

        float current = pkt.CurrentEngineRpm;
        float max = pkt.EngineMaxRpm;
        float idle = pkt.EngineIdleRpm;
        if (max <= 0f) return;

        float ratio    = current / max;
        float idleRatio = idle / max;

        var (stage, progressRatio) = ComputeLedState(
            ratio, idleRatio, _settings.SolidRatio, _settings.FlashRatio);

        _atLimiter = stage == LedStage.Flash;

        if (_leds is not null && !_atLimiter)
        {
            switch (stage)
            {
                case LedStage.Solid: _leds.AllOn(); break;
                case LedStage.Progressive: _leds.SetFromRatio(progressRatio); break;
                case LedStage.Off: _leds.TurnOff(); break;
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
            // Sleep in small chunks rather than ct.WaitHandle.WaitOne — accessing
            // WaitHandle allocates a kernel event that gets disposed by _cts.Dispose()
            // in StopLoops() while this thread is still blocked, causing c000041d.
            for (int i = 0; i < 30 && !ct.IsCancellationRequested; i++)
                Thread.Sleep(100);
            if (ct.IsCancellationRequested) break;
            if (_leds?.IsOpen == true) continue;

            bool hadDevice = _leds != null; // was open before but handle was invalidated
            _leds?.Dispose();
            _leds = G29HidLeds.Open();
            bool connected = _leds?.IsOpen == true;

            if (hadDevice || connected)
                G29StatusChanged?.Invoke(connected);

            if (connected) FlashTest();
        }
    }

    // ── Flash loop ────────────────────────────────────────────────────────────

    private void FlashLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_atLimiter && _leds is not null)
            {
                long now = Environment.TickCount64;
                if (now - _lastFlashTick >= _settings.FlashIntervalMs)
                {
                    _lastFlashTick = now;
                    _flashState = !_flashState;
                    if (_flashState) _leds.AllOn();
                    else _leds.TurnOff();
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
        _leds?.TurnOff();
        _leds?.Dispose();
    }
}
