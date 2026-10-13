# WP02-27 协调修复与最终本地验证

2026-10-13。原候选 `0057261a9b3506484afcc981f513381b9268da81` 的两项 Architecture 门禁现已通过；独立审查发现的 P2 Schema/API 路径不一致已修复，关联 [Issue #60](https://github.com/GTX537/CP6.Platform/issues/60)。**本报告交付本地可集成候选，尚未推送、PR、合入远端 main 或 DONE；Issue 保持 Open。**

## 分支、授权与准确源码

- 重新读取 `D:/CP6/AGENTS.md`、BUG 规则和派工窗口；创建前工作树干净，重新 fetch 核对的 `origin/main` 为 `9259a45d39bae7e98ca804a74778a588c1ad4619`。
- 从该基线建立 `codex/wp02-27-coord-fix-20261013`，fast-forward 合入原候选；原执行分支保留。随后 COORD 告知 main 已集成其他无交集任务，这不改变本分支真实创建基线，也不把未集成内容计入本地包。
- COORD 明确授权原写入范围内的单字符路径修复、必要回归，以及 `RepositoryArchitectureTests.cs` 仅两处资产数组窗口。生产 Schema 只把路径正则末尾 `.+[^/]$` 改为 `.*[^/]$`，保留全部路径防护；未缩小 API 已接受输入。
- 本次源码冻结为 **`7c59256e13c217fc0e8a66b8f3f46905222fdcbc`**，仅3个文件、43新增/6删除。`codex/bug-60-capability-schema-path` 指向此单缺陷修复提交供审计。后继提交只增加本任务证据/报告；最终提交身份由协调候选元数据提供。

## 根因与修复范围复查

旧 Schema 的 `.+[^/]$` 至少消耗两个字符，而公开 `ValidateShape` 接受合法单字符相对归档 entry `x`。新增14例覆盖 `x`、`xy`、常规多段路径，以及空/绝对/尾斜杠/重复分隔符/点段/父段/反斜杠输入。正例通过实际 fixture nupkg 的 `Evaluate` 和 JSON Schema；负例同时检查语义 API 与 Schema 拒绝。

两处 Architecture 修改应用原候选精确提案：只在原 release/v1 项目前增加 capabilities/v1 的 Include/PackagePath；继续保持项目无依赖、Release 独占资产、其他包所有权、Testing/Secret/部署/Exporter 等原断言。提案属于枚举同步，不是跳过产品缺陷；独立发现的 Schema 问题另外修复。定向复查最终3文件差异未发现额外权限、信任或合同变化。

## 实际执行与失败保留

| 运行 | 原始结果与用途 |
| --- | --- |
| [首次 RED](runs/coord-schema-path-red/run.json) | 14失败：`x` 是预期的 Schema 拒绝，其余13例因测试反复注册同一 `$id` 失败。保留原始日志/TRX，不把夹具错误称有效行为 RED。 |
| [隔离注册表后的 RED](runs/coord-schema-path-red-isolated/run.json) | **13通过/1失败/0跳过**；唯一失败为 `x` 的 Schema 结果 false，API/实际归档接受。此时尚未改 Schema 或架构数组。 |
| [GREEN 门禁](runs/coord-schema-architecture-green/run.json) | **Release/Schema 127通过/0失败/0跳过；Architecture 98通过/0失败/0跳过**。包含全部14个新增回归及原必需 Release filter；SDK8.0.424。 |
| [新七包 pack/inspect](runs/coord-schema-local-pack/run.json) | **7/7** 本地 restore/pack、Inspector构建和实际归档检查通过。源码前后hash相同，nuspec同源同版；新Schema原字节与包内字节相同。 |

GREEN 运行时 Git HEAD 仍为原候选，记录了未提交修复的完整 `source_working_bytes/source_after`；这些字节逐项与随后冻结提交工作文件一致。pack 运行 HEAD 为上述修复提交，工作字节也完全相同。没有把提交前执行伪装为提交后重跑，所有运行的前后源码指纹保持一致。

新增 [COORD-artifact-inventory.json](COORD-artifact-inventory.json) 保存 **41份精确证据原字节**和 **7个仅本地归档**的大小/hash。旧174份证据、29个归档及旧失败清单不覆盖、不删改。原候选审查固定在协调 scratch 的 `WP02-27-review.md/json`；本报告只追加修复复查，不回写原审查结论。

## 新本地包身份与状态

版本：`0.10.3-wp02.27.g7c59256e13c217fc0e8a66b8f3f46905222fdcbc.20261013134250`。七包完整ID/version/source/hash/feed见 [matrix](runs/coord-schema-local-pack/inspection/matrix.json)，消费身份见 [lock](runs/coord-schema-local-pack/inspection/consumer-lock.json)，原包路径见 [archive-paths](runs/coord-schema-local-pack/inspection/archive-paths.json)。

- capability-matrix Schema SHA256：`54aeabc581130e2119597a44a9b72953ad5fc0db1b3ef282904e108acd9975a1`。
- consumer-lock Schema SHA256：`e8868ed248000d2ec253d91819af61a175faa65a172011f45f39fbf0fe3c70fa`。
- 两个 Schema 均经真实 Release nupkg entry 字节比对；实际七个归档又逐包复核 hash/nuspec。新包仍为 **NOT_PUBLISHED / NOT_FORMALLY_TRUSTED**；兼容行为空，六层采用状态继续 Unknown。

## 复用与未验收边界

复用原 [formal-current-2](runs/formal-current-2/run.json) 的真实七包 Current 结论，评估时点仍是 **2026-10-13T12:46:51.582Z**；没有重新发起在线验签或把历史结果写作新的当前运行。原0.10.2归档、公有证书、固定policy、原publication、正式validator/CLI源码及其相关依赖均未改变。本次只改新能力Schema和测试；原信任负例适用范围不变。原Release113、能力58的历史结果保留；新的127门禁用于本次改动，不把旧结果改写为127。

没有修改或触发 Actions、正式发包、访问私钥/更改全局信任、操作数据库或部署；本轮没有 push/PR，因此未产生本任务远程运行。现有8个workflow未改变，RFC3161唯一PR过滤的两个文件未触及；最终push/PR前仍由COORD按当时受保护main复核全部事件/过滤与依赖。

WP02-02仍BLOCKED；旧0.10.2不含新ctx-binding，新本地包不能替代独立授权的正式不可变受信任消费身份。native profile schema未打包、完整API签名/运行、Main/CRM真实消费、host/driver/schema/共享事务/fence、原业务验收和远端main包含仍不在本报告通过范围。共享台账和四状态页由COORD集成时统一处理；Issue60须在远端main包含核对后关闭。
