# rawpreview simplify audit

审查日期：2026-08-30

## 结论

本轮对当前仓库全部生产代码、测试、脚本、打包清单和工程配置进行了逐文件、逐段审查。已实施的低风险简化如下：

- 将重复的 WorkerRequest、WorkerResponse、RuntimeReportDto 和 JSON 协议集中到 src/RawPreview.Shared/WorkerProtocol.cs。
- 将 Worker 的请求解析、异常映射、运行时缓存和失败响应合并到 WorkerSession。
- 将 Photos 支持模块从每张图片重复 LoadLibraryEx 改为每个 Worker 进程加载一次。
- 在每项导出 finally 中释放 Lightbox.dll 句柄，避免长批次资源累积。
- 将 JPEG 验证从整文件 ReadAllBytes 改为流式读取头部和必要 APP1 段，其余可寻址段直接跳过。
- 损坏 ARW 的元数据读取改为单项 InputMetadataFailed，同批其他文件继续处理。
- 删除没有调用方的 package-probe、package-export 和临时启动标记文件逻辑。
- 增加协议约束、竖拍、双端序 TIFF、JPEG 截断、读尾部、分配量、冲突和坏输入隔离测试。

反射 async 投影、COM/WinRT ABI、资源释放嵌套和 OutputFileStream 没有继续压缩。这些部分对应私有 ABI、引用计数所有权和已验证的 Photos 输出通道，合并会扩大回归面。

## 量化范围

按 PowerShell 对当前工作树统计，排除 .git、bin、obj、artifacts 和 runtime-receipts：

| 范围 | 文件数 | 行数 |
|---|---:|---:|
| src C# 与项目文件 | 19 | 1654 |
| tests C# 与项目文件 | 5 | 413 |
| scripts 与 packaging | 3 | 141 |
| 根目录工程、文档和配置 | 6 | 156 |

仅 C# 统计：生产代码 1765 行，测试代码 469 行。行数不是唯一优化目标；行为不变量、资源生命周期和性能收据优先。

## 逐文件审查

下列区间覆盖当前文件全部行。

### CLI 和导出层

- src/RawPreview.Cli/CommandLine.cs:1-138：保留手写解析器、显式错误信息、质量范围、默认输出目录和绝对路径归一化。固定命令不需要第三方解析器，未发现安全可合并分支。
- src/RawPreview.Cli/Program.cs:1-105：保留顶层异常到退出码映射、doctor、inspect、export、setup-raw 入口和 Windows-only winget 分支。紧凑 JSON 和漂亮 JSON 是不同公开输出模式，不能误并。
- src/RawPreview.Cli/Export/ExportOptions.cs:1-20：两个 record 是 CLI 到 pipeline 的最小数据边界，Json 字段暂保以维护公开选项和输出兼容性。
- src/RawPreview.Cli/Export/OutputPathPolicy.cs:1-32：文件枚举、大小写不敏感扩展名、稳定排序、同名冲突预检和目标命名均为独立不变量，保留。
- src/RawPreview.Cli/Export/ExportPipeline.cs:1-82：保留串行处理、已有 JPEG 校验、唯一 partial、验证后移动、finally 清理和逐项 JSONL 结果。已增加坏 ARW 单项失败继续处理；剩余重复属于状态机边界。
- src/RawPreview.Cli/Export/JpegValidator.cs:1-122：流式读取保留 SOI、帧尺寸、截断、APP1 EXIF 和 Orientation 语义。Span、ArrayPool 和 seek 分别承担低分配、段缓存和少读尾部职责。
- src/RawPreview.Cli/Export/RawMetadataReader.cs:1-158：保留 TIFF 双端序、IFD 队列、visited 防循环、Exif/SubIFD 跳转、字段类型和范围检查。ARW 的 IFD 是图结构，队列不是冗余。
- src/RawPreview.Cli/Runtime/IWorkerClient.cs:1-8：单方法接口是 pipeline 测试替身和真实客户端的最小依赖，保留。
- src/RawPreview.Cli/Runtime/WorkerClient.cs:1-171：保留普通进程、AppX AUMID、Named Pipe、ArgumentList、取消杀进程树、stderr 排空和工作目录。已删除 CreateStartInfo 未使用的 request 参数；两条传输路径不能强行合并。
- src/RawPreview.Cli/Runtime/WorkerClientException.cs:1-6：最小带 code 异常，保留每项错误码，保留。

### Shared 和 Worker

- src/RawPreview.Shared/WorkerProtocol.cs:1-85：单一协议定义替代重复类型，统一版本、操作名、质量、绝对路径和 jpg 目标约束；不向共享层泄漏 Photos 业务逻辑。
- src/RawPreview.Worker/Program.cs:1-93：正式入口只保留 jsonl 和 package-pipe。WorkerSession 缓存一次 runtime，统一解析、异常映射和 Failure 构造；删除无引用探针入口和启动标记。
- src/RawPreview.Worker/RawPreview.Worker.csproj:1-16：保留 Windows TFM、x64/arm64 RID、自包含单文件设置、Windows SDK 引用和共享协议链接。
- src/RawPreview.Worker/Photos/PhotosResizeBackend.cs:1-16：保留薄适配层，只负责能力门、Lightbox 路径、类型转换、invoker 调用和结果包装。
- src/RawPreview.Worker/Photos/PhotosRuntimeException.cs:1-6：统一 Photos 错误码和 inner exception，保留。
- src/RawPreview.Worker/Photos/PhotosRuntimeLocator.cs:1-116：保留包枚举、版本选择、架构解码器探测、WinMD 探测和 capability report。多个 Existing 调用是显式诊断字段，压成字典会降低可读性。
- src/RawPreview.Worker/Photos/PhotosContractProbe.cs:1-68：保留 PE metadata 类型和方法扫描及参数计数。SelectTargetFileAsync 只作为诊断字段，实际导出只调用 ResizeAsync；lens 字段仅作为能力探测结果。
- src/RawPreview.Worker/Photos/PhotosWinRtInvoker.cs:1-395：逐段审查 GUID、HSTRING、factory、vtable、async 投影、OutputFileStream、Bootstrap、模块加载和释放。已完成每进程模块加载和每项 Lightbox 释放；没有缓存反射桥或使用未验证的 ArrayPool 读取。

### 测试、脚本、打包和配置

- tests/RawPreview.Tests/Test1.cs:1-301：11 个单测覆盖 Orientation 8、TIFF 双端序、JPEG 尺寸和 EXIF、截断、读尾部、16 MiB 分配、quality 错误、冲突、坏输入隔离和协议绝对路径。夹具均在 TEMP 创建并清理。
- tests/RawPreview.Tests/MSTestSettings.cs:1：最小 MSTest 配置，无可删内容。
- tests/RawPreview.Tests/RawPreview.Tests.csproj:1-31：保留共享协议链接和项目引用，没有额外测试依赖。
- tests/RawPreview.IntegrationTests/PhotosRuntimeTests.cs:1-59：运行时 capability 测试和可由 RAWPREVIEW_TEST_ARW 启用的真实竖拍导出测试；不把私有 DLL、照片或回执带入仓库。
- tests/RawPreview.IntegrationTests/RawPreview.IntegrationTests.csproj:1-21：保留最小真实 Worker 和格式验证引用。
- scripts/install-worker.ps1:1-62：保留发布 Worker、从已安装 Photos 复制本地桥接 DLL、注册 AppX、StageRoot 防盘根校验和幂等卸载。
- scripts/Test-RepositoryBoundary.ps1:1-47：保留媒体、Photos 私有载荷、用户 profile 和 WindowsApps 路径扫描；本轮修复 TrimStart 字符参数并通过扫描。
- packaging/RawPreview.Worker/AppxManifest.xml:1-32：最小 full-trust Worker 清单，只声明自身进程和 ProviderHelper bridge，不打包 Microsoft 私有 DLL。
- Directory.Build.props:1-10：保留 nullable、implicit usings、warnings-as-errors、分析级别和 invariant globalization。
- Directory.Build.targets:1-14：保留构建期拒绝 Photos 私有载荷及 ARW、JPEG、PNG 文件规则。
- .gitignore:1-12：保留构建、媒体、partial 和回执排除。
- README.md：已与当前命令、边界和 Windows 依赖一致，示例使用通用路径。
- docs/superpowers/plans/2026-08-27-windows-photos-cli.md：历史计划，不作为当前实现；其中旧的 SelectTargetFileAsync 导出步骤和早期 .NET 描述不覆盖现状。

## 性能对比

### JPEG 验证分配

JpegValidatorAvoidsFileSizedManagedAllocation 使用 16 MiB 夹具，在同一进程比较历史 File.ReadAllBytes 路径和当前流式路径的 GC.GetAllocatedBytesForCurrentThread。实测历史路径为 16778184 bytes，流式路径为 66064 bytes，少分配 99.61 percent，约为历史路径的 1/254。断言优化版分配小于历史版四分之一。这个指标直接对应文件大小级托管数组的移除；没有对 elapsed 设置固定阈值，避免磁盘缓存和 CI 负载造成脆弱失败。

### Worker 生命周期

| 路径 | 旧行为 | 当前行为 | 预期影响 |
|---|---|---|---|
| Photos 支持模块 | 每张图 LoadLibraryEx，引用累积 | 每 Worker 进程成功加载一次 | 多图减少重复 loader 工作和句柄累积 |
| Lightbox.dll | 导出后句柄遗留 | 每项 finally FreeLibrary | 降低长批次资源增长 |
| JPEG 验证 | 整文件托管数组 | 只读头和必要 APP1 | 降低内存峰值和 GC 压力 |
| RAW 解码 | Photos 私有链路 | 未替换 | 端到端仍主要受解码、demosaic 和 JPEG 编码影响，未声称未经测量的导出加速 |

## 实施和验证步骤

1. 建立协议、路径、JPEG、TIFF、坏输入和真实 Photos 竖拍基线。
2. 合并共享协议和 Worker 会话重复逻辑，立即编译和测试。
3. 修复每进程模块生命周期和每项 Lightbox 释放，保持 ABI 调用顺序。
4. 将 JPEG 校验改为流式，增加读尾部和托管分配测试。
5. 增加发布边界扫描和构建载荷阻断规则。
6. 串行运行 build、test、性能过滤、PowerShell smoke test 和边界扫描。
7. 注册本机 Worker 后再验收横拍和 Orientation 8 竖拍真实导出；未设置 RAWPREVIEW_TEST_ARW 时不把跳过测试当作真实 RAW 结果。

## 风险

- Lightbox、MediaStore、MediaItem、ResizeService 和 PhotosCsProjection 是版本相关私有接口；Photos 更新后先运行 doctor --json。
- SelectTargetFileAsync 只保留为探针结果，实际导出链不依赖文件选择器。
- Photos 探针中的 lens 字段仅用于报告已发现的类型或方法；当前 CLI 协议和导出路径不暴露镜头校正参数。
- 反射 async 投影每张图仍有 Assembly.LoadFrom 和方法查找开销，是后续候选；必须先有真实导出前后回归收据，本轮保留以保护 ABI 稳定性。
- 真实集成验收依赖本机 Photos、Raw Image Extension、Windows App Runtime 和有效 ARW，离线测试不替代它。

## 本轮验证门

已串行执行 dotnet build RawPreview.sln --no-restore -v:minimal：0 warnings、0 errors。并行启动 build 和 test 曾发生 obj 文件争用，随后串行 build 已通过；这属于验证编排问题，不是代码失败。

已执行的边界扫描：Repository boundary verification passed，扫描 35 个非构建文件，未发现照片、私有 Photos 载荷或机器路径。

单元测试和集成测试的已知结果：11 个单元通过；集成 capability 测试通过，真实 Photos 导出测试因未设置 RAWPREVIEW_TEST_ARW 而跳过。性能过滤测试通过：legacy 16,778,184 bytes，optimized 66,064 bytes，减少 99.61%，约为 1/254。
