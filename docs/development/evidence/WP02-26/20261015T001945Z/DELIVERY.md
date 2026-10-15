# WP02-26 限定库实现交付

本报告仅覆盖第七轮 D 对话的 WP02-26。限定库实现与本地验证已闭环，候选交付 COORD 串行审查合并。roadmap、生成页及四份项目状态文件由 COORD 同步；本报告不宣告远端 main 集成或业务 DONE。最终候选完整 SHA / PR 由 Handoff DELIVERY 固定，避免报告自引用提交。

## 输入与范围

- Platform 创建基线：`b28db763f70ab6f164cb7c87d6ce49a6d97267f1`。
- Main 产品输入：`1129953156b7775152a74ad2f51cb95ef430ee56`；第七轮派工：`99fc0ce383d2d02c1404a8adbb3abe971b41d332`。
- Handoff 分配：`ebeae808b2aa35c8c9115519fed564edebbbde0c`；本任务 START：`1e8f0b78d908358533e1eaccb7831624bd221dcb`。
- S01-03 `b24f76445ffdc9b4df0de77eb394a6aa47726a0c`、S01-04 `bfa00a4c055bfcf4f6b7b7fe696f7a819af720c8`、修订后的 WP02-25 `80f43346e4e94e6d7b402d7b557c685b475ba743` 已核对属于各仓主线祖先。旧 WP02-25 原候选的测试不冒充 Issues 68/69 修订后的执行证据。
- 固定输入字节见 [input-pins.json](input-pins.json) 与 [inputs](inputs/)。实现使用的 Main 数值向量保持原始哈希；无生产 Main 项目引用或新的金融合同权威。

新增 opt-in SLO 资格计算与不可发包测试证据库；保留旧 SLO 1.0.0 比较器、schema、recorder/fault API。实际字节冻结、独立调用者绑定的期望总体、精确比率/带权分位数/采样 gauge、不可覆盖的修订血缘，与受限捕获、完整 DAG、原始数值表示、确定性故障脚本分别验证。具体 API 和边界见 [库说明](../../../../WP02-26-SLO-TEST-EVIDENCE-V1.md)。

## 本地 PostgreSQL 的实际范围

使用新建且独占的 PostgreSQL 18.6 原生进程，端口 `127.0.0.1:55429`，owner `4ba89ba3d399683bd75209e61c31f1ab`，system identifier `7696685906165408468`，数据库 `cp6_wp0226_20261015_4ba89ba3` / OID `16385`。受限 runtime 与 setup/admin 分离，凭据只存在私有 ACL 目录；未连接或更改其他任务的数据库/进程。

六项测试使用真实事务与原始 operation/message keys，观察唯一键锁竞争、原生 `40001`、实际回滚、`42501` 权限拒绝、受控本实例 immediate shutdown 后的恢复。已提交精确 NUMERIC/bytea 保留，未提交记录不存在。成功 COMMIT 后故意抛出的 ACK 丢失为 **应用注入**，不称为原生 SQLSTATE 或真实不确定 wire COMMIT。fixture Owner 不代表 Main/CRM 业务 Owner。

首次 provision 的超时、OID JSON 类型不匹配，以及恢复脚本 SQL 提取错误均已保留，见 [provision-history.json](pg-resource/provision-history.json)。原超时无法恢复的进程退出码明确为未知；未删除、重建或重置数据来获得通过。此后修复代码、核对同一资源身份并继续，最终门禁另行固定输入。

## 验证、审查和限制

| 正式门禁 | 实际结果 | 原始证据 |
|---|---|---|
| 任务单元、合同及两组旧接口 source-linked 回归 | 108/108，零跳过，退出码 0 | [unit](final-01/unit/execution.json) / [TRX](final-01/unit/wp02-26-unit.trx) |
| 当前 helper 下的原生 PG 并发、权限、回滚和崩溃恢复 | 6/6，零跳过，退出码 0 | [PG](final-01/pg/execution.json) / [TRX](final-01/pg/wp02-26-pg.trx) |
| 架构 | 98/98，零跳过，退出码 0 | [architecture](final-01/architecture/execution.json) / [TRX](final-01/architecture/wp02-26-architecture.trx) |

[verified-gates-final.json](verified-gates-final.json) 核对三项真实退出码、TRX、当前相关源码/配置、schema/向量、解析依赖及本地 CP6 程序集哈希。每次执行保留 before/after 源码清单、SDK 信息、命令和原始输出。测试在提交前的工作字节上执行，不把基线 HEAD 当作仅含旧代码的测试范围，也不把后续提交或 SHA 改变称为新执行。原生测试实际调用 `pg_resource.py`；独立证据生成脚本的修订与执行另记，不伪称它们是 PG 程序集的运行输入。

[规格审查](reviews/spec.md) 为一次任务级静态审查；[质量审查](reviews/quality.md) 保留发现和定向修复复核。三类问题为数值展开后的表示预算、清单枚举异常、清理/observer 重入的过早 Complete；各自真实 RED 保留，相关 Capture/Fault 统一回归为 39/39，之后执行上表正式门禁。矩阵生成器的原始/局部 evidence 字段重名错误也保留于 [matrix-red-01](matrix-red-01/failure.json)，修复后 [matrix-green-01](matrix-green-01/execution.json) 实际退出码 0，并逐字段核对原始记录未被覆盖。

[原验收矩阵](acceptance-final.json) 保留全部 56 条原始字段和业务 NOT_RUN，26 条仅附限定本地证据；[执行包修订矩阵](execution-matrix-final.json) 对应全部 15 项交付条目及 3 类质量修正，并保留 114 条实际单元/PG 场景。架构证据独立列于上表。

本实例于 **2026-10-15T01:18:29.612449Z** smart-stop。停止时无其他会话，之后验证 control state 为 shut down、PID 文件不存在且保留端口空闲；[资源收尾清单](final-01/pg-resource/collection.json) 固定 13 份安全复制件的哈希，包含实际服务器恢复日志、两轮受控 crash/start/stop 和配置封存。凭据、完整私有阶段文件和可能含提供者详情的其余命令日志留在私有目录；未复制凭据、删除数据或终止未知会话。

开发期真实失败保存在 `implementation/`、`pg-resource/` 和 `matrix-red-01/`。首次 PG 6/6 仅保留其原执行范围；资源拒绝路径的 6 项离线检查沿用 [resource-guards-02](resource-guards-02/execution.json) 的原始成功记录，不计作新的正式 PG 执行。先前 WP02-25 未受影响资产沿用其历史范围，本轮正式结果只包含上表实际运行。

原始 **32 OBS + 24 TEST = 56** 条业务验收保持 `NOT_RUN`。本地通过仅证明映射内的限定部分，不证明真实 producer、broker、业务 Owner、Main/CRM 采用、生产阈值批准、远端保留或独立验收。所有 task fixture 均 `productionSloClaimed=false`。

本任务未调用、重跑或取消 GitHub Actions，未修改工作流、保护、正式包版本或生产配置，未发 tag、发布包、迁移业务数据或部署。每次发布候选前重新核对候选/受保护 main 工作流、依赖、RFC3161 窄路径及 300 文件边界。资源收尾采用本实例 smart-stop 并保留数据与失败日志。
