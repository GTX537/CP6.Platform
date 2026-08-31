# P09 Compose Dapr 运行网络接口确定化设计

| 项目 | 值 |
| --- | --- |
| 里程碑 | P09-S02 Compose 非生产演练 |
| 文档状态 | 方案已选定，待书面规格复核 |
| 日期 | 2026-08-31 |
| 仓库 | `GTX537/CP6.Platform` |
| 分支 | `codex/p09-nonprod-runtime-implementation` |
| 开发模式 | `SoloDevelopment` |
| 关联设计 | `docs/superpowers/specs/2026-08-30-p09-non-production-runtime-design.md` |
| 关联计划 | `docs/superpowers/plans/2026-08-30-p09-s01-s03-platform-runtime.md` |

## 1. 决策摘要

P09 Compose 演练保留现有的四网络隔离模型，但为三个双网卡 Dapr sidecar 显式固定接口名和默认网关优先级：

- `runtime` 固定为 `eth0`，`gw_priority: 1`；
- 对应的应用私有网络固定为 `eth1`，`gw_priority: 0`；
- 应用容器继续只连接各自的应用私有网络；
- Kafka 与 `kafka-admin` 继续只连接 `runtime`；
- 不设置静态 IP，不设置 `DAPR_HOST_IP`，不引入新的名称解析服务。

该设计让 Dapr 在 Compose 自托管模式下获得确定的首个非 loopback 接口和默认出站网络，并使其可被其他 sidecar 发现的地址落在共享 `runtime` 网络。此处是基于 Dapr 地址选择规则和 Docker Compose 网络语义形成的工程推断，不能单凭静态配置声明修复；真实正负向矩阵全部通过才算验收。

## 2. 问题与现有证据

当前精确提交 `f2c96a953681f4ec15988d11a5c1a4d8e0826de2` 的真实 Compose 演练稳定完成以下阶段：

1. 合同与静态前置检查；
2. 临时凭据生成与受限写入；
3. Kafka controller、SASL 和数据平面 readiness；
4. Topic 与 ACL provision；
5. 失败后的精确 project teardown 与容器、网络、volume、本地 fixture image、临时凭据零残留核对。

演练稳定失败在 `invoke-positive`：publisher 的 Dapr service invocation 在共享 60 秒窗口内无法解析或到达 receiver AppId。收敛后的 runner 内诊断仍返回 `diagnostic-unavailable`，没有发现可安全保留的原始 Secret 或无限日志。

Dapr 自托管模式默认使用 mDNS 名称解析；Docker 运行时要求 sidecar 位于可互通的 Docker 网络。Dapr 的 `DAPR_HOST_IP` 文档同时说明，未覆盖时会选择非 loopback/出站地址。现有三个 sidecar 都同时连接 `runtime` 与一个应用私有网络，且没有固定接口名或默认网关，因此所选地址受 Compose 接口分配影响。官方参考：

- [Dapr self-hosted overview](https://docs.dapr.io/operations/hosting/self-hosted/self-hosted-overview/)
- [Run Dapr with Docker](https://docs.dapr.io/operations/hosting/self-hosted/self-hosted-with-docker/)
- [Dapr environment variables reference](https://docs.dapr.io/reference/environment/)
- [Docker Compose service network attributes](https://docs.docker.com/reference/compose-file/services/)

Dapr 官方仓库近期也记录过同类 `ERR_DIRECT_INVOKE`/地址发现超时现象，但该问题仅作为相似症状参考，不作为本仓库根因已经被证明的依据：[dapr/dapr#10253](https://github.com/dapr/dapr/issues/10253)。

## 3. 目标

本校准必须做到：

1. 让所有 Dapr sidecar 的共享 `runtime` 地址选择确定化；
2. 保持应用容器无法解析或直连 Kafka 的网络隔离；
3. 不扩大主机暴露端口、容器权限、挂载面或 Secret 面；
4. 让旧版 Compose 在产生任何运行副作用前明确返回安全的 `NotRun`；
5. 用静态合同测试和真实矩阵同时证明拓扑与行为；
6. 保持失败关闭、内容寻址证据和精确 project 零残留语义。

## 4. 非目标

本校准不：

- 修改 P09 Profile、Evidence Schema、公开版本或里程碑状态；
- 引入静态子网/IPAM、`DAPR_HOST_IP`、Consul、SQLite name resolver 或其他外部依赖；
- 使用 `network_mode: service:*`、host networking 或共享应用/sidecar 网络命名空间；
- 让应用容器、direct probe 或 Kafka 加入新的网络；
- 新增主机端口、云资源、真实 Kubernetes 集群、Registry、部署或人工审批；
- 取代 P09 Task 7 的 Kubernetes 资产和离线门禁；
- 因本次修复直接声明 `Frozen / Consumable` 或生产就绪。

## 5. 备选方案与选择理由

### 5.1 采用：Compose 接口名与默认网关确定化

在每个 Dapr sidecar 的 service-level `networks` 映射中固定 `interface_name` 和 `gw_priority`。优点是改动窄、没有额外运行服务、不需要固定子网，并保留既有隔离模型。Docker Compose 文档规定最高 `gw_priority` 的网络成为默认网关；`interface_name` 则消除容器内接口命名的不确定性。

### 5.2 不采用：静态 IPAM 加 `DAPR_HOST_IP`

该方案能直接指定 runtime 地址，但需要管理固定子网与每个 sidecar 的静态地址，增加开发机网段冲突、并行执行冲突和清理复杂度。P09 不需要用这类环境耦合换取确定性。

### 5.3 不采用：在 `runtime` 引入 Consul 名称解析

该方案可绕开 mDNS，但会新增镜像、digest、运行服务、健康检查、安全边界、证据字段和失败分类，超出本次 Task 6 校准范围。

### 5.4 明确禁止：共享网络命名空间

`network_mode: service:*` 或等价做法会使应用获得 sidecar 的 `runtime` 网络能力，破坏“应用不能直连 Kafka”的核心验收条件，也违反既有静态门禁。

## 6. 精确拓扑

修改后唯一允许的网络归属如下：

| 服务 | 网络与接口 | 默认网关语义 |
| --- | --- | --- |
| `kafka` | `runtime` | 单网络，保持不变 |
| `kafka-admin` | `runtime` | 单网络，保持不变 |
| `publisher` | `publisher-app` | 单网络，保持不变 |
| `publisher-dapr` | `runtime: eth0, gw=1`; `publisher-app: eth1, gw=0` | `runtime` |
| `receiver` | `receiver-app` | 单网络，保持不变 |
| `receiver-dapr` | `runtime: eth0, gw=1`; `receiver-app: eth1, gw=0` | `runtime` |
| `direct-probe` | `unauthorized-app` | 单网络，保持不变 |
| `unauthorized-dapr` | `runtime: eth0, gw=1`; `unauthorized-app: eth1, gw=0` | `runtime` |

每个双网卡 sidecar 的 Compose 结构必须等价于：

```yaml
networks:
  runtime:
    interface_name: eth0
    gw_priority: 1
  <app-private>:
    interface_name: eth1
    gw_priority: 0
```

`priority` 不能替代 `interface_name`；Docker Compose 文档明确指出网络连接优先级不决定 Linux 接口名。

## 7. Compose 版本与前置检查

`interface_name` 需要 Docker Compose `2.36.0` 或更高版本，因此 runner 的运行前置检查必须：

1. 在 Profile、路径、Git SHA 和静态合同校验之后执行；
2. 在创建临时目录、凭据、容器、网络、volume 或镜像之前解析 `docker compose version --short`；
3. 接受 `>= 2.36.0`；
4. 对不存在、无法解析或低于最低版本的 Compose 返回显式 `NotRun / unsupported-compose-version`；
5. 不为该结果写入 `Passed` evidence，并证明 Docker 运行副作用为零。

本地 Compose `5.1.1` 满足最低版本，但本地版本本身不是跨环境验收证据。

## 8. 测试与验收

### 8.1 先写静态 RED 测试

`P09ComposeContractTests` 必须在改 Compose 前新增或收紧断言，覆盖：

- 三个 Dapr sidecar 精确使用 `runtime/eth0/gw_priority=1`；
- 三个 sidecar 的对应私有网络精确使用 `eth1/gw_priority=0`；
- 只有这三个 sidecar 双网卡；
- 应用、direct probe、Kafka 和 `kafka-admin` 的网络归属不变；
- 不出现 `network_mode`、host network、静态 IP、额外端口或额外网络；
- 文本合同与 `docker compose config --format json` 的规范化结果一致；
- 任意接口名、网关优先级、网络归属或版本门槛变异都会使测试失败。

runner 的 fake-Docker 测试必须先覆盖 Compose `2.35.x`、`2.36.0`、更高主版本、不可解析输出和命令失败，证明支持与 `NotRun` 分界及零副作用。

### 8.2 真实 Compose 验收矩阵

在同一精确 Git SHA 上重新运行完整矩阵，至少要求：

1. `invoke-positive` 通过，并证明预期 trace parent/child 拓扑；
2. Pub/Sub 正向事件、topic、key 和 identity 一致；
3. `direct-kafka-denied` 仍因 Kafka DNS/TCP 不可达而通过；
4. 未授权 principal 的 produce 与 consume 均被 Kafka ACL 拒绝；
5. 未授权 AppId 不能看到或使用受 scope 限制的 component；
6. foreign topic 在首个相关 Docker 调用前被拒绝，broker topic 集合不变；
7. 成功和失败路径均核对精确 Compose project 的容器、网络、volume、本地 fixture image 与临时凭据为零残留；
8. 产物仍通过 Schema、运行时 validator、canonical JSON、hash 和 Secret/机器路径扫描。

若 service invocation 仍失败，结果必须保持 `Failed`，保留有界、脱敏、可分类的诊断摘要，并执行相同零残留清理；不得通过扩大网络、放宽 ACL 或跳过检查获得绿色结果。

## 9. 相邻的 Task 6 稳定性修正

现有 Kafka healthcheck 与 provision 流程共用 `provisioner.properties`，其中 readiness 所需的 5 秒 client timeout 会连带缩短正常 ACL provision 请求，形成与网络决策无关的偶发超时风险。实现阶段应以独立 TDD 变更处理：

- `readiness.properties` 继续使用同一 provisioner 身份，但保留 5 秒 client timeout；
- 正常 `provisioner.properties` 使用合理的有界 client timeout，例如 30 秒，并继续受 runner 外层 120 秒 deadline 约束；
- 两个文件都保持既有 UID/GID、`0600`、临时根、只读挂载和不进入 artifact/log 的约束；
- 该修正不能被当作 Dapr service discovery 根因或本设计验收的替代品。

## 10. 证据、状态与后续顺序

本校准只修改 P09-S02 的 Compose 拓扑、runner、临时 Kafka client 配置和对应测试。即使真实 Compose 矩阵通过，也不能生成完整 P09 `Passed` 结论，因为原计划 Task 7 的 Kubernetes base、CI overlay、render 和 policy checks 仍是完整证据的必需部分。

后续顺序固定为：

1. 在书面规格复核后生成窄范围实现计划；
2. 以 TDD 修改静态合同、Compose 和 runner 版本门槛；
3. 独立完成 Kafka readiness/provision client 配置拆分；
4. 完成 Task 7 Kubernetes 资产及离线门禁；
5. 在同一精确 SHA 运行完整 Compose 与 Kubernetes 验收；
6. 只有全部必需检查、diff 审查和项目状态文档闭环后，才进入原计划的提交、PR、合并与 exact-main 验证。

候选状态上限仍是 `Implemented / Rehearsal Candidate`。

## 11. 回退与失败关闭

该变更不涉及持久业务数据或外部环境。若确定化网络仍不能通过验收，回退范围仅限本校准引入的 Compose 字段、版本前置和对应测试；不得删除既有失败关闭、ACL、网络隔离、证据或清理门禁。失败提交不合并到 `main`，已生成的唯一 project 必须通过 canonical Compose teardown 和 label 查询证明零残留。
