# Contributing

CP6.Platform 当前采用个人开发者的轻量分支流程：规范靠仓库内可执行门禁落实，不要求 GitHub Pro 或分支保护。

1. 从最新 `main` 创建一个 `codex/<task>` 分支，一个分支只处理一个任务。
2. 同步更新代码、测试和必要文档；不要提交凭据、机器专属配置、`artifacts/`、`bin/` 或 `obj/`。
3. 至少运行 `Format`、`Build`、`Unit`、`Integration`、`Contract`、`Security` Gate。
4. 审查相对 `main` 的完整 diff，明确暂存本任务文件并提交。
5. 推送分支、创建 PR；CI 在 Windows 和 Linux 均通过后合并 `main`。

Platform 包的公开 API 是跨仓库契约。新增或破坏性变更必须有架构测试、迁移说明及对应版本决策。
