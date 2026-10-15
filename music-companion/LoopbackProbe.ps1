$ErrorActionPreference = 'Stop'
$samples = 44100
$durationSeconds = 1
$wave = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($wave)
$writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
$writer.Write([int](36 + $samples * 2 * $durationSeconds))
$writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$writer.Write([int]16)
$writer.Write([short]1)
$writer.Write([short]1)
$writer.Write([int]$samples)
$writer.Write([int]($samples * 2))
$writer.Write([short]2)
$writer.Write([short]16)
$writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
$writer.Write([int]($samples * 2 * $durationSeconds))
for ($index = 0; $index -lt $samples * $durationSeconds; $index++) {
    $writer.Write([short](10000 * [Math]::Sin(2 * [Math]::PI * 440 * $index / $samples)))
}
$wave.Position = 0
$player = [System.Media.SoundPlayer]::new($wave)
$packages = Join-Path $env:USERPROFILE '.nuget\packages'
Add-Type -Path (Join-Path $packages 'naudio.core\3.1.0\lib\net9.0\NAudio.Core.dll')
Add-Type -Path (Join-Path $packages 'naudio.wasapi\3.1.0\lib\net9.0\NAudio.Wasapi.dll')
$endpoints = [NAudio.CoreAudioApi.MMDeviceEnumerator]::new()
$device = $endpoints.GetDefaultAudioEndpoint([NAudio.CoreAudioApi.DataFlow]::Render, [NAudio.CoreAudioApi.Role]::Multimedia)
$originalMute = $device.AudioEndpointVolume.Mute
$udp = [System.Net.Sockets.UdpClient]::new(0)
try {
    $device.AudioEndpointVolume.Mute = $false
    $udp.Client.ReceiveTimeout = 200
    $heartbeat = [byte[]]::new(20)
    [System.Text.Encoding]::ASCII.GetBytes('EIHB').CopyTo($heartbeat, 0)
    $heartbeat[4] = 1
    $target = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 17334)
    [void]$udp.Send($heartbeat, $heartbeat.Length, $target)
    Start-Sleep -Milliseconds 150
    $player.PlaySync()
    $highest = 0
    $frames = 0
    $stop = [DateTime]::UtcNow.AddSeconds(2)
    while ([DateTime]::UtcNow -lt $stop) {
        try {
            $sender = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
            $packet = $udp.Receive([ref]$sender)
            if ($packet.Length -eq 12 -and [System.Text.Encoding]::ASCII.GetString($packet, 0, 4) -eq 'EILM') {
                $frames++
                $highest = [Math]::Max($highest, [int]$packet[5])
            }
        } catch [System.Net.Sockets.SocketException] {
            if ($_.Exception.SocketErrorCode -ne [System.Net.Sockets.SocketError]::TimedOut) { throw }
        }
    }
    "frames=$frames highest=$highest"
    if ($frames -eq 0 -or $highest -eq 0) { throw '系统音频未进入 EILM 数据流。' }
} finally {
    $device.AudioEndpointVolume.Mute = $originalMute
    $device.Dispose()
    $endpoints.Dispose()
    $udp.Close()
    $player.Dispose()
    $writer.Dispose()
    $wave.Dispose()
}
