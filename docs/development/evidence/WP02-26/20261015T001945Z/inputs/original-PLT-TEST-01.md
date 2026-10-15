# PLT TEST 故障注入与测试辅助完整静态设计

版本 v0.1，S7，2026-10-08 UTC，AUTHOR_FROZEN_FOR_INDEPENDENT_REVIEW。原三项SPEC完整设计；所有AC=NOT_RUN、RuntimeAdoption=UNPROVEN、ImplementationAuthorized=false、ProductionDeploymentAuthorized=false、Enabled=false。本文只写测试支撑合同和未来验收预期，没有运行测试、故障注入、网络流量、验证器、数据库、build或CI。

## 1 原范围与源码依据

PLT-TEST-01，原business_owner=CP6.Platform.Testing，lane=共享支撑，change_proposal=KEEP，原CUR-PLATFORM-016=TEST_ONLY/TEST_ONLY。三scope=测试故障注入、遥测断言、测试约束；原SC23/SC24、DG13、COMMON、DP10/DP12/DP16/DP17引用保留，不在本稿改写原场景定义。仅测试支撑，不能计入正式业务功能完成数或生产注册；原XLSX仍未创建。

固定Platform源30bd23af6808d217a23878bd9437513043c52834，Main 90c871fe571fd6b390f53e8678376d7ce60bcb60，CRM c778a3052a4416b82facb07b1244fc98fb6ad8a1，coord ad82a18c9511c3a8d3387060a9e2ed4755381e1d；00:46UTC fresh HEAD同值。原scope见SCOPE_EXACT_EXTRACT.json；SOURCE_INDEX.json与SOURCE_INDEX_ADDITIONS_TEST.json包含准确URL/blob。已实读Cp6HttpFaultHandler/Script/Outcome、FaultInjectionServiceCollectionExtensions、TelemetryRecorder/RecordedMetric、TestingUtilityTests与Testing.csproj；测试方法只显示已有设计断言，没有执行证明。

现Testing.csproj IsPackable=false，依赖Abstractions/AspNetCore/Contracts/EntityFramework/Messaging，正式七包集合不含Testing。它是repository-only支撑；不能把工程存在、内存stub或若干工具测试等同Main/CRM Host真实采用。S6准源SHA256 d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc与其他S7原语的业务/事务/权限边界不因测试替身改变。

## 2 Actor 对象及权限

Platform Testing维护测试专用类型、记录器和固定行为；测试用例Owner定义输入/预期和证据；业务Owner给原receipt、事务、native摘要和最终fence真实语义；独立评审者检查用例覆盖/原证据；运行环境Owner在未来另行授权具体隔离环境。普通业务用户、生产worker、支持人员无故障开关。测试环境名称不是运行授权，不能将Production改名Test规避保护。

候选 TestCaseContractV1：caseId、targetSpecId、sourceSHA、subjectArtifactRef、scope(Unit/Contract/HostIntegration/RuntimeQualification)、environmentClass、authorizationRef、fixtureIdentityRef、syntheticDataRef、operationKind、preconditions[]、stimulusDescription、expectedTraceRef、expectedTransactionOutcomeRef、expectedBusinessReceiptRef、assertions[]、cleanupScopeRef、evidencePolicyRef、status。本轮stimulus仅文字预期，不提供执行脚本或实际数据；未有授权/隔离/正确版本则NOT_RUN。

候选 TestEvidenceV1：caseId、attemptId、sourceSHA、subjectPackageHashes[]、testDefinitionHash、toolchainRef、environmentIdentityRef、started/completedAtUtc、result(NotRun/Pass/Fail/Indeterminate)、assertionResults[]、safeFailureCode、artifactRefs[]、cleanupResult、reviewerRef。证据属于测试Owner文书，不写业务账；失败/取消保原attempt，重试新attempt并引用原失败，不覆盖记录为Pass。全部示例使用合成tenant/用户/operation，禁止复制真实凭据或生产个人数据。

## 3 SPEC-PLT-TEST-01-01 测试故障注入

现Cp6HttpFaultScript构造复制outcomes数组、拒null条目；Next用Interlocked递增nextIndex，AttemptCount读取递增次数；脚本耗尽抛InvalidOperationException且AttemptCount仍增加，不能把它当已送达网络次数。并发Next只保证分配index不重复，不保证某个业务请求得到预定index；目标并发用例必须另有显式scheduler/每调用独立脚本，不能以race碰巧顺序验证确定性。

原Outcome四类：Status(HttpStatusCode 100..599)直接构造response，未调用inner；Throw(Exception非null)抛给定异常，未调用inner；Delay(>0且≤5分钟)通过TimeProvider等候后调用inner；Success直接调用inner，不自动生成200。Delay可用TimeProvider.System默认或测试可控clock，取消原样传播且未到inner。Success/Delay不是安全mock guarantee，若inner为真实网络handler就可能联网；未来测试harness必须固定终端内存transport，不把工具名称当隔离。

现AddCp6HttpFaultInjection只接受大小写精确Test或CI环境，其他名包括Production/Development/test拒绝且发生在改DI前；同一script引用重复注册幂等，换另一实例拒绝；script singleton、handler transient。此保护只在extension，直接new handler并无环境检查。仅注册也不会自动挂到所有HttpClient；具体named client pipeline attachment是用例责任。生产防误入还须构建/依赖/Host注册审查，不能只靠EnvironmentName。

候选 FaultCasePlanV1：scriptId、caseId、clientName、operationBindingRef、outcomes（只保存kind/safe status/delayMs/exceptionCategory，不保存真实exception文本）、maxInvocations、expectedScriptCalls、expectedTransportCalls、clockKind、timeoutBudgetRef、innerTransportKind=InMemoryTerminal、parallelismPlanRef、cancellationPoint、expectedDisposition、attemptHistoryRef。目标maxInvocations有限且由用例预期决定；脚本耗尽必须Fail，禁止默认为success、循环或真实网络fallback。

未来处理模型：先资格化隔离环境/合成输入→创建独立script/clock/client与内存终端→明确handler位于重试策略内还是外→执行该用例唯一刺激→记录script及inner两个计数→核对原operation/request bytes/状态和取消→dispose handler/listener→封存结果。本文没有执行这套模型。嵌在retry内时每transport attempt消费一个step，嵌外时只观测一个总调用，两者必须在case注明，不能混淆自动重试次数。

与HTTP合同接合：NonIdempotent预期不retry；IdempotentWrite即使键头合法也需远端Owner幂等合同才可称业务一次；状态/异常/延迟只证明client反应，不证明真实服务rollback、数据库锁序或跨库fence。UNKNOWN用例预期保持原operation并查询原receipt，不能只检查“抛了timeout”便算完整恢复。现HTTP attempt timeout不在原transient集合、现零delay/不尊重Retry-After的缺口按HTTP稿测试计划保留，不在Testing里默默修正预期。

应用事务/跨Owner：本库不模拟真实commit就当原子通过。内存fake可指定场景分支作设计示例；实际同connection/transaction、commit未知、lease fencing、CRM precheckpoint及所有早退/异常重读、S6三typed replay与digest冲突必须未来用原Owner真实adapter另验。不能为使测试绿临时移除最终权限门、改tenant或把Main activeTenant先验缺口从用例删掉。

错误和恢复：脚本构造/环境不符→配置拒绝，无服务变更；script耗尽→测试失败，不能吞；delay取消→原CancellationToken、inner未进入；inner失败→保真实测试观察，cleanup后另attempt，不在同case改变预期；listener/exporter失败observer-only是否成立另有断言。所有错误文本避免真实url/token/body，安全类别足够定位。

API/FE：只有上述库构造、Outcome factory与DI extension；不新增HTTP fault endpoint、生产配置开关、普通用户FE或远程注入管理页。FE不适用的理由是目标TEST_ONLY且无业务参与者；测试报告只读显示case/预期/实际两个次数/状态/精确版本/证据，不代替业务页面或业务验收。

AC（NOT_RUN）：TEST01-A1 四Outcome原语义及Status/Delay边界；A2 Success/Delay必经登记内存终端且无网络fallback；A3 Test/CI精确注册、同引用幂等/异实例拒绝及直接构造差距；A4 script计数/inner计数/耗尽含义分离；A5 并发index不冒确定业务顺序；A6 controlled clock/取消与handler位置明确；A7 HTTP原重试/Unknown/幂等预期不被测试改写；A8 fixture不冒真实事务/共同fence及无生产故障UI。

开发 TEST-T01：Testing明确FaultCasePlan和终端/handler位置约束；TEST-T02：各Owner按HTTP/DEL/S6设计完整失败/未知恢复预期；TEST-T03：隔离harness/报告与生产引用防误入方案。当前只计划；缺隔离或业务adapter则相关case NOT_RUN，不能默认skip即通过。

## 4 SPEC-PLT-TEST-01-02 遥测断言

现Cp6TelemetryRecorder创建ActivityListener及MeterListener，默认监听三个Platform source/meter；自定义names须非空唯一，空集合可表示不监听该类。Activity采AllDataAndRecorded，停止时记录；numeric callbacks支持byte/short/int/long/float/double/decimal，统一存double。sequence Interlocked递增、两个ConcurrentQueue，GetActivities/GetMetrics按sequence排序返回快照；这只是本进程捕获顺序，不是跨服务业务提交全序。

原Cp6RecordedActivity={Sequence,OperationName,DisplayName,Kind,Status,TraceId,SpanId,ParentSpanId,Tags,Baggage}；RecordedMetric={Sequence,MeterName,InstrumentName,Value:double,Tags}。tags转immutable dictionary；同key后值覆盖前值，不能用这个快照证明从未产生重复tag；decimal转double可能丢精度，不适合金额/精确大整数结算断言。测试metric语义按OBS单位/measurement point解释，不做第二账本。

现AssertTraceTopology要求每个指定operation恰好一个activity、同一非空traceID、不同非空spanID、按输入次序严格direct parent-child链；它不能直接表达分支/重试多span/合法Dapr中间span。真实P09示例可能有Dapr parent，不能为了套直接链断言删中间span或伪parent。目标GraphExpectationV1={nodes（source,operation,kind,min/maxCount）,edges（parent/link/ancestor）,traceRelation,allowedIntermediates,forbiddenJoins,scopeRef}为测试harness另定义图断言，未称现record API已支持。

EnsureOnlyAllowedTags现只检查名字以cp6.开头的key是否在给定集合，不检查其他vendor/http属性，也不限制allowlist key的value基数；ThrowIfContainsForbiddenData只在记录到的operation/display/tag/baggage/meter/instrument文本中搜索提供的非空唯一token，不扫描所有日志、exception events、网络payload、collector输出。未提供token或未录到的数据不能推出无敏感信息。目标隐私断言须检所有输出通道实际样本与schema allowlist，含URL/query/auth code等，只用合成canary值，不能拿真实secret测泄漏。

队列现无maxCount上限，listener为进程全局，可捕到同时运行其他测试同source事件；Dispose关闭两listener但不等价全进程隔离/清理，队列快照不是天然案件专属。目标用例独立process或互斥collection/唯一source+case scope，限定最大events/metrics和执行时长；超过上限Fail/Indeterminate并停止该测试捕获，不能丢样本后宣称完整。仅测试harness采用，不为生产Host加全量AllDataAndRecorded录制器。

断言顺序：固定source/version/operation inventory→明确定义scope及expected count/graph→listener前置注册→所有预期活动结束后取快照→核code/status/unit/tag key与固定值→核父子/links/因果与不串案→检查合成敏感标记不在准许输出→对照原业务receipt/事务观察→Dispose并验证不再捕获。大量并发record序号只用于可重复观察排序，业务线性化要依原DB/Owner证据。

MSG/DEL接合：MSG consumed成功发生在合同通过而非业务commit；EF enqueue可在caller事务最终结果前计数；Testing须有“业务回滚而技术计数已发生”的反例预期，不能断言出现success必已入账。duplicate/replayed/retained/out_of_order每类都保持原语义，retention hold单writer和S6 precheckpoint内容观察不能由相应metric存在一条替代。metrics、trace丢失或sampling变化只影响证据完整性，不自动倒推业务失败。

观测器异常测试只验证observer-only：业务Owner原结果不受listener/exporter异常改变；必要合规审计失败由原事务门定义，不把审计与telemetry混为可丢通道。对远端采集器真实输出、跨进程传播、生产PII策略，需要授权环境独立资格化，内存Recorder不具备那些证明力。

API/FE：GetActivities/GetMetrics、AssertTraceTopology、EnsureOnlyAllowedTags、ThrowIfContainsForbiddenData、Dispose保现合同；新graph和bounded capture属于候选harness增强。无业务FE；只读测试报告显示期望/实际拓扑、捕获范围、样本数/丢失/溢出、版本与局限，敏感canary匹配失败只给类别，不回显真实秘密。trace ID仅在受限测试报告关联，不传播真实用户身份。

AC（NOT_RUN）：TEST02-A1 record字段/数字转换/sequence准确；A2 原direct-chain限制与graph中间span分列；A3 cp6-only tag检查/禁词搜索范围不冒全量脱敏；A4 合成canary覆盖HTTP自动字段等真实输出通道；A5 并发listener隔离和有界capture/overflow不假Complete；A6 Dispose后停止捕获且不串下case；A7 技术success/业务commit/原receipt分列；A8 observer-only异常不改业务、内存record不冒远端/生产证据。

开发 TEST-T04：bounded recorder harness与case隔离；TEST-T05：图拓扑/固定tag值/精度和隐私检查设计；TEST-T06：Owner原receipt及真实环境采集证据计划。缺捕获范围/结束条件或发生overflow，完整性=Partial/Indeterminate，不给全部Pass。

## 5 SPEC-PLT-TEST-01-03 测试约束

正式业务功能数与测试支撑数分别统计，TEST仅支撑。原Testing IsPackable=false与七包排除保留；生产应用不得ProjectReference或按源码复制Testing类型进Host；生产故障控制面不存在是设计要求，需真实依赖/注册清单证明，不能因当前csproj属性就推导所有consumer无引用。开发/Test/CI与生产界限由真实隔离、网络/数据范围、身份和批准记录共同证明，环境字符串只是一个门。

测试层级矩阵：
- StaticSpecification：本轮文本/字段/源码阅读与普通文件hash；可结论为设计完整性待独审，所有执行AC仍NOT_RUN
- UnitContract：未来隔离进程/合成数据/内存终端，证明原语分支和本地DTO行为，不证明broker/DB/真实Host
- HostIntegration：未来真实Main/CRM组合和准确包、schema、provider/driver、事务/权限adapter，证明指定入口，不代表生产注册
- RuntimeQualification：未来明确授权隔离环境、真实transport/DB/采集、完整原AC与cleanup，证明指定环境/版本/时间
- ProductionAdoption：另外需真实部署/权限/配置/业务原receipt和独立接受，本轮无此授权

每层禁止把较低层测试通过复制到高层结论。测试里本地ProjectReference作为库单测的合理依赖，与RELEASE要求consumer真包无source fallback是两个用途，报告必须注明是哪一类。保SC23/SC24原场景ID引用；本册不替原场景Owner给通过、不把TEST工具本身计作场景已完成。

完整覆盖计划与现S7接口接合：CTX覆盖缺可信映射、后台scope、撤权后历史read/新write分流；HTTP覆盖三operationKind、请求发送阶段/Unknown/同键异输入、预算/取消；MSG覆盖准确版本/原bytes/可信metadata/commit前后ack；DEL覆盖caller-owned原子写集、所有checkpoint/duplicate/catch早退、lease expiry/版本fence、requeue审计同事务及retention hold单writer；OBS覆盖真实SLI/缺证/候选与生产；RELEASE覆盖原bytes、Current/Historical、真包/Host分层。只在合成、授权的测试边界作合同预期，不开展攻击、利用或第三方探测。

S6不可省略预期：读已有operation在当前read mask下优先于今天新写许可；新作用须Owner共同final fence持到commit/rollback，所有安全writer参加；CRM precheckpoint及内容观察/Published/Projection/Reconciliation/Inbox相关写集由应用持同事务，duplicate/旧版本/冲突catch重读也经正确门；三个replay action保typed native digest/队列版本/原业务与技术结果分层；Main activeTenant先验已知差距不能将当前行为当目标预期。这些是未来业务Owner集成AC输入，工具库不取代其实现。

数据/权限：只用合成业务对象、隔离tenant、专门测试身份和明确目的/过期范围；不得复制prod数据库、真实客户资料、密钥或会话令牌。需要真实外部服务/包/云资源/权限时须另行明确授权及安全工具路径，不在本轮以“测试需要”为由开网络/认证/持久访问。测试fixture不向其他用户/系统发送消息或执行真实订单。

运行/恢复（未来）：每case预先绑定artifact identity、环境和资源预算、停止条件；前置不可用=NotRun或按该CI合同Failed，不能跳过关键用例计通过；超时/取消/cleanup未知=Indeterminate/Fail，保原记录；发生业务结果Unknown用原Owner查询，不重置数据库“修绿”。共享数据库/容器/队列未证归本case不得删除；cleanup仅明示本次隔离资源，失败覆盖测试整体Pass。本文没有运行这些步骤或创建资源。

证据与幂等：case definition/input synthetic set/content hash冻结；expected改变须新definition version而非修改失败expected；重复attempt保同原case/version并新attemptId；原日志/安全摘要/工具链/时间/输出hash绑定同run；外部证据URL不是可变“latest”。按DP17保留原证据与访问权限/hold，不保秘密；旧测试历史不删，已发现缺口修复后重验新版本，不追认过去未运行的AC。

Required-TEST-01 精确case→SPEC/业务Owner/原场景映射和完整预期；02 合成数据/隔离scope/终端transport/运行授权；03 精确source/package/schema/工具链与harness身份；04 真实事务/权限/fence/原receipt适配（对应集成层）；05 bounded capture/隐私/证据完整性；06 本case资源归属及cleanup结果；07 独立审阅与真实执行原证据。缺01/03不得执行未知对象；缺02禁运行；缺04不能宣称集成/业务AC通过；缺05/06不能Pass；缺07不能Accepted。本轮无运行授权，所有测试保持NOT_RUN。

API/FE：测试运行器/报告属于开发工具，原库无面向业务HTTP API、数据库CRUD或FE操作。若日后有报告页面只能权限受控只读，展示definition、scope、attempt/status、证据、局限；不提供生产注入开关或自动重试业务按钮。无业务状态机/跨Owner写事务是“不适用”，其原因是TEST_ONLY且不持业务实体；需要验证的业务状态与事务全部引用原Owner，不能以N/A遗漏测试覆盖。

AC（NOT_RUN）：TEST03-A1 TEST_ONLY/IsPackable=false/正式七包排除不升业务数；A2 层级证据不相互冒充且SC23/24保原Owner；A3 生产Host无Testing引用/开关须真实证明；A4 合成数据/隔离/授权与具体范围完整；A5 S6关键read/precheckpoint/fence/typed replay预期不删；A6 NotRun/取消/缺前置/cleanup失败不假Pass；A7 原case版本/attempt/失败历史不可变；A8 Required缺证按层关闭且无业务UI/API/副作用。

开发 TEST-T07：完整case到五类证据层/原SPEC映射；TEST-T08：各应用Owner真实adapter/原业务AC输入；TEST-T09：报告/证据保存与授权隔离计划。全部PLANNED_NOT_AUTHORIZED。

## 6 冻稿结论

三原SPEC各8 AC，共24新AC全部NOT_RUN。本稿完成测试支撑详细设计，没有执行测试或证明生产接线；不得计算为正式业务功能通过。作者本版STOPPED交父独审；不改已冻稿、S6准源、V2/T06、ERP-CREDIT或SYS业务Owner。
