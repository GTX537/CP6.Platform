# P09 Non-production Runtime 设计

| 项目 | 值 |
| --- | --- |
| 里程碑 | 公开执行语义 `P09` |
| 设计状态 | 对话设计与书面规格均已批准 |
| 日期 | 2026-08-30 |
| 权威前置 | P05、P08 均为 `Frozen / Consumable` |
| 计划版本 | repository `0.9.0.0` / package `0.9.0-alpha.1` |
| 仓库 | `GTX537/CP6.Platform` |
| 开发模式 | `SoloDevelopment` |

## 1. 权威语义与背景

公开执行规格把 P09 定义为 Compose/Kubernetes Dapr 组件、Subscription、Kafka Topic/ACL provision 和非生产部署演练，前置为 P05/P08。该公开定义是唯一当前语义。

私有 CRM 历史计划曾把“CandidateLocator/精确对象版本”称为 P09；该旧编号只作为历史快照保留。CandidateLocator、System Manifest、签名候选和对象 VersionId 读取继续属于公开 P10/R00，不能混入本设计。

P05 已用真实 Dapr 1.18.2 和 Kafka 4.3.1 证明 service invocation 与 Pub/Sub；P08 已冻结 exporter-neutral telemetry、health、release identity、resilience 和证据合同。P09 不重复实现这些 SDK 能力，而是首次把它们放入受限的非生产运行拓扑，并证明组件 scope、Topic/ACL、网络隔离、确定性 provision、失败关闭和清理语义。

## 2. 目标

P09 必须证明：

1. 一个版本化、失败关闭的 Profile 能唯一描述允许的非生产 AppId、Dapr component、Subscription、Topic、ACL、端口和网络关系；
2. Compose 演练使用真实 Dapr/Kafka、SASL principal 和 Kafka ACL，而不是仅靠 Mock 或字符串检查；
3. 应用只能通过自己的 Dapr sidecar 使用 Kafka，不能从应用网络直接连接 broker；
4. 未授权 AppId/principal 不能发布或消费 probe Topic；
5. Kubernetes 资产可在无集群、无 Secret、无云账号的 CI 中确定性渲染并通过静态安全门禁；
6. 每次演练输出内容寻址证据，并在成功或失败后清理唯一 Compose project 的容器、网络、volume 和临时凭据；
7. 从精确 Platform `main` 发布独立不可变 `CP6.Platform.Deployment 0.9.0-alpha.1`，再由 CRM 固定版本做黑盒消费；
8. 只有 Platform、CRM、公共项目记忆和最终 Platform 审计全部通过 PR 与 exact-main 门禁，P09 才能成为 `Frozen / Consumable`。

## 3. 明确不做

P09 不：

- 连接或创建真实 Kubernetes 集群、云资源、Registry、DNS、Ingress 或 LoadBalancer；
- 部署 DEV/UAT/PROD，不修改 CP6 已有根 Compose、Lab、生产 Compose/Kubernetes 或 Azure/GitHub Release 流程；
- 写入真实 Secret、主机凭据、机器专属路径、生产证书或生产 Kafka 地址；
- 创建 CRM Gateway route、CRM Worker、业务 Topic、业务事件、业务 Subscription、数据库或迁移；
- 实现 C01/C02/CRM03、真实登录、权限投影或业务 API；
- 部署 Collector/dashboard/alert/exporter，声明生产 SLO、容量、可用性或现场接受；
- 生成 P10 CandidateLocator、System Manifest、OCI 签名、受保护 Tag 或生产候选；
- 要求第二名 Reviewer、多人签字、双人审批、真实云环境、长时间 soak 或现场参与者。

## 4. 方案选择

采用“合同优先、分阶段闭环”：

1. 先冻结 Profile、Schema、拒绝矩阵和证据 Schema；
2. 再做真实 Compose runtime rehearsal；
3. 随后做 Kubernetes 离线 render/client dry-run 与静态策略验证；
4. 最后发布独立包、完成 CRM 固定版本消费和三仓证据对账。

不采用 Compose-first，因为运行配置先于合同会导致 AppId、Topic、ACL 和证据格式漂移。也不在 P09 引入统一环境代码生成器；同时生成 Compose/Kubernetes 的抽象层会扩大首期复杂度，并隐藏两个运行时各自的安全差异。两套资产保持显式，但必须由同一 Profile 和一致性测试约束。

## 5. 仓库与包边界

P09 只在 `CP6.Platform` 实现生产者资产。计划目录为：

```text
contracts/p09/
  non-production-runtime-profile.v1.schema.json
  rehearsal-evidence.v1.schema.json
  examples/
deploy/p09/compose/
deploy/p09/kubernetes/base/
deploy/p09/kubernetes/overlays/ci/
eng/run-p09-compose-rehearsal.ps1
eng/test-p09-kubernetes.ps1
src/CP6.Platform.Deployment/
tests/CP6.Platform.DeploymentTests/
tests/p09/
docs/P09-NON-PRODUCTION-RUNTIME.md
docs/P09-PUBLICATION.md
```

新增 `CP6.Platform.Deployment`：

- 目标框架继续为 .NET 8；
- 提供 Profile/evidence 的类型、规范 JSON 与纯验证 API；
- 包含 `contracts/p09/**`、`deploy/p09/**` 和安全消费说明；
- 不启动 Docker、kubectl、Dapr、Kafka 或外部进程；
- 不读取环境变量、连接串、Secret 或当前机器配置；
- 不依赖 ASP.NET Core、EF、Dapr SDK 或 Kubernetes client；
- 不让其他五个 P08 生产包依赖它。

P09 只发布这个独立包为 `0.9.0-alpha.1`。P08 五包保持不可变 `0.8.0-alpha.2`，不得为版本整齐而重发相同能力。`CP6.Platform.Testing` 继续是仓库内测试支持，不发布。

## 6. Canonical Profile

Profile 使用 Draft 2020-12 JSON Schema，关闭未知属性，至少包含：

- `schemaVersion=1`；
- `environmentClass=NonProduction`；
- 固定 `profileId=cp6-platform-p09-ci-v1`；
- Dapr/Kafka 精确版本；
- publisher/receiver/provisioner/unauthorized-probe 的稳定 AppId/principal；
- Dapr component 名称、scope 和 Secret 引用名；
- 复用现有 P04 `com.gtx537.platform.contract-example.changed.v1` 作为合成 probe event type，不新增业务或部署事件合同；
- 唯一 Topic `cp6.platform.deployment-probe.v1`、3 partitions、retention 和最大消息大小；
- publisher、receiver、consumer group 和 provisioner 的精确 ACL；
- Compose network/port 断言；
- Kubernetes namespace、ServiceAccount、NetworkPolicy 和禁止资源类型；
- 证据 Schema ID、预期检查 ID 和清理要求。

允许的应用身份固定为：

- `cp6-p09-probe-publisher`；
- `cp6-p09-probe-receiver`；
- `cp6-p09-provisioner`；
- `cp6-p09-unauthorized-probe`，只用于负向验证。

任何 `crm.*`、`cp6.crm.*`、客户/组织标识、生产环境名、业务事件类型、任意外部 Host 或 Profile 外 Topic/AppId 都必须失败关闭。

## 7. Kafka、Dapr 与 ACL 合同

Compose 演练使用 `apache/kafka:4.3.1` 和 `daprio/daprd:1.18.2`，实现阶段还必须记录解析后的镜像 digest。不得使用 `latest`、浮动 major/minor 或本机预存镜像身份代替证据。

Kafka 启用 SASL password 认证；演练开始时在操作系统临时目录下的唯一、边界校验目录生成一次性高熵凭据，并通过只读 Compose secret mount 传入。仓库不保存 secret value，证据不保存 value、可逆编码或 password hash，CI artifact 也不得包含临时凭据目录。

Self-hosted Dapr sidecar 使用运行时生成的 `secretstores.local.file` component 读取该临时凭据文件，再由 Kafka component 的 `secretKeyRef` 取值。local file secret store 只允许出现在 Compose 演练生成目录，不能进入发布包的 canonical Profile/Kubernetes assets，也不能被描述为生产 Secret 方案。运行结束必须在停止全部使用者后删除 component、凭据文件和整个临时目录。

为避免一个共享 Dapr component 凭据同时拥有读写权限，使用两个 component：

- publish component：只 scope 到 publisher AppId，使用 publisher principal；
- subscribe component：只 scope 到 receiver AppId，使用 receiver principal，并由 Subscription 引用。

ACL 最小化：

- publisher 只允许对 probe Topic Write/Describe；
- receiver 只允许对 probe Topic Read/Describe，并只允许固定 consumer group；
- provisioner 只在 provision 阶段拥有创建 Topic/ACL 所需权限，完成后不参与业务流；
- unauthorized principal 对 Topic/group 没有允许 ACL。

重复 provision 必须幂等：精确相同的 Topic/partition/config/ACL 得到成功；任何已存在但不一致的配置必须失败，不能原地放宽或猜测修复。

Dapr Kafka component 使用 `authType=password`、独立 `saslUsername` 和 `saslPassword secretKeyRef`；实现字段必须与固定 Dapr 版本的官方 component metadata 一致。Kubernetes 资产只包含 Secret 引用，不提交 `Secret.data` 或 `Secret.stringData`。

## 8. Compose 拓扑与网络隔离

每次运行使用唯一 project name `cp6-p09-<run-identity>`，且只允许写入仓库 `artifacts/p09-rehearsal/<run-identity>`。

网络分为：

- app network：应用与自己的 Dapr sidecar 通信；
- runtime network：Dapr sidecar、Kafka、provisioner 和负向 probe 通信；
- 应用容器不加入 runtime network；
- Kafka 不映射宿主端口；
- 唯一测试入口绑定 `127.0.0.1` 的随机端口；
- 禁止 `network_mode: host`、`privileged`、Docker socket、host path 和固定公共端口。

sidecar 同时加入对应 app network 与 runtime network，应用只知道本 sidecar endpoint。正向验证必须经过 Dapr service invocation/PubSub。负向验证分别证明：

1. 应用容器没有 Kafka 路由/DNS 可达性；
2. unauthorized principal 可以到达 broker 但不能 publish/consume；
3. 未 scope 的 AppId 不能使用受限 Dapr component；
4. 非 Profile Topic 在任何副作用前被拒绝。

演练不复用或修改 P05 Compose project；P05 继续是 SDK integration regression，P09 是运行拓扑/provision/隔离验收。

## 9. Kubernetes 离线资产

Kubernetes 使用 `base` + `overlays/ci` 的 Kustomize 结构。CI 固定 kubectl 版本并执行：

1. `kubectl kustomize` 离线渲染；
2. 在不可达 kubeconfig 下执行 client dry-run，证明流程不依赖集群；
3. 自有 validator 对渲染后的对象做结构与跨对象语义验证。

允许对象只包括 Namespace、ServiceAccount、ConfigMap、Deployment、Service、Job、Dapr Component、Dapr Subscription 和 namespaced NetworkPolicy。禁止 Secret 对象、ClusterRole/Binding、Ingress、LoadBalancer、NodePort、PersistentVolume、hostPath、hostNetwork、privileged、hostPort 和生产 namespace。

NetworkPolicy 至少包含：

- namespace 默认 deny ingress/egress；
- DNS 最小放行；
- probe 测试入口到 publisher 的最小 ingress；
- sidecar/provisioner 到 Kafka endpoint 的最小 egress；
- 不允许应用 pod 直接访问 Kafka；
- 不允许任意 `0.0.0.0/0` egress 规则。

Kubernetes CI overlay 使用不可拉取的 `example.invalid` + 固定 fixture digest，明确标记 `cp6.io/nondeployable=true`。它只证明渲染与策略合同，不冒充可部署镜像或真实集群验收。真实镜像、Secret、namespace 与集群连接需要未来独立环境任务授权。

## 10. 数据流

执行顺序固定为：

```text
Profile/Schema validate
  -> Compose/Kubernetes consistency validate
  -> Docker/kubectl preflight
  -> ephemeral credential generation
  -> Kafka start
  -> Topic/ACL provision + idempotent replay
  -> probe apps and Dapr sidecars start
  -> positive service invocation and Pub/Sub
  -> trace/topic/partition/CloudEvent validation
  -> direct-access/AppId/principal/topic negative matrix
  -> evidence normalization and SHA-256 sealing
  -> finally teardown
  -> zero-residue verification
```

任何 Profile、Schema、manifest 或安全扫描失败必须发生在 Docker 启动前。运行时不会自动修改 Profile、改变 Topic、放宽 ACL 或回退到 `authType=none`。

## 11. Evidence 合同

`rehearsal-evidence.v1.schema.json` 至少记录：

- schema/profile ID 与 Profile SHA-256；
- Platform Git SHA、repository/package version；
- Compose/Kubernetes manifest SHA-256；
- Dapr/Kafka/kubectl 版本和镜像引用/digest；
- Topic partitions/config 与 ACL principal/operation/resource 清单；
- 每个正向/负向检查的稳定 ID、结果和安全摘要；
- service invocation、event ID、topic、partition key 和 trace topology；
- started/completed UTC；
- teardown 命令结果和零残留检查；
- overall `Passed`/`Failed`。

证据不包含 Secret、Token、connection string、Host 路径、Docker daemon identity、用户名、组织/客户信息、完整环境变量或自由文本异常。日志在保存前做敏感模式扫描。

证据 JSON 使用稳定属性顺序与 UTF-8/LF 规范化后计算 SHA-256。只有全部必需检查成功且零残留时 overall 才能为 `Passed`。失败证据可保留用于诊断，但不能用于冻结、发布或消费状态。

## 12. 错误处理与清理

Runner 使用单一主错误和独立 cleanup errors：

- 主步骤失败立即停止后续业务验证；
- `finally` 始终收集允许范围内日志并执行 `docker compose down --volumes --remove-orphans --rmi local`；
- 清理后按唯一 project label 检查容器、网络、volume 和本地构建镜像；
- 删除临时凭据前先关闭全部使用者；
- 清理失败使 overall 为 `Failed`，不能被先前成功覆盖；
- 日志保存失败也记录为失败，但不得阻止资源清理；
- runner 只允许清理自己生成且精确匹配 project label/证据目录的资源，不使用宽泛 prune。

本地 Docker 不可用时，脚本明确返回 NotRun/环境错误，不生成 Passed。任务仍可由同提交的 GitHub-hosted Ubuntu 真实 Compose 门禁提供执行证据。

## 13. 自动化验收

### 13.1 Profile/package contract

- Draft 2020-12 正向样例；
- 未知/重复属性、错误类型、digest 漂移和非规范 JSON；
- 生产命名、CRM Topic/AppId、任意外部 Host 和未知资源；
- 明文 Secret、Secret manifest、host path、浮动镜像和固定公共端口；
- 包内容、路径、assembly、Schema、example、machine-path 和敏感模式扫描。

### 13.2 Compose real rehearsal

- 真实 Dapr service invocation；
- 真实 Kafka publish/consume、CloudEvent/schema/region/topic/key 和 Trace；
- publisher/receiver 最小 ACL；
- unauthorized principal、未 scope AppId、非 Profile Topic 和 app direct-Kafka 负向；
- Topic/ACL provision 首次与重复执行；
- 篡改 partitions/config/ACL 后失败关闭；
- 正常、验证失败和启动失败三类 teardown 零残留。

### 13.3 Kubernetes static gate

- Kustomize render 确定性和重复 SHA；
- 无集群 client dry-run；
- Dapr component/subscription scope 与 SecretKeyRef；
- namespace default deny、DNS/ingress/Kafka egress 最小放行；
- 删除 default deny、扩大 CIDR、增加 LoadBalancer/Ingress/Secret/ClusterRole/hostPath 等变异必须失败。

### 13.4 Cross-repository consumer

CRM 固定 `CP6.Platform.Deployment 0.9.0-alpha.1`，从 NuGet 包读取而不是复制仓库文件，并验证：

- package/hash/Schema/Profile/manifest 身份；
- 正向与负向 fixture；
- P02-P08 locator 回归；
- `Program.cs` 仍不注册 Gateway/auth/Worker/business subscription/exporter；
- 无 CRM runtime route、business Topic、Secret、数据库或部署副作用。

## 14. 单人开发门禁

P09 使用最小充分门禁：

- 每个阶段独立分支、PR、完整 diff 自审和 exact-main 核验；
- Windows/Linux unit/contract；Ubuntu real Compose；Kubernetes offline static；
- 不要求第二 Reviewer、额外组织角色或多人批准；
- 不要求本机 Docker 与远端重复成功；远端同 SHA 真实 Compose 成功即可满足运行证据；
- 不要求真实集群、生产 Kafka、云账号、外部 Secret、性能/业务 UAT、soak 或现场接受；
- 只对 Critical/High、安全越界、明文 Secret、哈希漂移、失败关闭和资源残留保持零容忍。

普通可维护性问题可由单人自审关闭，但不得以 SoloDevelopment 为理由跳过自动化、PR/main 证据或降低安全边界。

## 15. 阶段与状态机

阶段固定为：

1. `P09-S00`：本设计经批准、书面复核并合入 Platform main；
2. `P09-S01`：Profile/Schema/Deployment package/validator 与正反向测试；
3. `P09-S02`：真实 Compose provision、运行、隔离、证据和清理；
4. `P09-S03`：Kubernetes 离线 render/dry-run/策略矩阵；
5. `P09-S04`：从精确 Platform main 发布不可变 `0.9.0-alpha.1`；
6. `P09-S05`：CRM 固定版本黑盒消费与 locator；
7. `P09-S06`：公共项目记忆同步、Platform 最终对账和 exact-main 审计。

状态只允许前向：

```text
Absent
  -> Design Accepted
  -> Implemented / Rehearsal Candidate
  -> Published / Consumer Candidate
  -> Consumer Verified
  -> Frozen / Consumable
```

包已上传、单次 Compose 成功、CRM PR head 成功或公共文档已更新都不能单独关闭 P09。`Frozen / Consumable` 只有在 S00-S06 的 PR/main/run/artifact/hash 全部可追踪后生效。

## 16. 发布与三仓证据

Platform publication 必须：

- 从当前精确 `origin/main` 手动 dispatch；
- 固定 expected commit 和 `0.9.0-alpha.1`；
- 在 publish 前重跑 unit/contract、P05/P06/P08 regression、P09 Compose 和 Kubernetes static；
- 只发布 `CP6.Platform.Deployment`，若版本已存在则失败，不覆盖；
- 保存 package/snupkg/SHA-256、Profile/manifest/evidence 和 workflow identity。

CRM 与公共仓库分别在自己的独立分支更新 locator/project memory。Platform 最终审计必须绑定：Platform implementation/publication、CRM consumer PR/main、公共同步 PR/main，以及 exact-main job 名称、ID、结论和 artifact digest。

## 17. 安全与授权边界

P09 只授权仓库内合同、真实本地/CI Compose 演练、Kubernetes 离线静态验证、不可变非生产 Deployment 包和跨仓黑盒消费。

它不授权任何真实环境部署，也不解锁 P10、C01/C02/CRM03、CRM-F3-CONTRACT、CRM Worker、业务页面、业务 API、业务 Topic、Gateway route、生产 exporter/SLO 或候选发布。后续能力必须继续独立设计、授权、分支和验收。

## 18. 权威技术参考

- Dapr Apache Kafka component metadata、SASL password 和 `secretKeyRef`：<https://docs.dapr.io/reference/components-reference/supported-pubsub/setup-apache-kafka/>
- Dapr self-hosted local file secret store（仅限开发演练）：<https://docs.dapr.io/reference/components-reference/supported-secret-stores/file-secret-store/>
- Kubernetes NetworkPolicy v1：<https://kubernetes.io/docs/reference/kubernetes-api/networking/network-policy-v1/>
- `kubectl kustomize` 离线渲染入口：<https://kubernetes.io/docs/reference/kubectl/generated/kubectl_kustomize/>
- `kubectl apply --dry-run=client`：<https://kubernetes.io/docs/reference/kubectl/generated/kubectl_apply/>
