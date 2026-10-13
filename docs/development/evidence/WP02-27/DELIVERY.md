# WP02-27 候选交付报告

> 原候选历史报告保留如下。2026-10-13 协调修复已实际通过 Release/Schema 127/127、Architecture 98/98，并刷新七包本地证据；当前后继候选入口见 [COORD-FIX.md](COORD-FIX.md)。尚未合入远端main或DONE，原失败记录不删除。

2026-10-13，B。**本地实现与包验证已交付候选；Architecture两项必需检查失败，等待COORD精确编辑窗口，尚不可合并/DONE。** 未更新Main台账/四状态页，未发布正式包、操作数据库或部署。

## 提交与实现

Platform基线 `9259a45d39bae7e98ca804a74778a588c1ad4619`。源码冻结提交 `77fbf0311ffa4d4b1220861fd7624c7a1d20c405`，分支 `codex/wp02-27-package-capabilities-20261013`；后继提交只补本任务证据/报告。最终候选完整SHA由固定Handoff消息提供，不能用源码冻结SHA假称远端main已包含。

- 独立CapabilitiesV1能力矩阵、消费lock、闭合schema和validator；Release项目仅增加新资产登记。
- 精确七包/version/source/feed/hash与真实归档、包内schema、证据核对；准确S01-09 profiles和ctx-binding-v1/ctx-v1；独立批准的逐字节兼容行，其他组合Unknown。
- 当前信任组合既有policy/正式validator和真实CLI；publication原字节另绑定批准证据。source/shape/artifact/trust与六个后续采用层分开。
- 私有本地runner/Inspector；没有更改旧formal合同、CLI、媒体类型、P10规则、版本、依赖、锁、工作流或信任根。

入口：[主说明](../../../WP02-27-PACKAGE-CAPABILITIES.md)、[原32AC](ACCEPTANCE.md)、[审查/失败](REVIEW.md)、[来源](inputs.json)。

## 实测结果

| 门禁/证据 | 结果与范围 |
|---|---|
| 必需Release过滤门禁（修复后） | **113通过/0失败/0跳过**，Release、SDK8.0.424；[run](runs/release-publication-fix-2/run.json)、[TRX](runs/release-publication-fix-2/release.trx) |
| CapabilitiesV1定向回归 | **58/58**；[run](runs/green-publication-evidence/run.json)。publication绑定4例[先失败](runs/red-publication-evidence-py314/run.json) |
| Architecture | **96通过/2失败/0跳过**；[run](runs/release-architecture-1/run.json)、[日志](runs/release-architecture-1/architecture.log)。不能合并 |
| 当前原七包 | **7/7 Current成功**；[run](runs/formal-current-2/run.json)、[状态](runs/formal-current-2/inspection/state.json)、[matrix](runs/formal-current-2/inspection/matrix.json)、[lock](runs/formal-current-2/inspection/consumer-lock.json) |
| 信任负例 | [首次](runs/trust-negatives-1/run.json)：历史Current、撤销、未知根、缺timestamp拒绝；HistoricalAudit成功仅历史，坏timestamp拒绝。篡改归档exit1而预期2，首次整体FAIL保留；[仅该例修正重跑](runs/trust-tampered-2/run.json)通过 |
| 唯一本地pack/inspect | **七包构建与检查通过**；[run](runs/local-pack-1/run.json)、[矩阵](runs/local-pack-1/inspection/matrix.json)、[状态](runs/local-pack-1/inspection/state.json)、[lock](runs/local-pack-1/inspection/consumer-lock.json) |
| 集中审查 | 初始1个Important已修复并定向复查关闭；[记录](REVIEW.md)。不替代失败门禁 |

Current评估时间 **2026-10-13T12:46:51.582Z**，7次实际调用/起止时间逐包在`runs/formal-current-2/inspection/current-verification/*.call.json`。policy SHA256 `da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f`；原publication SHA256 `17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181`；原0.10.2 source `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`。未修改验证器，仍强制RFC3161和在线撤销；未把缓存hash当验签。此时点结果不保证未来受信任，未重查远端feed/workflow API真实性。

本地版本：`0.10.3-wp02.27.g77fbf0311ffa4d4b1220861fd7624c7a1d20c405.20261013124916`。七包nuspec同版同源，实际归档hash各自固定。Release包内两个新schema与源码相同；真实DLL metadata确认native/context公开type/method名称，不证明全部signature/运行行为。native schema尚未打包，实际兼容矩阵为空，宿主/业务采用Unknown；候选始终 **NOT_PUBLISHED / NOT_FORMALLY_TRUSTED**。

源码冻结前，新schema按仓库既有gitattributes从CRLF统一为LF，JSON内容未变。113项Release复用换行前相同代码/JSON语义的结果；最终pack使用规范LF源码/包内字节并保留准确hash，不把两个原始字节hash说成相同。后继证据提交没有修改core/工具源码，不因文档提交全量重测。

[artifact-inventory.json](artifact-inventory.json)记录原产物大小/hash/保留状态。174个证据文件按字节复制，29个本地归档仅记录hash/路径，不提交nupkg；DER均公开，`.bin`证据仅JSON。早期RED同期source快照缺失及初次编译/runner失败均在REVIEW明列，没有倒填最终源码。

## 唯一未闭合门禁

Architecture旧断言只允许release/v1，需准确增补capabilities/v1。文件不在原窗口，已通过Handoff `ccd26b3c0162897cc1fe561f52573209a95f0826`申请。[补丁](architecture-window-proposal.patch)已通过git apply --check，尚未应用；获准后只改两个数组并重跑Architecture，保留旧约束。不跳过失败，不私自扩大共享文件范围。

COORD需答复窗口并核互斥资源，接收后继通过证据后集中复核、正常PR集成、同步唯一台账/生成页/四状态页，最后核远端main包含。候选提交或消息运输成功均不是DONE。

## Actions与未验收范围

[workflow-preflight.json](workflow-preflight.json)核候选与origin/main八个工作流，均无push触发；唯一RFC3161 PR窄路径均未触及，无pull_request_target/workflow_run/schedule。action引用及依赖未变。未触发/重跑Actions、未改触发器；任务排队/执行中0、取消0。实际推送前再核最终diff和远端main。

WP02-02仍BLOCKED：旧0.10.2不具新ctx-binding，本地候选不替代正式不可变受信任消费身份。新正式发包、Main/CRM真实消费及host/driver/schema/共同事务和fence等须独立授权/验收。原32AC外部责任、125业务目标、247运行NOT_RUN、41历史REFERENCE_ONLY、ITER/REL缺件和WP05 PROPOSED保持原状态；无生产、数据库或宿主采用结论。
