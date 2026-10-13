# S6｜Main–CRM 身份投影与集成异常恢复

版本：CP6-INT-CROSS-OWNER-20261007-CANDIDATE-v0.2  
修订范围：独审RETURN B01–B04及直接传播；N01–N03同轮小修；原v0.1/审阅报告冻结，70AC身份不变
文书身份：INT-ID-01 / INT-OPS-01 原范围的完整 Markdown 开发设计候选  
状态：CANDIDATE；作者冻结时点及 STOPPED 见 CURRENT.json；独立审阅 PENDING；接受 NOT_ACCEPTED  
实施授权：false；新增验收用例均 NOT_RUN；真实 Owner 接入、部署及生产采用均 UNPROVEN

## 0. 阅读顺序、真实性与边界

本文是本组唯一完整正文。索引、矩阵、任务、AC 只定位本文，不另复制第二份权威合同。先读第1–3节公共约束，再按第4节 INT-ID 五个原 SPEC、第5节 INT-OPS 五个原 SPEC实施拆分，第6–9节处理跨组依赖、兼容、开发和验收。任何实施均须另获授权。本稿不是已经运行的软件，也不是对原 Excel 母版的替代验收。

原册名保持 INT-ID-01_機能詳細設計書.xlsx、INT-OPS-01_機能詳細設計書.xlsx。没有取得 MSBBPA010 原 Excel 母版及两册 native 实例，不制造空白 Excel 冒充同风格原件。Required-NATIVE-01 明确保留。原 SPEC、Catalogue target_intent/subscopes、Plan current/H/SC/DP 全部在 ORIGINAL-SCOPE.json 精确保留，不因本稿模块合册丢失身份。

Owner 不变：Main IAM 持有租户、用户、组织和权威权限；CRM 持本地会话、身份投影及自己业务结果；各接收业务 Owner 持原请求判断、幂等、业务事实和原回执；Platform 提供上下文、签名验证、Inbox/Outbox、租约、重试、技术死信和遥测原语，不成为身份或业务主数据库。S6唯一拟稿跨仓接入/恢复合同；新增§3.3–3.7只闭合本次四根，不扩Target；SYS-IAM/TENANT/AUD/PRIV由S5确认政策，PLT-*由S7确认原语，业务消费方确认采纳。未收到确认不能填“已接入”。

不要求 Main 内部全面消息化；原本地 Bridge 继续存在。不得开两个消费者对同一对象双写。不得将 messageId 当作业务操作键。重放不是重新授权、不是修改原 payload、不是补造源事实。V2、T06、old-WP0冻结，ERP-CREDIT-01主链文书及母版未触碰。

### 0.1 四库实际远端观察

观察：2026-10-07 15:33:37–15:34:01 UTC；方法为当前 GitHub 只读连接器 GET 仓库 metadata 和 branch HEAD，不是旧本地 checkout 或 clone。当前执行器没有本组源代码 checkout，local HEAD=NOT_APPLICABLE_NO_CHECKOUT。v0.2依本次定点修订授权精确复用这些固定SHA；上述是15:33–15:34观察，不声称16:21以后重新取得最新HEAD。

| 仓库 | 实际默认分支 | 读取用完整 SHA | main/协调说明 |
|---|---|---|---|
| GTX537/CP6 | main | 90c871fe571fd6b390f53e8678376d7ce60bcb60 | main 存在 |
| GTX537/CP6.CRM | main | c778a3052a4416b82facb07b1244fc98fb6ad8a1 | main 存在 |
| GTX537/CP6.Platform | main | 30bd23af6808d217a23878bd9437513043c52834 | main 存在 |
| GTX537/CP6.AI-Handoff | codex/t02c-entry-handoff-20260921 | c5af26da818151eb362455b8709c53b43fe004c2 | main 实查404 Branch not found |
| 同上协调分支 | codex/plan-reuse-01b-20260928 | ad82a18c9511c3a8d3387060a9e2ed4755381e1d | 范围及Registry专用基线，不混称默认HEAD |

与启动包11:57历史观察对应SHA相同；与Catalogue历史Main current审计的157630594e3371fe181955d2f6227ff3b6962c84不同。本稿源核以90c871…为准，但范围不静默重基。Main新增持久generation和多数据库适配属于静态较深证据的 BASELINE_CORRECTION，不增加设计接受分数，也不将旧测试结果改成本轮运行结果。

Catalogue 379778 bytes / SHA256 47dfa72e9b27c7c76d2a12ea2c886024958babe02d505718787854c185a4d095；Plan 391642 bytes / e6f960f933f2e03c457ced8d927eaa2bf3c660a1c18587a5d8c465259de5b508；Registry84 889959 bytes / 31271d960d5c9e4533b5939a621dc28440cbbe7c600203bccac7e2e6ea7d8fb7。三份全文均从协调SHA重取，落盘字节核对一致。Registry在本稿只读；未找到本组两项独立accepted正文，本组不自建接受记录。

### 0.2 原范围，不扩成新IAM平台

| Target | 原Catalogue/Plan定位 | 原Owner/current | 原接口与评审参数 |
|---|---|---|---|
| INT-ID-01 | target_candidates[106] / books[106] | Main身份权威 / CRM投影Owner；CUR-CRM-010 | SYS-IAM-01、PLT-CTX-01、PLT-DEL-01；H36；SC24；DP10/12/16/17 |
| INT-OPS-01 | target_candidates[108] / books[108] | 各接收业务Owner；CUR-MAIN-SYS-017 | INT-COM-01、INT-ID-01、PLT-DEL-01；原H/SC为空，保持为空；DP10/12/16/17 |

两项原lane均共享支撑，DG13，REWIRE；原status PLANNED_NOT_CREATED/TO_AUTHOR 是历史原册状态，不证明没有代码。本文后续AC为本组新设计，不给INT-OPS补造原H/SC。DG13另有H03/H06/H08、SC02/18/23/24及PLM-AC-13/19的组级规则，只有具体命中才作为依赖，不擅自写回两册原Plan。

## 1. 真实代码依据与复用/缺口

以下E编号是审阅引用。完整repo/SHA/path/blob/local bytes/hash在 SOURCE-READ-INDEX.json；sources为GitHub原文.txt读证，不是可运行补丁。取得全文与实际逐段审阅分列；大文件仅按标注区间作结论，不能称全库审计。没有运行源代码、测试、迁移、schema校验、CI或数据库。

| 证据 | 固定SHA路径与定位 | 已见事实及复用 | 仍不能证明 |
|---|---|---|---|
| E01 | Main CP6.Core/Services/CrmIdentity/IdentityEventContracts.cs L12–33,52–62 | 固定topic、五种身份事件、租户/aggregate/version/相关键、原数据DTO | 生产Topic部署、所有租户启用 |
| E02 | Main IdentityChangeCapture.cs L17–111；CP6Context.cs L2780–2853 | 用户/角色/部门/权限/会话族变更捕获，业务与身份及审计同事务/保存点 | 所有非EF或批量写入口已纳入；需S5完整writer清单 |
| E03 | Main IdentitySnapshotWriter.cs L18–151,199–205 | 锁住原aggregate，最终事实生成快照；hash相同不加版本；禁用/token撤权进入优先队列，调用方事务 | 真实并发和故障下的运行保证 |
| E04 | Main IdentitySnapshotReader.cs L21–79；CrmIdentityController.cs L23–79 | versions/snapshot、tenant-bound受保护cursor、10分钟、pageSize1–200、generation变返回409；服务client显式授权 | 实际服务注册、跨仓基线跑通 |
| E05 | Main CrmIdentityTenantGenerationV1迁移 L13–32 | TenantId主键、非负Generation、seed/trigger、forward-only | 迁移在目标数据库执行或触发器覆盖所有writer |
| E06 | Main CrmIdentityConfiguration.cs L15–77；Program.cs L86,95,527,781–795 | 条件绑定/注册，独立C02.Services认证profile，原默认scheme保留 | 运行配置Enabled=true；源码注册不等于host当前启用 |
| E07 | Main IdentityEventDispatchWorker.cs L14–25,44–90 | 普通/优先独立循环，batch32/16、lease5分钟、max10，原CloudEvent bytes发布 | broker、运行告警及撤权延迟SLO |
| E08 | Main CrmOidcDirectory.cs L29–35,94–154；PermissionAggregator.cs L18–29,73–113 | slug→Main tenant；在线active身份；停用角色排除；action并集、scope MAX、字段显式MIN | 部门集合任意并集语义；逐字段在线DTO交付 |
| E09 | CRM CrmAuthenticationService.cs L37–117,120–180 | 一次性登录state、服务端会话，每请求online reconcile，再投影校验；401/403删会话，503不当撤权 | 每个业务入口均在同一请求时点完成字段级授权 |
| E10 | CRM IdentityAuthorizationPolicy.cs L15–79,82–130；Reader.cs L27–60 | baseline20分钟、grant交集、部门收窄、依赖健康、无跨请求allow-cache、published核对 | fields逐字段执行，不是仅凭存在FieldsJson完成 |
| E11 | CRM IdentityProjectionStore.cs L25–81,84–136；IdentityEventConsumer.cs L36–95 | Store局部具备老版本忽略、同版异hash/缺口标漂移，实际进入回调后同事务投影/会话删除/published | Platform版本checkpoint早退可能不进入Store；端到端同版冲突判定UNPROVEN |
| E12 | CRM ReconciliationWorker.cs L20–65,84–168；Client.cs L24–116 | 租约、分页baseline、逐项一致、最终重读boundary+目录、15分钟间隔、失败退避5–300秒 | 把Main boundary解释为CRM自算数字或自造全局时钟 |
| E13 | CRM IdentityPublishedCompiler.cs L17–101；ProjectionContext.cs；三份identity migrations | 原投影派生published图、依赖齐全、FieldsJson、nullable真实撤权origin | published表成为新权威、旧缺失origin被推断补齐 |
| E14 | CRM CrmActorContext.cs L4–13；CrmBusinessService.Views.cs L13–68；BusinessService.cs L209–241,298–325,366 | 真实动作/行范围、view-pii掩码、原命令重放读当前可见性；Actor无字段集合 | Main逐字段readonly/hidden从传输到CRUD/导出全链执行 |
| E15 | Main FieldPermService.cs L12–53；CurrentPermissionContext.cs L17–20,51–92 | 字段1读写/2只读/3隐藏；反射掩码及原值恢复；30分钟滑动权限缓存、invalidate API | 每一授权写入口调用失效；不把滑动30分钟称撤权硬上限 |
| E16 | Main IntegrationEvent.cs L24–127；BridgeHookBase.cs L32–151 | 原Bridge ledger、PENDING/SUCCESS/SKIPPED/FAILED/DEAD/COMPENSATED；持久化失败best-effort日志 | 所有Bridge事实和ledger原子；无行不证明未执行 |
| E17 | Main IntegrationEventDispatcher.cs L18–84,121–155；RetryWorker.cs L77–197及Space分支 | 原路由复用；Space persistEvent=false及retry fence，非Space维持旧状态机 | 所有路由统一lease/receipt或bool=false可准确分类 |
| E18 | Main BridgeHealthService.cs L17–102；Controller.cs L8–44；BridgeHealthView.vue L75–205 | 现有指标/最近10死信；compensate只改状态/操作者/时间，页面显示成功 | 业务补偿已做、原结果一致、具体重放权/当前权限 |
| E19 | Main ErpReplayController.cs L11–64；ErpInboxReplayService.cs L11–90 | 原ERP deadletters、Admin人类actor、operationId/rowVersion/hash、原消息只调度、审计同事务 | 跨Owner通用重放资格和现权限政策已统一 |
| E20 | Main ErpDeliveryReplayController.cs L11–69；Service.cs L21–210 | 结果Outbox与post-commit bridge不同重排；原消息/原订单保留；active tenant/lease/precondition | delivery成功即订单新建或接收方已应用 |
| E21 | Main ErpRequestHandler.cs L48–121,165–228,241–263 | 原request journal按tenant/kind/aggregate/requestVersion，InputSha256冲突，终态原结果复用，Inbox/business/Outbox同事务 | 所有Owner均有通用原回执API；InputSha256等同raw payload hash |
| E22 | Main ErpReadService.cs L7–55；ERP配置 L23–67 | 业务对象read含原RequestId/Version；ERP replay/worker条件注册 | 仅对象当前值足以证明某原请求未执行 |
| E23 | Platform Cp6InboxProcessor.cs L31–128,132–224,226–268；OutboxDispatcher.cs L32–80 | 传输去重/版本checkpoint/事务回调/重试；公开requeue原语只处理技术队列 | 业务操作幂等、应用鉴权/审计、可直接给最终用户执行 |
| E24 | Platform RetentionService.cs L17–47；TransactionalMessagingContractTests.cs L9–20 | 默认published7天、processed30天、deadletter90天；技术清理原语 | 满足凭证/审计法定政策；未决UNKNOWN可安全清理 |
| E25 | Main和CRM contracts/events/platform/contract-bundle.v1.json | 两边均blob ac1bf0c31b5f7302dfea4994f0d3be57617bdf19 | 仓库相同不代表运行资产一致；未运行schema validator |
| E26 | Main CrmIdentityChangeCapture/Contract/RevocationTests；CRM IdentityProjection/RevocationOrigin/Authentication/ReconciliationClientTests；ERP ReplaySqlTests/DeliveryReplaySqlTests | 已有断言文本覆盖捕获、版本/tenant、origin、会话、同operation/原payload重放 | 本轮执行均NOT_RUN；不沿用旧报告PASS |
| E27 | CRM CrmIdentityProjectionExtensions.cs L15–83；Program.cs | 条件consumer/reader/reconcile接线；sidecar来源/region/topic/partition校验 | 已部署组件配置、确切tenant清单及真实收到事件 |
| E28 | CRM CrmPageState.tsx L22–45,51–115；CrmEndpoints.cs L38–64 | 现成Forbidden/Conflict/Unavailable与workspace动作/业务API | 本稿新增诊断/重放台已经有页面 |

### 1.1 明确缺口登记

G01 字段权链：身份事件有Fields，published有FieldsJson；在线CrmContextDto、CrmActorContext及已读业务mapping没有携带任意字段权限，本地仅view-pii策略已证。设计补字段决策和field映射回执；不能以“有FieldPermService”判CRM完成。
G02 权限失效：Main滑动缓存的invalidate能力已证，但本轮未逐一重验所有Main user/role/menu/dept写入口；S5须给writer/epoch/invalidation矩阵。CRM不得以此缓存延迟放宽在线+投影交集。
G03 运维旧终态：COMPENSATED现状仅人工标记；迁移展示为“历史已标记，恢复凭据未核”，新增结果对账回执后才可认定业务恢复完成，原状态字节不回写。
G04 幂等域：messageId、operator replay operationId、Owner businessOperationKey、identity aggregateVersion、reconcile boundary相互独立，现Platform原语不替代业务键。
G05 原结果查询：ERP内部journal已存在，读取业务对象不等于按原操作查询；通用receipt查询端点本轮未定位，拟由Owner在现服务内增加受控查询adapter。
G06 通用资格：ERP现原消息重排较完整，旧Bridge/通用Platform尚无统一应用权限与Owner资格凭据；设计按owner adapter渐进接入，不建第二份业务账。
G07 配置/运行：所有真实tenant/issuer/client注册、迁移状态、生产消费者唯一性、S5/S7确认均Required/UNPROVEN。
G08 留存：Platform默认时长不是业务留存许可；未决回执/审计/撤权tombstone/业务凭证须由Owner分类，不能先清理再推断未执行。
G09 冲突路径：现Platform checkpoint可在CRM Store前返回过时版本，局部Store分支不证明端到端检测。v0.2已在§3.5选定CRM持事务的ICp6InboxPreCheckpointV1 seam、共同锁序、封闭结果、冲突/DRIFT/published同commit和Main基线恢复；ID10为明确接入任务。真实实现/采用仍Required/UNPROVEN，缺能力关闭新授权/依赖新写。未做动态复现。

## 2. 公共合同 v0.2：身份、键、状态和权限

本节为INT-ID/INT-OPS唯一公共拟稿。逻辑DTO名和新增路径均标CANDIDATE，未创建类型/表/权限码或发布schema。当前事件五种v1、topic及原claim保持原义。文书v0.2不是冻结软件V2，不自动触发服务或客户端升级。

### 2.1 对象与唯一写入权

| 对象 | 唯一写Owner | 唯一键/边界 | 不可从外部接受的字段 |
|---|---|---|---|
| Tenant/organization映射 | Main租户及IAM配置Owner | Main TenantId GUID；organizationId必须等值；issuer/region/slug显式注册 | UI tenantId替换、任意issuer、默认tenant、跨region回退 |
| User/Department/RoleGrant | Main IAM | tenant+稳定user/dept/role aggregate；删除保tombstone | CRM本地新增主身份、从displayName推ID、未授权grant |
| IdentitySnapshot | Main快照writer | tenant+aggregateId；单调int版本；data bytes hash | consumer重编版本、同版本新授权 |
| IdentityProjection/Published | CRM | tenant+aggregateId；原eventType/version/hash/tombstone | 当作主账回写Main、把published缺项补默认 |
| BrowserSession | CRM会话Owner | 服务端随机cookie的hash；tenant+subject+issuer+jti+epoch绑定 | 浏览器Actor/role/tenant覆盖、延长token寿命 |
| OwnerOperationReceipt | 各业务Owner原journal/回执adapter | tenant+Owner+businessOperationKey+原requestVersion；原input身份 | 由DLQ计数生成success或NOT_APPLIED |
| Delivery/Inbox/Outbox | 应用持表、Platform原语操作 | consumer+messageId或既有outboxId；source/target/partition | 用transport key覆盖原businessOperationKey |
| ReplayAudit | 接收Owner原审计/新增受控adapter | tenant+replayOperationId；固定input tuple | actor伪装、修改原理由/原摘要、覆盖旧attempt |

### 2.2 原身份与摘要域

1. originalRequestRef：源业务原请求ID/版本、Owner、原业务键；缺失不从messageId猜。Replay申请具有独立replayOperationId，只识别本次运维操作。
2. originalMessageRef：topic、consumer、messageId、aggregateId/version、原方向和原队列行；重复运输与新业务请求分开。保原correlationId/causationId；新增attemptId用于观测，不替换原因果链。
3. nativeEvidenceRef：原文件/回执/报文的出处、版本、bytes/hash、权限范围，记录原引用原样。Library/Markdown是存储或交付容器，不改变native身份。
4. rawEnvelopeSha256：仅对确有原CloudEvent载体的请求/结果分支实际UTF-8 bytes；bridge无此字段，其身份摘要domain/codec单列见§3.3；dataPayloadSha256：对原data JSON原文bytes。二者不得互填。Main snapshot与CRMFromEvent使用data原文hash；传输包装不得重新序列化data来假装一致。
5. canonicalInputDigest：只按特定Owner已版本化codec算，例如ERP订单ExpectedAmount的G29口径；必须带digestDomain和codecVersion。raw不同与canonical相同可以并存，不能擅自全局归一化。
6. rowVersion：native并发标记。现ERP byte[8]经JSON为base64，保持原序列；不转时间戳、不当业务版本。boundary：Main持久generation输出的opaque string；CRM只比较等值并随cursor传递，不解析为自己的全局水位。
7. originStatus取RECEIVED_EVENT、SOURCE_SNAPSHOT、LEGACY_UNKNOWN三类。来源未知的历史撤权不从expiresAt减去某个时长反推revokedAt；对账包装只用于验证，不能登记成已收消息。

### 2.3 Actor及授权矩阵

权限名称以下为逻辑能力候选，实际映射由S5和相应业务Owner确认。现ERP接口的Roles="1,Admin"是已读事实，不把候选码声称已部署。

| Actor | 允许动作 | 强制条件 | 禁止默认取得 |
|---|---|---|---|
| 租户IAM管理员 | 在Main编辑本租户组织/权限/禁用；查看本租户同步状态 | Main既有用户/角色/tenant策略及并发版本；变更审计 | 直接写CRM投影、跨租户重放 |
| CRM普通用户/主管 | 登录、自己会话退出；业务操作；可见范围内结果查询 | online actor ∩ 当前健康投影 ∩ 对象行范围 ∩ 字段策略；token/epoch有效 | 运维死信、payload、grant编辑 |
| 身份投影服务client | Main versions/snapshot只读，CRM消费固定身份topic | 已注册client/tenant/issuer/region；服务token记录有效；sidecar验证 | 人类运维POST、代用户业务决策 |
| 集成只读支持人员 | 本授权租户/Owner元数据、安全码、回执状态 | integration.query候选；对象可见性和字段掩码 | 原payload、令牌、PII自由文本、重排 |
| 业务Owner恢复操作员 | 查看资格、按固定原请求申请一次重排、查询该申请 | integration.replay候选+目标业务恢复权；当前租户/授权/状态；replay precondition | 改原payload/终态、已撤权身份续权 |
| 审计/留存Owner | 看授权范围audit与内容保留依据 | 独立audit.read；敏感证据另授权、按需脱敏 | 强制改结果、删除未决证据 |
| Worker | 按已记录Owner命令执行、写技术attempt | 固定service identity+route epoch+lease+当前Owner资格 | 沿用历史用户的显示名作为当前授权 |

统一分支顺序：当前认证→服务端tenant绑定及当前历史读/字段范围→定位原operation。已有同义operation按§3.4只回原结果，异义冲突；未有才取§3.6动作对应最终门，再核完整新调度资格和当前原生版本。错误不泄露跨租户对象存在性；重复查询只重新判断本次可见性，不要求新写权/旧eligibility未过期。过去成功的回执可保存，但返回字段按当前权限投影，不把历史内容原样泄出。严禁将失败默认为匿名、默认租户或全读写。

字段策略消费设计：构造每resource/action的字段决策对象，带policyVersion、源role版本集合、资源字段映射版本、expiresAt/decisionId。Main现行显式角色字段合并MIN（1读写、2只读、3隐藏）保留；没有字段配置的默认语义必须由S5明确，不能从“字典缺key”推断跨仓授权。已接入CRM某resource时，未知字段名/未知Access/缺必须policy依赖返回配置未就绪而非扩大权限。资源Owner维护Main字段名→API DTO字段（含嵌套/集合/导出列/搜索索引）精确映射。读时隐藏字段不发送；兼容旧default/null语义须单列，不能把被隐去的数值0当真实业务值。写时对只读/隐藏字段出现不允许变更，返回字段级403或受控错误而非悄悄部分成功；旧Main StripReadOnly的静默恢复行为保留在旧接口映射，变更它由S5另审。PII总开关与逐字段决策取更严格交集，未通过不得用自定义字段配置恢复已匿名化资料。

### 2.4 状态不得互相折叠

身份状态：UNCONFIGURED→BOOTSTRAPPING→READY；任何必要版本/祖先/角色缺失进入DRIFT/UNAVAILABLE；tenant/user已禁用、token撤销或epoch变化为DENIED；对账成功只恢复同步健康，不撤销权威禁用。READY同时需要当前身份事实、可信版本/摘要及有效baseline，不能仅看最后任务成功时间。

运维记录显示两个正交状态：
- deliveryState：待发、已发送、处理中、可重试技术失败、死信、已重排；技术已发送只证明publisher步骤
- ownerOutcome：NOT_OBSERVED、PROCESSING、APPLIED、REJECTED_TERMINAL、NOT_APPLIED_PROVEN、UNKNOWN、SUPERSEDED、RESULT_CONFLICT

UNKNOWN是提交/回执不确定，不是失败，也不是“允许重新执行”。查询Owner原操作后：APPLIED复用原结果；REJECTED_TERMINAL展示原拒绝；PROCESSING等待并显示原操作；NOT_APPLIED_PROVEN且资格仍有效才可安排原请求重放；UNKNOWN或结果矛盾继续隔离/调查。NOT_FOUND不是NOT_APPLIED证明，尤其原回执已清理、只读副本延迟、旧Bridge best-effort无ledger的情况。

UI“已重排”须同时显示仍未确认的ownerOutcome；“业务已完成”需要Owner terminal回执。旧COMPENSATED只作为legacy native status显示，旁边标“历史人工标记，恢复未核”；不得迁移成APPLIED或证明已补偿。

## 3. API、DTO与事务共同约束

### 3.1 已存在接口，保留原契约

| 接口 | Owner/实读实现 | 入参与结果 | 重要错误语义 |
|---|---|---|---|
| GET /internal/crm/identity/versions | Main/E04 | cursor可空、pageSize1–200；tenant由token；返回TenantId/Boundary/Items/NextCursor | 400参数/cursor，409边界变化，503bootstrap/store未就绪，401/403身份 |
| GET /internal/crm/identity/snapshots/{aggregateId} | Main/E04 | 无tenant query；aggregate长度1–128限定字符；返回tenant/type/version/hash/deleted/data | 404原快照不存在；不得转为空授权 |
| POST /internal/crm/identity/events | CRM/E27 | 固定topic/sidecar/partition，结构化事件；SUCCESS/RETRY/DROP | SUCCESS是消费层结果，不是Main身份重新授权 |
| CRM认证与 /api/crm/v1/workspace | CRM/E09/E28 | 服务端session；workspace含actions/tenant/subject与CSRF | 401会话失效，403业务拒绝，503身份暂不可用，响应no-store |
| GET /api/erp-integration/deadletters | Main ERP/E19 | 当前tenant，最多100个CommandInbox metadata | 读取不证明可重放；须补稳定分页和权限确认 |
| POST /api/erp-integration/deadletters/{messageId}/replay | Main ERP/E19 | ErpReplayRequest(OperationId, RowVersion[8], PayloadSha256[64], ReasonCode) | 202scheduled；403tenant；404缺件；409原版本/operation冲突/非死信 |
| GET /api/erp-integration/delivery-deadletters | Main ERP/E20 | kind=result/bridge，targetId，messageId，attempt/hash/error/time/rowVersion | result与bridge原语不可互换 |
| POST …/delivery-deadletters/results/{outboxId}/replay | Main ERP/E20 | 同ErpReplayRequest，复用原结果事件 | 不创建业务订单、不改结果版本 |
| POST …/delivery-deadletters/bridges/{orderKey}/replay | Main ERP/E20 | 同ErpReplayRequest，原已提交订单/无active lease | 不重新创建原订单 |
| GET /api/bridge-health/metrics；POST …/compensate/{eventId} | Main/E18 | 旧指标/旧人工状态标记 | 保历史记录；新UX不称实际恢复完成 |

ErpReplayRequest reason当前仅dependency-recovered或contract-verified；不随意加入自由文本解释代替枚举。operator identity由服务端取得，客户端不提交actor或tenant。输入hash必须与原记录绑定，不是用户上传新的hash。返回202不能消除“提交结果未知”的客户端恢复流程。

### 3.2 候选新增应用层接口

下列为明确开发拆分目标，不是当前已存在API。以应用自身 /api/integration-ops/v1 为候选命名；Main/CRM在各自Host提供相同逻辑adapter，不能由Platform集中转写业务库。最终路由必须由Owner签认后入API清单；此刻Required-API-01=UNPROVEN。

1. GET /failures：只读 FailurePage；filters owner/route/kind/safeStatus/from/to及cursor；tenant从认证。pageSize默认50上限100，窗口最大31日；按timePartition（可信UTC/未知时间）、occurredAtUtc（未知为空且独立分区）、ownerId、nativeType、nativeId及稳定opaque failureRef构成唯一全序；签名cursor绑定tenant/actor/filters/routeVersion、snapshotAt与本次sourceSetVersion/可用Owner集合；这些是v0.2候选参数，不改旧接口上限。未知旧时间区不得伪标UTC，保rawTime+sourceTimeZone+normalizedTimeStatus。返回items、nextCursor、snapshotAt、sourceSetVersion、partial和unavailableOwners，不用空列表掩盖Owner故障。续页固定首次可用Owner集合；缺源恢复不插入旧分页，用户“重新查询全部来源”建立新snapshot/sourceSet并清旧页。既选Owner中途失联则续页返回SOURCE_SET_UNAVAILABLE且重启查询，不拼接部分漏项旧页。
2. GET /failures/{failureRef}：FailureDetail（只元数据、原身份、原因分类、当前Owner回执、权限范围）。failureRef为opaque locator并再查授权，不能承载可任意指向其他库的URL/SQL/路径。
3. POST /failures/{failureRef}/eligibility：只读业务资格评估，可记录查询审计但不调度。入参allowedAction/nativeQueueRef、expectedNativeVersion、expectedDigest(domain/codecVersion/value)，按§3.3封闭分支校验。返回EligibilityDecision（Eligible/Blocked/PendingOwner/Unknown）、reasonCodes、ownerReceiptRef、decisionVersion、expiresAt、expectedCurrentAuthVersion（仅诊断）、allowedAction及exactInputTuple（§3.4完整原意图元组，不以摘要替代）。decision是短时前置视图；执行时必须重验，不能只信前端传回“Eligible”。候选有效期2分钟，只缩短不替代Owner版本/事务锁。
4. POST /failures/{failureRef}/replays：ReplayRequest，申请一次原请求/原结果/原post-commit桥中的一种恢复。字段replayOperationId（GUID必填）、allowedAction与nativeQueueRef（必填）、expectedNativeVersion（opaque必填）、expectedDigest{domain,codecVersion,value}（必填）、reasonCode、eligibilityRef、inputContractVersion（ReplayRequest.v1）。三分支必需域及null规则唯一见§3.3，bridge不得填伪raw envelope hash。不接受replacementPayload/newTenant/force/skipValidation。三种allowedAction是封闭枚举，固定nativeType/摘要域映射；不得靠缺字段猜分支。对已接纳operation的同义回查先走§3.4，不重判旧eligibility有效期。
5. GET /replays/{replayOperationId}：ReplayReceipt；按本tenant绑定+当前历史结果读取权限查询，返回Scheduled/Running/Completed/Denied/Unknown与原Owner结果独立字段。不因客户端已timeout重新发第二operation。
6. GET /owner-results/{operationRef}：OwnerResultQuery adapter；从原Owner journal取原request identity及结果，不计算副本“像成功”。opRef映射完全由服务端授权表决定。未知/迁移缺证明确返回UNKNOWN，不自动提交。

FailureDetail字段：failureRef、ownerId、routeId/routeEpoch、nativeType/nativeId/nativeStatus/nativeVersion、tenantRef（受控显示）、originalMessageRef、originalRequestRef、replayEvidence[{allowedAction,nativeQueueRef,expectedDigest(domain,codecVersion,value)}]、failureClass/safeCode、firstSeen/lastAttempt/nextAttempt、deliveryState、ownerOutcome、ownerReceiptRef/version、attemptCount、availableActions、ineligibilityReasons、evidenceState。敏感字段默认为不返回，原payload单独受控取证接口不在此稿自动开放。

ReplayReceipt字段：replayOperationId、allowedAction、nativeQueueRef、expectedDigest(domain/codec/value)、originalRequestRef、originalMessageRef、acceptedAtUtc、acceptedBy、scheduledAttemptRef、nativeVersionBefore/After、status、ownerOutcome、ownerReceiptRef/version、resultProjectionRef、safeCode、retryAfter、auditRef、contractVersion。重排返回202带Location（由服务端生成，当前同站）及Receipt；同义重复先查原operation，按当前read mask返回200原Receipt/现执行状态，不验新调度条件；原提交进行中返回Processing/Unknown/RetryAfter并保持同ID。schema/输入400，未认证401，无权403或防枚举404，版本/幂等冲突409，资格未就绪409/PendingOwner，后端不可用503。仅当本轮确知所有事务已回滚且没有持久操作记录时才能返回可重试未接纳；否则503附operationId和UNKNOWN，前端转查询。

OwnerResultReceipt字段：ownerId、tenantBinding、businessOperationKey、originalRequestId/version、inputDigest+domain+codecVersion、receiptId、receiptVersion、terminalFlag、outcome、targetRefs（含原对象版本）、decidedAt、committedAtEvidence、originalResultNativeRef、projectionMaskVersion、retentionClass、schemaVersion。APPLIED要有Owner原提交凭据；NOT_APPLIED_PROVEN要有受控不存在/回滚证据及观察边界，不能仅“没有查到”。返回结果含历史事实和当前脱敏视图两个独立字段，不能为迎合当前页面改写原结果hash。

### 3.3 三种恢复动作的封闭输入、摘要及幂等映射（B01）

ReplayRequest V1为逻辑DTO版本，文书v0.2只澄清候选，不发布运行schema。必填字段为replayOperationId、allowedAction、nativeQueueRef、expectedNativeVersion、expectedDigest{domain,codecVersion,value}、reasonCode、eligibilityRef、inputContractVersion（ReplayRequest.v1）。allowedAction必须显式为下表三值之一；tenant/actor均由服务端绑定。originalRequestRef/businessOperationKey从原Owner登记解析，不接受客户端改写。raw envelope不存在的分支既不要求也不允许填伪造expectedRawEnvelopeHash。

| allowedAction | 固定原对象及message条件 | expectedDigest的唯一domain/codec | 新调度必须核验的内容 | 原ErpReplayRequest映射 |
|---|---|---|---|---|
| REPLAY_ORIGINAL_REQUEST | nativeType=ErpCommandInbox，nativeId/messageId为原命令；原structured CloudEvent必须存在 | domain=cp6.cloud-event.raw-envelope.sha256；codecVersion=raw-utf8-v1；value=原envelope实际UTF-8 bytes SHA256 | tenant/message/type/aggregate/version/region/topic及原请求业务键；原payload/hash/schema；原Owner未应用证明与当前新作用资格 | input.PayloadSha256等于此raw摘要；messageId沿原path；rowVersion原byte[8]；operation/reason不变 |
| REDELIVER_ORIGINAL_RESULT | nativeType=ErpResultOutbox，nativeId=原outboxId；原messageId及结果envelope存在 | domain=cp6.cloud-event.raw-envelope.sha256；codecVersion=raw-utf8-v1；value=原结果envelope实际UTF-8 bytes SHA256 | 原result topic/schema/tenant/region/唯一未replayed deadletter；已提交Owner原结果及resultVersion，绝不新建原业务对象 | input.PayloadSha256等于原结果raw摘要；outboxId沿原path；rowVersion/operation/reason原义 |
| RESUME_POSTCOMMIT_BRIDGE | nativeType=ErpOrderBridgeDispatch，nativeId=原orderKey；messageId/envelopeRef必须null，不生成替代消息 | domain=cp6.erp.postcommit-bridge.identity.sha256；codecVersion=c03-bridge-key-v1；value=既有BridgeHash，原编码为UTF-8的c03:bridge:{tenant:D}:{orderKey} | 当前tenant、原orderKey、nativeVersion、原已提交订单/Owner操作绑定、原bridge未Completed/确实耗尽/无active lease；新下游效果另核§3.6最终门 | 旧input.PayloadSha256按既有BridgeHash传递；该旧字段名称不改变实际domain；bridge endpoint、rowVersion/operation/reason不变 |

摘要校验严分“载体内容”和“桥身份”：原请求/结果分支才要求payload原文及其schema；bridge分支无envelope，不做不存在的payload/schema条件。bridge身份摘要不能替代Owner原业务输入digest或证明其业务结果。若原业务键/原订单回执缺失，即使bridge身份摘要正确也PendingOwner；不得从hash反推输入内容。

EligibilityDecision、FailureDetail、ReplayRequest、ReplayReceipt、ReplayAudit统一保存allowedAction/nativeType/nativeRef/expectedDigest三元组，审计另保存各自的originalRequestRef及Owner原inputDigest（存在时带独立domain/codec；未知明确unknown）。旧接口adapter由已固定的endpoint/nativeType选择合法分支，不能因某字段缺失猜动作。动作与domain/codec/nativeType不匹配拒绝DIGEST_DOMAIN_ACTION_MISMATCH；合法domain但原value不同拒绝ORIGINAL_IDENTITY_CONFLICT；缺原载体或原业务映射拒绝ORIGINAL_EVIDENCE_UNAVAILABLE。拒绝无新调度、无业务效果。

纯文书三分支例（均NOT_RUN；仅合同示例，不是外部执行请求）：
1. 原命令死信A：输入REPLAY_ORIGINAL_REQUEST、A的message/nativeVersion及raw envelope摘要，Owner证明原请求未应用且当前资格有效，才调度原A。若把业务canonical digest放入raw域稳定拒绝。响应丢后按同replayOperationId回查原Receipt，A已改版不要求旧version再次满足新调度条件
2. 原成功结果Outbox B：输入REDELIVER_ORIGINAL_RESULT、B的outboxId/nativeVersion/原结果raw摘要；核Owner已提交结果及唯一原deadletter，仅重投B。若传bridge摘要域或换结果版本拒绝。回查命中原operation时返回同已接纳Receipt与当前投递状态，不再产生新Outbox
3. 原订单post-commit bridge C：输入RESUME_POSTCOMMIT_BRIDGE、C的orderKey/nativeVersion和c03-bridge-key-v1身份摘要，message/envelope为空；原订单/业务键成立及当前下游效果门满足才续原bridge。要求填raw摘要或自行生成messageId属于非法输入。相同operation再次查询保原bridge身份，不将其hash当原订单输入内容证明

### 3.4 原operation查询优先、仅新接纳走调度门（B02）

唯一路径先分读取既有事实与新增效果。此顺序约束所有POST replay adapter及其UI，不允许§5.3另写反向顺序。

1. 验证当前认证、服务端tenant绑定、该Owner原operation/结果的当前读取范围，以及基础输入语法。tenant绑定不是“tenant当前允许新业务”的同义词；暂停但仍获历史审计读权者可只读原记录。基础语法只核ID/编码/DTO可解释性，不要求旧eligibility未过期、旧queue状态仍DEAD、当前新写权或现在source在线
2. 以tenant+Owner+replayOperationId定位原持久operation。只读快照命中可先回原状态；需要与首次提交串行时，使用与新接纳一致的原Operation根/唯一约束锁。读取分支绝不转成调度。新接纳在最终安全门之后按同一Operation根再次检查，以解决并发首次提交，不能反向长期持Operation锁再等AuthorizationFence
3. 已有operation：比较其固定tuple与本次原意图；原request/message派生引用从已持久operation读取，不重新请求当前源/queue解析；failureRef只比已登记原locator，即使原队列归档也不要求它再次可重排。tuple为admittedActor身份、allowedAction、nativeType/nativeRef、原request/message引用、原requestVersion、expectedNativeVersion（按原记录比，非当前queue）、expectedDigest(domain/codec/value)、reasonCode及inputContractVersion；eligibilityRef的原身份亦保持，过期不改变已经接纳的事实。相同只读回当前可见的原Receipt/现执行状态，零新调度；不同409 OPERATION_ID_CONFLICT，原审计不覆盖。GET状态可由另一个有读权的支持员查询，不要求其冒充原admittedActor；POST同义复发仍核本次认证人是否与原申请tuple相符
4. 未有operation：才进入完整新调度路径。§3.6最终安全门→Operation根锁→再次回查。若竞争请求已提交，立即退到步骤3；仍不存在才核新动作权限、当前tenant启用、有效eligibility、Owner原结果、原queue/hash/nativeVersion/lease/route epoch、目标状态与业务用途，审计和调度同事务。短预检结果、二次GET不替代最终门。若新门DENIED/UNAVAILABLE，在最终返回前再只读查询同operation：竞争提交已经可见则按步骤3回原结果；仍无法判断时Processing/Unknown+RetryAfter，不能以新门不足宣称原operation不存在
5. 原operation为Scheduled/Running/Unknown仍可查。它若继续产生新的业务效果，原worker在实际效果事务重新消费§3.6门；读取其状态不授续写权。已有Terminal只回原结果，不能因系统恢复重执行
6. 首次提交尚在进行而读取没有稳定结论：返回202 Processing或503 OPERATION_OUTCOME_UNKNOWN，携相同operationId、statusLookupRef和RetryAfter；读锁超时不推定NOT_FOUND。未观察到row只表示NotFoundObserved，不能据此生成新ID；客户保持同ID查询/同义重试，由相同唯一根决定一次接纳

四种恢复情形：首次接纳但响应丢→回同operation；worker已改queue版本→按原tuple比、不验新queue前置；eligibility已过期→原事实照查、不重生成任务；已无新写权但有历史读权→只读原Receipt及脱敏结果，未开始/未完成的新效果仍由当前执行门阻断。各情形均不绕过当前读取权限。

新操作固定tuple与audit不可变；结果内容按当前字段政策裁剪，但原native结果hash/domain不变。原ERP服务当前先audit回查的语义保留，统一adapter不能在其前端插入会使原结果回查失效的新调度门。

### 3.5 身份Inbox checkpoint前的一致性接入合同（B03）

选定方式：CRM应用Owner持事务编排，Platform提供候选ICp6InboxPreCheckpointV1 / ProcessWithPreCheckpointV1技术seam，取代该接入调用现ProcessAsync自有事务后再best-effort标健康的路径。不是另建身份权威；接口尚未实现/采用，Required-PLT-01保持UNPROVEN。调用方已开启的CRM受控DB事务、已验证delivery和policy是必需输入；Platform不另开事务、不commit，所有“Duplicate/IgnoredOutOfOrder成功”决定必须先经此seam。不能把seam放在当前应用回调里而保留原checkpoint早退。

输入PreCheckpointIdentityInputV1：tenantId、consumerName、aggregateId、aggregateVersion、eventType、dataPayloadSha256（data原UTF-8片段）、rawEnvelopeSha256（原transport bytes）、originalMessageRef（message/topic/partition/source）、schema/bundle身份、region、validatedRegistrationRef、当前DB事务句柄/调用上下文。它来自已认证sidecar及完整合同校验，不能由普通UI直接提交，也不接受外部布尔“已验证”。data与raw摘要不互换。

可信比较源为CRM原Projection及其Main source lineage，不从传入未知事件建立“既有可信值”。辅助IdentityContentObservation记录tenant/consumer/aggregate/version/eventType/data摘要/origin/nativeRef，仅是不可变接收观察及冲突证据，不赋予授权，不写回Main。已有Projection必须过tenant、eventType、version、原payload hash和来源/健康检查。已有基线声明却缺当前可信版本锚点、来源损坏或新部署缺该能力均输出DependencyUnavailable，不默认一致。首个合法版本可走FreshCandidate接收分支，授权仍需完整baseline及依赖健康。

顺序与共同事务边界：
- 先验证真实服务注册/transport范围；应用在同接收事务持注册保护门，再按既有CRM调和顺序锁ReconciliationState→PublishedState→Projection(tenant,aggregate)，随后锁Inbox(consumer,message)→AggregateCheckpoint→本观察/冲突记录。对账与发布也遵循State→Published→Projection顺序；应用Owner负责逐入口统一，不让current ProcessAsync先持Inbox再反向等应用锁
- 在任何技术重复/版本早退前调用EvaluateContent；取同一锁下的原Projection/current source selection、观察摘要与Inbox原运输绑定。比较只读阶段不提交，返回以下封闭decision，由同事务ApplyDecision持久化
- TransportIdentityConflict：同consumer/messageId却raw envelope摘要或tenant/topic/partition/aggregate绑定不同，优先于任何Duplicate/Equal；隔离原运输冲突，只对可信原绑定的受影响aggregate按冲突分支标DRIFT，不以不可信输入指定另tenant数据。
- FreshCandidate：version高于原Projection或首次合法输入；应用Store保原版本/缺口规则，必要时NeedsReconciliation，发布模型同事务更新；不是直接授权
- TransportDuplicate：message绑定及raw一致，且应用层必要一致性已确定；不重做session业务效果。若原Projection已有更高健康版本，回重复/已过时并明确没有证明历史所有版本内容一致
- OlderVersionIgnored：incoming version确低于当前可信版本；只记录过时观察，不回退。若保存的同版本历史观察已有内容冲突则归SameVersionConflict，不以低版本名义抹除已知冲突
- SameVersionEqual：同tenant/aggregate/version/type且data摘要相同；message/raw可不同但各自运输身份必须自洽；只记一次相应观察/transport结果，不重复应用。原健康状态若DRIFT未解决，不因等值重复而清除
- SameVersionConflict：同一aggregate/version的eventType或data摘要不一致，或与同版本可信source selection不一致；追加冲突记录，Projection.NeedsReconciliation=true及DriftCode，受影响Published依赖撤出授权。冲突记录、健康标记、published不可用、Inbox冲突结果/对账调度在同一CRM事务提交，零新增allow。原内容保留，不采用到达更晚者
- DependencyUnavailable：可信原记录缺证、注册不允许、共同锁/事务能力不可用、校验依赖失效；不完成成功Inbox/checkpoint，不更新allow；返回可重试未就绪或明确拒绝。发生存储/提交不确定先查原Inbox/冲突记录，不能先发SUCCESS再期待后台标记

Platform对上述decision只执行技术去重/checkpoint处理。只有FreshCandidate应用成功或Duplicate/Older/Equal的一致性判定与必需观察记录已经同事务commit，才允许最终transport SUCCESS；它仍不代表身份有业务授权。SameVersionConflict/TransportIdentityConflict在持久隔离成功后返回明确CONFLICT_QUARANTINED，Dapr映射DROP及安全告警，不计成功采纳；DependencyUnavailable/事务失败映射RETRY，注册拒绝/格式非法按原拒绝路径DROP，无成功记录。任何IgnoredOutOfOrder都不得跳过PreCheckpointIdentityDecisionV1。

恢复：只由§4.5 Main可信snapshot+完整首尾baseline修复选择，锁同State→Published→Projection；追加AuthoritySelection与resolvedConflictRefs，保原冲突观察和source nativeRef，不覆盖历史原hash。完成时持当前source selection/version/CAS核未有新的未解决冲突；新冲突与Complete竞争只有一方先提交，后提交方按新版本重判，不能用过时baseline清DRIFT。

缺能力关闭：启动接入声明必须列consumer、seam版本、事务ownership、完整共同调用者和锁序/返回映射。未采用的接入不宣称具备此保证；相关身份新授权及依赖它的新业务执行返回IDENTITY_CONSISTENCY_GATE_UNAVAILABLE。可信运输事实在当前注册接收门允许时可持久为PendingInspection供只读核查，不能提前推进授权checkpoint/published allow。历史原结果查询按当前读权保留。实施和运行仍UNPROVEN，不要求现在改Platform。

### 3.6 消费既有Owner共同最终授权门（B04）

精确采用边界：OA-APP-01 accepted UA-20261001-B-OA-APP-MD01，原件180801 bytes/SHA256 0cc2a482fd1dc8790f19239f5965e437948c6c9c92929f94b4e32fdd6319891c，§15.1 L582–586的ScopeAuthorizationFence共同写者与外域不足关闭规则；INT-COM-01 accepted UA-20261001-A-INT-COM-MD01，原件冻结SHA256 5282d5545f8c9b1db11f649541ca35af8dd1d9df1264af8df6a4ed34f2fd0c63，R1§2.3–2.5 L619–647的本地当前IAM安全/字段门与业务用途grant分离。S6只消费其边界，不复制其整套审批/商业账，不将accepted文书等同真实运行采用。

选定逻辑调用OwnerFinalAuthorizationGateV1.EvaluateAndHold，由真正业务Owner实现其已有门adapter。输入：ownerId、authenticatedTenant/subject、actorKind（human/service）、originalRequesterRef、requestedAction、targetRef及expectedBusinessVersion、原operation identity、expectedPermissionEpoch、permissionDomain/scope、fieldPolicyVersion、实际业务事务上下文。全由可信认证/Owner原事实构造，不能相信payload自称tenant/epoch或短时eligibility。输出只三类：ALLOWED_HELD（准确fenceRef/epoch/安全字段决策/native proof与当前事务绑定）、DENIED（稳定code及当前policy版本）、UNAVAILABLE（AUTHORITY_FENCE_UNAVAILABLE及缺能力ref）。ALLOWED_HELD只有真实门已取得并持有到事务结束才成立，布尔/短期票据/二次GET都不是此证明。

同库唯一新写决定边界：依Owner采用的Scope+permissionDomain fence先获得本域授权锁，锁内判断当前主体/动作/对象/字段及expected epoch；再按原Owner锁序进入Operation根→原业务控制头/queue→业务状态→Receipt/Outbox→Audit。原Owner更细的Binding/Instance等顺序保留，S6不换序。业务完整写集、原operation结果、receipt/outbox及强制audit同事务提交，fence保持至commit/rollback。权限/角色/委派/SOD政策/租户停用与会影响该scope的撤权入口、worker、管理员恢复工具均须先持同fence并递增epoch；只给新adapter加锁而旧撤权旁路不参加不算采用。已完成撤权先持门提交时旧epoch新写拒绝；业务先合法持门并提交的历史结果保持，不把后来撤权改为业务未发生。

跨库明确关闭：Main当前在线GET/CRM投影读事务与CRM业务事务不同；CRM不能用自己的缓存epoch或本地queue锁约束Main另一库撤权。若依赖的安全权威没有被Owner实际采用的等价共同门协议，则受此依赖的新业务执行及会产生新下游业务效果的bridge步骤关闭，返回BLOCKED/AUTHORITY_FENCE_UNAVAILABLE，不进入业务写/新调度commit。保原operation可查；有既有合法持久意图时可保BLOCKED_WAITING_AUTHORITY状态，但不补造新业务效果。短TTL、lease、两次GET、用途保护grant都不能放宽这个关闭点。等价协议需Owner声明完整共同写者/撤权竞争语义/持久代次和原终态恢复，不在本稿另造第二套安全协议。

四类动作分别用门，不能把原人类新写权加到所有历史事实：
| 类别 | 当前门 | 门不足时 | 原事实与结果 |
|---|---|---|---|
| 已提交operation/回执的只读查询 | 当前认证、tenant绑定、历史结果read/字段/PII范围 | 无读权拒绝/脱敏；新写门不可用不阻合法历史只读 | 不要求原用户新写权、旧ETag、旧eligibility或源当前在线 |
| 可信事实持久接收/去重（尚不应用业务） | 接收Owner当前AppOwnerRegistration/transport范围共同接收门；identity另需§3.5内容门 | 未注册拒绝；可可信持久但内容未核只PendingInspection | 保存收到事实不代表APPLIED，不借此生成allow或业务效果 |
| 原已提交结果的技术重投/确认收尾 | 当前机器/操作员delivery范围及原结果证据；当地调度写按自己的授权fence | 对应delivery门缺失暂停重投，仍可查原已提交结果 | 不要求原人类新建订单权限，不重建业务；消费者新应用另核自己的门 |
| 原持久任务尚未完成的新业务效果/新replay/bridge业务步骤 | 真正效果Owner的OwnerFinalAuthorizationGateV1；若还需用途保护再消费既有INT-COM协议 | DENIED或AUTHORITY_FENCE_UNAVAILABLE阻新效果/新接纳 | 恢复按同operation/原输入重判；若已终态只读回原结果 |

租户暂停不是删除原证据的理由；历史read范围由当前政策明确决定。当前合法read成功也不是续写许可。准入能力恢复后worker取原operation、核原输入/目标版本/route epoch，再取当前门；不能生成新ID或恢复旧token来续写。任何已持久终态复用原结果。Required-ID-04/OPS-02和X02负责此精确adapter、共同写者与关闭分支；生产采用及测试仍UNPROVEN/NOT_RUN。

### 3.7 其余持久化、运输与业务幂等约束

- 不新增Platform业务主表；Owner已有journal优先做adapter，缺口只在本Owner库追加恢复operation/audit/receipt关联，不提供SQL或迁移代码
- businessOperationKey沿原业务已存在键；replayOperationId只标运维申请，messageId只标运输。业务输入修正走原业务界面新版本，并链接旧终态，不冒原样重放
- 外部broker不与业务DB共事务；本地business+Owner receipt+outbox同事务，broker at-least-once。发布后写Published不确定可重发，同一原业务键不得重复效果
- 所有同义重复先按§3.4回查；所有新调度及新业务效果按§3.6最终门；应用身份接收早退按§3.5内容门。三门不同，不用一个eligibility布尔替代
- 分库不声明同事务；接收方receipt+其本地效果按真实Owner事务，原请求UNKNOWN先查双方持久回执。原结果查询/gate不具备的route只读RequiredFenced

## 4. INT-ID-01：五项原SPEC完整设计

### 4.1 SPEC-INT-ID-01-01｜租户/组织映射

原需求：一个Main权威来源、一个CRM投影，不把Platform变成身份主业务数据库。证据E01/E04/E06/E08/E27；原H36/SC24、DP10/12/16/17。开发任务ID01、ID02、X01。

业务对象/Actor：Main Tenant、注册OrganizationMapping、Main Department树、CRM注册tenant/region及投影依赖图。Main管理员在已有租户/组织入口维护，CRM用户仅用slug选择登录组织，投影服务client只读本注册tenant。接入支持员看本授权租户诊断，无编辑grant能力。

字段与动作：TenantId/organizationId同一GUID（必填、服务端权威）；slug为现小写字母数字/连字符、1–63、仅解析入口，不当数据库键；region为显式登记值，不按IP推；issuer精确HTTPS origin；DeptId、ParentId、Path、LeaderId来自Main；Enabled/ExpiresAt来自权威状态；mappingRevision、ownerApprovalRef、effectiveAt为候选部署证据字段，不能伪装已有DB列。跨租户部门/leader引用无效；删除/停用保tombstone，不转移用户或历史业务记录所属tenant。

处理过程：
1. 管理员完成Main既有tenant/组织政策操作；S5提供同一变更的写入口/权限/并发版本。S6只消费结果
2. 启用映射前核Main OIDC organizations、identity tenant map、service client tenant及CRM storage organizations的tenant/region一致。任意缺项为UNCONFIGURED，不能自动补默认
3. CRM BeginAsync按slug向Main解析有效tenant，返回organizationId/region并绑定登录transaction；回调token主体/tenant必须与transaction一致
4. Main变更生成对应tenant/department快照，CRM原投影接收；依赖完整后发布读模型。祖先缺失/停用，受影响descendant不产生授权；无关可选目录缺失仅移除对应选项
5. 改slug不改TenantId；改region/issuer不是普通展示改名，必须新映射版本、兼容窗口及会话重建评估，未批准时阻断切换

状态与幂等：映射未就绪→核对完成→bootstrap中→READY；停用→DENIED，恢复Main Enabled也要新有效版本、全依赖健康及重新认证，不能恢复旧session。映射比较用精确身份与版本，重复同版本同步不创建第二组织；同版本不同内容进CONFLICT，人工核权威来源。

API/DTO：沿用versions/snapshot及Main解析返回三字段organizationId/organizationSlug/region（CrmEnabled）。候选诊断 MappingView 含sourceTenantRef、localOrganizationId、issuer、region、mappingRevision、sourceVersion、projectionVersion、dependencyStatus、lastVerifiedAt，全部只读；只有Main管理链接可跳原配置页，不能由此页提交任意tenant map。

FE：登录组织输入框/错误文字不暴露禁用组织详情；已登录切换组织先结束本地session再走新绑定。同步诊断显示“未配置/初始化/可用/不同步/已禁用”并把source与projection分列；部门树只显示已授权目录。保存跨region/issuer草稿需明确影响会话和Owner确认；实际配置修改不在本轮授权。

失败与恢复：映射缺失404组织不可用；服务不可用503保错误状态，不退到其他组织；bound cursor换tenant无效；dept依赖冲突由Main修正并新版本发布，不在CRM手工补节点。既有跨租户历史数据处理为单独Owner迁移任务，不在重放中重绑。

AC（全部NOT_RUN）：
- AC-ID01-01：同tenant/region/issuer完整注册后，登录transaction与snapshot投影身份一致；取得三方注册与运行读证才判成立
- AC-ID01-02：缺映射、空tenant或region不一致时，状态未就绪，无默认组织或新本地主身份
- AC-ID01-03：源部门祖先缺失/停用时，依赖用户不获取该部门范围；不相关可选目录不扩大权限
- AC-ID01-04：同TenantId变slug不改历史业务归属；切换组织后旧session不可续用
- AC-ID01-05：同版本不同映射内容被隔离，保两份原摘要与来源，不覆盖权威
- AC-ID01-06：tenant暂停再恢复产生新权威版本，旧token/session不因恢复自动有效
- AC-ID01-07：诊断读受tenant/PII范围限制，跨租户对象不暴露存在性，未知状态不显示绿色READY

### 4.2 SPEC-INT-ID-01-02｜用户权限版本

原需求：用户、角色、字段/动作、部门范围以可信版本传播；旧消息不得扩大或覆盖新有效授权。证据E01–E03/E08/E10–E15/E25；开发ID03、ID04、ID05、X01。

业务对象/Actor：Main User、UserRole、RoleAction、RoleDataScope、RoleFieldPerm及其身份快照；CRMProjection/Published；Main IAM管理员写、身份producer/consumer传输、CRM business Owner执行。Platform只验证结构与运输序列。

字段/动作：User payload含tenantId/userId/deptId/enabled/version/roles/managerId/authenticationEpoch/mustChangePassword/validAfterUtc/passwordExpiresAtUtc/lockedUntilUtc；Permission payload含tenantId/subjectId/enabled/version/grants；grant含resource/action/scope/deptIds/fields(field/access)。这些是当前真实DTO，复用不另造角色主账。字段permissionSourceVersions与mappingVersion为候选授权决策数据，不修改现v1事件。version按tenant+aggregate单调，不用updatedAt排序；无变化不加版本。

主流程：
1. Main业务writer在事务中保存用户/角色关系，IdentityChangeCapture列出旧新受影响aggregate，SnapshotWriter读最终事实并锁定原版本，快照+outbox+field audit同事务
2. CRM接收前核event contract、issuer来源、tenant、region、topic、partition、envelope/data版本相等；传输接收去重后应用投影
3. 本稿目标行为（并非全部现路径已证）：先区分运输重复、同aggregate同版本内容冲突和真正过时版本；使用§3.5选定的ICp6InboxPreCheckpointV1，在任何技术checkpoint成功早退前完成同事务应用一致性判定；尚未接入时明确未就绪。低版本忽略；相同版本相同data摘要幂等；相同版本不同data摘要将投影标DRIFT并撤出可用published；跨版本缺口也标DRIFT，只能由可信source对账清除
4. 请求授权先取Main当前在线actor，再与CRM健康投影的动作/范围交集。停用或删除角色不贡献grant；缺失/漂移的必要角色不按“空数组就安全”默许其他敏感操作
5. 字段链按2.3：构造有来源版本的决策，应用在response DTO、导出、搜索/排序/筛选、附件/证据内容授权及mutation。Main现MAX Scope和MIN Access保持可见；是否从数值Scope改集合语义归S5，不能在S6悄改
6. 业务提交前只消费§3.6 OwnerFinalAuthorizationGateV1：同库与全部撤权共同写者持同fence至业务/receipt/outbox/audit commit。Main–CRM分库无已采用等价共同门时关闭相关新效果并保历史只读；二次GET、TTL或缓存epoch均不是最终门

状态/幂等：接收版本与发布版本分列；RECEIVED不等于AUTHORIZED；任一必需来源不健康为503/不可用，真实撤权为403/会话流程401。对于并发请求，response记录authorizationDecisionId及policy版本（诊断不含敏感grant）；重放原业务结果按当前字段mask，但不改变原结果native hash。

API/DTO：继续使用PermissionIdentityData等真实v1；候选 FieldDecisionView：resource/action、fields[{apiField,canRead,canWrite,sourceField,policyVersion}]、decisionId、mappingVersion、validUntil，服务端派生，不接受浏览器覆盖。业务DTO带允许编辑字段与rowVersion，但权威仍后端。未知字段或超出请求白名单为400/403，整体写入回滚，不把禁写字段静默忽略后称全量成功。需要兼容旧StripReadOnly行为时以旧API adapter明确记录，不能全局替换。

FE：权限更新时清除失去可见性的已加载数据、表单缓存和下载链接；保留非敏感未提交草稿的选择由业务Owner与隐私政策决定。readonly字段不呈可编辑控件；hidden字段不放DOM/前端state。提交时权限变化展示“权限已更新，请刷新”，不盲重发旧表单；操作按钮消失不是后端授权证明。

失败恢复：G01未闭合的resource field mapping保持RequiredFenced，不能只因为online actor有edit而上线逐字段承诺；schema不兼容隔离，回到最新可信snapshot；新版本不等于改原事件。cache失效失败须保持受影响操作不扩大权限并生成恢复证据；具体Main全writer失效策略由S5确认。

AC（全部NOT_RUN）：
- AC-ID02-01：单次角色/用户多项变更只发布最终一致事实；业务回滚时快照/outbox/审计无半成品
- AC-ID02-02：经§3.5前置内容seam再作技术早退：低版不回退、同版同data摘要幂等、同版异摘要隔离；冲突记录/DRIFT/published不可用同commit，失败不先返回SUCCESS
- AC-ID02-03：动作与部门范围按online∩projection收窄；停用角色不贡献grant
- AC-ID02-04：显式字段1/2/3由Main语义映射到CRM DTO；只读/隐藏字段写入拒绝且无部分业务提交
- AC-ID02-05：列表、详情、导出、搜索、排序、日志与重放返回使用同一当前字段决策；未知映射关闭相关能力
- AC-ID02-06：同库业务写与所有撤权共同持Owner fence直到commit；撤权先生效旧epoch拒绝。Main–CRM分库无已采用等价门则关闭新效果并保合法历史只读，二次GET/TTL不算闭合
- AC-ID02-07：无配置字段策略保留明确版本化政策，S5未确认前不推断默认全权；旧API兼容行为单列

### 4.3 SPEC-INT-ID-01-03｜撤权

原需求：已撤权用户/token、租户暂停、认证epoch变化不能被迟到事件或对账恢复；记录真实撤权origin。证据E02/E03/E07/E09–E13；开发ID03、ID06、X02。

业务对象/Actor：Main disabled tenant/user、browser grant/session family、service token、role grants；CRMRevokedTokens及Session indexes。管理员按Main既有权限撤销，人类自身logout为独立动作；service client撤销只能管理自己的登记token，不能获得人类重放权限。

字段动作：issuer+jti定义token身份；subjectId/tenantId/expiry必须与原签发记录一致；epoch关联认证版本，权限aggregateVersion与epoch分别保留。token revoked payload只有真实issuer/jti/subject/expiry/version，无密码或secret；RevokedAtUtc+OriginJson来自真实validated Inbox，不由snapshot推断。RetainUntilUtc现为expiresAt+60秒；生产保留、最长token寿命与时钟偏差证据须确认，不能把当前常量当全局业务留存许可。

撤权流程：
1. Main禁用tenant/user、密码/2FA/外部身份/epoch等认证失效变更由捕获器列出；普通refresh轮换有替代token不误当终止family
2. 原grant/token撤销与对应snapshot/outbox同调用方事务，启用false或token.revoked写优先queue；普通发布堵塞不消耗优先循环预算
3. CRM在Inbox事务核原撤权身份，写RevokedTokens、删匹配session或tenant/user session、发布不可用/受限模型，一次原消息只有一次业务效果
4. 请求路上先online核当前身份，再本地撤权与epoch，触发DENIED则移除本地session；DB/store超时进入503，不记录“已撤权”假事实
5. 迟到的enabled=true低版本、旧permission和历史snapshot不越过高版本/epoch；恢复账号需要Main新授权版本和重新认证，不重用被撤销session

状态/恢复：撤权事实是不可“反撤销”的token级记录；账号恢复不删除旧token tombstone。收到重复真实事件可补“真实origin”，但不能重复删session造成新session受误伤；来源历史未知保LEGACY_UNKNOWN。错误origin与不可变token身份冲突隔离并告警，不覆盖第一次真实证据。优先发布失败保持持久待重试及明确租户影响，不宣称所有下游即时撤权。访问仍依赖online；新效果还必须§3.6共同最终门，分库无等价门时AUTHORITY_FENCE_UNAVAILABLE关闭。历史读取/可信事实接收/技术结果重投各走该节分类门，不把新写权加在所有历史事实上。SLO以Main committed→CRM observed→真实业务gate分别测量。

API/DTO：复用token.revoked.v1及会话本地logout；候选 RevocationStatusView 含tenantRef、subjectRef（脱敏）、revocationKind、sourceVersion、sourceCommittedAtEvidence、consumerAppliedAt、originStatus、impactedSessionsCount（授权后）和pendingConsumers；不回token本体或cookie。客服诊断仅安全码，无直接“恢复旧session”按钮。

FE：清除已失效页面数据/操作，显示重新登录；503显示临时不可用并保留本地退出入口，不用反复登录掩盖后端故障。账号恢复页面须提醒旧会话不会恢复。管理员看到本地已撤销/下游待确认的分层状态，不能用发送成功当完成。

AC（全部NOT_RUN）：
- AC-ID03-01：禁用和撤权事实、优先outbox与Main业务提交原子；失败不返回假成功
- AC-ID03-02：普通queue阻塞时优先queue仍有独立调度预算；实际延迟证据另验
- AC-ID03-03：token撤权身份精确绑定issuer/jti/tenant/subject，重复真实消息不复活或误删新会话
- AC-ID03-04：低版启用/权限不能复活高版禁用；恢复需新认证；在途新效果只能经共同最终门，缺门明确AUTHORITY_FENCE_UNAVAILABLE而非沿旧actor续写
- AC-ID03-05：真实撤权时间只取validated origin，snapshot/历史缺证不推断时间
- AC-ID03-06：认证服务/投影store故障返回503，不改成撤权事实；logout本地仍可用
- AC-ID03-07：撤权保留与清理边界需覆盖未到期token/容许skew，Required未确认不执行清理

### 4.4 SPEC-INT-ID-01-04｜本地会话

原需求：CRM会话由CRM持有；Main身份权威保持在线校验；不信任前端tenant/角色，不离线复用过时allow。证据E08–E10/E14/E27/E28；开发ID07、ID08、X02。

对象：LoginTransaction、CrmBrowserSession、CrmStoredAuthState及IdentityTokenMetadata。Actor为本人、当前业务用户、CRM auth服务；运维只看脱敏状态，不看token/cookie。现10分钟login transaction；CrmAuthContracts L11–15明确session.ExpiresAtUtc=min(TokenExpiresAtUtc, AbsoluteExpiresAtUtc（创建后8小时）, LastActivityAtUtc+30分钟idle)。20分钟projection freshness另作授权健康条件。四者分列，不能用8小时或页面活动覆盖token到期。

字段/动作：login输入slug、合法returnPath；state/cookie绑定服务端随机值；session带subject/organization/region/issuer/access/id token/expiry/created/lastActivity/CSRF，敏感值服务端受保护存储；cookie只随机opaque值。认证索引tenant/subject/issuer/jti/iat/epoch必须同已验证token一致。request body禁止Actor或org替换。会话touch的saved.Version防并发删除后重新创建。

流程：Begin解析当前有效组织→存bound login transaction→回调核state/cookie/issuer、一次性取走→code兑换并核token组织→Main在线reconcile→本地identity投影authorize→保存索引和受保护session。每个受保护业务请求执行ReadAsync同样两道授权，业务API再做对象/字段门禁。重复回调不建第二session。用户活动touch只更新LastActivityAtUtc从而推进30分钟idle候选边界；持久到期仍取token/8小时absolute/idle三者最小值，不延长token或绝对认证到期。退出先删本地session，再独立通知Main；上游退出失败返回本地完成/上游未确认两个字段。组织切换先本地退出，再新组织登录。

错误状态：401登录或session无效，前端只提供登录入口；403业务不可做不等于全账号撤权，认证层401/403上游拒绝按现行为移除session；503依赖不可用禁止业务但允许local logout；409业务并发冲突不重新认证。服务启用配置缺失显示未配置，不自动切fallback profile。Main原应用Bearer与C02.Services不能因为名称相近合并；不使用Platform服务token作CRM浏览器session。

API/DTO：复用现认证端点和workspace，已读workspace返回FunctionIds/Actions/CSRF等。候选SessionStatusView只含organization display、subject display、expiresAt、authState、freshnessState、canLocalLogout、upstreamLogoutState；不含原access token。CSRF来自已认证session，写操作校验；诊断页面不得把已有HttpOnly机制替为localStorage令牌。

FE：加载时显示Loading；不把缓存workspace动作当授权；401清空受保护数据并登录、503停写且可重试读取/退出、409保冲突说明；新的页面/请求序列忽略旧响应，切租户后不得显示上个组织资料。Cancel/Back/重复提交/两tab并发退出及超时都走服务器现session状态，不能前端乐观复活会话。

AC（全部NOT_RUN）：
- AC-ID04-01：同state仅消费一次；回调组织/issuer不匹配不建立session
- AC-ID04-02：每请求Main online与CRM投影均被执行；任一不可用不fallback到旧allow
- AC-ID04-03：session到期严格min(token expiry,创建后8小时absolute,lastActivity+30分钟idle)；touch只推进idle候选边界，且与撤权并发不能重建已删session
- AC-ID04-04：退出在上游不可用时仍删本地session，并诚实显示上游未知
- AC-ID04-05：组织切换/Back/迟到response不显示上一个tenant数据或权限
- AC-ID04-06：会话/访问令牌不出现在诊断、日志、URL或FE持久缓存
- AC-ID04-07：401/403/409/503分别展示，503不无限循环登录也不自动提交旧业务请求

### 4.5 SPEC-INT-ID-01-05｜重放和对账

原需求：按原身份修复漂移，可信水位/版本防回退，对账不伪造真实事件来源。证据E04/E05/E11–E13/E25；开发ID09、ID10、X03。

对象/Actor：Main IdentityVersionPage、Snapshot、持久generation、CRMReconciliationState（Ready/Boundary/LastSuccess/Lease/Failure）、Projection、Published；后台reconcile worker以注册service reader执行。支持操作员只能提出本tenant重新核对请求，不能上传替代快照。

字段/流程：
1. claim本tenant对账lease；现租约2分钟、deadline90秒、循环5秒、正常间隔15分钟，失败5→300秒有界退避。候选UI诊断引用实际配置版本，不另定更快轮询
2. 读versions全目录，每页不超过200，tenant/boundary一致、aggregate唯一、cursor不重复、最多100万项；不自行比较boundary大小
3. 对本地缺失/异摘要/漂移项标不可用，再读准确snapshot；核tenant/aggregate/type/version/hash/deleted完全对应baseline项，原data hash与schema一致
4. 按原源快照导入，保持比baseline更新的健康投影不回退；相同版本异data只能由可信Main当前snapshot及冲突记录恢复；published与导入事务同步
5. 再次全量读versions，首尾boundary及目录完全一致才Complete；Main跨页变化409、final边界变化、缺项、响应超限、丢lease均不能置Ready
6. Complete持久化LastSuccess/boundary，保原snapshot来源；原Inbox message未收到就不补“真实接收”。失败不清空LastSuccess来误造从未成功，也不能延长freshness来掩盖超龄

状态/幂等：一次对账runId为技术运行，非业务权限版本；同baseline重复运行不新增grant或撤销tombstone。source omitted本地项标Drift，不能删除后把缺项当无约束。lease丢失的旧worker不得写完成；新worker从完整边界重新开始而非混接旧cursor。原DLQ身份消息重排须经§3.5前置内容门，即使技术checkpoint将早退也不能跳过。冲突修复沿同State→Published→Projection锁序，以新AuthoritySelection及resolvedConflictRefs追加证明；Complete核无新未解决冲突和源选择CAS，旧baseline不能清掉后来DRIFT。修复以最新权威snapshot为主，不“全部历史重放”。

API/DTO：现versions/snapshot不改变；候选 ReconciliationStatusView含tenantRef、sourceBoundaryOpaque、baselineAge、lastSuccessAt、lastAttemptAt、nextAttemptAt、failureCode、driftCount、missingDependencyCount、sourceBundle/publishedBundle、runState与consumerObservedVersions。manual reconcile request若批准仅调度现协调器，不赋予数据写override；完整重建要单独owner变更方案，非自动重试按钮。

FE：显示“对账成功时间”和“当前可用性”两项，保超龄与漂移原因。支持按aggregate定位安全差异（版本/hash/存在性），不默认展开身份PII。重试按钮在lease运行时显示处理中；超时查询同run，不发并行全量拉取。未有专用页面证据，复用CRM状态组件新建诊断视图为CANDIDATE。

AC（全部NOT_RUN）：
- AC-ID05-01：分页途中Main generation变更返回409，CRM舍弃旧baseline并重启，不能置Ready
- AC-ID05-02：snapshot/hash/version不符时关闭相关身份；前置seam冲突恢复只沿可信Main完整baseline并追加source selection，缺内容门不先推进授权
- AC-ID05-03：final目录变化/未解冲突不记Ready；Complete与新冲突在共同锁/CAS下重判，旧baseline不清新DRIFT，较新健康投影不回退
- AC-ID05-04：失去lease的worker无权写Ready；重复同baseline不生成新身份事实
- AC-ID05-05：snapshot-only撤权保持SOURCE_SNAPSHOT/LEGACY_UNKNOWN，不制造messageId或revokedAt
- AC-ID05-06：上游auth拒绝失效当前cached service token；技术503保有效token缓存并按有界退避，均不扩大授权
- AC-ID05-07：新Main generation接口与现CRM opaque string消费在真实环境的兼容证据Required/UNPROVEN；静态对应不作运行PASS

## 5. INT-OPS-01：五项原SPEC完整设计

### 5.1 SPEC-INT-OPS-01-01｜死信查询

原需求：保留原消息/原请求，按Owner、租户、故障状态可追查；不把DLQ当无限重试列表。证据E16–E20/E23/E28；开发OP01、OP02、OP03。原H/SC仍为空；DP10/12/16/17。

业务对象/Actor：Bridge IntegrationEvent、ERP CommandInbox、ERP result Outbox、ERP OrderBridgeDispatch、CRM identity Inbox、Platform DeadLetterRecord；每个原对象仍在应用Owner。应用集成只读支持员看自己授权tenant/Owner的metadata，业务Owner操作员看自己恢复范围，审计员看审计。list/read无恢复副作用，不清理队列，不自动改DEAD或attempt。

数据来源与字段：
- 旧Bridge提供eventId/sourceModule/targetModule/hook/sourceNo/targetNo/attempts/LastError/createDate。LastError原始异常可能含业务数据；新列表仅safeCode和受控supportRef，旧全文只在Owner受限证据中保留，不直接复制到共享运维视图
- ERP inbox提供messageId/eventType/aggregateId/version/attempt/payloadHash/error/received/processed/replayed/rowVersion；result/bridge提供kind/targetId/messageId（bridge可null）/attempt/hash/availableAt/rowVersion。bridge hash是c03:bridge:tenant:key域，不冒充payload raw hash
- Platform死信方向、consumer、messageId、payloadHash、createdAt是技术事实；无Owner原请求键时evidenceState=OWNER_LINK_MISSING，不能从message推定businessOperationKey

处理过程：校验当前认证及查询scope→分别调用授权Owner read adapter→按原类型投影统一FailureDetail→保nativeStatus并另算安全分类→用当前权限限制原业务链接及字段→分页。单个Owner不可用返回partial与safeCode，其他Owner结果仍可显示；若全部失败返回503而非空成功。跨owner排序使用可信occurredAtUtc，不可信旧本地时间保unknown并单独分区；不会无依据把CreateDate改成UTC。

筛选/分页：kind/Owner/route/status/time窗口，跨Owner唯一全序和签名cursor/sourceSet规则见3.2；计数显示当前查询范围和snapshotAt，不把Bridge最近10条当全租户死信总清单。raw nativeId是引用，前端只能带回opaque failureRef；服务端每次再检查tenant/owner，不把cursor内tenant视为认证。

状态与动作：列表显示技术状态、Owner结果、可见性/证据状态三个独立栏目。availableActions来自后端资格；Query不是Replay批准。未知/原请求缺失/已清理标“需核实”，按钮只允许查看Owner原回执/申请调查。历史COMPENSATED显示“旧标记”，不混入恢复成功指标。

API/DTO：复用旧GET以保客户端兼容；新增/failures及/{ref} adapter详见3.2。新增元数据字段必须有来源定位；拒绝以“查不到SQL”来填0或empty date。Error DTO只含safeCode/supportRef/correlation/partial，无exception.ToString、payload、token。原payload取证是独立Owner权限和审计动作，不随列表默认返回。

FE：保留BridgeHealth指标组件，新增来源类型、当前Owner结果、原请求键的受控链接、证据完整性、刷新时间及partial条。刷新/分页需请求序列控制，租户切换清旧数据；Empty与Unavailable不同。选择一条仅查看不触发replay；复制诊断仅安全字段，下载需显式权限，不把payload导出当普通CSV功能。

异常恢复：查询不写attempt；Owner故障后依3.2固定sourceSet：原缺源恢复时重新查询新集合，中途丢失既选源则SOURCE_SET_UNAVAILABLE并重新查询；cursor失效同样重启，不能拼上旧页形成假完整清单。原行保留不足时显示EVIDENCE_EXPIRED，不造空原消息供重放。并发worker修改原状态后详情用最新nativeVersion，旧列表行不能直接重排。

AC（全部NOT_RUN）：
- AC-OP01-01：三allowedAction/nativeType/digest(domain/codec/value)准确：request/result为真实raw envelope，bridge仅原身份摘要且message/envelope=null；不伪造业务input摘要
- AC-OP01-02：查询按当前tenant及Owner scope，未授权对象不泄露存在性或payload
- AC-OP01-03：跨Owner分页使用含owner/nativeType/nativeId/failureRef的唯一全序，cursor固定sourceSet；缺源恢复新查询，中途丢源明确未就绪，不拼漏项旧页
- AC-OP01-04：部分Owner不可用明确partial；全部不可用503；不以空列表冒成功
- AC-OP01-05：原始异常、PII和secret不进入列表、复制诊断、通用日志或指标标签
- AC-OP01-06：COMPENSATED历史标记不计业务已恢复；旧时间未明不伪造UTC
- AC-OP01-07：页面Back/刷新/切tenant/迟到响应不保留旧组织数据，不因查看自动重排

### 5.2 SPEC-INT-OPS-01-02｜业务拒绝和技术失败

原需求：终态业务拒绝、可恢复技术失败、提交UNKNOWN分别处理；不能把false/异常一概重试。证据E16/E17/E19–E23；开发OP04、OP05、OP06。

对象：OwnerOperationReceipt、Attempt、FailureClassification。Owner决定业务结果；运维支持不能改分类来绕过业务政策。当前ErpCommerceException→terminal business failure与未知异常→technical retry已有实现；BridgeDispatcher返回bool丢失失败语义，需业务Owner为每route提供分类adapter。

分类规则与字段：
| failureClass | 依据/字段 | 行为 |
|---|---|---|
| BUSINESS_REJECTED | Owner稳定code、原requestVersion、terminal=true、receiptRef | 展示原拒绝；修正业务要新业务版本；不重放旧输入求不同结果 |
| TECHNICAL_RETRYABLE | Owner证明未提交/可幂等恢复、retryable=true、dependency/nextRetry、attempt | 现有有界退避；超过上限转死信；仍核当前资格 |
| TECHNICAL_TERMINAL | 结构不兼容、原内容完整性冲突、不可重试技术code | 隔离保原证据；修复合同或发新版本，不替换旧payload |
| AUTHORIZATION_DENIED | 当前tenant/actor/policy决定及版本 | 不调度原业务；恢复授权后也要重新评估，不自动续授权 |
| COMMIT_UNKNOWN | 发送/提交超时、回执不完整、外部请求结果不明 | 查询Owner原操作；无NOT_APPLIED_PROVEN不得新执行 |
| SUPERSEDED | Owner原版本已被新版本取代且有receipt | 保旧终态，链接新请求；不回滚业务版本 |
| DUPLICATE_RESULT | 原业务键/原输入匹配且已有终态 | 返回原结果，当前字段投影，零新增业务效果 |
| EVIDENCE_CONFLICT | 原身份/hash/native版本不一致，多个互斥回执 | 隔离；人工Owner核验，不取“最新时间戳”覆盖 |

业务流程：每次Owner处理原请求先查自身journal及business key，若原输入不同返回幂等冲突；原终态复用；否则当前权威tenant/权限/业务状态校验，在同事务执行、写回执及outbox。捕获技术故障前确保失败事务已经销毁/回滚，用新事务记录retry；若提交状态未确立，先标UNKNOWN并查询已持久回执，不能把网络timeout当rollback证明。现ERP handler已有“先inspect receipt/handle prior再结果”路径；适配不替换原journal。

原Bridge差异：SUCCESS只代表hook返回成功，SKIPPED可能是已存在/业务规则跳过；必须由对应Owner映射为APPLIED_EXISTING、BUSINESS_REJECTED或其他具体结果。bool=false没有足够信息时保持UNCLASSIFIED/PendingOwner，不能宣传此路由支持完整业务恢复。Space分支fence/完成回执可复用，不扩称所有非Space已经同样保证。

API/DTO：FailureClassification{class,ownerCode,terminal,retryable,ownerReceiptRef,originalRequestVersion,classifierVersion,classifiedAt,commitCertainty,dependencyRef,supportRef}；原errorCode不删除，nativeErrorRef单独封存。HTTP transport code与业务outcome不强行一一对应：202=调度接纳；200原回执可含业务拒绝；503可能UNKNOWN；Dapr SUCCESS可仅Inbox已处理。

FE：业务拒绝显示可见的具体规则与回原业务页面；技术故障显示依赖/下次尝试；UNKNOWN固定“结果待确认”，主动作“查原结果”；不要以红色失败按钮旁放一键重新提交。后台任务成功不覆盖业务拒绝；原请求版本和当前对象版本并排。

异常恢复：依赖恢复只提升技术资格，不改变原业务输入；后续成功结果须Owner receiptVersion单调并关联同originalRequestRef。多个attempt历史完整保留。对于修正后业务新版本，历史终态保留并显式supersedes link，不能原地改旧terminal=false。

AC（全部NOT_RUN）：
- AC-OP02-01：同一可预期业务拒绝持久为terminal，不被worker技术retry重新执行业务
- AC-OP02-02：技术回滚后用新事务记录retry；失败上下文不二次提交旧跟踪实体
- AC-OP02-03：提交超时仅UNKNOWN，先查原Owner回执；HTTP503不自动证明未执行
- AC-OP02-04：重复业务键同输入复用原结果，异输入冲突；messageId变化不创建第二业务效果
- AC-OP02-05：SKIPPED/bool=false逐route经Owner映射；未映射保持未分类且重放关闭
- AC-OP02-06：已被新版取代旧请求不可恢复旧业务版本，UI同时展示原版本与现版本
- AC-OP02-07：transport success/Inbox processed与Owner applied分列，任一技术状态不冒充业务成功

### 5.3 SPEC-INT-OPS-01-03｜重放资格

原需求：重放不得绕过租户/权限/当前业务状态、不得替换原消息；受控审计保同operation语义。证据E19–E23/E16–E17；开发OP07、OP08、OP09、X04。

Actor与对象：业务Owner恢复操作员（当前人类身份）；现ERP admin入口保留，候选精细权限需Owner/S5签认。Worker使用明确service authority，只执行已登记恢复意图。重放对象为原请求或原结果delivery或原post-commit bridge，三者必须选一种，不设“全部重来”。

资格判定（只适用于§3.4未命中原operation的新调度分支；全部满足才Eligible，已有原operation只读回查不受此门）：
1. 当前认证、tenant允许该新作用、Operator具有对应allowedAction的当前目标权限；按§3.6持相应共同最终门，不能以读到policy版本代替。原结果技术投递不要求原人类新建业务权限
2. 原native行与本allowedAction的请求/消息/对象引用唯一；按§3.3分支核摘要域及codec。仅原请求/结果要求真实envelope原文/hash/schema；post-commit bridge核原tenant/orderKey/bridge身份摘要/nativeVersion及Owner原业务绑定，不伪造payload；无替换内容
3. 原Owner结果已查询且按动作对应的效果范围判断：原请求APPLIED不重做；结果重投须原结果已提交；bridge须原订单已提交，并另查被恢复下游步骤的原operation/结果，订单APPLIED不等于bridge步骤已完成。REJECTED_TERMINAL/SUPERSEDED不能重放对应旧业务；步骤UNKNOWN/PENDING不得强行新执行；NOT_APPLIED_PROVEN才可进入该原步骤业务重试
4. 原目标当前状态、revision、生效窗口、技术内容/用途授权仍满足业务规则。旧批准不能作用新对象版本；准确技术文档内容版本依赖不明则RequiredFenced
5. 仅在§3.4未有原operation的新接纳分支，原queue须可重排、expectedNativeVersion/分支摘要匹配、无活动lease/冲突恢复操作，route epoch及单consumer归属有效；已有同义operation不重验这些会自然变化的条件
6. 原始actor已撤权时不借operator身份擅自续办其业务。业务Owner若存在独立系统义务，需明确service authority/目的/对象范围和当前政策证据；没有则Blocked
7. 新调度的审计与调度能够同Owner事务持久且§3.6相应共同门已真实采用；分库无等价安全门返回AUTHORITY_FENCE_UNAVAILABLE。原结果查询/保留证据不足或只有stub均Blocked/PendingOwner；既有operation的合法只读查询仍保留

执行唯一顺序见§3.4：当前认证/tenant绑定/原结果read范围及基础语法→按原operation回查。命中同义只读返回原Receipt/状态、异义409，零新增调度，不要求当前queue仍DEAD/旧ETag仍当前/eligibility未过期/原新写权仍有效。未命中才取§3.6动作对应共同门→Operation根再次回查以处理并发→完整新资格→原queue/Owner键→强制审计和调度同commit→202。原worker继续新效果时再次在效果事务持真实Owner最终门；结果纯技术投递用delivery门。预检不是授权票据，不能跨过门未就绪关闭点。

现ERP重排保留：先按原operation固定tuple核inputOperationId/原rowVersion/分支摘要/reason/actor，同义回原Receipt；以下仅新调度才检查。不替换原载体；inbox必须DeadLettered；result必须Outbox DeadLettered且唯一未replayed deadletter；bridge必须未Completed、达到attempt阈值、code对应、无active lease且原订单存在。新适配不能弱化这些条件。现桥无messageId不是缺省生成一个；其业务operation原引用仍必须由Owner提供。

状态/未知提交：Scheduled→Running→Owner outcome已确认或Unknown。客户端网络断开不撤销已经持久任务；按同replayOperationId查询。重复点击不生成新operation；用户取消dialog只取消尚未发送的请求，若服务端已经accepted则显示已安排，不能写“取消完成”。支持取消若Owner没有原子取消协议，UI不提供。

API/DTO：使用3.2 ReplayRequest/Receipt；原ERP路径继续接收原ErpReplayRequest由adapter按已确认政策评估，不无声增加required字段破坏旧客户端。旧调用者若无法满足新政策，须明确兼容拒绝及升级说明，而不是默认放行。ReasonCode依Owner枚举，不支持force。

FE：资格页展示原业务和message键、native状态/版本、当前Owner outcome、预计动作（仅重新投递原结果/重新调度原请求/补原订单bridge）、阻塞原因和审计理由。按钮“安排原请求重放”后转原operation查询；202提示“已安排，等待Owner结果”，不弹“业务成功”。审批要求如Owner政策规定双人审批，由Main Workflow决定、Owner校验应用；S6不新建审批主账。

AC（全部NOT_RUN）：
- AC-OP03-01：同operation先查原tuple再分支；首次响应丢、queue已改版、eligibility过期或已无新写权但有读权，仍只读回原Receipt，零新调度；异义冲突
- AC-OP03-02：按三动作分支核原nativeVersion和typed摘要；bridge不要求不存在的payload/schema；域/codec/action错配稳定拒绝且无队列/业务变更
- AC-OP03-03：新调度/新效果只经§3.6已采用共同门；失权DENIED、分库缺门UNAVAILABLE，历史原结果读/可信事实接收/纯技术重投按各自门，不借原grant续权
- AC-OP03-04：原已APPLIED业务不重做；原结果delivery或原bridge尚未完成步骤按各自原效果范围/门处理，不能把订单APPLIED当下游步骤成功；对应终态拒绝/取代不改旧结果
- AC-OP03-05：lease/route epoch变化、并发worker或新状态导致资格重判，零双消费者双写
- AC-OP03-06：新调度audit+queue同事务；响应丢/提交进行中/锁超时返回同operation的Processing/Unknown/RetryAfter，先回查，不以查无/旧版本失配生成新ID
- AC-OP03-07：202只表示已安排；关闭dialog不取消已接纳任务；同ID可重开查询，过期eligibility或原新写权变化不被误作已有操作消失

### 5.4 SPEC-INT-OPS-01-04｜审计

原需求：保存谁因何在何原请求/原内容上调度、判断与恢复；可证明不绕过业务Owner，兼顾敏感字段和证据保留。证据E19–E21/E24/E26，SYS-AUD/PRIV政策Required；开发OP10、OP11、X05。

对象/Actor：Owner ReplayAudit、Owner原request journal、技术attempt、OwnerResultReceipt、原origin evidence。操作员不能编辑既有audit；审计只读角色按tenant/Owner/PII policy访问。Main字段before/after审计、CRM审计完整性和Platform技术trace是不同资料，不能混称同一业务审计链。

必填审计字段：auditId、tenantBinding、ownerId、replayOperationId、originalRequestRef/businessOperationKey/requestVersion、originalMessageRef/nativeQueueRef、actorId/actorKind、operatorAuthDecisionId/policyVersion、reasonCode、allowedAction（唯一动作枚举，不另设可偏离的requestedAction）、expectedNativeVersion、expectedDigest(domain/codec/value)+allowedAction/nativeType、eligibilityDecisionRef、ownerReceiptBeforeRef/version、nativeStatusBefore/After、scheduledAt、attemptRefs、ownerReceiptAfterRef/version、outcome、contractVersion、retentionClass、supportRef。必须区分originalActor与replayOperator，不把旧payload.UserName写为当前认证人。秘密值不入日志；需要exact payload证明时保存受控nativeRef+hash，而非将payload拷贝至全员日志。

记录链：
1. 列表/详情/资格读取可记录访问审计，故障不得引发业务写；有敏感证据访问则审计作为读取必要条件
2. 重排audit与原队列状态同事务。现ErpReplayAudit/DeliveryReplayAudit结构原样保留，新增字段用版本化扩展或关联记录，不能回填虚假以前未记录的值
3. Worker start/attempt/failure/result关联同originalRequestRef，追加记录不覆盖原attempt；重复同operation没有第二调度audit
4. Owner业务回执与业务事实同事务，不由运维API代写APPLIED；最终恢复回执链接原操作和实际Owner原提交证据
5. 扫描/报表显示originStatus、captureAuthority、proofStatus。只有真实received envelope才记录RECEIVED_EVENT；对账snapshot/历史unknown保原分类

生命周期：Append-only使用原ERP审计保护；保留期间需SYS-AUD/PRIV确认。技术默认7/30/90天不自动覆盖Owner账务/技术文档/合同证据政策。UNKNOWN、待重排、争议、被业务回执引用的证据不可按普通成功过期策略先删除。允许脱敏后的digest/最小身份保留需要明确政策；删除PII和保持审计链冲突交由Owner，不无限保存完整payload。清理设计只列计划，不执行SQL/retention worker。

API/DTO：候选GET /replays/{id}/audit返回AuditPage，含已授权字段、事件顺序/seq、时间来源、sourceNativeRef、proofStatus、nextCursor；默认不含payload/secret。导出需audit.export及敏感字段授权并记录purpose，归SYS-PRIV/SYS-AUD政策。本稿未增加export实际端点或权限。audit缺件返回evidenceGap，不生成“补齐记录”冒原事实。

FE：时间轴分用户申请、资格评估、已调度、worker尝试、Owner提交、结果投递；技术时间与业务时间分栏。原结果证据可点击受控链接；隐藏字段展示“受限”，不显示0或空而让人误判原事实。旧审计没有auth版本等字段显示“历史未记录”，不可强制默认当前版本。

失败恢复：审计写失败使新调度不提交；日志服务不可用是否影响只读由数据敏感度决定，写路径的强制审计不降为best-effort。source hash冲突隔离并保原记录；需更正以append correction链接旧audit，不直接修改原因/actor。

AC（全部NOT_RUN）：
- AC-OP04-01：原operation同义回查零新调度/调度audit；新接纳强制audit与调度同commit，异义不重写原audit
- AC-OP04-02：原actor/operator/worker分列，新增效果引用真实Owner最终门fence/epoch/事务证据，缺能力不能补造ALLOWED_HELD
- AC-OP04-03：allowedAction/nativeType及typed digest原样入audit；raw/data/canonical/bridge identity四域不互换，不存在载体保持null
- AC-OP04-04：错误/日志/导出不带secret和未授权PII；敏感证据访问受单独scope
- AC-OP04-05：历史缺失数据保持LEGACY_UNKNOWN，不伪造auth版本、真实撤权时间或Owner提交时间
- AC-OP04-06：UNKNOWN/争议/引用证据不被通用技术retention自动清理；Owner政策未证则不启用清理
- AC-OP04-07：审计只能追加更正，原history保持可追溯；平台trace不冒充Owner业务回执

### 5.5 SPEC-INT-OPS-01-05｜原结果对账

原需求：提交UNKNOWN和原结果丢失先查Owner原回执，保原请求键与状态，避免二次业务执行和旧结果覆盖新版本。证据E20–E23/E14；开发OP12、OP13、OP14、X03。

对象：Owner原journal、结果Outbox、消费者Inbox、消费者业务投影、ReconciliationCase。Operator只推动查询和允许的delivery恢复；Owner决定原结果，消费者决定是否采纳其当前投影。Platform不根据“队列空了”生成业务成功。

关键字段：原Owner/tenant/requestId/requestVersion/businessOperationKey/inputDigest(domain+codec)；resultType/resultVersion/terminal/succeeded/nativeResultRef；consumer targetRef/appliedSourceVersion/receiptRef；观察时间及可信来源；lastProbeCode/nextProbeAt/lease。单独保原请求版本、结果版本和目标当前对象版本，不能混为一个version。

决策过程：
1. 以原operation identity查询Owner journal，不重新拼入参与业务对象当前值后产生新输入；缺原key则PendingOwner人工定位
2. 原Owner回执匹配原request/tenant/input/domain且terminal=APPLIED时，取得其原结果native证据与targetRef；仅投递缺失时重排原结果Outbox，不能再创建原业务对象
3. terminal=REJECTED则消费者显示原拒绝；修正需新业务请求版本，不覆写旧结果。PROCESSING继续同operation观察
4. 原回执UNKNOWN/不可读/不存在时，询问Owner可信提交记录或本地原子事务证据；未证明NOT_APPLIED就保持UNKNOWN，不将队列查无记成可重试
5. 消费者按原request/结果版本、scope、target当前状态及可用schema判断。重复同结果幂等；低版不回退；同版不同内容隔离；已被新版业务取代返回明确NotApplied/Superseded回执，保原Owner结果
6. 本次证据收口定义为：原Owner终态可证明+下述固定RequiredConsumerSet各项原回执完整；原业务是否完成另按nativeCompletionPredicateRef。Producer Published、Dapr SUCCESS、Inbox Processed或通用Case Resolved均不能替代原Owner业务完成


本次对账的RequiredConsumerSet固定为原route/部署登记中的精确集合，不按UI当前选项或“谁先回了”决定。ReconciliationCase保存consumerSetRef、registryVersion、routeEpoch、每项consumerAuthority/targetScope/adoptionContractVersion/nativeCompletionPredicateRef和适用原结果版本，均由已登记Owner读取。集合缺证时Case=WaitingOwner，不能宣称齐全。原业务完成谓词尤其WMS采纳/确认收尾、INT-COM规范流和用途义务保持原Owner定义，S6只引用准确回执与谓词，不以通用Resolved覆盖。

| 消费者native回执 | S6本次对账映射 | 本次证据状态 | 原业务义务 |
|---|---|---|---|
| Applied，原request/result/target/version绑定齐全 | CONFIRMED_APPLIED | 该项原事实已核 | 仍按nativeCompletionPredicateRef判断是否尚有独立confirm/settle等义务 |
| RejectedWithReason，Owner明确终态拒绝 | CONFIRMED_REJECTED | 该项拒绝事实终态 | 业务未成功；是否另起新业务版本由原Owner处理 |
| NotApplied/Superseded，带原版本/取代回执且Owner明确终态 | CONFIRMED_NOT_APPLIED | 该项未采纳事实终态 | 不回退现版本，不等同整体业务已完成 |
| NotApplicable，Owner按固定集合适用规则给精确范围证据 | CONFIRMED_NOT_APPLICABLE | 该项不适用终态 | 不由操作员删required consumer；原规则仍保留 |
| Received/InboxProcessed/Published/Queued/PendingGap/Processing/Blocked/Unknown | PENDING_EVIDENCE_OR_APPLICATION | 未决，不计已业务应用 | 保持原Owner义务；缺前驱链/最终门不能借技术成功关闭 |
| 同版本异内容/身份不符/互斥终态 | CONFLICT | 隔离 | 不选择最晚时间戳覆盖，按原Owner恢复合同解决 |

CaseEvidenceComplete仅表示固定RequiredConsumerSet中每项有合法终态事实及完整身份链；它不等于BusinessObligationComplete。Case只有原Owner nativeCompletionPredicate同时满足才可显示其原业务完成；若证据齐但某原义务未结，显示EvidenceComplete/OwnerObligationPending而非“业务完成”。通用Resolved只可作为运维调查已收口标识并展示此分层结果，不能更改原Owner回执。

运行中登记集合变化不静默删成员：原Case保持旧集合/版本；Owner明确批准新的适用集合时创建CaseRevision关联old/new集合及原未结义务处理回执，重新计算本轮证据状态。不能因消费者下线或长期无响应自行NotApplicable。分页/重试/重开仍引用同CaseRevision，避免不同集合的回执拼成假齐全。

状态/重试：Case=Open→Probing→WaitingOwner/Conflict→EvidenceComplete或Resolved（只指调查收口）；BusinessObligationComplete须原Owner独立谓词。Owner和consumer outcome仍独立。probe只读有界退避，前端看到更新时间/下次检查；如果此task被用户要求持续跟踪，调度按授权范围另设，不在文书中自动执行。重复probe同case不增业务操作。新结果version可以是对同原请求的后续技术失败/成功更新，但不能降低已知terminal；版本并发规则复用Owner原journal。

实例A（ERP已提交结果投递失败）：原RequestJournal成功及原OrderRef存在→result outbox死信→以result动作重排原outboxId/message/payload/resultVersion→CRM查询/消费同原结果→Owner与consumer分别回执。不能走inbox业务重放重新createOrder。
实例B（ERP命令业务拒绝）：Inbox可Processed，原RequestJournal terminal failure→消息层成功不代表业务成功→页面显示原业务拒绝；新输入先经CRM业务审批/提交新requestVersion，原request不变。
实例C（旧Bridge无ledger/COMPENSATED）：无法从无ledger或人工标记证明没有/完成效果→查接收Owner原业务操作/回执→如果只找到当前同单号对象而无原输入版本绑定，证据仍不足→只读调查，禁止自动补执行。
实例D（权限已撤销但需查历史结果）：允许的运维角色可读安全状态；普通原用户不能获得已经失去可见权限的原payload。当前read policy仅投影返回字段，原native结果bytes/hash不变。

API/DTO：3.2 OwnerResultQuery/Receipt为候选adapter；现ErpReadService可辅助定位RequestId/Version/目标，不足以单独证明某命令未执行。新增GET按operationRef读journal先授权，404须区分对调用者不可见与Owner内部真实not found，不对外泄露。返回UNKNOWN包含supportRef，不提供“force confirm”写接口。consumer adoption receipt必须注明是技术接收还是业务应用，origin证据用真实native ref。

FE：结果对账页分四列：原请求、Owner原结果、投递、消费者应用。显示对象最新版本时标“当前对象”，不替代“原请求结果”。UNKNOWN主操作仅查原结果/联系Owner；原Owner已成功但未采纳时给安全重发资格入口；同版冲突显示红色隔离，禁止按最新时间自动覆盖。轮询停止/页面关闭不取消业务任务；重新打开通过同operation恢复展示。

AC（全部NOT_RUN）：
- AC-OP05-01：提交UNKNOWN只查原Owner回执；查无/已过期/副本延迟均不等于NOT_APPLIED_PROVEN
- AC-OP05-02：Owner已APPLIED且delivery失败仅重发原结果，业务对象数/金额/版本不重复变化
- AC-OP05-03：业务终态拒绝保持原结果；Inbox processed/Dapr success不显示业务成功
- AC-OP05-04：消费者同版异内容隔离、低版不回退、新版取代返回明确未采纳原因
- AC-OP05-05：原请求/结果/当前对象三套版本与raw/canonical域完整保留，对账不重算原输入身份
- AC-OP05-06：历史结果返回按当前字段/PII权限投影；原不可变结果摘要和nativeRef不改
- AC-OP05-07：固定consumerSet/registryVersion及每项原完成谓词；证据齐全与业务义务完成分层，Received/Processed/Unknown不当Applied，原WMS/INT-COM独立义务不被Case Resolved覆盖

## 6. 跨组依赖、Required证据和旧新映射

### 6.1 Required登记：填表不代表采纳

每项证据须包含Owner姓名/角色（或正式负责人身份）、repo/完整SHA、配置/部署版本、native回执/日志原件、时间/环境、适用tenant/route/字段集合、内容bytes/hash/原Ref、审阅人结论。生产敏感配置只证明存在及版本，不把secret写文书。当前所有项均Required / UNPROVEN；无空白默认为approved。

| Required ID | 责任Owner/协作组 | 所需完整证据 | 未具备时边界 |
|---|---|---|---|
| Required-NATIVE-01 | 主协调/原册管理 | MSBBPA010母版及本组两册native身份/格式/原内容，或明确允许本稿交付格式的记录 | 不伪造xlsx；只提交Markdown候选 |
| Required-IAM-01 | Main IAM/S5 | SYS-IAM/TENANT正式政策、所有身份writer清单、角色/用户/部门/菜单/权限及批量路径、原权限码 | 未核writer不能宣称全变更传播 |
| Required-IAM-02 | Main IAM/S5 | 字段默认/合并政策、Main字段→CRM字段映射、row/field/action冲突处理及cache失效证据 | 逐字段资源接入RequiredFenced |
| Required-ID-01 | Main身份Producer | 本tenant OIDC组织/服务client/identity配置和版本、token记录/优先outbox，实际Host注册/启用证据 | 源码存在但运行UNPROVEN |
| Required-ID-02 | CRM投影/会话Owner | 本tenant/region注册、session索引、consumer唯一性、published及online交集运行证据 | 不宣称可离线授权或全链接入 |
| Required-ID-03 | Main持久化+CRM Owner | Main持久generation在目标provider真实迁移/触发器覆盖；CRMopaque boundary兼容及首尾目录运行证据 | 不能称新版Main与旧CRM运行兼容 |
| Required-ID-04 | Main/CRM安全Owner | OwnerFinalAuthorizationGateV1 adapter版本、ScopeAuthorizationFence/等价门完整共同写者与锁序、撤权竞争/缺门关闭、时钟/token/保留及SLO证据 | 按§3.6共同最终门关闭缺能力的相关新业务执行；历史read/可信事实接收/结果delivery分别用其门，仍不承诺未证SLO |
| Required-OPS-01 | 每一接收业务Owner | 原operation键/输入digest域、原结果journal、APPLIED/NOT_APPLIED/UNKNOWN及业务拒绝分类adapter | 该route只读，重放关闭 |
| Required-OPS-02 | 每一接收业务Owner | §3.6最终安全门/原Owner adopted fence、完整共同写者、目标状态及技术内容/用途/版本/baseline批准绑定；安全门与INT-COM用途grant分离 | 缺共同安全门AUTHORITY_FENCE_UNAVAILABLE阻新效果；原operation只读可查，旧权限/旧批准不套新对象 |
| Required-OPS-03 | ERP Owner | 现inbox/result/bridge三路重排原件兼容、事务审计/lease/unknown与原回执读API证据 | 保原入口语义，不称全应用恢复完成 |
| Required-OPS-04 | Main各Bridge Owner/S1–S4 | 每route业务键、原结果查询、bool/SKIPPED分类、原子性/lease/重复保障；Space采纳原回执 | 不把Space加强覆盖到其他route |
| Required-PLT-01 | Platform/S7 | ICp6InboxPreCheckpointV1/ProcessWithPreCheckpointV1准确版本、应用持事务及§3.5共同锁序/封闭decision/Dapr映射/全早退覆盖；另核RequeueDeadLetteredAsync应用权限与原子审计 | 缺前置内容门关闭相关新授权/依赖新写，可可信PendingInspection；不直暴露Requeue原语 |
| Required-PLT-02 | 应用部署Owner/S7 | producer/consumer/schema/topic/contract-bundle矩阵、单写route epoch、旧消费者关停/回退证明 | 不开双consumer、不能直接停旧消费 |
| Required-AUD-01 | SYS-AUD/SYS-PRIV/S5及业务Owner | 原结果/撤权/重放/技术文档/PII保留冲突策略、访问/导出权限及legal hold依据 | 通用retention不自动启用 |
| Required-API-01 | Main/CRM/API Owner | 3.2候选路由、DTO兼容、错误码、分页、权限码及客户端版本签认 | 新API仅设计；不冒已有 |
| Required-UI-01 | Main/CRM FE Owner | 旧COMPENSATED文案/历史保留策略、身份失效缓存清理、UNKNOWN重试交互 | 不能声称页面已修改 |
| Required-TEST-01 | 各Owner+独立评审 | 被授权真实测试环境、AC与原测试identity、原始run/日志、验收人结论 | 70项新AC全NOT_RUN |
| Required-ACCEPT-01 | 主对话总负责人 | 作者STOPPED→独立review→修订→正式接受/Registry记录 | 作者不能自判PASS或更新分数 |

### 6.2 公共变化命中矩阵

| 消费范围 | 命中/理由 | S6交付、消费方应答 | 状态 |
|---|---|---|---|
| SYS-TENANT/IAM | HIT：可信tenant映射、writer/field/cache/epoch关系 | 引用第2及4节；S5给精确policy/mapping/拒绝及恢复方案 | Required/UNPROVEN |
| SYS-AUD/PRIV | HIT：元数据脱敏、原证据保留、UNKNOWN不清理 | 引用5.4；S5确认retention class和字段可见性 | Required/UNPROVEN |
| PLT-CTX/HTTP/MSG/DEL | HIT：服务身份、原bytes/摘要、重排应用gate与事务原语 | 引用§3.3–3.7/5.3；S7确认具体版本调用契约，非另写新权威合同 | Required/UNPROVEN |
| PLT-OBS/RELEASE/TEST | HIT：分层结果、单consumer、发布/运行与fixture证据 | 引用6.3/8；S7确认registry和native证据口径 | Required/UNPROVEN |
| INT-COM-01 | HIT：业务原请求/结果及消息键保持、原owner回执 | 接口消费声明，不接管INT-COM正文、不改商业流程Owner | Required/UNPROVEN |
| SPACE-PUB/WMS-MASTER | HIT（接口兼容）：Space retry fence与WMS采纳分层 | 引用E17/5.5，S4/S1提供Owner原采纳回执；不重写其accepted正文 | Required/UNPROVEN |
| OA-APP | HIT（通用恢复规则）：旧审批对象版本不可套新对象，approved≠applied | 只引用审批决定/应用回执语义，原accepted由S3只读采用 | Required/UNPROVEN |
| Finance/制造/仓储其他route | PARTIAL-HIT：现dispatcher确有FIN/MES/WMS路径 | 各Owner逐route确认业务拒绝和原结果查询；此稿不定义其过账/库存恢复规则 | Required/UNPROVEN |
| DG14各行业专项 | TO_ASSESS，不自动重写 | 专项只报告本合同tenant/field/receipt/replay变更具体命中；无代码调用证据不称NOT_HIT | Required/UNPROVEN |
| ERP-CREDIT-01主链 | OUT_OF_SCOPE | 不读取/修改其候选业务文书，不扩任务或复用其未决状态当S6证据 | FROZEN_TO_S6 |

### 6.3 旧入口/版本/消费者的兼容与切换

1. Contract-bundle current两库相同blob，现topic cp6.platform.events.v1和五事件v1不更名。本稿只补应用接入行为、诊断/回执/资格；若未来确需字段扩schema，另走producer/consumer矩阵与正式审批，不把增加文书当许可
2. Main原Bearer、C02.Services、CRM browser session保各profile，scope/audience不跨用。相同名字“tenant”不足以互认，始终核issuer/client/subject/region及服务token记录
3. legacy Bridge仍持T_IntegrationEvent；不迁全量到Kafka。ERP命令队列、result outbox、OrderBridgeDispatch保原键/审计。CRM identity使用现consumer cp6-crm-identity-v1；新诊断adapter只读，不成为第二consumer
4. 新恢复接入按tenant+route+message/schema窗口登记RouteActivation：oldOwner/newAdapter、singleWriterEpoch、effectiveBoundary、已知积压范围、OwnerResultLookup能力。它是部署证据逻辑记录，不是现存表证明
5. 切换先盘点原存量与status、payload/hash、原Owner结果和依赖，停止新增旧route接纳的条件由Owner批准；旧in-flight须有终态或明确移交回执。只有旧消费者确实停止写、未决消息明确归属、新owner gate就绪才能启新执行consumer。observer可并行只读，不能双写
6. 回退不回滚已提交业务/审计。停止新接纳，保当前原回执和route epoch；按兼容矩阵让支持原schema/版本的唯一consumer继续。如果旧消费者不认识已发新schema，不能直接降包强读；隔离并修复adapter
7. 所有历史DEAD/COMPENSATED/旧token origin保native值。旧COMPENSATED与本稿业务Resolved通过附加evidence状态映射，不批量改旧账或删除历史；historical unknown不能默认NOT_APPLIED
8. 源码、包引用、Host注册、配置Enabled、环境迁移、实际收到消息、Owner业务采纳、独立接受八个证据层单列。存在测试fixture/RecordingStub不证明生产接入；历史test run原样保留，不改写为本轮PASS

## 7. 可直接进入实施评审的开发任务拆分

下表是开发待办设计，所有状态PLANNED_NOT_AUTHORIZED，不提交代码/SQL/PR。任务依赖表示设计/接口就绪关系，不机械当开工DAG：不依赖未确认生产Owner的纯UI模型和契约测试设计可独立准备，真实写入能力须Required闭合并另获授权。

| 任务ID | Owner与准确落点 | 交付内容/完成边界 | 关联SPEC/前置/验收 |
|---|---|---|---|
| ID01 | Main IAM+S5；CrmOidcOptions/Directory、Identity配置 | 原tenant/slug/issuer/region/client来源矩阵；区分展示改名与身份迁移；收敛全部配置入口 | ID-01-01；Required-IAM/ID；AC-ID01-01/02/04/06 |
| ID02 | CRM Owner；CrmIdentityProjectionExtensions/Options、登录入口 | 注册与storage组织/region一致性、mapping诊断DTO，未知映射fail-closed | ID-01-01；ID01；AC-ID01全组 |
| ID03 | Main IAM；IdentityChangeCapture、SnapshotWriter、CP6Context | 完整writer inventory与当前同事务机制复用；补未捕获路径方案，认证失效/role变更区分 | ID-01-02/03；S5writer证据；AC-ID02-01/02、ID03-01/04 |
| ID04 | S5主导、CRM消费；PermissionAggregator、在线Context DTO | 明确scope与field默认/合并政策，源字段→resource/action/DTO映射及policy版本合同 | ID-01-02；Required-IAM-02；AC-ID02-03/04/07 |
| ID05 | CRM业务/FE Owner；Actor、AuthorizationReader/Policy、Views/业务mutation | 字段决策贯穿CRUD/list/export/search/历史结果；在真实业务事务消费§3.6既有Owner最终门；分库无等价门关闭新效果；清FE受保护缓存 | ID-01-02；ID04；AC-ID02-04/05/06 |
| ID06 | Main身份+CRM会话；Priority dispatcher、ProjectionStore/Origin | 精确撤权origin/epoch/token保留；共同fence撤权入口/并发语义及缺门关闭；真实event/snapshot分列与未证SLO | ID-01-03；Required-ID-04/AUD；AC-ID03全组 |
| ID07 | CRM Auth；AuthenticationService/AuthStore/Endpoints | 每请求online+projection、touch/删除并发、logout独立结果，核全受保护入口 | ID-01-04；Required-ID-02；AC-ID04-01/02/03/04/06 |
| ID08 | CRM FE；CrmPageState/AppShell/认证页 | 401/403/409/503、组织切换/Back/多tab/迟到response/会话失效交互；禁止旧数据缓存回显 | ID-01-04；ID07接口；AC-ID04-05/07 |
| ID09 | Main持久化Owner+CRM；SnapshotReader/generation migration、ReconciliationClient | provider-specific generation原理与opaque boundary兼容；cursor/最终目录条件；不改schema v1 | ID-01-05；Required-ID-03；AC-ID05-01/02/03/07 |
| ID10 | CRM投影/FE；ReconciliationCoordinator、Published、诊断页 | 按§3.5实现CRM持事务PreCheckpointV1 seam调用、锁序/decision、冲突+DRIFT+published原子更新及§4.5恢复CAS；保source-only origin和lease诊断 | ID-01-02/05；ID09/Required-PLT-01；AC-ID02-02、AC-ID05-02/03/04/05/06 |
| OP01 | Main/CRM各Owner；原queue read adapters | 统一FailureDetail和§3.3三allowedAction/nativeType/digest域映射，bridge无envelope不伪造；不复制业务主数据 | OPS-01-01；Required-OPS-01；AC-OP01-01/02 |
| OP02 | Main/CRM API Owner；新只读/failures adapter | 稳定分页/cursor、partial/时间来源、安全错误、current-scope过滤及原详情定位 | OPS-01-01；OP01；AC-OP01-03/04/05/06 |
| OP03 | Main FE；BridgeHealthView及新详情；CRM FE对应状态组件 | 旧COMPENSATED重标说明，双状态列表，安全复制/分页/租户切换 | OPS-01-01；OP02；AC-OP01-06/07 |
| OP04 | 各业务Owner；旧Bridge route接口/dispatcher | 逐route将bool/SKIPPED补具体业务结果分类，未证明route保持只读 | OPS-01-02；Required-OPS-04；AC-OP02-01/05/07 |
| OP05 | ERP/CRM业务Owner；现request journal/handler | 明确business/input/msg/operation键、原结果复用、terminal/unknown判定；不触ERP-CREDIT正文 | OPS-01-02；Required-OPS-01；AC-OP02-02/03/04/06 |
| OP06 | FE/API Owner | FailureClassification DTO及各状态交互，保原Owner code/version/transport状态 | OPS-01-02；OP04/05；AC-OP02全组 |
| OP07 | Main IAM+各接收Owner | §3.6按read/fact-receive/delivery/new-effect分类消费既有共同最终门及完整共同写者，逐route缺门关闭；安全门不替代用途/目标版本门 | OPS-01-03；Required-OPS-02/IAM；AC-OP03-03/04/05 |
| OP08 | ERP Owner；ErpInboxReplayService/DeliveryReplayService及controllers | §3.4先原operation回查，仅新接纳走gate；§3.3三分支摘要映射到旧PayloadSha256，保原hash/rowVersion/reason原子审计；不破旧DTO | OPS-01-03；OP05/07；AC-OP03-01/02/04/06 |
| OP09 | 各应用API/FE Owner；候选eligibility/replays/status | 按§3.3typed摘要/§3.4先回查后新调度/§3.6最终门编排；提交UNKNOWN保持同ID，过期eligibility不阻历史查询，202/Cancel语义明确 | OPS-01-03；OP07/08、Required-API；AC-OP03-05/06/07 |
| OP10 | 各Owner审计；ReplayAudit/原journal | 原actor/operator/worker分层，typed action/digest tuple、原operation回查零新审计、最终门fence/epoch证据与append-only关联；历史unknown保真 | OPS-01-04；Required-AUD；AC-OP04-01/02/03/05/07 |
| OP11 | S5 AUD/PRIV+应用Owner | retention class/legal hold/敏感导出政策与访问审计，区别技术默认与业务保留 | OPS-01-04；Required-AUD；AC-OP04-04/06 |
| OP12 | 各接收业务Owner；OwnerResult read adapter | 以原操作查询已提交journal，完整Receipt DTO、先原operation读分支；可信NOT_APPLIED证明条件；缺者仅阻新重放而保合法历史读 | OPS-01-05；Required-OPS-01/API；AC-OP05-01/05/06 |
| OP13 | Producer+consumer应用Owner；原result outbox/inbox | 原结果重投、固定RequiredConsumerSet/原native完成谓词、consumer终态映射及单调处理；分离证据齐/业务完成 | OPS-01-05；OP12；AC-OP05-02/03/04/07 |
| OP14 | Main/CRM FE Owner | 四层对账页、原对象/现对象版本、UNKNOWN只读路径、同operation重开恢复 | OPS-01-05；OP12/13；AC-OP05全组 |
| X01 | S6拟稿/S5签认 | 固定authority/tenant/field/版本合同；消费声明精确版本与拒绝/恢复映射 | ID01–05；Required-IAM；不能把讨论当采纳 |
| X02 | Main/CRM/S7 | 消费OA-APP §15.1及INT-COM R1§2.3–2.5既有门：定义OwnerFinalAuthorizationGateV1 adapter、同库锁序/共同写者、分库缺门关闭和四类权限分支；真实采用取证计划 | ID02/03、OPS03；ID05/06/OP07–09；Required-ID-04/OPS-02；实现未授权/运行UNPROVEN |
| X03 | Main/CRM/业务Owner | 原结果查询与identity baseline两个不同对账流程/合同窗口 | ID05/OPS05；Required-ID-03/OPS-01 |
| X04 | 应用发布Owner/S7 | 逐route单consumer/route epoch/旧积压/停旧/回退审阅方案 | OPS03/6.3；Required-PLT-02；不启双写 |
| X05 | 独立测试/评审Owner | 70AC test design、原测试映射、fixture/生产证据层、缺证与回归范围；独立验收 | 第8节；Required-TEST/ACCEPT；本轮NOT_RUN |

### 7.1 逻辑数据与迁移任务（仅文书）

- Main IdentitySnapshot/Generation/priority队列及CRMProjection/Published/RevokedTokens现表保留；先检查目标provider现迁移与索引/并发标记，缺运行证据不能宣布已迁移。Main现generation forward-only，不提供回滚脚本
- Owner新增审计/回执关联尽量复用原表，不建立跨库写视图。新列以nullable/显式unknown表达历史缺件；不能回填当前权限版本、默认APPLIED、推算撤权时间
- Replay新旧operation唯一性与原字段命名保持，审计扩展有独立formatVersion。byte[8] concurrency token和GUID/native key格式兼容应在现provider真实测试中证；不将不同provider token做同名强转换
- 所有迁移需迁移前存量只读盘点、数据Owner审批、备份/恢复与未决消息保留计划。本轮没有候选SQL、migration代码、validator、DB操作或CI

### 7.2 FE细分验收责任

身份诊断由CRM身份Owner持有，只读复用Main管理跳转；不提供本地新用户/新角色入口。运维恢复页属于目标应用Owner，不放在Platform做全租户总开关。API作为授权权威返回availableActions、current mask和Owner结果；前端只负责呈现和提交预期版本。通用组件要覆盖Loading/Empty/Forbidden/NotFound/Conflict/Unavailable/UNKNOWN，键盘focus、aria-live、重复点击和请求序列均纳入AC。新增i18n词条由SYS-LANG Owner确认，旧界面指标不能误变为本稿已接线证明。

## 8. 验收、测试文本复用与证据分层

本稿70条新AC全部NOT_RUN，见AC-INDEX.json精确定位。测试设计可以复用已有文本，但不得执行。已有文件名含SqlTests或E2E不代表当前真实DB已运行；原测试内stub/in-memory/fault injection的角色须记录，不升格Owner证据。

| 原测试文本 | 可复用的断言主题 | 仍需补的验收证据 |
|---|---|---|
| Main CrmIdentityChangeCaptureTests | 展示字段不发布；密码/epoch/2FA/lock/disable捕获；role与user身份；refresh轮换；未配置tenant | 全writer清单、真实事务/outbox与provider并发 |
| Main CrmIdentityContractTests | 五种合同、tenant/version/hash语义、空grants合法撤权 | 真实双方同bundle、已部署consumer及当前接入 |
| Main CrmIdentityRevocationTests | 签发原token记录、持久失败不返回token、认证撤销范围 | 真正Main→CRM撤权延迟及在途业务gate |
| CRM IdentityProjectionTests | 动作/范围交集、缺失依赖关闭、revocation/epoch、停用角色、祖先 | 字段粒度完整mapping和各业务endpoint执行 |
| CRM IdentityRevocationOriginTests | 原origin绑定、历史null、不可推算时间、仅nullable迁移 | 真实source event与snapshot分别保存、生产retention |
| CRM CrmAuthenticationServiceTests | bound登录一次性、expiry、撤权、logout失败、组织切换 | 真实cookie/HTTP/多tab/缓存清理及全入口 |
| CRM IdentityReconciliationClientTests | auth拒绝缓存token替换、技术故障不乱失效 | Main新generation分页/写并发与final baseline闭环 |
| Main IntegrationEventDispatcher/RetryDeadLetterE2ETests | 原路由、payload传参、原Space correlation、重试耗尽告警 | 非Space业务结果分类、原结果查询及当前资格 |
| Main eng/crm/erp-integration-tests/ReplaySqlTests | 原message/payload重排、同operation幂等、并发前置、append-only审计 | 调用者当前权限、原业务authorization/UNKNOWN跨Owner证据 |
| Main DeliveryReplaySqlTests | result/bridge独立原记录重排及审计 | consumer实际采纳和最终Owner完成证据 |
| Platform TransactionalMessagingContractTests | 默认lease/retry/retention、稳定安全code/telemetry | 应用authorization/retain合法性/真实事务调用 |

每项未来执行记录至少：AC ID、原SPEC ID、环境及tenant/route、repo完整SHA/包版本、配置identity、case input nativeRef及digest域、运行开始结束、期望/实际、Owner原回执、原始日志bytes/hash、工具返回、执行人、独立review。结果允许NOT_RUN/BLOCKED/FAIL/PASS；本轮只NOT_RUN，缺依赖单列BLOCKER，不将未跑标PASS。安全验收以授权的防御性验证/回归为准，不给动态漏洞复现或绕过步骤。

六层结论模板：Source=PRESENT/PARTIAL/UNLOCATED；HostRegistration=STATIC_CONDITIONAL/STATIC_PRESENT；ConfigEnabled=UNPROVEN；Runtime=NOT_RUN；OwnerAdoption=UNPROVEN；DesignAcceptance=NOT_ACCEPTED。任何一层不能替代另一层。

## 9. 作者交接和未决问题

### 9.1 本轮未决，不影响独立正文完成

1. 原Excel母版/两册native尚未取得，不能称完成原格式native填册；本轮静态四根复审不以补母版或运行证据为前置，仍保Required/UNPROVEN
2. S5字段默认/权限码及所有Main writer失效覆盖未签认；本文已给确定消费接口/关闭语义，实际政策/采用Required/UNPROVEN，不以S5未完成整册悬置静态设计
3. Main最新持久generation与CRM现consumer静态可以opaque string对应，真实provider迁移和跨仓运行未验
4. 通用OwnerResult按原operation查询API在已读范围未定位；ERP内部journal可复用，其他route原回执/gate必须Owner证据
5. 原Bridge的best-effort ledger/compensate语义不能被包装成已原子恢复；只读映射和后续adapter计划均已列
6. S7/CRM尚未实际采用§3.5选定的PreCheckpointV1同事务接入；Owner尚未实际采用§3.6最终门的范围仍关闭新效果。接口设计已确定，签认/运行仍Required/UNPROVEN，不以缺证再悬置设计或自定停旧消费者
7. 所有新页面/API/字段决策/Owner证明属于候选设计，源码未修改，部署/测试/DB/CI未运行
8. 父主对话需安排独立review，指出具体SPEC/章/字段/AC问题；作者只能按问题修订新版本后再次STOPPED，不自行accepted或改Registry

### 9.2 交接完整性

交接包包含本文全文一次、CURRENT/main索引、原范围、四库fresh观察、原输入全文、SOURCE-READ-INDEX（完整SHA/blob/字节/hash/读取层级）、源文本、原SPEC覆盖/复用缺口、AC索引、开发任务索引、Required清单、REVIEW-HISTORY和普通字节清单。文件为本执行器真实生成，不引用旧桌面路径假装可读。旧桌面identity作者已在01a1163a-5f77-766c-874f-e1a44fa8a00b明确STOPPED；其120源读/14落盘只是历史线索，本稿源证据重新读取，不继承未验证全文。

冻结时作者状态STOPPED，完成的是本组设计候选而非软件交付、生产接入或原Workbook接受。后续新writer须先核此CURRENT/main/hash与STOPPED版本，再开始续写；不得把本索引再打包进递归历史大包。所有原accepted、Registry84和母版保持只读。
