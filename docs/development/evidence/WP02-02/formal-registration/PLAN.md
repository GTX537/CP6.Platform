# WP02-02 第八轮：生产者登记执行记录

本窗口为 `PRODUCER_REGISTRATION_ONLY / CONFIRMED`，Main 总任务仍为
`BLOCKED / NOT_CHECKED`。执行分配为 Main
`b3eb755bc43e370f697480a6ea41474c3d978488`；已 fetch 核对的 Platform 起点为
`66480fe24d51c706708be971e7e9d75a684a3bc4`，tree
`4bb731924244ef7876805294b623fdbe8dd2155a`。沿用协调已建独立工作树与单任务分支。

1. 重核真实授权主体和七个 package 的全部分页；记录观测而不预留版本。
2. 先记录新行为 RED，再实现四版准确 reader、仅 .3 writer 和 .3 的 ctx 能力。
3. 运行 Release 正反矩阵、实际唯一 prerelease 包内 Schema 和 Architecture 本地门。
4. 固定复制第七轮诊断工具，仅更新 producer SHA/tree 与隔离目录。
   在新 owner 中针对当前 producer 重验三个构建入口及私有 synthetic authority probe；
   Main 产品输入仍固定 `1129953156b7775152a74ad2f51cb95ef430ee56`，不声称最新 Main 采用。
5. 一次任务级 SPEC/QUALITY 审查；定向处理实质问题。逐路径检查只读字节、
   workflow 事件与实际 diff，保存候选提交并提交协调交付。

实际执行身份、失败和结果分别保存在 feed、implementation、compatibility 和交付证据中。
历史结果只有在相关输入完全不变时才复用；新 SHA 本身不触发无关测试。
不运行正式 writer、Actions、签名、发布、数据库或 Main Program。
最终正式 producer SHA 保持 null，须在协调合入并核远端 main 后另行冻结。

登记只说明代码能够表达正式 .3，不产生真实 publication、matrix、批准 hash 或 consumer lock。
正式信任、Current 在线密码学验证、Main 消费与 17 PGT 均不能由此升级。
