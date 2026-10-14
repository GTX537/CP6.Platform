# PLT OBS 可观测性与运行身份完整静态设计

版本 v0.1，S7，2026-10-08 UTC，AUTHOR_FROZEN_FOR_INDEPENDENT_REVIEW。原四项SPEC完整设计；所有新AC=NOT_RUN，RuntimeAdoption=UNPROVEN，ImplementationAuthorized=false，ProductionDeploymentAuthorized=false，Enabled=false。本稿不运行遥测、健康探测、验证器、测试、CI或部署；原计划工作簿仍未创建。

## 1 原范围与证据

PLT-OBS-01，Owner=CP6.Platform，lane=共享支撑，change_proposal=ADJUST；CUR-PLATFORM-011 Observability and SLO evidence、012 Release identity 均PARTIAL_SLICE/WIRED。scope=Trace/指标、SLO证据、运行版本、运维端点。遥测和发布身份用于排障，不能替代不可变业务事实或用户验收。保留DG13、COMMON及DP10/DP12/DP16/DP17。

固定SHA：Platform 30bd23af6808d217a23878bd9437513043c52834、Main 90c871fe571fd6b390f53e8678376d7ce60bcb60、CRM c778a3052a4416b82facb07b1244fc98fb6ad8a1、coord ad82a18c9511c3a8d3387060a9e2ed4755381e1d；00:46 UTC fresh HEAD同值。原scope见SCOPE_EXACT_EXTRACT.json；SOURCE_INDEX.json和SOURCE_INDEX_ADDITIONS_OBS.json提供逐文件固定URL/hash。已实读Cp6ObservabilityProfile/ServiceCollectionExtensions、TelemetryConventions、MessagingTelemetry、EntityFrameworkTelemetry、SloEvidenceDocument/Evaluator、ReleaseIdentity/Accessor、OperationalEndpointProfile/RouteBuilderExtensions、SafeHealthResponseWriter、TraceContextDistributedPropagator、CRM Program；相关测试文本不代表运行通过。P_Program是SQL fixture，不是Main/CRM生产Host。

S6 v0.2正文SHA256 d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc为身份/恢复准源。OBS只观察其read/新作用/原回执/typed recovery，不增加写业务状态权。PLT-HTTP/MSG/DEL既定事务和真实Owner不变；RELEASE负责包身份/信任来源，本册负责身份暴露与观测关联。

## 2 Actor 权限与共同模型

Platform维护source/meter/tag合同、观察器、SLO DTO及安全健康输出；应用Owner定义业务SLI口径、原receipt和提交事实；SRE/运维Owner登记采集器、查询定义、覆盖率、告警接收与环境；安全/隐私Owner审查可见字段/外送目标/保留；独立评审者核验证据与声明；普通用户只看到其业务operation状态和安全correlation。

遥测listener/exporter无业务写权；不能触发重放、删除Inbox、开写门或改变用户权限。运维read权限只覆盖获准app/tenant/region，跨租户聚合需明确授权及去标识口径。traceId、requestId、tenant、user、messageId、operationId和payload摘要为高基数/敏感关联，不进入公共metric标签；必要诊断关联存在受控日志/业务审计中并受Owner权限。

候选 ObservabilityAdoptionV1：app、hostId、sourceSHA、packageIdentityRef、profileVersion、releaseIdentityRef、metricDefinitionsRef、redactionPolicyRef、exporterDestinationRef、collectorConfigurationHash、samplingPolicyRef、resourceAllowlistRef、healthCheckInventoryRef、permissionPolicyRef、retentionPolicyRef、codePresent、hostRegistered、configEnabled、runtimeObserved、businessReceiptBound、independentlyAccepted、lastObservedAtUtc、safeCode。每层独立状态UNPROVEN/PROVEN，不从上一层自动推导；尚无真实目标采集器或输出权限则不外送数据。

证据类型分清：观测日志可以晚到/丢失/采样；不可变业务审计/receipt在业务事务内，不能由观测counter替代；SLO文件是按固定查询/窗口形成的统计证据；静态架构审阅只证明设计文本或代码结构，不能证明业务AC执行。所有证据带内容hash和来源/环境/时刻，不把同名文件或图截图当精确版本。

## 3 SPEC-PLT-OBS-01-01 Trace与指标

现三个ActivitySource/Meter均为CP6.Platform.AspNetCore、CP6.Platform.Messaging、CP6.Platform.EntityFramework。现六operation为cp6.http.outbound、cp6.messaging.dapr.invoke、cp6.messaging.publish、cp6.messaging.consume、cp6.outbox.dispatch、cp6.inbox.process。metric标签allowlist仅cp6.region、cp6.operation、cp6.outcome、cp6.error.code、cp6.messaging.transport、cp6.messaging.disposition、cp6.http.operation_kind；允许键不代表任意值都低基数，目标值必须各自固定枚举。

现MSG counters：cp6.messaging.published/consumed/rejected/trace_context.rejected，duration为ms；EF counters cp6.outbox.operations/cp6.inbox.operations，duration与oldest_available.age为ms，attempts为数值histogram，age/attempt数值不能当label。现MSG consumed发生在delivery合同验证通过时、未等待业务commit；published发生transport调用返回；EF不同disposition发生具体库调用点，enqueue还可能在caller commit之前。因而这些既有指标是技术操作计数，不可直接作为业务成功/财务/库存变更总数。

目标MetricDefinitionV1：name、version、unit、kind、measurementPoint、commitRelation(BeforeCommit/AfterCommit/ObserverOnly)、attemptOrBusinessScope、allowedTagValues、resetSemantics、aggregationWindow、owner、sourceRef、independentReviewRef。既有名字语义不偷偷改变；新增业务已提交计数由业务Owner在原receipt commit后生成观察事实，标明可丢/可重复，不反写业务；精确业务统计仍从原receipt去重查询。S6 duplicate/precheckpoint/rejected/unknown按业务与transport两个维度分开，不能一列success吞没。

注册现AddCp6Observability验证profile，重复相同profile幂等、不同profile拒绝；profile serviceName≤64，environment/region≤32且canonical lower name；service/version与ReleaseIdentity一致。注册source、ASP.NET/HttpClient instrumentation和metrics，exporter-neutral，无默认已送达collector证明。它修改进程全局DistributedContextPropagator及OpenTelemetry TextMapPropagator；同进程多Host或第三方再次改propagator需登记最终生效顺序，不从DI单次注册推导全局永不变。

传播仅traceparent/tracestate，不传播baggage；MSG采用有效远端parent，invalid trace计数并丢parent。trace不能成为认证证明、可信租户源或任意自动重试依据。外部trace关联可能被指定，敏感访问仍按CTX/业务Owner校验。HTTP关联ID和traceID不是同一格式，保HTTP定义，不强行混用。

隐私处理：原自定义MSG/EF标签有严格值allowlist，录制内部异常被捕获以保持observer-only；自动ASP.NET/HttpClient instrumentation可能含URL/query/headers等默认字段，本注册代码未配置全量清理，不能宣称所有遥测已无PII。目标Host在export前仅保方法、模板化route、状态类、duration、低敏error enum、登记resource；去除query、完整URL、Authorization/Cookie、body、SQL参数、登录code、完整tenant/user/message/operation值。CRM Program禁止请求URL相关日志的原限制不撤销；exporter安全处理需实际证明。

采集/故障：采用有界队列与批量，队列上限、flush预算、drop策略由具体exporter profile明确；满队列丢观测并独立报告collector健康，不阻原业务commit，不在业务请求无限重试export。若合规审计是业务硬前置，则使用Owner事务审计通路而非把telemetry变同步强依赖。进程退出best-effort flush不能声称无丢失；Counter重启归零、多个instance合并、重复retry都在查询定义明确。

API/FE：应用只读TelemetryBindingView显示release/profile、采集状态、lastSeen、缺证/丢弃计数和安全code；不公开原trace payload。支持从有权限的业务operation跳到受控诊断引用，不把凭据放URL。exporter离线显示“观测缺失”，业务页仍按receipt显示真实结果，不由dashboard无数据推断业务失败。

AC（NOT_RUN）：OBS01-A1 原source/meter/operation及标签键值枚举；A2 age/attempt为数值且高基数不入metric；A3 consumed/published/enqueue时点不冒业务提交；A4 trace-only传播与无baggage、非法parent处理；A5 自动instrumentation隐私清理含URL/query/code；A6 exporter异常/满队列不改变事务且缺证可见；A7 多Host全局propagator/注册漂移准确识别；A8 instance重启/重复/采样口径与业务receipt分离。

开发 OBS-T01：Platform补精确measurementPoint说明及versioned tag词表；OBS-T02：每Host export清理/有界profile/实际采集注册；OBS-T03：业务Owner receipt统计口径和只读诊断。缺隐私/目的地证明不启export，缺业务口径不作业务成功声明。

## 4 SPEC-PLT-OBS-01-02 SLO证据

现Cp6SloEvidenceDocument={schemaVersion,evidenceId,generatedAtUtc,release,sli,window,measurement,sources,completeness,result,productionSloClaimed}，schemaVersion=1.0.0。Parse只收非空≤1MiB JSON、MaxDepth32、禁止注释/尾逗号/递归重复属性、未映射字段拒绝；UTC时间，generated≥window.end。Parse重算result须相同，不能仅接受文件自报Pass。

SLI字段={id,definitionVersion,definitionDigest,unit,aggregation,comparator,threshold}；aggregation=Ratio/Percentile/Gauge，comparator=GreaterThanOrEqual/LessThanOrEqual；Window={startUtc,endUtc,expectedCoverage,observedCoverage}且start<end、coverage∈[0,1]；Measurement={sampleCount,value,numerator?,denominator?,percentile?,excludedCount}，样本≥0，排除0..sampleCount，numerator≥0、denominator>0、percentile0..100；Source={sourceType,sourceId,definitionDigest,releaseArtifactDigest,queryDefinitionDigest,evidenceArtifactDigest,exclusionsVerified}。digest使用sha256:小写64位，identifier≤128；具体字段语义保原JSON拼写。

现Evaluate fail-closed条件：release.Candidate=true，completeness=Complete，expectedCoverage=1且observedCoverage完全相等，sampleCount>0，sources非空，SLI/source各digest格式有效且source release与definition digest精确绑定；有exclusion时所有source需exclusionsVerified。任一缺失→Indeterminate；其余只比较measurement.Value与threshold得Pass/Fail。它不下载/核hash实体、不验证查询来源真假、不推导ratio/percentile，也不以ProductionSloClaimed旗标验证生产声明；此局限须准确保留。

目标先于原Evaluate的EvidenceQualificationV1（应用/证据Owner适配，当前未实现）字段：evidenceId、evidenceContentHash、environmentKind、authorizedWindowRef、sliDefinitionHash、queryBytesHash、sourceArtifactHashes[]、releaseBindingRef、populationDefinition、samplingCoverageEvidenceRef、excludedPopulationRef、calculationMethodVersion、independentReviewRef、qualificationState、productionClaimAuthorized=false。依真实不可变内容和被批准SLI口径校核后才可把统计result作为候选资格化结果。仅校验hash字符串格式不算内容验证。

计算口径必须精确：Ratio记录计入总量N、成功/合格分子K、排除数及原因，0≤K≤N且N>0，value与definition要求的比例尺度一致；Percentile须定义p值、bucket或原始样本、插值方法、权重、时间和单位，不平均多个实例p95；Gauge定义采样频率/时点/跨实例聚合及missing规则。sampleCount/excludedCount到底是原始还是有效数量由definition明确，不能重复扣除；ObservedCoverage针对定义总体/时间/实例而非“收到多少就是100%”。本稿不凭空设置业务SLO阈值，缺Owner已批准threshold/范围→Indeterminate。

窗口状态：Planned→Collecting→Closed→ArtifactsFrozen→Qualified→Evaluated→IndependentlyAccepted，修订另建evidenceId/version并保原文件/结果；LateData/缺测/查询变化不得原地改已接受证据；新结论引用supersedesRef并说明范围。连续窗口不可挑选最好片段，deploy滚动跨多个release须分release子窗口或准确mixed-release定义，不能拿一个Candidate digest冒全部实例。计数丢失、查询失败、collector断档保Partial/Missing与Indeterminate，不零样本Pass。

候选环境通过仅代表指定release/route/window下观察，productionSloClaimed始终false直到有独立生产采集/覆盖/批准证据；测试fixture、故障注入、记录器数据绝不混入生产SLO。即使合法候选result=Pass也不能自动提升业务AC/用户验收或注册表完成度。系统状态变量SLO Pass与本设计AC NOT_RUN并存而不互相改写。

保留和权限：查询定义与证据原字节不可变归档，hash/作者/来源/窗口/权限/保留类别可查；业务明细按tenant/region和字段策略脱敏；统计证据如导出第三方仍按批准目的地，拒绝任意外部URL回源。审计保留与隐私删除冲突交Owner hold，OBS不自行清数据。技术文档生命周期归PLM/文件服务，统计观察不创建第二PLM主账。

API/FE：库Parse/Evaluate为纯计算，当前不运行；应用候选只读SloEvidenceView显示definition/version/hash、release身份、window、样本/排除/覆盖、Complete/Partial/Missing、Pass/Fail/Indeterminate、qualificationState、sourceRefs和声明层级；不可由普通用户修改value/coverage后点击通过。对缺来源显示“证据不足”，对不达阈值显示“该窗口未达标”，不从Fail直接回滚业务。

AC（NOT_RUN）：OBS02-A1 原JSON字段/1MiB/depth/重复键/UTC约束；A2 原Evaluate精确条件及result重算；A3 源实体/查询/definition/release内容身份真实绑定；A4 ratio/percentile/gauge单位与计算一致；A5 真实覆盖/缺测/零样本保持Indeterminate；A6 候选与生产分离，flag不自证且无测试混入；A7 修订/晚到/多release窗口血缘保原结果；A8 权限/保留/hold且SLO不冒业务验收。

开发 OBS-T04：业务Owner冻结SLI definition/阈值/总体/排除口径；OBS-T05：EvidenceQualification适配及可审计计算证据；OBS-T06：文件服务不可变证据/FE安全只读。缺定义/真实来源/覆盖任一则Indeterminate，不能人工填写Complete绕过。

## 5 SPEC-PLT-OBS-01-03 运行版本

现Cp6ReleaseIdentity={service,version,gitSha,artifactDigest,contractBundleDigest,candidate}；内部Mode=NonCandidate/Candidate。service≤64且canonical lower name；version非空≤128、无首尾空白/控制字符；Candidate必须canonical SemVer不含build metadata，可有合规prerelease；GitSHA为40位小写hex、digest为sha256:小写64位。Candidate三身份必填，NonCandidate可空但非空仍校验格式。它只验证DTO格式，未自行计算运行二进制/包hash，candidate=true不是已发布/可信/生产。

AddCp6Observability安装单例accessor并向resource提供service/version、deployment.environment.name、cp6.region、cp6.release.candidate及存在时git/artifact/contract digest。资源维度不承载tenant/用户；release的值必须来自批准的不可变构建/部署清单，不由外部请求/浏览器参数注入。容器镜像digest、.nupkg digest、bundle digest三者各域，不把NuGet包hash填image artifact而不说明身份种类。

目标 RuntimeIdentityBindingV1：instanceRef（受限、非metric标签默认）、service、declaredReleaseIdentity、observedArtifactKind、observedArtifactDigest、packageSetRef、configurationDigest、schemaSetRef、deploymentRef、startedAtUtc、observedAtUtc、verifierRef、comparisonResult(Match/Mismatch/Unproven)、evidenceRef。由部署/运行Owner实际观察生成；应用自报只能Declared，不以自报与自身DI相同证明已运行正确字节。RELEASE关联包可信签名/来源/依赖矩阵，OBS只暴露安全已审核部分。

状态Declared→FormatValid→ArtifactBound→HostObserved→IndependentlyQualified；Mismatch/Unproven不得映ReadyForNewEffects。目标错误码RUNTIME_IDENTITY_UNPROVEN/RUNTIME_IDENTITY_MISMATCH为新候选安全码；对应新能力关闭写流/切流，保只读诊断与原operation read，是否影响旧批准Host由独立发布策略决定，不能本稿主动停止整个系统。滚动升级逐instance/route显示旧新身份，不伪全fleet一致。

幂等/事务：读取运行身份无业务写、无业务事务；重复observed evidence以部署Ref+instance+观测epoch归档，不能覆盖原部署记录。identity变更需新不可变发布和部署记录，进程内不热改version伪“已更新”。未知原操作仍按原业务Owner/routeEpoch读，不因当前版本换了重新执行旧命令。回退保新版本已经接纳的schema/receipt读路径。

API/FE：/health/release现从ICp6ReleaseIdentityAccessor与Cp6ObservabilityRegistration比较，格式正确且对象一致→200，异常/缺注册/不相等→503，输出schemaVersion/status/observedAtUtc及有值release字段；不返回异常细节。这个200仅注册身份一致，不证明运行字节、签名、部署批准或用户接受；UI分别显示DeclaredIdentity与RuntimeEvidence，不写“发布已验证”一个勾。

AC（NOT_RUN）：OBS03-A1 Candidate/NonCandidate字段和格式准确；A2 self-declared身份不冒实测字节/签名/生产；A3 service/profile一致且禁止请求注入；A4 artifact/package/bundle/config身份不混域；A5 /health/release对象一致/异常分支无敏感细节；A6 多instance滚动升级与Mismatch/Unproven精确关闭新能力；A7 版本/部署历史不原地改写；A8 回退与旧operation/schema读路径保留。

开发 OBS-T07：RELEASE/部署Owner真实artifact binding及instance观测；OBS-T08：Host身份来源/health adapter；OBS-T09：FE分层身份与差异提示。缺真实部署/身份核验，RuntimeAdoption保持UNPROVEN。

## 6 SPEC-PLT-OBS-01-04 运维端点

现Cp6OperationalEndpointProfile默认路径/health/live、/health/startup、/health/ready、/health/release，四者不同且规范绝对path≤128；publishedComponentNames为小写规范名≤64且输出allowlist。Map同profile幂等、不同profile拒绝；对startup/ready按对应health tag筛选；Healthy→200，Degraded/Unhealthy→503。Live是静态Healthy+空components，不依赖数据库。Writer统一schemaVersion=1.0.0、status、observedAtUtc，health仅allowlist的name/status，no-store，不输出description/exception/data。

原映射未自动附认证policy或网络范围；可通过返回的endpoint convention builder让Host配置，但当前代码本身不是“仅管理员可访问”的证明。Startup/Ready如没有匹配真实check，不能仅因端点存在/默认Healthy认为依赖全可用；目标Required health inventory确保每必要能力check实际注册、tags正确、执行有界，且无RecordingStub。CRM Program已有自己的live/ready，ready检查auth启用、catalog/org数/DB连通/迁移/write gate/OIDC；不能重复映同路径或把它替成空Platform ready丢掉原检查。

HealthCheckBindingV1字段：componentName、purpose(Live/Startup/Ready)、checkType、requiredForRoutes[]、dependencyRef、timeoutMs、failureClassification、startupOnly、permissionScope、registrationLocation、safeOutputName、evidenceRef、lastObservedAtUtc。Live只证明进程能响应；Startup检查必需配置/初始化/不可变资源就绪；Ready检查当前承接指定route的必要依赖/共同fence/实际schema，避免把“能TCP连接DB”当业务可写。业务Owner最终门仍在每次提交执行，不能用一分钟前readiness缓存授权。

读/写分层：某新写路线依赖final fence未采用时该route不可写并在只读adoption显示Blocked；原历史operation读取如其读依赖可用保持单独ReadyRead，不因为新写缺门遮蔽全部历史。若部署平台仅一个全局ready布尔，Host须明确是否整个instance摘流，必要分host或route能力，不在文书假设可按route摘流。health GET不做迁移、清Inbox、补数据、创建topic或重放。

访问协议：最小公开live可只留schema/status/time（允许暴露经Owner批准）；startup/ready/release按运行环境明确内网probe身份或只读运维policy，不能要求浏览器普通业务token作为pod探测身份。应用诊断页面需tenant/app范围read，返回最小safeCode；具体网络/认证配置未实施，本稿不改变。probe频率/timeout/retry以平台资格化profile限定，不因探测失败高频打爆数据库；长check可用短TTL缓存健康事实但明确observedAt/stale，不缓存业务授权。

故障恢复：依赖check异常→503低敏状态，保correlation/记录安全component，不返回连接串；观测collector故障是否影响ready由requiredForRoutes明确，通常不阻业务，但强制审计通道由业务门另处理。check取消传播并计未观测，不能记Healthy。部分components隐藏不改变整体report状态；未列出的依赖仍可能影响overall，support须另有授权详细视图，不暴露给公众。

UI展示live/startup/ready/release四类及观测时间，stale独立色标；允许跳有权限的运行/原业务状态，不提供“忽略检查继续生产”。health green不是SLO达标或业务验收绿灯；选定时间以前的历史状态保不可变观测，不改写为当前。

AC（NOT_RUN）：OBS04-A1 四路径/注册幂等/冲突准确；A2 live静态含义与真实startup/ready inventory分离；A3 CRM原ready不被空检查替换、无重复路径；A4 health输出allowlist/no-store且不泄异常；A5 显式probe/运维权限及部署层采用；A6 有界check/取消/stale/collector影响明确；A7 read与新write能力、全局摘流边界准确；A8 health GET零业务副作用、不冒SLO或验收。

开发 OBS-T10：Main/CRM每Host health inventory与原路径兼容；OBS-T11：部署Owner探测权限/频率/摘流范围；OBS-T12：FE四类状态/时间/权限与read-write分层。全部PLANNED_NOT_AUTHORIZED。

## 7 真实采用Required门与冻稿

Required-OBS-01 每Host真实source/meter/instrumentation/collector注册点、版本和配置hash；02 自动遥测安全字段/目的地/保留与权限；03 每指标measurementPoint、attempt/commit语义和业务receipt口径；04 SLO definition/query/来源实体/覆盖及独立资格化；05 declared与observed artifact身份绑定；06 每必需health check、权限/路径与实际Host兼容；07 经授权环境原AC/原日志及独立接受。缺01/02不启新export；缺03不作业务成功统计；缺04不作SLO Pass/生产声明；缺05保RuntimeUnproven；缺06新route不借绿色健康开写；缺07继续全部UNPROVEN。

四项SPEC各8个AC，共32新AC全部NOT_RUN。本文不创造生产SLO通过、健康运行通过或业务接受记录；作者本版STOPPED交父独审。V2/T06冻结，ERP-CREDIT、SYS业务Owner及S6准源未变。
