# WP02-11 原始 42 AC 的本次证据范围

原始 AC 内容、身份与 NOT_RUN 状态不变。`PARTIAL_PASS` 仅指下表库子断言在指定本地 TRX 中有通过证据，绝不表示完整业务 AC 通过。无运行子断言的条目标为 `NOT_RUN`；所有真实宿主、Owner、UI、数据库与生产采用继续待验收。准确用例名称和运行路径见 ac-scope.json。

| 原 AC | 库子断言状态 | 本任务范围 | 仍需真实宿主 / Owner 负责 |
|---|---|---|---|
| HTTP01-A1 | PARTIAL_PASS | 原三种定义、旧writer与新安全writer的字段/Content-Type兼容 | 各真实Host的错误字典与实际消费者签收 |
| HTTP01-A2 | PARTIAL_PASS | 端点字段/安全消息白名单、32×4×256和实际16KiB正文限制；不回显异常或输入 | Host登记文本与字段可见性权威、实际业务错误映射 |
| HTTP01-A3 | PARTIAL_PASS | 32位trace及安全correlation生成/保留，不读取它们作为授权 | 真实Host因果链、日志访问及隐私策略 |
| HTTP01-A4 | PARTIAL_PASS | 已开始/取消/断连不追加第二正文；写入失败不能伪造成功 | Host部分响应的中止与低敏审计 |
| HTTP01-A5 | PARTIAL_PASS | 旧API保留，仅新增显式入口，运行既有认证/HTTP回归 | 真实CRM about:blank、业务码、CSRF/OIDC端到端兼容 |
| HTTP01-A6 | PARTIAL_PASS | 技术Unknown与确定结果区分，原operation查询优先的库协议 | 应用状态DTO、UI恢复与真实Owner结果 |
| HTTP01-A7 | PARTIAL_PASS | 可信绑定保tenant/action/key，历史查询和新写授权分离的fixture | 真实历史read/字段mask和跨租户防枚举权威 |
| HTTP01-A8 | PARTIAL_PASS | 发送后响应丢失保持原身份并查询结果的fixture | 真实Owner提交、receipt与响应失败后的数据库证据 |
| HTTP02-A1 | PARTIAL_PASS | 既有GatewayContractTests回归，不修改网关profile | 每Host实际route/cluster/目的登记 |
| HTTP02-A2 | PARTIAL_PASS | 既有网关头清理回归；HttpV2不从外部header授写去重能力 | 真实下游无身份header fallback与部署代理信任 |
| HTTP02-A3 | NOT_RUN | 本任务不新增/启用网关route | Host新受保护route必须显式policy、methods和目的allowlist |
| HTTP02-A4 | PARTIAL_PASS | 安全correlation及已有网关/出站关联回归 | 后台因果引用和真实Host的关联传递 |
| HTTP02-A5 | PARTIAL_PASS | 保留已有进程/IP限流回归，不声称全局额度 | 实际代理IP来源、多副本配额和共享出口策略 |
| HTTP02-A6 | NOT_RUN | 本任务不改变ForwardedHeaders或可信代理配置 | 部署Owner当前代理登记及客户端IP来源验证 |
| HTTP02-A7 | PARTIAL_PASS | 操作绑定固定目的和route epoch，不透明切另一个写域 | 真实路由切流/回退及跨区域同一Owner去重域证明 |
| HTTP02-A8 | PARTIAL_PASS | 新绑定不因失败改目的/凭据，安全错误无原异常正文 | 真实TLS、证书、代理与认证失败集成 |
| HTTP03-A1 | PARTIAL_PASS | legacy默认/范围保留，新profile强制attempt≤total | Host批准的具体client预算及外层重试清单 |
| HTTP03-A2 | PARTIAL_PASS | 单调预算覆盖连接/发送/正文/等待；子deadline不扩张 | 实际嵌套服务跨进程可信deadline传递 |
| HTTP03-A3 | PARTIAL_PASS | equal-duration按明确strategy来源分类，未知来源停止 | Host对最终安全错误的版本化映射 |
| HTTP03-A4 | PARTIAL_PASS | caller取消原样传播，等待取消零后续attempt | 业务取消命令与撤单由Owner独立定义 |
| HTTP03-A5 | PARTIAL_PASS | 发送后未知保持同operation并先查原结果的fixture | 真实业务提交/丢响应/receipt数据库恢复 |
| HTTP03-A6 | PARTIAL_PASS | 一般传输异常不证明NotSent；已有Unknown不被CircuitOpen降级 | 真实transport未送达证明由实际adapter提供 |
| HTTP03-A7 | PARTIAL_PASS | UTC回拨不增加预算或提前已锚定等待 | 部署机器时钟观测和真实长生命周期恢复 |
| HTTP03-A8 | PARTIAL_PASS | 原结果查询先于新写授权；不可用保Unknown，查询受剩余预算约束 | 真实历史读取权限和Owner查询服务可用性 |
| HTTP04-A1 | PARTIAL_PASS | GET/HEAD/OPTIONS与四类写方法准确；唯一key不是去重证明 | Host逐操作分类和原业务键域 |
| HTTP04-A2 | PARTIAL_PASS | NonIdempotent和未证明写入零自动重试 | 真实code兑换/登录/支付端点分类与注册 |
| HTTP04-A3 | PARTIAL_PASS | legacy transient集合/TimeoutRejectedException排除不变；新来源明确 | 各Host额外业务拒绝分类必须受控 |
| HTTP04-A4 | PARTIAL_PASS | 固定S/L/R/A子例、一次jitter、三日期、cap、双预算门、早迟醒和取消 | 实际网络/代理对Retry-After的完整传输 |
| HTTP04-A5 | PARTIAL_PASS | 可信绑定及每次请求的tenant/key/body/digest/version/epoch不变 | 远端原键冲突/不可变原结果的真实持久化 |
| HTTP04-A6 | PARTIAL_PASS | stream/超限体不自动重放，不因buffer自证业务幂等 | 大流量端点的独立non-replay合同 |
| HTTP04-A7 | PARTIAL_PASS | Unknown先查原结果，fixture最多证明客户端协议次序 | 真实远端并发原子去重与单一业务效果：NOT_RUN |
| HTTP04-A8 | PARTIAL_PASS | 熔断不生成新operation，开路不丢Unknown，探测次数受底层策略控制 | 真实Owner并发/半开和DLQ治理 |
| HTTP04-A9 | PARTIAL_PASS | 一个v2总预算与retry owner，拒绝冲突/叠加注册，库内attempt有界 | Host、SDK、gateway、sidecar、worker实际多层清单与总上界 |
| HTTP04-A10 | PARTIAL_PASS | 有历史read权但无当前new-effect权时允许原查询而不授新写的fixture | 真实Owner最终权限共同fence和字段mask |
| HTTP05-A1 | NOT_RUN | 交付分列源码/包/Host/配置/端点/运行/业务/独立接受状态 | 真实Host八层采用证据与包供应链 |
| HTTP05-A2 | PARTIAL_PASS | 不改CRM或旧client默认，NonIdempotent零重试规则供显式消费 | CRM拆分OIDC只读与code兑换、原安全选项端到端验证 |
| HTTP05-A3 | PARTIAL_PASS | 旧认证回归、显式安全writer和client注册入口 | 每Host实际中间件顺序与service/browser绑定 |
| HTTP05-A4 | PARTIAL_PASS | 新增版本入口保留旧writer/client合同，安全字段单独启用 | 真实客户端兼容窗口和切换接受 |
| HTTP05-A5 | PARTIAL_PASS | 原结果读权与产生新效果的当前权威判定分层 | 各Owner真实历史operation读取策略 |
| HTTP05-A6 | PARTIAL_PASS | 无可信proof的写retry关闭，错误policy未绑定时不披露字段 | 全部Required-HTTP门由实际Host逐项关闭/验收 |
| HTTP05-A7 | PARTIAL_PASS | 本任务无切流/回退/Main内部传输变更，库内身份/epoch固定 | 真实单route切换/回退、历史可读、无双发 |
| HTTP05-A8 | NOT_RUN | 全部fixture、loopback、本地库结果与生产采用明确分开记录 | 真实Host授权环境运行及独立业务接受：NOT_RUN |
