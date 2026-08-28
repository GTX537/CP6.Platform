# P04-S01 — CloudEvents、JSON Schema 与 Contract Bundle

| 项目 | 值 |
| --- | --- |
| 公开里程碑 | `P04-S01` |
| 仓库交付版本 | `0.4.0.0` / package metadata `0.4.0-alpha.0` |
| 状态 | Implemented / Awaiting main verification；未发布 |
| 前置 | P01；P02/P03 继续向后兼容 |
| DRI | Platform Owner（BUBAO.GAO） |
| Reviewer | Security、SRE、CRM Owner；单人流程可由同一 ProgramOwner 承担角色，但不能豁免自动化证据 |
| 输入 | CP6 SaaS V1 公开工程契约 §5、CRM V1 可执行规格 §9 / §18.1、CloudEvents 1.0、JSON Schema Draft 2020-12 |
| 输出 | `CP6.Platform.Messaging`、`contracts/contract-bundle.v1.json`、完整 Schema/示例矩阵、兼容性与包内容测试 |

仓库交付版本使用四段 `VERSION`：`0.4.0.0`。当前 package metadata `0.4.0-alpha.0` 只用于本地可重复 pack 和合同验证；不可变候选发布属于后续独立 `P04-S02`，本任务不得改写或冒充已发布的 `0.3.0-alpha.1` P03 包。

## 1. 可观察行为

生产者和消费者可以从同一个内容寻址 bundle 加载事件合同，并在任何 Outbox、Inbox 或领域副作用之前验证完整的 CloudEvents 1.0 structured JSON。验证只在以下条件全部满足时成功：

- envelope 是 CloudEvents 1.0，结构化媒体类型为 `application/cloudevents+json`，`data` 为 `application/json` 对象；
- `type` 使用 `com.gtx537.<producer>.<event-name>.v<major>`，`source` 使用匹配的 `urn:cp6:<producer>`；
- `dataschema` 精确映射到 `https://contracts.cp6.uk/events/<producer>/<event-slug>/v<major>/schema.json`；
- 必需扩展为 `tenantid`、`correlationid`、`causationid`、`aggregateid`、`aggregateversion`、`schemaversion`、`region`；
- `subject` 以同一个 `tenantid` 开头，`time` 使用 UTC `Z`，aggregate version 为正整数；
- bundle 同时匹配 event type、schema ID、schema version，且 Schema 和示例 SHA-256 未漂移；
- 完整事件通过声明为 Draft 2020-12 的 JSON Schema，包括 required、类型、format、长度、未知字段与 PII 负向规则。

失败统一返回稳定 Platform code `CP6_EVENT_SCHEMA_INVALID` 以及 `MalformedJson`、`UnknownContract`、`SchemaMismatch` 或 `InvalidCloudEvent` 分类。错误只公开 JSON Pointer 位置，不复制值、事件 body 或 PII。

## 2. Bundle 布局

```text
contracts/
├─ contract-bundle.v1.json
└─ events/<producer>/<event-slug>/v<major>/
   ├─ schema.json
   └─ examples/
      ├─ valid.json
      ├─ missing-required.json
      ├─ unknown-optional.json
      ├─ wrong-type.json
      └─ pii-negative.json
```

索引固定 bundle version、CloudEvents spec version、JSON Schema dialect、event type、schema semver、schema ID/path/hash 及每个示例的预期结果/path/hash。路径必须相对 bundle 根目录，绝对路径、目录逃逸、缺失文件、重复 type/schema ID 或 hash 漂移全部失败关闭。

当前 `com.gtx537.platform.contract-example.changed.v1` 是 P04 的无业务语义合同夹具，不代表 C02 身份/租户事件已实现或获准发布。真实事件仍由各生产者仓库在独立任务中提交自己的 Schema、示例和消费者责任。

## 3. 兼容策略

Event Type 尾部携带 major。删除/改名、改变已发布字段类型/format/const/enum/pattern、改变 required 集合、移除字段、收紧数值或长度边界、拒绝未知属性均是 breaking change，必须发布新的 `.v2` 和 `/v2/schema.json`。

同 major 只允许新增可选字段或放宽已存在的长度/数值边界。消费者必须忽略未知字段；对象 Schema 保持 `additionalProperties: true`。`Cp6SchemaCompatibility` 在合并前比较已发布与候选 Schema，返回稳定位置和原因，不把人工“看起来兼容”作为证据。

## 4. 示例和负向矩阵

| 示例 | 预期 | 证明 |
| --- | --- | --- |
| `valid` | Pass | 完整 envelope/data 可由生产者和消费者共同验证 |
| `missing-required` | Fail | 缺 `correlationid` 不得进入副作用 |
| `unknown-optional` | Pass | envelope/data 新增可选字段保持前向消费能力 |
| `wrong-type` | Fail | aggregate version 字符串不能冒充整数 |
| `pii-negative` | Fail | 未批准 `email` 字段不能进入事件 data；错误结果不回显值 |

## 5. 验收命令与 DoD

```powershell
pwsh ./eng/verify.ps1 -Gate Format
pwsh ./eng/verify.ps1 -Gate Build
pwsh ./eng/verify.ps1 -Gate Unit
pwsh ./eng/verify.ps1 -Gate Integration
pwsh ./eng/verify.ps1 -Gate Contract
pwsh ./eng/verify.ps1 -Gate Security
```

DoD：

1. Factory/codec、bundle loader、validator 和 same-major compatibility checker 为公开、带注释的 `CP6.Platform.Messaging` API；
2. 五类示例结果、PII 不回显、未知 schema version、type/schema/source/tenant 对齐和兼容性负向矩阵自动化通过；
3. Contract Gate 可重复 pack 四个批准包，并证明 Messaging nupkg 包含 bundle 索引、Schema 和全部示例；
4. Format、Build、Unit、Integration、Contract、Security 在 Windows/Linux 使用同一入口；
5. 分支相对 `main` diff 无 Secret、真实 tenant/user、业务 PII、云资源或运行时 Dapr/Kafka 配置。

## 6. 失败与前向修复

- Schema 或索引错误：阻止合并/发布，修复源文件并重算 hash；不得关闭验证或跳过示例。
- 已发布 same-major 兼容失败：保留旧 v1，创建新 `.v2`；不得覆盖不可变包或 Schema。
- 验证库/CloudEvents SDK 漏洞：Security Gate 失败，升级固定依赖版本并重跑全矩阵。
- P04-S01 合并后发现合同缺陷：从最新 main 建立独立前向修复分支；不重写共享历史。

## 7. 明确不做

- 不发布 NuGet，不配置 GitHub Packages 凭据；
- 不实现 Dapr、Kafka、Topic/ACL、partition transport binding 或运行时订阅，这些属于 P05/P09；
- 不实现 Outbox/Inbox、DLQ 或重放 worker，这些属于 P06；
- 不定义 CRM/ERP/Identity 业务事件语义，不启用 C01、C02、CRM03 或真实登录；
- 不创建云资源、Secret、数据库、部署或生产配置。

P04 后续严格顺序为：`P04-S02` 从已验证 Platform main 发布不可变候选包；`P04-S03` 在 CRM 独立分支固定版本消费，并证明生产者/消费者对同一 bundle 双向验证。
