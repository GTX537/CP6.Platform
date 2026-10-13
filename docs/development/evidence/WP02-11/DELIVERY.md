# WP02-11 — HTTP 安全错误、重试和截止时间

协调集成修正：原候选 `0f7c69f13a26b36895cd40aca3fc17299e866b70` 的两项 P2 已按独立 Issue 分支修复并保留真实 RED/GREEN；组合后的当前 HttpV2 门禁为 **198/198**。最终源码、基线、哈希与历史验证复用范围见 [COORD-FIXES.md](COORD-FIXES.md)。下文 150/98/98 及原审查结论保留其原候选历史范围，不冒称修正后的新执行；远端集成仍由 COORD 完成。

状态：候选实现与定向修复已通过本地验证、独立规格复查和完整代码质量审查，可进入候选交付。固定提交及传输结果以 Handoff DELIVERY 为准；本文件不登记路线图 DONE，也不授予真实宿主或生产写重试权限。

## 变更与兼容范围

新增显式启用的 `CP6.Platform.AspNetCore.HttpV2`：安全错误策略、不可变调用绑定与实际请求快照、单调截止时间、严格 Retry-After 和预算决策、有限重试及原结果查询协议。默认 Disabled 对读写均零重试；NonIdempotent 永远零重试。写重试需宿主登记的可信 authority 验证实际 Owner 证据，外部头、键格式和声明 DTO 不能代替证明。

安全错误只输出当前端点登记的字段和安全消息，并按实际序列化字节限制 16KiB；开始响应或取消后不追加第二正文。发送后不确定结果保持 Unknown；查询原结果先于新写权限判断。一次调用保持 tenant/action/key/version/digest/body/目的/凭据/策略/route epoch，预算覆盖响应正文和查询。HttpV2 禁止自动重定向、Cookie 和多层策略叠加。

原 HTTP profile、默认值、旧 writer、网关、认证、CP6 定义及既有测试未改行为。唯一既有源码编辑是 `Cp6ProblemDetailsExtensions.cs` 增加兼容重载；HTTP 重载通过新目录中的原 partial class 增加。没有改共享合同、项目/依赖配置、数据库、工作流、CRM、安全代理配置或宿主接线。API 与宿主义务见 [HTTP v2 说明](../../../WP02-11-HTTP-V2.md)。

## 来源与可复现证据

- 创建基线和直接前置：Platform `9259a45d39bae7e98ca804a74778a588c1ad4619`；WP02-01 `d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6` 已在该主线祖先中。
- 本轮派工 Main `47c9b12c796620effe41c62d16992ccd629ee949`；原指定 Main 输入 `a1824fec3d4824d45e27693f2e715b311ee5f665`；CRM 输入 `1e4eea61c31d7547c3b3495d392eefbed99b7339`。未以同轮未合并候选作已交付能力。
- [sources.json](sources.json) 固定五份原字节来源；HTTP v0.2 SHA-256 为 `9d1416cbd7d0ea6e58a0da79de80dcb2a4e675890345dfb4558194c61048eb3f`，S6 为 `d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc`。原件的历史授权与 NOT_RUN 状态不修改；当前执行授权来自本轮任务卡。副本保留原上下文相对链接，不重写为本仓路径。
- 工具使用既有 `.NET SDK 8.0.424`、运行时 `8.0.30`，命令和全部真实失败记录见 [RUNS.md](implementation/RUNS.md)。默认系统 SDK10 未用于这些门禁。
- 初版三个门禁的 235 项源码/测试/构建输入及 8 项依赖、25 项产物曾独立逐项复核：[independent-evidence-check.json](independent-evidence-check.json)。审查修复后的当前输入为 236 项源码、8 项依赖和 25 项产物，最后一次 GREEN 前后源码/依赖一致：[current-evidence-check.json](current-evidence-check.json)。旧产物哈希只对应原执行，不冒称与修复后重编译产物相同。哈希标明工作区字节，不把 Git 换行规范化后的 blob 冒充原字节。
- 任务边界、五份来源、17 项只读输入和原始 42 AC 身份检查见 [final-scope.json](final-scope.json)。这些检查不替代行为测试或业务验收。

## 本地验证

| 门禁 | 实际通过 / 发现 | 失败 / 跳过 |
|---|---:|---:|
| 当前全部 HttpV2（parent-cancellation-http-v2.trx） | 150 / 150 | 0 / 0 |
| 原 HttpResilience / GatewayContract / AuthenticationContract（初版执行，修复后复用） | 98 / 98 | 0 / 0 |
| ArchitectureTests（初版执行，修复后复用） | 98 / 98 | 0 / 0 |

审查修复只涉及新 HttpV2 deadline/handler、对应新测试与任务说明；旧实现/测试、项目、依赖、公开 API 和架构边界未改变。两项原门禁按原 `after-final-fingerprints.json` 和当时独立哈希核对结果复用，没有称为当前版本重新执行。当前 HttpV2 全集覆盖修复影响；初版 128、第一轮修复 146 及全部失败记录继续保留。

真实本地 Kestrel HTTP 验证尝试数、取消、等待上限、正文预算和不跟随重定向；HTTPS 使用未落盘、未安装的临时证书，并只在 fixture 内固定信任，验证 POST → GET 原查询 → 相同 POST 的线上字节和键。确定性时钟验证固定 S/L/R/A 边界、提前/迟醒、父预算和 UTC 回拨。全部结果只证明本地库及 fixture 协议。

原 42 项逐条映射见 [AC-SCOPE.md](AC-SCOPE.md) 和 [ac-scope.json](ac-scope.json)。38 项含实际本地库子断言证据，标记 PARTIAL_PASS；4 项宿主专属项没有本地运行子断言，保持 NOT_RUN。全部原始 AC 与业务验收仍为 NOT_RUN；不得把 38 项局部通过汇总为 42 项业务通过。

保留解析器 14 个 RED、后续边界失败、编译失败及传输重定向失败的真实记录；仅初始解析器有完整实现前 RED，未声称所有行为逐项 TDD。旧成功记录未改称新执行。文档/证据收尾不重跑未受影响的业务测试。

## 采用与未验证范围

| 层级 | 本次真实状态 |
|---|---|
| CodePresent | 本任务候选工作区存在；远端集成待 COORD |
| PackagePinned | 未发布包、未更新 Main/CRM 包引用 |
| HostRegistered | 仅测试 fixture；真实宿主 NOT_RUN |
| ConfigEnabled | 真实配置未启用；写绑定 Disabled / UNPROVEN |
| EndpointBound | 仅测试端点绑定；真实端点 NOT_RUN |
| RuntimeObserved | 本地库与 HTTP/HTTPS fixture；真实环境 NOT_RUN |
| BusinessApplied | NOT_RUN；无真实 Owner 事务/receipt/Outbox 证据 |
| IndependentlyAccepted | 本地库候选规格/质量审查 PASS；业务接受 NOT_RUN |

真实 Owner 必须证明远端原子去重、同键不同输入冲突、原结果权限/字段 mask、保留期限与最终授权门。自定义 primary handler、SDK、代理和 sidecar 的额外重试/凭据/路由行为必须由宿主提供实际清单；本库不证明不透明组件。不可验证原字节的 stream/超限体不获重放或原查询权限，失败保持 Unknown 交宿主恢复。跨调用、离线及持久化恢复属于 Host/S6；重新 SendAsync 不能冒充原 Unknown 的已批准继续。

## 审查与交付

独立规格审查：两项 P1 及其父取消边界修复后 **PASS**。独立代码质量审查：**PASS**，无剩余实质发现。完整发现、定向复查与证据范围见 [REVIEW.md](REVIEW.md)。

START 已发布至 Handoff `codex/dev-handoff-wp02-11` 固定提交 `4ba621d268280911fcc4d375d415f34156f74d54`。候选将由本对话仅推任务分支并追加固定 SHA DELIVERY；COORD 负责台账、生成页、四份状态文件、串行正常合并和远端 main 包含核对。执行对话不改这些共享状态。

未触发、重跑或取消 Actions；未修改触发条件。发布前须再次检查候选与保护分支事件、过滤及依赖，保证既存 RFC3161 窄路径 PR 门不被命中。没有正式包、tag、迁移、环境切换或部署。最终传输审计与提交 SHA 由 Handoff 的固定提交附件承载，避免本报告自引用。
