# WP02-02 第八轮交付：登记实现已审查，交付门禁仍阻塞

**BOUNDED_IMPLEMENTATION_REVIEWED / DELIVERY_BLOCKED / NOT_MERGE_READY**。
本次交付是 Platform `0.10.3` 正式登记源码候选，不是正式发布或 Main 采用完成。
Main WP02-02 仍为 `BLOCKED / NOT_CHECKED`。最终 producer SHA 为 null，
只可在 COORD 合入并核对远端 main 后冻结。

分配源：Main `b3eb755bc43e370f697480a6ea41474c3d978488`，Handoff
`6c44c1f8ffba124b381366adefe41bf86ea924e3`。Platform 起点与本轮最后 fetch 核对的
origin/main 均为 `66480fe24d51c706708be971e7e9d75a684a3bc4`。
任务分支 `codex/wp02-02-formal-registration-20261015`；最终候选提交由 Handoff
DELIVERY 固定完整 SHA，不在本文件预填自身未来提交。

## 实际改动

- publication API 与 source/实际 packed schema 扩读准确 `.0/.1/.2/.3`；
  新版 formal capability 仅允许 `ctx-binding-v1/1.0.0`、`ctx-v1`、三个 scope。
  旧三版仍能力为空，未测 consumer 保持 Unknown，TaskLocal 不能转成正式信任。
- 四个正式 writer 仅 `.3`；package-set 检查和 publication-record/recovery 入口保留四版读取。
  workflow 仅改版本说明及 Windows/Linux 两处准确版本门，其余发布与信任门保持。
- 新增 API/schema/能力/脚本/实际 unsigned 包的正反覆盖，保留旧版本回归。
  核心生产改动、测试和脏候选打包输入见 [实现记录](implementation/IMPLEMENTATION.md)。
- 固定复制第七轮诊断 runner，以新 producer 做三入口构建和私有 synthetic authority 验证。
  为隔断承载仓库的中央包配置向上导入，新增 owner 内惰性包配置边界及真实 MSBuild 回归；
  原 Main/Platform 项目、NuGet 信任与原锁不修改。

## 实际验证

| 检查 | 结果与准确范围 |
|---|---|
| 无警告覆盖参数的 Release 定向命令 | restore FAIL：既有 Pkcs 8.0.1 NU1902/NU1903；测试未开始 |
| 新行为 RED | 实际 `.3` API/schema/脚本拒绝已观察；另含测试 registry 冲突，原日志不删，不把全部失败归因产品 |
| 受影响定向诊断 | 171/171 PASS；NU 警告保持可见，DIAGNOSTIC_ONLY |
| 完整安全 Release 诊断 | 353 PASS、2 FAIL、0 skip，共执行 355；三项签名测试被明确过滤为 NOT_RUN |
| historical record-reader 修正 | 6/6 定向参数回归 PASS；复用其余不受影响结果，未重跑整个 suite |
| Architecture 标准命令 | 98/98 PASS，0 skip，无警告豁免 |
| 私有 runner | 初始 29 项 PASS；隔离修正 RED 后最终 30 项 PASS |
| 新 producer 三入口 | WebApi、DatabaseCompatibility.Probe（仅编译）、私有 Probe 均 restore/build exit 0 |
| 新 producer 私有运行 | 31 项断言；HTTP 200/401/403/503；21 Log、7 Trace、7 Metric；11 实际程序集；policy=MATCHED_SPECIFIED_PATHS |
| 依赖闭包 | 15 assets、15 deps、17 私有锁、189 包、0 graph errors；整体 compatibility 保持 UNKNOWN |
| 清理 | owned host stop/dispose 成功；原端口 ConnectionRefused，观测 2046.3126 ms；无 Main/DB/broker 启动 |
| 范围/只读/脚本 | 29 只读输入未改；六个 PowerShell 文件仅 parser 检查；最终 diff/scope 检查见审计记录 |

实际唯一 unsigned TaskLocal Release 包 SHA-256 为
`49ba29abe9eb050e54896229cec539144b188d4392d442fe34c6d30c3e97c24f`。
版本、SDK8.0.424、归档位置、原始 nuspec、逐输入哈希和 source/packed schema 字节记录在
`implementation/tasklocal-pack.json`。它以起点 SHA 加候选 diff/hash 标识脏源码，
不是起点 Git 提交本身的正式制品；不上传、不签名、不发布。

兼容诊断实际执行 Platform `66480fe...` + 固定 Main `1129953156b7775152a74ad2f51cb95ef430ee56`。
本候选对运行五项目及其 134 个源码/依赖/资产输入无改动，核对见 `runtime-input-reuse.json`。
这是按输入复用该次真实执行，不把它改签为新候选或最新 Main 的验收。
完整边界见 [逐输入矩阵](COMPATIBILITY-MATRIX.md)；两次 owner、失败和 47 份原始图输入均已归档。

## 阻塞与协调请求

1. 只读依赖 `System.Security.Cryptography.Pkcs 8.0.1` 导致标准恢复门禁失败。
   已发布 [依赖窗口 QUESTION](https://github.com/GTX537/CP6.AI-Handoff/blob/a602023b87148543ae512ef957bd284cc1c4a8c8/handoffs/CP6-DEV-20261012/tasks/WP02-02/messages/20261015T054310Z-wave8-release-gate-dependency-question/MESSAGE.md)。
2. 未获写窗口的 `P10PackageTests.cs` 仍将 `.3` 断言为未知版本，并遗漏既有 capabilities 资产路径。
   安全诊断实测这两项失败；已发布 [精确测试窗口 QUESTION](https://github.com/GTX537/CP6.AI-Handoff/blob/14f86eb13a5061b822e625e52b2705f9d4d19c19/handoffs/CP6-DEV-20261012/tasks/WP02-02/messages/20261015T054746Z-wave8-exact-test-scope-question/MESSAGE.md)。

上述门禁未解决前不合并。诊断命令仅将 NU1902/3 保持为可见警告，不关闭审计或宣称标准门禁通过。
三项会进行签名的既有测试保持未运行，原测试文件不改；范围详见
[失败与验证边界](VALIDATION-BOUNDARIES.md)。

## 审查、发布边界和后继动作

[SPEC](SPEC-REVIEW.md) 与 [QUALITY](QUALITY-REVIEW.md) 完成一次任务级审查。
历史 publication-record reader 退化已修复并回归，无其他有界实现缺陷；审查没有豁免上述门禁。
总台账、四份项目状态、协调合并窗口均未由本执行对话修改。

`feed-version-observation.json` 记录当前主体 GTX537 的七槽位完整分页观测，均未列 `.3`；
它不是预留。`scope-audit.json` 记录候选与 protected-main 全部 workflow 事件和哈希；
RFC3161 两条自动 PR 窄路径未触及，实际变更数低于 300。无本任务排队/执行运行需取消，
未改触发器、保护、信任或远程额度配置，未运行 Actions。

七包计划与 null 身份见 `formal-package-plan.json`；后续正式运行的具体授权清单见
[FORMAL-EXECUTION.md](FORMAL-EXECUTION.md)：最终 main SHA、准确 `.3`、一次受保护 workflow、
签名及七包写入、预计 20–50 runner 分钟/65 分钟硬上限，实际费用届时核查。
本轮不请求或预设这项授权，不含额外重试、恢复、独立 RFC3161 预检或部署。

**未验证：正式签名/Current 在线信任/正式 feed readback、真实 Main 宿主采用、业务 Owner、
数据库与原 17 PGT。无正式发布、无签名、无迁库或部署。**
