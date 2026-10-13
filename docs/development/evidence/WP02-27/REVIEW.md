# WP02-27 任务级审查与失败保留

2026-10-13。审查基线 Platform `9259a45d39bae7e98ca804a74778a588c1ad4619`；对象为本任务新合同、生产实现、测试、Inspector/runner 和打包登记的完整改动。按项目节奏只做一次独立任务级规格/质量审查，修复后只定向复查。审查者只读，测试由执行对话实际运行。

## 结论与修复

- 初审 Critical 0 / Important 1 / Minor 0：公开Evaluate入口未将publication原字节绑定独立批准的matrix；自洽的workflow/run/createdAt可被改变而保留原包。
- 修复：publication SHA256必须属于approved matrix的evidence集合，原字节与已验证snapshot完全一致，再进入签名边界。旧formal validator/CLI/信任根未变。
- 四个新回归修改workflow run/buildInvocation、workflow file SHA、createdAt或删除批准证据；前三者先证明仍符合原formal validator，四者均报publication-evidence且verifier调用0次。
- 定向复查确认Important已关闭、无新增实质问题。真实包检查、Architecture和集成仍各有独立门禁。

## 实际运行与失败

运行目录均位于artifacts/wp02-27；保留原文件，不覆写失败。

| 目录 | 原始结论与范围 |
|---|---|
| core-red | 初始编译错误，不作为行为RED |
| core-red-fixed | 34个预期实现缺失失败 |
| core-red-boundaries / core-red-trust / core-red-source-version | 边界、未来publication、source-bound prerelease先失败 |
| core-green-initial / core-green / core-green-source-version | 初期34、Debug相关180、能力54通过；不替代后续Release门禁 |
| inspector-build / inspector-build-fixed / inspector-build-final | 初次编译失败及后继修复，最终Release构建0错误/警告 |
| release-architecture-1 | Release109通过；Architecture96通过/2失败；均0跳过。两个架构资产预期仍只允许旧release/v1 |
| red-publication-evidence | 4个预期行为失败，但Python3.8不支持runner写入选项导致finish失败，仅log/TRX，无同期source snapshot |
| red-publication-evidence-py314 | 修复前相同代码，Python3.14；58中54通过/4预期失败，同期前后source hash未变 |
| green-publication-evidence | 修复后58/58通过，Python3.13；同期前后source hash未变 |
| release-publication-fix-2 | 修复后Release必需filter113/113通过、0跳过；同期source snapshot未变 |
| formal-current-1 / formal-current-2 | 原七包均真实Current成功；后者在publication绑定修复后重建Inspector并记录source/binary/hash，作为最终当前信任证据 |
| trust-negatives-1 | 历史Current、撤销、未知根、缺timestamp按预期拒绝；HistoricalAudit成功仅历史；坏timestamp拒绝；篡改ZIP实际exit1而runner预期2，整体FAIL保留 |
| trust-tampered-2 | 分析归档被NuGet解析层拒绝后，仅修正该例预期为exit1并重跑；PASS指拒绝损坏归档，不声称进入特定签名算法错误 |

早期core-* RED/GREEN没有同期源码快照，保留原日志/TRX及共同起始baseline，不将最终文件hash倒填成当时源码。最终有效门禁以source_working_bytes/source_after记录为准；旧结果仅在相同相关输入范围复用。

签名回归/负例使用测试数据或公开DER，不是正式发布。新Inspector仅导出公开证书，不输出私钥，不更改原政策或系统trust store。原七个NuGet缓存归档已复核未改变。

## 待协调的精确架构窗口

两个失败位于原只读RepositoryArchitectureTests.cs资产数组。建议补丁[architecture-window-proposal.patch](architecture-window-proposal.patch)保留全部旧检查，只增加新schema路径；git apply --check确认可应用，不代表获准或测试通过。

Handoff请求已发布于`ccd26b3c0162897cc1fe561f52573209a95f0826`，路径`handoffs/CP6-DEV-20261012/tasks/WP02-27/messages/20261013T122600Z-architecture-window/MESSAGE.md`。COORD授权前不修改该文件、不宣称架构门禁或任务完成。后继实际应用/回归应追加到交付报告，本节保留首次失败来源。
