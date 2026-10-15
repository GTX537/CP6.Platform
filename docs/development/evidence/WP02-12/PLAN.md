# WP02-12 execution plan

Lane C; assignment fixed at Handoff `ebeae808b2aa35c8c9115519fed564edebbbde0c`.
Source baseline Platform `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`; Main dispatch `99fc0ce383d2d02c1404a8adbb3abe971b41d332`.
Follow the exact wave7 paths/resources; no shared project, contract, migration, ledger or legacy-default changes.

1. Verify baseline, cached image identities and free assigned ports; append START on task-only Handoff branch.
2. TDD bounded TransportV1 route/contract selection, Host authentication seam, owned bytes, safe diagnostics, publish outcomes, committed quarantine readback and narrow OutboxV2 delegate bridge. Separate implementer owns library and unit tests.
3. Create task-owned runtime controls with canonical paths, full owner labels, no pulls, explicit owner/settings on each phase. Kafka SASL/ACL separates publisher, subscriber, observer and denied principal; authenticated Dapr API/app channels. Independent broker record read supplies actual topic/key/partition/offset/body bytes.
4. Build offline fixture on SDK8; new PostgreSQL55430 fixture Owner integrates corrected OutboxV2 and durable raw quarantine/read-only recovery. Only owned fixture schema/data; no real business adoption.
5. Exercise actual positive and negative Kafka/Dapr/PG cases, distinguish application ACK suppression from native behavior; preserve all failures. Add exact original MSG AC mapping, source/command/artifact identities, limitations.
6. One task-level spec and security/quality review, relevant contract regressions and one architecture check. Stop only identity-verified owned resources, retain data/logs. Review complete diff and workflow trigger reachability before candidate push; fixed-SHA DELIVERY for COORD serial integration.

No C02 business route enablement (WP02-05/06/29 actual adoption missing); no production SLO, deployment, external paid runner, or business AC claim. Missing runtime proof remains UNPROVEN/NOT_RUN and blocks the corresponding positive guarantee.
