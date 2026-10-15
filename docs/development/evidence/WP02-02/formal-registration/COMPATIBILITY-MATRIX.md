# 逐输入兼容性复用与重验边界

旧来源：Main `1129953156b7775152a74ad2f51cb95ef430ee56` + Platform
`b28db763f70ab6f164cb7c87d6ce49a6d97267f1`，第七轮 owner
`0615c0909a5a4ee4976ca49cd1339694`。新诊断固定同一 Main 产品 Git 对象，
producer 改为本轮已确认主线 `66480fe24d51c706708be971e7e9d75a684a3bc4`。
完整差异见 `producer-delta.txt`；新执行事实见本目录交付结果。

| 输入 | 相对旧诊断 | 本轮处理与适用范围 |
|---|---|---|
| Main 固定源码、三个覆盖项目、四个原 PackageReference、原锁 | 不变 | 保持原 Git 对象；只在新快照中生成诊断 ProjectReference，不改生产文件 |
| Main NuGet.config | 不变 | 保留原签名要求、source mapping、信任 pin；独立全新缓存恢复 |
| producer 根 props、依赖版本、global.json、NuGet.config、项目文件 | 不变 | 不改版本或依赖来绕过闭包；仍捕获实际 restore 图 |
| Contracts | 新增 SloQualificationV1 | 重新构建三个入口并核实际程序集；不授予 SLO 正式能力 |
| Messaging | CloudEvent validator/bundle 修改，新增 TransportV1 | 重新构建与私有 probe；不把 ctx probe 当作 transport 业务验收 |
| EntityFramework | 新增 TransportOutboxPublisherV1 | 重新构建；本轮无数据库，事务/数据库验收 NOT_RUN |
| Abstractions、AspNetCore | 源码不变，依赖闭包变化 | 仍随新闭包重建，并核运行实际 DLL/hash/MVID |
| Testing | 新增 EvidenceV1 | 不进入生产五包或这三个构建入口；不声明测试库新增行为验收 |
| Release validator 与两份 release schema | 本登记窗口修改 | 新运行 Release API/source/实际 packed-schema 正反矩阵；不把 runtime 五包 probe 当 Release 验证 |
| 七包部署/信任身份 | 尚无正式 .3 | 正式签名、RFC3161、Current 在线验证、feed readback 均 NOT_RUN |
| 诊断工具 | 精确复制，更新 producer SHA/tree 与 owner 前缀 | 29 个工具测试重新执行；探针实现保持原字节，重新运行新 producer |
| 旧 31 项私有宿主断言 | producer 闭包已变化 | 不整体复用旧 PASS；新 owner 必须生成新观察和独立 policy 结果 |
| 最终候选与 66480fe 的运行五包输入 | 收尾按实际 diff 核定 | 只有构建闭包源码/依赖/工具完全不变才可引用本轮诊断；明确原执行 SHA，不能改签为最终 SHA |

所有私有宿主 authority 均为 synthetic，诊断只能证明固定源闭包下的兼容行为。
它不启动 Main Program、DatabaseCompatibility.Probe 程序、数据库或 broker；
不证明最新 Main 业务采用、真实 Owner/授权/权限、正式包信任或 17 PGT。
