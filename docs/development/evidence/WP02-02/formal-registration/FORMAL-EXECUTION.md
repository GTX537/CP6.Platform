# 后续正式运行清单（本轮不执行）

当前状态：`NOT_PUBLISHED / NOT_FORMALLY_TRUSTED`。最终 producer SHA、正式七包 hash、
publication hash、正式 matrix 和 consumer lock 均尚不存在，不填占位成功值。

| 准确包 ID | 待正式运行版本 | 当前正式 hash |
|---|---|---|
| CP6.Platform.Abstractions | 0.10.3 | null |
| CP6.Platform.AspNetCore | 0.10.3 | null |
| CP6.Platform.Contracts | 0.10.3 | null |
| CP6.Platform.Deployment | 0.10.3 | null |
| CP6.Platform.EntityFramework | 0.10.3 | null |
| CP6.Platform.Messaging | 0.10.3 | null |
| CP6.Platform.Release | 0.10.3 | null |

1. COORD 完成一次完整任务审查、串行合入，并核对远端 Platform main。
   此后冻结完整 producer SHA/tree；本候选不是最终发布身份。
2. 使用届时相同且明确的授权主体重查七个版本槽位全部分页、保护环境、
   Current signer 与跨仓 policy/cert pin。网络/403/404 为 UNKNOWN；已占槽位不得重发。
3. 以最终 SHA 向用户提出一次具体授权：仓库 `GTX537/CP6.Platform`，版本 `0.10.3`，
   `.github/workflows/p10-formal-packages.yml`，受保护 `p10-formal-release` 环境，
   一次 Windows build/sign/RFC3161/publish/readback 和同一归档的 Linux 独立验证。
4. 规划预计合计 20–50 runner 分钟；现有作业硬上限 Windows 45 + Linux 20 = 65 分钟，
   不计人工审批等待。实际账户额度、runner 费率与账单在提出授权时重新查询。
   本轮未运行 Actions，未消耗这项远程运行预算。
5. 授权范围须明确签名与七包 feed 写入。额外重试、恢复、独立 RFC3161 preflight、
   signer revalidation、Main 接线、迁库和部署均不包含在该次授权内。
6. 实际执行保留 exact-main/event/checkout、固定 actions、最小权限、one-build、
   immutable consumed marker 和未知发布结果只读查回。保全失败；不自动重建或覆盖。
7. 读取实际 immutable artifact/digest、七包原字节与 BytePreserving readback，
   核 Current 签名、撤销和时间戳。运行记录中的 success 字段不能替代密码学验证。
8. 后续独立批准精确 consumer matrix；未经实测的行仍 Unknown。
   Main 真实宿主、授权、EF/租户存储和原 17 PGT 另开窗口验收。

以上是可审查的后续清单，不是已经提出或获得的正式运行批准。
