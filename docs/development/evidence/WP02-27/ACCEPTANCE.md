# 原 PLT-RELEASE-01 的32项验收责任

原文 SHA256 `9246ef6b40f500da22c570bf3c31a51701442bbbed50250d9f259d12cb1bbdbf`，四组各8项。本表保留原AC的业务/运行 **NOT_RUN**；库内自动断言、包文件验证和宿主验收是不同层。实际本轮命令、结果及源码/制品身份见交付报告与各运行记录，不用此对应表替代测试结果。

| 原AC | WP02-27内的子断言/证据 | 仍由原Owner验收的范围 |
|---|---|---|
| REL01-A1 四lane/kind/deployable | 新CapabilitiesV1独立命名空间和schema；不改旧四primary lane或媒体类型 | 真实四仓System候选、部署批准 |
| REL01-A2 七包同version/source | 闭合七包集合、同源同版、Testing排除；实际缓存与本地pack逐包核对 | 消费方实际采用全部固定身份 |
| REL01-A3 canonical原字节 | 重复/未知字段、非法JSON及非canonical拒绝；SHA256与返回lock防御性复制 | 原始签名、历史证据长期留存与访问 |
| REL01-A4 formal/root/package/feed | 组合旧正式validator，保留0.10.0/.1/.2读验证；错feed/源/版本拒绝 | 新正式版本的合同演进、运行与发布 |
| REL01-A5 信任分域/撤销/历史 | 复用NuGet pin；Current、Historical、Revoked、未知根、过期/错政策分开；真实CLI负例 | 信任Owner的政策更新、现运行风险处置；Locator/OCI分域不改 |
| REL01-A6 shape不冒签名/run | 形状与制品/信任状态分列；实际CLI逐包结果；无verifier则Unknown | 工作流来源实际API审计、后续发布run独立验收 |
| REL01-A7 同版不可覆写 | 外部批准matrix哈希拒绝同身份不同字节；脚本只本地pack/check，新attempt目录 | feed并发/未知发布结果查询与不可变存储执行 |
| REL01-A8 缺四仓/Portal No-Go | 不生成System/R2/Locator，不制造Portal身份或正式run | 四仓/Portal真实制品与完整候选链 |
| REL02-A1 P09冻结身份 | 旧P09合同/资产只读；新能力资产单独打包 | P09 profile的目标用途采用 |
| REL02-A2 probe用途边界 | 本任务无probe运行，不将其当业务消息路线 | 业务route与生产配置 |
| REL02-A3 tag与digest分列 | 版本/source字段拒绝latest、tag替代完整SHA；本任务不引入镜像 | 真实runtime镜像digest与运行身份 |
| REL02-A4 Secret只引用 | API无Secret值字段，闭合JSON拒绝额外字段；脚本仅公开policy/DER与本地包 | 目标环境秘密管理与访问控制 |
| REL02-A5 required checks | 未删除/修改P09检查项；本任务结果不替代原检查 | P09/目标配置完整执行与用途批准 |
| REL02-A6 新应用binding独立 | 本地能力矩阵不修改P09或Main/CRM配置 | DeploymentBindingV1、环境执行窗口 |
| REL02-A7 config/CAS/routeEpoch | 精确configuration身份/兼容行不推出HostRegistered或ConfigEnabled | 配置CAS、routeEpoch和原operation回退 |
| REL02-A8 未观察不称应用 | ConfigEnabled、RuntimeObserved等状态始终Unknown | 实际配置观察及独立批准 |
| REL03-A1 全依赖身份 | 源repo/SHA、七包ID/版/hash/feed、schema/validator/工具链/消费行；formal另绑定原publication/hash | 真实workflow/run/attempt、镜像、迁移、配置、Host全链；本地pack非正式build |
| REL03-A2 schema/API一致 | 实际Release包内新schema比源码字节；实际包DLL的API存在性；未打包native schema的缺口明列 | 完整native source/packaged schema与真实consumer语义资格化 |
| REL03-A3 真包无源码回退 | 检查真实nupkg与其中DLL/资产；本地检查器不把源ProjectReference当包内API证据 | 独立真实宿主从正式feed消费，无ProjectReference/source-copy fallback |
| REL03-A4 capability具体缺口 | 精确S01-09三profile与ctx-binding-v1/ctx-v1；明确0.10.2缺少新能力 | 各新原语实际包版本、宿主注册/配置与全writer采用 |
| REL03-A5 同包非同事务 | DB/driver/migration作为精确行维度；未测范围Unknown，无数据库操作 | 同physical connection/transaction、共同fence和Owner |
| REL03-A6 精确兼容窗口 | 请求须与已批准行逐字节一致；任一SDK/OS/arch/schema/profile/DB/host差异Unknown | 旧存量消息、原operation/typed digest及跨版本真实验收 |
| REL03-A7 撤销/缺证不提升 | 当前政策pin/时间、真实verifier结果分开；Historical不能成为Current | 当期信任/制品过期后重新评估及消费门 |
| REL03-A8 分层采用/receipt | 十层状态分列，deterministic lock不是权限或永久信任令牌 | ConsumerQualified至IndependentlyAccepted与真实业务receipt |
| REL04-A1 P09证据原身份 | 原P09证据/requiredChecks只读，无新P09通过声明 | 原canonical/profile/运行证据完整关联 |
| REL04-A2 NotRun/Failed/清理 | 每attempt原始日志/退出码/TRX保留，失败不覆盖为通过，零发现/跳过拒绝 | 真实runtime资源/清理观测；本任务无数据库/容器 |
| REL04-A3 offline非cluster | 本任务不连接或部署Kubernetes | 真实目标cluster验收 |
| REL04-A4 qualification全前置 | 矩阵/lock只表达精确身份，所有宿主/业务状态Unknown | Owner/fence/事务/唯一writer实际qualification |
| REL04-A5 Unknown恢复不重放 | Unknown兼容不启用消费；无重放/清队列动作 | 原业务Owner查询、lease与三typed恢复 |
| REL04-A6 回退保schema/receipt | 明列兼容/迁移/回退责任，脚本不执行回退 | 已接纳格式、历史receipt/Outbox/Inbox和回退执行授权 |
| REL04-A7 attempt/失败/清理 | 新目录CreateNew，保留原失败和每文件hash；不删除共享资源 | 运行qualification幂等与真实资源清理验收 |
| REL04-A8 包/健康非业务接受 | 当前信任成功也不改变Consumer/Host/Business层，更不解除WP02-02 | 真实运行、业务AC与独立接受 |

Required-REL-01..07仍分别要求真实不可变发布与Current信任、源/包schema及真consumer、新API与Host、DB/全writer/fence、环境用途授权、单writer/存量/回退、原AC真实证据与独立接受。本任务仅交付所分配本地能力与身份验证；125业务目标、247运行、41历史REFERENCE_ONLY、ITER/REL缺件与WP05 PROPOSED不升级。
