using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MusicCompanion;

internal static class Program
{
    private const int DiscoveryPort = 17334;
    private const int FrameIntervalMs = 40;
    private static int level;
    private static long lastAudioTick;
    private static long audioCallbacks;
    private static long nonzeroCallbacks;
    private static int highestRawLevel;
    private static int highestPcmPeak;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
            return SelfTest();
        var diagnostic = args.Length == 1 && args[0] == "--diagnostic";
        if (args.Length != 0 && !diagnostic)
        {
            Console.Error.WriteLine("用法：MusicCompanion [--self-test|--diagnostic]");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        using var endpoints = new MMDeviceEnumerator();
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        var discovery = new BoardDiscovery();
        var receiver = ReceiveHeartbeats(socket, discovery, cancellation.Token);
        WasapiRecorder? recorder = null;
        string? deviceId = null;
        var recorderStopped = 0;

        WasapiRecorder StartCapture(MMDevice endpoint)
        {
            // WASAPI loopback binds one render endpoint, so the main loop
            // recreates this recorder when Windows changes its default output.
            var created = new WasapiRecorderBuilder()
                .WithDevice(endpoint)
                .WithLoopbackCapture()
                .WithPollingSync()
                .WithBufferLength(40)
                .WithFormat(new WaveFormat(48000, 16, 2))
                .Build();
            created.DataAvailable += (buffer, _, _, _) =>
            {
                if (!ReferenceEquals(recorder, created)) return;
                var next = LevelFromPcm16(buffer);
                Interlocked.Increment(ref audioCallbacks);
                if (buffer.IndexOfAnyExcept((byte)0) >= 0)
                    Interlocked.Increment(ref nonzeroCallbacks);
                if (diagnostic)
                {
                    var peak = 0;
                    for (var offset = 0; offset + 1 < buffer.Length; offset += 2)
                        peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(offset, 2))));
                    if (peak > Volatile.Read(ref highestPcmPeak))
                        Volatile.Write(ref highestPcmPeak, peak);
                }
                if (next > Volatile.Read(ref highestRawLevel))
                    Volatile.Write(ref highestRawLevel, next);
                var previous = Volatile.Read(ref level);
                Volatile.Write(ref level, next > previous
                    ? (previous * 2 + next * 3) / 5
                    : (previous * 4 + next) / 5);
                Interlocked.Exchange(ref lastAudioTick, Environment.TickCount64);
            };
            created.RecordingStopped += (_, e) =>
            {
                if (!ReferenceEquals(recorder, created)) return;
                if (e.Exception is not null)
                    Console.Error.WriteLine($"音频采集停止：{e.Exception.Message}");
                Volatile.Write(ref recorderStopped, 1);
            };
            recorder = created;
            deviceId = endpoint.ID;
            Volatile.Write(ref recorderStopped, 0);
            Volatile.Write(ref level, 0);
            Interlocked.Exchange(ref lastAudioTick, 0);
            Console.WriteLine($"正在采集：{endpoint.FriendlyName}");
            if (diagnostic)
                Console.WriteLine($"设备静音={endpoint.AudioEndpointVolume.Mute}，音量={endpoint.AudioEndpointVolume.MasterVolumeLevelScalar:P0}");
            created.StartRecording();
            return created;
        }

        Console.WriteLine("正在监听电脑当前播放设备，并等待开发板发现心跳。按 Ctrl+C 退出。");
        uint sequence = 0;
        bool announced = false;
        var nextDiagnosticTick = Environment.TickCount64 + 1000;
        var nextDeviceCheckTick = 0L;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (Environment.TickCount64 >= nextDeviceCheckTick || Volatile.Read(ref recorderStopped) != 0)
                {
                    nextDeviceCheckTick = Environment.TickCount64 + 1000;
                    try
                    {
                        using var selected = endpoints.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        if (recorder is null || deviceId != selected.ID || Volatile.Read(ref recorderStopped) != 0)
                        {
                            if (recorder is not null)
                            {
                                var previous = recorder;
                                recorder = null;
                                previous.StopRecording();
                                await previous.DisposeAsync();
                            }
                            StartCapture(selected);
                        }
                    }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine($"等待默认播放设备：{error.Message}");
                        Volatile.Write(ref level, 0);
                        Interlocked.Exchange(ref lastAudioTick, 0);
                    }
                }
                if (diagnostic && Environment.TickCount64 >= nextDiagnosticTick)
                {
                    Console.WriteLine($"音频回调={Interlocked.Exchange(ref audioCallbacks, 0)}，非零回调={Interlocked.Exchange(ref nonzeroCallbacks, 0)}，PCM 峰值={Interlocked.Exchange(ref highestPcmPeak, 0)}，原始最高等级={Interlocked.Exchange(ref highestRawLevel, 0)}，已发送等级={Volatile.Read(ref level)}");
                    nextDiagnosticTick = Environment.TickCount64 + 1000;
                }
                if (discovery.Current is { } board)
                {
                    if (!announced)
                    {
                        Console.WriteLine("已发现开发板，正在发送系统音频能量；静音时恢复原灯光。" );
                        announced = true;
                    }
                    var fresh = Environment.TickCount64 - Interlocked.Read(ref lastAudioTick) < 200;
                    if (!fresh) Volatile.Write(ref level, 0);
                    var packet = EncodeLevel(fresh ? (byte)Volatile.Read(ref level) : (byte)0,
                        sequence++);
                    await socket.SendAsync(packet, board, cancellation.Token);
                }
                else if (announced)
                {
                    Console.WriteLine("开发板心跳中断，等待重新发现。" );
                    announced = false;
                }
                await Task.Delay(FrameIntervalMs, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (discovery.Current is { } board)
            {
                try { await socket.SendAsync(EncodeLevel(0, sequence), board); }
                catch (SocketException) { }
            }
            cancellation.Cancel();
            if (recorder is not null)
            {
                var previous = recorder;
                recorder = null;
                previous.StopRecording();
                await previous.DisposeAsync();
            }
            try { await receiver; }
            catch (OperationCanceledException) { }
        }
        return 0;
    }

    private static async Task ReceiveHeartbeats(UdpClient socket, BoardDiscovery discovery,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var received = await socket.ReceiveAsync(token);
                discovery.Observe(received.Buffer, received.RemoteEndPoint);
            }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.ConnectionReset ||
                                                error.SocketErrorCode == SocketError.HostUnreachable ||
                                                error.SocketErrorCode == SocketError.NetworkUnreachable)
            {
                // Windows reports ICMP failures from a departed UDP peer on
                // the receive socket. Discovery expiry handles that peer.
            }
        }
    }

    private static byte LevelFromPcm16(ReadOnlySpan<byte> bytes)
    {
        var count = bytes.Length / 2;
        if (count == 0) return 0;
        double sum = 0;
        for (var index = 0; index < count; ++index)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(index * 2, 2));
            sum += (double)sample * sample;
        }
        var rms = Math.Sqrt(sum / count) / 32768.0;
        return (byte)Math.Clamp((int)Math.Round((rms - 0.004) * 1800), 0, 255);
    }

    private static byte[] EncodeLevel(byte value, uint sequence)
    {
        var packet = new byte[12];
        "EILM"u8.CopyTo(packet);
        packet[4] = 1;
        packet[5] = value;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), sequence);
        return packet;
    }

    private static int SelfTest()
    {
        Span<byte> tone = stackalloc byte[400];
        for (var index = 0; index < tone.Length; index += 2)
            BinaryPrimitives.WriteInt16LittleEndian(tone.Slice(index, 2),
                (short)((index / 2 % 2 == 0) ? 8000 : -8000));
        var quiet = LevelFromPcm16(new byte[400]);
        var audible = LevelFromPcm16(tone);
        var frame = EncodeLevel(audible, 0x12345678);
        if (quiet != 0 || audible < 100 || frame[5] != audible ||
            frame[8] != 0x78 || frame[11] != 0x12 ||
            !frame.AsSpan(0, 4).SequenceEqual("EILM"u8))
            throw new Exception("PCM 能量或 wire 自检失败");
        var discovery = new BoardDiscovery();
        var heartbeat = new byte[20];
        "EIHB"u8.CopyTo(heartbeat);
        heartbeat[4] = 1;
        discovery.Observe(heartbeat, new IPEndPoint(IPAddress.Loopback, 12345));
        if (discovery.Current?.Port != 12345)
            throw new Exception("开发板发现自检失败");
        Console.WriteLine("音频能量、报文和发现自检通过。");
        return 0;
    }
}

internal sealed class BoardDiscovery
{
    private readonly object gate = new();
    private IPEndPoint? endpoint;
    private long lastSeen;

    internal IPEndPoint? Current
    {
        get
        {
            lock (gate)
                return Environment.TickCount64 - lastSeen < 6000 ? endpoint : null;
        }
    }

    internal void Observe(ReadOnlySpan<byte> bytes, IPEndPoint sender)
    {
        if (bytes.Length < 20 || !bytes.Slice(0, 4).SequenceEqual("EIHB"u8) ||
            bytes[4] != 1 || !sender.Address.IsIPv4MappedToIPv6 &&
            sender.Address.AddressFamily != AddressFamily.InterNetwork)
            return;
        lock (gate)
        {
            if (endpoint is not null && Environment.TickCount64 - lastSeen < 6000 &&
                !endpoint.Address.Equals(sender.Address))
                return;
            endpoint = sender;
            lastSeen = Environment.TickCount64;
        }
    }
}
