# Windows 系统音频五灯伴随程序

这个程序读取 Windows 当前默认播放设备的 loopback 音频能量，经本地 Wi-Fi 向 EasyInput V2.0 固件发送五灯律动等级。默认播放设备是耳机时也会采集耳机输出。它不上传音频内容；UDP 报文只有 0～255 的能量等级。

## 前提

- 开发板刷入包含 EILM 支持的固件，已配置 Wi-Fi、`audio_host`（本机在同一局域网的 IPv4 地址）及有效 `audio_port`；原麦克风 companion 若使用 17333，可同时运行。
- Windows 防火墙允许程序接收 UDP 17334 的板端心跳。仓库只提交源码，不包含 `publish/MusicCompanion.exe`；可用 .NET 10 SDK 自行构建。
- 在受信任的局域网使用。协议目前按来源 IP 限制，没有密码学认证。

## 启动

```powershell
dotnet run -c Release --project MusicCompanion.csproj
```

需要从源码重新生成可执行文件时，使用 .NET 10 SDK 执行 `dotnet publish MusicCompanion.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`。

启动后程序监听电脑当前默认播放设备，并等待板子每隔约 2 秒发来的 `EIHB` 心跳。切换扬声器或耳机时程序会自动重新绑定新默认设备。发现板子后会显示提示并每 40 毫秒回送 `EILM` 等级。按 Ctrl+C 退出；静音或心跳断线时，灯效会自动恢复原状态。

自检：

```powershell
dotnet run -c Release --project MusicCompanion.csproj -- --self-test
```

自检使用测试音频与模拟心跳，只验证电脑端计算和报文逻辑；不能证明开发板已经烧录或真实灯效。

如果耳机播放音乐但灯不动，先看程序是否显示“已发现开发板”，再确认 Windows 默认播放设备确实是这副耳机，并检查板端 Wi-Fi、`audio_host` 和防火墙设置。程序不需要开启板载麦克风。

诊断时可运行 `MusicCompanion.exe --diagnostic` 查看当前默认设备、静音状态和采集等级。源码目录的 `LoopbackProbe.ps1` 会临时取消静音并播放一秒测试音，结束时恢复原静音状态；此脚本需要本机已还原 NAudio NuGet 包。
