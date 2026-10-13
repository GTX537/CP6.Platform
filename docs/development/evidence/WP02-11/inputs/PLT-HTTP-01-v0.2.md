# PLT HTTP 错误与弹性契约完整静态设计

版本 v0.2，S7，2026-10-08 UTC。状态 AUTHOR_FROZEN_FOR_TARGETED_REVIEW。在v0.1完整正文上仅闭合独审S7-B01及同处非阻塞S7-N03，原v0.1和独审原件冻结。五项原 SPEC、42项AC身份不变；全部 AC=NOT_RUN，RuntimeAdoption=UNPROVEN，ImplementationAuthorized=false，ProductionDeploymentAuthorized=false，Enabled=false。本文是技术设计，未运行代码、验证器、测试、数据库、CI或部署；原计划 XLSX 未创建，本 Markdown 不冒充母版工作簿。

## 1 范围来源与结论

PLT-HTTP-01，Owner=CP6.Platform，lane=共享支撑，change_proposal=ADJUST。原 CUR-PLATFORM-003/004 均 ISOLATED_CAPABILITY/IMPLEMENTED；包内能力不能提升为 Main/CRM 全入口已接线。原五项为 ProblemDetails、网关头、超时、重试、应用接线。公共技术契约不包含订单、库存或会计规则；副作用调用不得因通用重试造成重复业务。DP10/DP12/DP16/DP17、DG13/COMMON 保留。

固定输入见 SOURCE_INDEX.json 与 SCOPE_EXACT_EXTRACT.json：Platform 30bd23af6808d217a23878bd9437513043c52834，Main 90c871fe571fd6b390f53e8678376d7ce60bcb60，CRM c778a3052a4416b82facb07b1244fc98fb6ad8a1，coord ad82a18c9511c3a8d3387060a9e2ed4755381e1d。2026-10-08 00:46 UTC 重新读取三库 main 与 AI-Handoff 两分支 HEAD，均与原快照相同，回执见 HEADS_FRESH_20261008_0046.json；此时间后的变化不在本稿声明内。

已实读 Platform Cp6ProblemDefinition、Cp6Problems、Cp6ProblemDetailsExtensions、Cp6CorrelationId、Cp6CorrelationMiddleware、Cp6OutboundCorrelationHandler、Cp6GatewayHeaders、Cp6GatewayProfile、Cp6GatewayExtensions、Cp6HttpResilienceProfile、Cp6HttpResilienceServiceCollectionExtensions、Cp6HttpOperationKind、Cp6HttpFailureCategory；HttpResilienceTests 仅静态断言来源。CRM Program 实读显示 crm-identity 客户端仅 Timeout=10秒、禁止自动重定向和Cookies；独立 middleware 保留 CRM 错误码及CSRF/session处理，未在该 Program 注册 AddCp6HttpResilience / AddCp6ProblemDetails / AddCp6Gateway。Main 已读身份/ERP适配片段不能证明完整Host HTTP接线；本稿不据此猜全Host。sources/P_Program.cs.txt 是 Platform SqlServerFixture 程序，不是生产Host。

S6 v0.2 唯一准源 SHA256 d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc。消费原operation查询优先、typed replay、Owner最终授权fence、UNKNOWN恢复；不改已接受正文。PLT-CTX 管身份建立，PLT-DEL 管原子业务/回执/Outbox，HTTP 只负责边界及调用策略。

## 2 Actor 对象与不变量

Platform维护技术错误字典、profile构造、handler次序和安全关联；Main/CRM业务Owner持原operation、输入摘要、最终结果与新效果授权；Host Owner持端点/客户端清单及实际注册；网关部署Owner持目的地址和可信代理登记；支持人员仅可读脱敏错误与采用状态，无重新执行业务权限；浏览器可发其业务命令，不能声明自己已幂等或可信代理。

原生 Cp6ProblemDefinition 字段：type=绝对HTTPS URI；title=非空且≤200字符；status=400..599；code=大写多段下划线机器码；messageKey=namespace.error.camelName。现三项固定为401 CP6_AUTHENTICATION_REQUIRED /403 CP6_FORBIDDEN /429 CP6_RATE_LIMIT_EXCEEDED。业务错误字典仍归应用，不将 CRM crm.* 或 ERP 既有码偷偷改名。

候选应用内部 HttpOperationBindingV1：bindingId、hostId、clientName、endpointContractRef、owner、tenantScopeRule、operationKind、requestVersion、businessKeyDomain、inputDigestContractRef、originalOperationLookupRef、idempotencyRetentionRef、allowedMethods[]、destinationRef、credentialProfileRef、httpProfileVersion、gatewayProfileVersion、configurationVersion、routeEpoch、registrationEvidenceRef、runtimeEvidenceRef、adoptionState。各Ref为受控不可变引用，缺值不能隐式默认通过。业务键、消息ID、关联ID、traceId分别有域，不互换；字段为技术接入文书/配置DTO，不新建Platform业务操作表。

幂等成立须远端Owner用 tenant+action+原operation/business key 绑定规范输入摘要和不可变结果，并把业务效果、receipt及必要Outbox同事务提交；同键不同输入返回冲突；未知结果可按原身份查询；保留窗口不短于所有可重试/离线/灾备窗口。Idempotency-Key格式通过仅证明头合法，不能证明上述任何一项。

跨Owner HTTP没有分布式原子承诺。调用前本地意图可先持久化；外部响应丢失保留 Unknown，查询远端原receipt；本地确认与远端效果通过显式状态/对账闭合，禁止catch后再生成新操作键。权限判定及最终共同fence留在产生效果的Owner事务内，网关成功、JWT成功或客户端retry条件不能取代它。

## 3 SPEC-PLT-HTTP-01-01 ProblemDetails

需求：客户端得到稳定、安全、可本地化的错误合同；技术原因不泄漏凭据、SQL、个人字段或原请求；HTTP错误不伪造业务终态。Writer输入 definition 与可选 errors:map<string,string[]>；现输出 type/title/status + code/messageKey/traceId/correlationId/errors；不写 detail/instance。trace取当前Activity有效32字符ID，否则新建；correlation优先已建立TraceIdentifier，不存在则生成。现writer不校验errors内容，也不替Host捕获所有异常，不能当作全局脱敏器。

处理顺序：Host建correlation→认证/应用上下文→业务安全判断及调用→应用分类异常→以本地登记definition和白名单字段生成错误→写未开始响应。Response.HasStarted时现writer直接返回；目标Host必须中止该流/记录低敏失败，不追加第二JSON、不把部分响应宣称完整成功。取消且请求已断开只记取消事实，不再发送错误正文。认证401、已认证无权403、字段400/422、同原键不同摘要409、限流429、依赖/门未就绪503、远端超时504依具体Host兼容映射；UNKNOWN是操作状态，不统一当拒绝或允许新建。

候选安全增强由Host adapter承接：errors字段名必须在当前端点可见字段集合；消息内容只用登记的安全校验文本，不回显用户值/异常Message；最多32字段，每字段最多4条、每条256字符，总错误正文目标≤16KiB。超限只保留通用code/correlation，记录安全计数，不截断后制造无效JSON。这是新设计边界，现writer未实现，不改变原业务错误码。为原CRM Problem维持 type=about:blank、现 title/status/code/traceId/correlationId/errors，单独逐客户端评审后才可引入新messageKey，不能直接将原type不兼容地强制HTTPS。

对外状态DTO不被Problem取代。候选 HttpOperationStatusViewV1：operationId、actionType、state(Pending/Applied/Rejected/Unknown/Blocked)、ownerResultRef、safeCode、readMaskVersion、observedAtUtc、correlationId、canReadOriginal、canRequestRecovery。原状态与类型由业务Owner映射，HTTP无写状态权；unknown时202/503或原API约定由版本合同给出，UI以state判断后续，不从某次504推断业务未执行。跨租户不存在与无权统一按应用的防枚举策略，不返回另租户原结果。

FE：在原页面显示本地化code文案和correlation；字段只映射已授权可见字段；401清会话并走登录，403不自动重试；409回读原operation并呈差异，429等待受限提示，503/504若已发写先查原状态；网络断开不自动生成第二命令。全局support页只读脱敏错误，不提供通用“重发全部”按钮。

恢复：错误转换本身不写业务库，审计由Owner记录事务真实结论；响应序列化失败不得回滚已经确认的业务后反写Rejected。异步审计/遥测失败不伪造效果撤销，但必要合规审计若是业务提交前置条件，由Owner先阻提交。

AC（NOT_RUN）：HTTP01-A1 原三技术定义及字段稳定且Content-Type一致；A2 errors白名单/上限和异常脱敏；A3 trace与correlation有效且不成为权限；A4 Response.HasStarted/取消无第二正文或假成功；A5 原CRM/业务码兼容保留；A6 unknown/504不变业务终态且UI查原operation；A7 同tenant/read mask访问原结果，不跨租户泄漏；A8 事务已提交但写响应失败仍可读原结果。

开发 HTTP-T01：Platform保writer API并设计可选安全errors构造；HTTP-T02：Main/CRM每端点错误映射和owner状态DTO；HTTP-T03：FE错误/原结果恢复与字段权限。依赖各Owner错误字典及S6原结果读取；缺字典时相关新adapter不启用，保已审核旧行为。

## 4 SPEC-PLT-HTTP-01-02 网关头

现 Cp6GatewayProfile：Routes/Clusters必须非空；Route={RouteId,ClusterId,MatchPath,Methods[],AuthorizationPolicy?,Order,RateLimit}；Cluster={ClusterId,Destinations[]}；Destination={DestinationId,Address}；RequireHttpsDestinations默认true。ID为小写DNS式≤63字符且同层唯一，路由需存在cluster；路径以/起、不含空白/反斜杠/query/fragment；方法为DELETE/GET/HEAD/OPTIONS/PATCH/POST/PUT且不重复，空集合现表示全部；RateLimit.PermitLimit=1..10000，Window=1秒..1小时；地址为绝对HTTP(S)、无userinfo/query/fragment，默认必须HTTPS。

现网关删除请求及Content headers中的 X-CP6-*、X-Organization-*、X-Tenant-*、X-User-*及精确Forwarded、Forwarded-Client-Cert、X-CP6、X-Forwarded-Client-Cert、X-Organization、X-Tenant、X-User。未因此证明所有代理头均被清除，特别不能声称该列表处理所有X-Forwarded-*。下游不得据任意残留头决定tenant/user/region/权限。可信代理地址/证书及ForwardedHeaders middleware由部署/Host登记；本轮不更改任何安全设置。

网关处理：启动前验证profile→routing选择唯一路由→显式认证/授权policy→rate limiter→transform移除不可信身份头→向登记地址转发→下游重新验证token/service身份并按CTX建立tenant上下文。禁止把删除header称为重新签发可信身份；若确有网关身份声明，须独立经批准的签名/受众/轮换合同，未具备则不使用。目标新受保护route必须有显式非空AuthorizationPolicy、显式methods和目的allowlist；现validator接受null policy/空methods，是需收紧的应用发布门，不宣称库已拒绝。

限流现为每route按RemoteIpAddress或unknown分区的进程内fixed window，QueueLimit=0；429若lease含RetryAfter则回≥1秒整数Retry-After。不能称全局/分布式配额或租户计费。代理后IP可信来源需证明；unknown不能回退用户提交头。多副本额度与合法共享出口影响需由网关Owner选明确策略，未声明不能给总量保证。无业务数据库事务，计数丢失不能改变业务命令幂等。

关联头 X-Correlation-Id 独立规则：单值且首字符字母数字，其后字母数字/点/下划线/冒号/连字符，总长1..128才沿用，否则生成；响应回同ID。出站handler覆盖任意请求自带值，以可信当前HTTP TraceIdentifier或CTX值产生安全关联；后台无HTTP时现行为生成新值，需任务adapter显式传递因果引用，不能谎称自动继承后台链。token、cookie、authorization code不进日志或相关DTO。

网关状态：Unconfigured→Validated→Registered→Configured→RuntimeObserved；变更routeEpoch时新接纳按一个epoch，旧in-flight保持原目的/原operation查询。目标地址不可随业务输入替换；故障切换必须指向同一Owner幂等域及可查原结果的等价实例，跨区域不同写域不做透明fallback。TLS/授权失败拒绝，不能重试为匿名或明文。回退旧route前确认新业务/消息合同仍可读取，不丢原operation。

FE/API：没有普通用户编辑网关头的页面；运维只读 GatewayBindingView={routeId,owner,methods,policyRef,rateLimitKind,profileVersion,routeEpoch,adoptionState,safeCode}，不显示内网地址/证书/凭据；改变配置或限流另走授权变更，不由诊断GET触发。错误使用401/403/429/503与安全correlation，响应不返回后端堆栈。

AC（NOT_RUN）：HTTP02-A1 原profile边界及引用完整；A2 两类header集合清除且下游无header身份fallback；A3 受保护route显式policy/methods/allowlist；A4 correlation单值、非法替换、出站覆盖及后台因果区别；A5 限流按实际进程/IP范围描述，多副本不伪全局；A6 可信代理未登记则不采信身份/客户端IP声明；A7 routeEpoch切换不双发到两个写域；A8 TLS/依赖失败不降级匿名/明文且诊断脱敏。

开发 HTTP-T04：逐route提供policy/verb/目的Ref/代理登记清单；HTTP-T05：Host中间件顺序与下游CTX映射；HTTP-T06：限流粒度/副本采用及只读诊断。缺授权policy或唯一写域证据，新route关闭。

## 5 SPEC-PLT-HTTP-01-03 超时

现profile为单named client+operationKind不可变对象。clientName非空、无首尾空白/控制字符、≤128；attemptTimeout默认2秒、范围100毫秒..30秒；totalTimeout默认10秒、范围250毫秒..120秒；circuitSamplingDuration默认10秒、范围1..120秒；minimumThroughput默认10、范围2..1000；breakDuration默认30秒、范围1..300秒。现不强制attempt≤total，实际总预算先到即止；新profile发布门要求attempt≤total并把重试等待计入总预算，不能将现构造器描述成已有此校验。

现pipeline次序为总timeout包围retry、circuit、每次attempt timeout。failure mapper把TimeoutRejectedException.Timeout与profile.TotalTimeout相等判为TotalTimeout，否则AttemptTimeout；如果attempt和total恰相等，单靠数值不能可靠识别来源，目标新增v2显式strategy标识映射，不通过改error文案掩盖。v2将cp6-total-timeout来源固定映Cp6HttpFailureCategory.TotalTimeout，cp6-attempt-timeout来源固定映AttemptTimeout；来源未知只报未分类技术失败并停止本轮，不能靠相同Duration猜来源。Caller cancellation原样传播，不翻成超时再重试。任何超时都不能证明远端rollback。

目标 HttpDeadlineContextV1 为内部调用参数：startedAtUtc、totalBudgetMs、attemptBudgetMs、remainingBudgetMs、deadlineSource(Client/User/Worker)、operationRef、profileVersion。来源由Host/worker赋值，不能让外部header扩大预算。上游预算≤当前剩余；子调用并行/串行不得分别重置一整份总预算。连接、发送、读响应均纳入调用预算；大流式响应另用经批准non-replay合同，不把stream当可无限buffer写。HttpClient.Timeout与resilience timeout须逐client明确一个总预算权威，避免多层默认timeout乱映射。

副作用状态：NotSent表示本地验证/开路已阻网络；SentAwaitingResponse表示可已送达；Confirmed表示读到Owner确定结果；Unknown表示发送后超时/断连或无法确认。只有传输adapter有充分证据证明未送达才可记NotSent；一般HttpRequestException不足以证明未送达。状态属于本地调用观察，不替代远端operation状态。Unknown保持同operation及inputDigest，先查原Owner回执；已存在operation历史读取只需当前read权限，不先查今天新写权限/activeTenant。

业务提交权限仍在远端事务最终门，deadline过期/调用者取消也不自动撤销已接受业务。若业务支持取消，另有Owner取消命令和新审批，不将CancellationToken当撤单。队列/重放/ERP恢复使用S6既定typed action，不借HTTP retry重新生成action。

错误恢复：NotSent可在既定总预算内按已批准策略重试；Unknown先query，若query不可用继续Unknown/Blocked并退避，不新建；确定Rejected返回原安全拒绝；确定Applied回原结果。查询使用独立IdempotentRead profile但必须受原请求整体恢复预算/操作生命周期限制，不能查询风暴。所有时间用单调elapsed控制预算，UTC只作审计，不因系统时间回拨获得更多重试。

FE：显示“请求结果待确认”及原operation入口；刷新只GET，不自动POST；关闭页面/停止等待只取消客户端等待，文案不说业务已取消。支持人员可查safeCategory、阶段、profileVersion、原结果可读状态，无原payload。

AC（NOT_RUN）：HTTP03-A1 原范围/默认准确及新attempt≤total门；A2 各阶段预算与嵌套剩余预算不扩张；A3 同值timeout按v2来源精确分类，旧差距明确；A4 caller取消不重试、不伪撤单；A5 已提交后超时保持Unknown并取原结果；A6 未证未送达不记NotSent；A7 时钟回拨不扩大单调预算；A8 原结果read优先于当前新写门且查询不可用保持待确认。

开发 HTTP-T07：Platform显式timeout来源与deadline契约；HTTP-T08：每Host明确HttpClient与pipeline权威及查询profile；HTTP-T09：Owner receipt查询/FE Unknown恢复。缺可查原结果合同则该副作用route不得提升为幂等重试。

## 6 SPEC-PLT-HTTP-01-04 重试

现三类：IdempotentRead只允许GET/HEAD/OPTIONS；IdempotentWrite及NonIdempotent只允许POST/PUT/PATCH/DELETE。RetryAttempts=0..5默认2，NonIdempotent强制0。IdempotentWrite要求唯一Idempotency-Key，1..128字符同安全键格式。不同named client不得混合安全等级；登记同名同profile幂等、同名不同profile抛异常，禁止重复叠加handler。

当前可重试分类仅HttpRequestException与408/429/500/502/503/504；TimeoutRejectedException不在该ShouldHandle中，不能声称attempt timeout现会自动重试。当前delay=0、Constant、jitter=false、RetryAfter=false，circuit FailureRatio=0.5且使用相同transient集合；未来生产采用需目标退避策略/预算门，现测试通过（即使未来运行）不能证明不存在下游冲击。

目标 RetryPolicyBindingV2 显式字段：retryMode=Disabled|OwnerProvenIdempotent，maxRetries≤5，baseDelayMs=200（候选默认），maxDelayMs=2000（候选默认），jitterRatio=0..0.5（候选0.2），respectRetryAfter=true，retryAfterCapMs=30000（本次允许等待上限，非截短服务器等待的值），retryableStatusSet、requestReplayLimitBytes=1048576（候选默认），operationBindingRef、policyVersion。v2仅由已审核named-client binding显式opt-in；旧profile的零delay/无jitter/忽略Retry-After行为不原地改变。以下是唯一决策合同，不再留退避和服务器等待优先级选择。

等待输入和解析：当前响应首次可观察时同时采样可信UTC t0和单调时刻 m0；remainingBudget来自§5单调总deadline，不信任外部Date或deadline头扩大预算。Retry-After只接受唯一字段值，不把HTTP-date自带逗号拆为列表。值去HTTP首尾空白后，非负十进制整数按delta-seconds解释；HTTP-date接受IMF-fixdate、历史RFC850-date及asctime-date三种HTTP日期形式，英文日/月名、GMT/UTC解释固定；RFC850两位年份若落在t0之后超过50年则取同末两位的最近过去年份。有效日期减t0且向上取整到毫秒，已过去或等于t0为0。多个字段值、合并后无法解析为一个日期/整数、负值、小数、非法日期均标InvalidRetryAfter并只走本地退避；可辨为非负整数但溢出或远超上限的值直接StopServerWaitExceeded，不将超大合法等待误归非法后提前重试。日期解析器身份/支持HTTP日期形式列入profile实现证据；不得用本地文化/时区解析。UTC只在t0转换一次，之后系统时钟调整不提前单调最早时刻。

唯一合成规则：第k次重试（k从1）本地基础等待B=min(maxDelayMs,baseDelayMs×2^(k−1))；每次决策仅取一次u∈[0,1]，L=向上取整的clamp(B×(1−jitterRatio+2×jitterRatio×u),0,maxDelayMs)，将所取L保持到本attempt结束。无有效服务器值时S=0；有效时S为上述非负等待。服务器最早时刻固定为m0+S，本地为m0+L，最早下一attempt=m0+max(S,L)。maxDelay仅限制L；jitter仅作用L，绝不作用S或将服务器要求缩短。有效S>retryAfterCapMs时直接停止本轮，不等待30秒后重试，也不截为2秒。

预算与停止：在决策及真正发送前都重算单调剩余R，下一次完整attempt预算A=profile.AttemptTimeout。若取消、retry次数已满、S超过30秒cap、或到最早发送时刻的剩余等待W加A大于R，则Stop，不提前发送；等号允许一次A预算attempt。允许等待上限可表为min(30000,R−A)，仅用于准入比较，R<A直接Stop，不将负数当0继续。sleep中取消/总deadline到达立即结束且不再发请求；sleep提前唤醒仍须等至最早时刻，迟醒重新检查R≥A。解析/排队消耗的时间计入总预算，W=max(0,最早时刻−当前单调时刻)。全部停止都保原operation/key/inputDigest及已知远端结果；429等保限流分类，发送后结果未知仍Unknown并转原receipt查询，不生成新操作，也不声称远端已回滚。

HTTP04-A4的固定文字子例（仍是原A4，不新增AC身份）：给定u=0.5、k=1，L=200ms；S=100ms则等200ms；S=5000ms且剩余预算充足则至少等5000ms，2秒maxDelay不截它；S=60000ms、R=100000ms且A=2000ms时立即StopServerWaitExceeded，绝不等30000ms再发；S=15000ms、R=10000ms时StopBudgetInsufficient；S=9000ms、R=10000ms、A=2000ms也停止。非法/重复值或过去日期按L等待但不绕过A预算门；任何等待中取消/预算耗尽都零后续attempt。短等待、HTTP-date及UTC回拨均按m0一次锚定，预期唯一。

重试前置必须同时满足：Owner幂等证明和查询合同已接受、同tenant/action/key/digest、可重放完全相同请求体、授权/路由范围未变、剩余预算足够、无强制禁止。流/不可重复读取body、超上限body、一次性token交换/登录code/外部无receipt支付类动作默认NonIdempotent，不复制流后就称业务幂等。HTTP method语义也不足以证明业务实现幂等。401/403/409/422以及业务拒绝不通用重试；不得刷新身份切tenant后继续原命令。

发送多次始终一个原operation identity、一个稳定business key和输入摘要；transport attempt id可变，correlation保持关联。同键不同请求在本地检测/远端Owner409，两端不得覆盖原结果。同键重复Applied返回原结果；Processing返回原pending；Unknown先query。远端Owner重新验证新效果的当前权限fence，已提交历史result按read policy可查；重试库不预判原Actor当前仍可新建。

熔断：Closed→Open→HalfOpen→Closed或Open；Open请求在网络前拒绝为CircuitOpen，对未曾发送的新命令属NotSent，对先前Unknown仍Unknown。HalfOpen不引入额外业务operation；并发探测量须按底层pipeline实际限制及Owner幂等证据验证。不能以probe成功自动清除DLQ/重放旧业务。外层SDK、网关、sidecar或调度器可能各重试；采用矩阵须指定一个业务调用重试Owner，其他层仅有已批准、预算相容的传输重试，给出最坏总attempt上界，禁止乘法放大。

并发同键由远端Owner原子去重；HTTP不加第二业务锁表。跨库最终效果用业务Owner明确saga/receipt机制；不会因为客户端加Idempotency-Key获得exactly-once。策略变更不可在一次操作中放大已接受预算；恢复时保policyVersion历史，未来新操作采用新版。安全/业务授权不足只允许读取原结果，不自动创建新operation。

AC（NOT_RUN）：HTTP04-A1 三种方法和唯一键约束准确；A2 NonIdempotent零重试含code兑换等动作；A3 原transient集合和timeout排除保真；A4 v2按本节唯一合成及固定子例，服务器最早时刻不被2秒/jitter缩短，超30秒或完整attempt预算不足即停止，日期/重复/非法/取消各预期确定；A5 同key/digest/body不变，差异冲突不覆盖；A6 超限/stream不自动重放；A7 并发同键远端只一业务效果且Unknown先查；A8 circuit各状态不生成新业务/清DLQ；A9 多层retry上界可证且无乘法风暴；A10 当前权限不足不阻合法原结果read也不授新作用。

开发 HTTP-T10：Platform v2 bounded retry显式opt-in，按本节单值解析、UTC到单调锚定、max(S,L)、cap准入和A预算完整实施，§5两strategy来源映射一起资格化，不能换另一合成策略；HTTP-T11：Owner原receipt+摘要/并发去重合同；HTTP-T12：Host每named client分类及外层重试清单；HTTP-T13：FE同操作恢复、S6 recovery分流。任一幂等必要项缺证时retryMode=Disabled，保零重试。

## 7 SPEC-PLT-HTTP-01-05 应用接线

采用必须分列 CodePresent、PackagePinned、HostRegistered、ConfigEnabled、EndpointBound、RuntimeObserved、BusinessApplied、IndependentlyAccepted。Platform单测/fixture/样例Host不能填Main或CRM生产列。现CRM crm-identity是OIDC用途，原10秒timeout、AllowAutoRedirect=false、UseCookies=false不可全局替换；授权code兑换不能仅因POST有键就重试；metadata/JWKS只读与token交换必须分client/profile，经CRM Owner兼容批准。

候选 HttpAdoptionV1 每项字段：app/host/refSHA/packageVersion、clientName/routeId、entrypoint、method、actorProfile、tenantResolverRef、operationKind、ownerReceiptContractRef、inputDigestContractRef、idempotencyWindow、HttpClientTimeoutOwner、resilienceProfileVersion、gatewayBindingRef、middlewareOrder、oldErrorMappingRef、uiConsumerVersions[]、multiLayerRetryInventory、requiredEvidence[]、state、disableReason。原字段长度/枚举依§2和各原类型；不保存secret/真实token。Main未知项明确UNPROVEN，不能凭引入NuGet包填true。

入口顺序合同：安全correlation在错误/认证之前；可信proxy处理在任何需IP/TLS判断之前且限明确登记；routing后显式认证授权；受保护应用经CTX resolver；rate limiter置routing后proxy endpoint前；Host exception adapter覆盖其允许层且不会拦截必要challenge/CSRF语义；原业务提交处保Owner最终门。CRM browser原CSRF/Origin/session/transport核验完整保留，Platform service JWT入口独立绑定，不把AddCrmPlatformAuth类存在误称Program已启用。每Host必须列实际调用位置与接口测试计划。

历史适配：旧code/type/title/状态/错误字段约定逐client登记；新版本支持期间旧接口照原稳定合同，增量新messageKey需要兼容；new retry只给已证明原operation合同的具体route，Main内部本地调用不强迫变HTTP或Kafka。旧调用失败历史不能全部重放，按原receipt、业务key、routeEpoch分层核验。

上线前门（本轮不执行）：Required-HTTP-01 精确named client/route及方法/Owner/实际Host位置；02 远端去重/摘要/原结果可查/保留证明；03 middleware/认证/CRM会话兼容；04 timeout来源/外层attempt上限/安全退避；05 目的/代理登记和低敏错误契约；06 旧新client兼容及单route切换/回退；07 经授权环境逐AC真实证据与独立接受。缺01/03/05关闭该新route；缺02关闭写重试；缺04不采用v2弹性；缺06不切流；缺07保持UNPROVEN，禁止用文书批准代运行。

运维API/FE是应用所持只读接入清单及原operation链接，Platform不建业务控制台。诊断read需每tenant/应用范围的运维权限，未授权只返回泛化错误；修改profile、解除熔断、重发业务不挂到同GET。变化用配置版本和审计记录，不能由浏览器发送profile覆盖运行策略。

AC（NOT_RUN）：HTTP05-A1 每入口八层证据独立；A2 CRM OIDC只读/兑换分流且旧安全选项保留；A3 middleware顺序、service/browser认证不互换；A4 旧错误/客户端兼容窗口及安全字段；A5 原operation历史读取与新写授权分离；A6 缺Required按对应能力精确关闭；A7 route切换/回退不清历史、不双发、不强改Main内部传输；A8 真实Host证据与fixture严格区别。

开发 HTTP-T14：Main/CRM完成HttpAdoption精确清单；HTTP-T15：逐route兼容adapter与双版本读取设计；HTTP-T16：授权后AC/运行证据计划。所有开发条目PLANNED_NOT_AUTHORIZED。

## 8 冻稿结论

原五 SPEC 全覆盖，共42项新设计AC（8+8+8+10+8），全部NOT_RUN。无业务Owner转移；无代码/SQL/测试执行；无包发布、配置/权限变更或消费者切换。Required门给出缺证的具体关闭行为。V2/T06冻结、ERP-CREDIT与SYS正文均不在本稿范围。作者停止修改本版本并交父独审。
