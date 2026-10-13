# WP02-27 包能力、版本身份与信任校验

本任务增加独立 `CP6.Platform.Release.CapabilitiesV1` 合同和两个随 Release 包交付的 JSON Schema。消费者可以固定仓库、源码、七包集合、版本、实际归档哈希、feed、schema 和兼容证据，生成确定性的消费身份 lock。源码能力、实际制品、当前信任和宿主/业务采用分别判断。这里只交付本地合同与校验能力，没有新增正式发包、运行环境或消费授权。

来源基线为 Platform `9259a45d39bae7e98ca804a74778a588c1ad4619`，前置 S01-09 和 WP02-01 已合入；本轮 Main 派工输入 `47c9b12c796620effe41c62d16992ccd629ee949`。原 PLT-RELEASE-01 静态设计原字节 SHA256 为 `9246ef6b40f500da22c570bf3c31a51701442bbbed50250d9f259d12cb1bbdbf`。来源逐项核对见 [inputs.json](development/evidence/WP02-27/inputs.json)，原32AC责任映射见 [ACCEPTANCE.md](development/evidence/WP02-27/ACCEPTANCE.md)。

## 消费接口与身份边界

先由可信宿主独立审阅并固定 `Cp6CapabilityApproval` 的 matrix SHA256、完整源码 SHA；正式旧包还需固定当前 NuGet policy SHA256。不能从收到的待验证矩阵抄出 hash 后自动批准它。`ValidateShape` 仅接受已 canonical 的原字节；重复/未知字段、非法 JSON、未知能力、非闭合集合、可变版本标记、改 source、混版和错 feed 都会拒绝。

`Evaluate` 接受实际七个 nupkg 的 byte memory、按哈希寻址的证据原字节、准确请求行和评估时间。它复制输入，验证归档哈希、nuspec 的 ID/version/repository/full SHA、唯一归档项和每行引用的实际包内 schema。证据缺失或变化不会退回源码文件。返回 lock 的字节也有防御性复制。

兼容行固定 route、consumerApp、host、消费方 source、producer package、SDK/runtime/OS/architecture、source/packaged schema、semantic validator、message/schema versions、PostgreSQL provider/driver/migration、capability/profile、API/registration point、configuration hash、证据和结果。包 version/feed/hash/source 从外层唯一集合绑定。其余工具链等标识是审批者负责实测的准确标识符，不进行 semver 范围推断；任何字节不匹配都为 `Unknown`。`Allowed` 只表示该完整批准行的兼容判断，不能授权写入、启用 host 或代替信任判断。

完整格式和使用责任见 [能力合同 README](../contracts/release/capabilities/v1/README.md)。本任务不增加依赖，不修改已有 formal v1、媒体类型、发布版本白名单或 ReleaseTool CLI。

## 能力与包内可见性

| catalog capability/version | 准确 profile | 作用边界 |
|---|---|---|
| `S01-09` / `1.0.0` | `crm-inq-r3-v15`、`erp-credit-current-v1`、`erp-credit-frozen-r8` | native routing、原始 JSON 字节；不是完整业务命令接受 |
| `ctx-binding-v1` / `1.0.0` | `ctx-v1` | Background、Http、Message 上下文绑定；不证明数据库共享事务、共同 fence 或全 writer 采用 |

本地 Inspector 从真实 nupkg 内 PE metadata 检查 Messaging 的 native catalog/JSON API、Contracts 的上下文合同、Abstractions 的执行器，并逐字节比对 Release 包内两个新 schema。此检查证明公开 type/method 名存在，不证明所有 API signature 或真实运行行为。native profile 原 schema 尚未打入现有包，不能拿本次新增 Release schema 替代它；因此实际矩阵不构造已实测的 native consumer 行，兼容状态为 `Unknown`。

正式 `0.10.2` 的源码为 `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，早于以上新能力。即使重新验签成功，也必须保留空能力/兼容列表。新源码仅使用唯一 `0.10.3-wp02.27.g<完整源码SHA>.<正整数attempt>` 本地版本；准确源码 commit 和实际归档哈希在证据中记录，不使用稳定版本槽或正式 feed。

## 当前信任与分层状态

正式包路径复用现有固定 NuGet policy、公有 DER 和未修改的真实 ReleaseTool。原 publication 本身也必须属于已批准矩阵的证据哈希集合；包签名不能证明任意 publication JSON 中的 workflow/run 声明。宿主实现 `ICp6CapabilitySignatureVerifier` 时必须实际校验精确七包和当前政策，不能读取 JSON 的“成功”字段当密码学结果。

本轮使用 Current 模式、在线撤销、严格 RFC3161 timestamp 和原固定 signer。历史/撤销/未知签名者、坏或缺失时间戳、损坏归档拒绝；网络或撤销状态未知时不能改成离线成功。历史审计成功不表示当前可消费。真实命令与时间在交付证据中，当前 feed API 和 workflow 运行真实性并未重新在线审计。

| 状态 | 此合同能证明的内容 |
|---|---|
| SourceFrozen | 与宿主独立批准的源码身份相符 |
| ContractShapeValid | 当前版本的 canonical/闭合/语义关系通过 |
| ArtifactBytesVerified | 实际七包身份/哈希及引用包内 schema 相符 |
| TrustCurrentlyAccepted | 仅正式旧包通过当前政策及真实验签边界后成立 |
| ConsumerQualified / HostRegistered / ConfigEnabled / RuntimeObserved / BusinessApplied / IndependentlyAccepted | 本 API 始终 Unknown，由各层 Owner 的独立真实验收提供 |

确定性 lock 绑定 matrix、request、源码、七包、证据、publication 和 policy 哈希，不含永久信任或权限授予。使用时必须重新判断当前信任。本地候选即使合同/归档通过，也始终 `NOT_PUBLISHED` / `NOT_FORMALLY_TRUSTED`。

## 验证与后续消费

复现入口为 [run_checks.py](../eng/wp02-27/run_checks.py)、[check_trust_negatives.py](../eng/wp02-27/check_trust_negatives.py) 和 [Inspector](../eng/wp02-27/Inspector/README.md)。每次使用新的运行目录，保留原日志、退出码、TRX、源码工作字节及制品哈希。Release 必需 filter 和完整 Architecture 是本任务本地门禁；任何必需门禁失败均不合并。普通 Actions 不触发，亦不以本地结果伪造远程检查。

WP02-02 仍需准确不可变且当前可信的新正式七包消费身份，其中包含已交付 ctx-binding API；还需真实包消费、host/driver/schema/事务与共同 fence 等自身验收及对应窗口。正式发布应另行批准并按 P10/DevOps/R2 的唯一发布权威执行。不能从本次 source freeze、本地 pack、旧0.10.2验签或 Handoff ACK 自动解除其阻塞。

125业务目标、247运行验收仍 NOT_RUN；原32AC的外部 Owner 责任、41项历史 REFERENCE_ONLY、ITER/REL缺件和 WP05 PROPOSED 均不升级。没有数据库、环境部署、正式包发布或 Main/CRM 配置改动。共享台账及四份项目状态由 COORD 在交付集成窗口更新；远端 main 包含核对前不声明 DONE。
