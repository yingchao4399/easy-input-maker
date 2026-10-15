# 第三课作业代码：EasyInput 系统音频五灯律动

这是从 [EasyInput Maker](https://github.com/CY-CHENYUE/easy-input-maker) 公开固件基线制作的个人课程练习分支。原项目版权、非商业使用条件与第三方声明见本仓库的 [LICENSE](LICENSE) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 这份代码包含什么

- `components/keyboard/`：音频等级报文解析、五灯亮度映射与宿主测试。
- `main/platform/`、`main/app_main.cpp`：Wi-Fi 音频等级接收和既有状态灯体系接入。
- [`music-companion/`](music-companion/)：Windows 当前默认播放设备的 loopback 采音伴随程序源码，含 `.NET 10` 项目文件与自检入口。
- [`docs/security/audio-control-v1.md`](docs/security/audio-control-v1.md)：报文格式和本地网络使用边界。

音量映射使用的是系统音频能量等级，不做鼓点、旋律或情绪识别。它借鉴课程实时鼓机的声音到灯光反馈，但没有复刻原版 Beatbox 的完整功能。

## 软件侧验证与边界

本地记录中，`music-companion` 的 `--self-test` 输出“音频能量、报文和发现自检通过”；固件相关代码在提交 `64a0cc4` 中。本分支现有记录不能证明音乐固件已经烧录到开发板，或五颗灯随真实音乐变化。自检使用测试音频与模拟心跳，不等于真实 Wi-Fi 联动。手机中的效果视频尚未纳入本仓库。

开发板目标为 EasyInput V2.0 / ESP32-S3，固件工具链基线为 ESP-IDF 5.5.5。使用前先阅读 [`docs/hardware/easyinput-v2-safety.md`](docs/hardware/easyinput-v2-safety.md)；尤其不要改变 GPIO8 共享供电和 GPIO12 灯带数据脚。仓库不提供本机 Wi-Fi 凭据、`audio_host` 地址、可执行文件或固件镜像。
