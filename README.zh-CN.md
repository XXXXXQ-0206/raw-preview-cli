# RawPreview CLI

[English](README.md) | [简体中文](README.zh-CN.md)

通过已安装的 Microsoft Photos 调整大小链路，将 Sony ARW RAW 照片导出为 JPEG 的 Windows 命令行工具。

## 项目概览

RawPreview CLI 调用 Windows“照片”应用使用的 Photos resize 链路，将 Sony ARW 文件导出为 JPEG。项目把依赖 Photos 的代码限制在独立 Worker 进程中，把文件枚举、元数据检查、输出验证和批处理策略保留在 CLI 中。

项目仅支持 Windows，因为渲染后端依赖 Windows Runtime，以及本机安装的 Photos 和 Raw Image Extension 包。

## 功能

- 支持单个或批量 ARW 转 JPEG。
- 使用 Photos RAW 渲染链，而不是自行实现第三方去马赛克器。
- 保留竖拍物理尺寸并校验 EXIF Orientation。
- 支持 1 到 100 的 JPEG 质量设置。
- 支持输出冲突检测和可选覆盖。
- 使用唯一 .partial 临时文件和写入后校验实现原子输出。
- 支持 JSON 和 JSONL，便于自动化。
- 提供 doctor 能力诊断和 inspect 元数据检查。
- 仓库不保存 Microsoft Photos 私有 DLL，也不保存用户照片。

## 截图

这是命令行工具，因此不适用截图。默认输出人类可读文本，使用 --json 后输出 JSON/JSONL。

## 环境要求

- Windows 10 22H2 或 Windows 11。
- 用于构建的 .NET 9 SDK。
- Microsoft Photos。
- Microsoft Raw Image Extension，可通过 Microsoft Store 或受支持的 Windows 包机制安装。
- 与所选运行时标识匹配的 x64 或 ARM64 Windows。

Photos 和 Raw Image Extension 属于第三方平台组件。其安装版本会影响渲染结果和可用的私有合约。

## 安装

克隆仓库后，在必要时使用提升权限的 PowerShell 安装本地 Worker 包：

~~~powershell
pwsh -NoLogo -NoProfile -File ./scripts/install-worker.ps1 -Configuration Release -RuntimeIdentifier win-x64
~~~

ARM64 Windows 使用 win-arm64。安装脚本从本机 Photos 安装目录寻找匹配的桥接 DLL，不下载或再分发 Microsoft 二进制文件。

## 使用

~~~powershell
dotnet run --project ./src/RawPreview.Cli -- doctor --json
dotnet run --project ./src/RawPreview.Cli -- inspect '<ARW_FILE>' --json
dotnet run --project ./src/RawPreview.Cli -- export '<SOURCE_DIRECTORY>' --output '<OUTPUT_DIRECTORY>' --quality 95 --json
~~~

发布后的可执行文件：

~~~powershell
rawpreview.exe doctor
rawpreview.exe export '<SOURCE_DIRECTORY>' --output '<OUTPUT_DIRECTORY>' --quality 95 --overwrite
~~~

导出保留源文件名主体，生成小写 .jpg，保留竖拍像素尺寸并校验 EXIF Orientation。启用 --json 时，每个输入文件生成一条 JSONL 结果。

## 构建

~~~powershell
dotnet restore RawPreview.sln
dotnet build RawPreview.sln --configuration Release
dotnet test RawPreview.sln --configuration Release --no-build
dotnet publish ./src/RawPreview.Cli/RawPreview.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o artifacts/publish/cli-win-x64
~~~

测试本地发布版本时，Worker 应从同一次构建安装。不要把 Photos DLL、WinMD、ARW 或生成的图像放进仓库。

## 项目结构

~~~text
src/RawPreview.Cli       公开 CLI、批处理策略、元数据和 JPEG 校验
src/RawPreview.Shared    有版本的 JSONL Worker 协议
src/RawPreview.Worker    Windows Photos 发现和 WinRT/ABI 适配层
tests/RawPreview.Tests   离线单元测试和分配量测试
tests/RawPreview.IntegrationTests  可选的本机 Photos 集成测试
scripts/                  Worker 安装和仓库边界检查
packaging/                最小 full-trust AppX 清单
.github/                  CI、CodeQL、Dependabot 和贡献模板
docs/                     设计文档和简化审查报告
~~~

## 路线图

- 增加不发布用户媒体的可复现实机 Photos 竖拍测试夹具。
- 扩展对未来 Photos 和 Raw Image Extension 版本的能力协商。
- 测量并记录支持的 Windows 架构上的端到端导出吞吐量。
- 在打包和签名策略确定后，增加签名且可复现的发布产物。

## 贡献

提交 Issue 或 Pull Request 前请阅读 CONTRIBUTING.md。改动应保持 CLI/Worker 边界，不打包 Microsoft 载荷，添加针对性测试，并通过本地质量门。

## 许可证

本仓库原创代码采用 MIT License。Microsoft Photos、Windows Runtime 组件和 Raw Image Extension 不属于本许可证，仍受各自条款约束。

## 常见问题

### 为什么仅支持 Windows？

渲染器依赖 Windows Photos 私有 WinRT 合约和 Windows RAW 支持。macOS 与 Linux 没有相同的后端。

### 为什么仓库没有 Microsoft DLL？

安装时从本机 Photos 目录发现这些 DLL。这样既避免再分发平台私有二进制文件，也让不同 Photos 版本可以使用各自匹配的组件。

### 会修改 RAW 源文件吗？

不会。CLI 读取源元数据并创建新的 JPEG，不修改 ARW 文件。

## 致谢

- Microsoft Photos 和 Windows RAW 支持提供 Worker 使用的渲染后端。
- .NET 和 MSTest 团队提供运行时与测试基础设施。

## 免责声明

本软件按“原样”（AS IS）提供，不附带任何明示或默示保证。在适用法律允许的最大范围内，作者和贡献者不对因本软件或其使用产生或与之相关的任何直接、间接、附带、特殊、后果性或其他损害承担责任。用户自行承担使用风险，并负责验证导出文件以及遵守适用法律、许可证、平台条款和隐私要求。不得将本项目用于违法活动、侵权、未授权访问，或处理无权处理的数据。

Microsoft、Windows、Microsoft Photos 及相关标识归 Microsoft 所有。本项目独立于 Microsoft，也未获得 Microsoft 背书。
