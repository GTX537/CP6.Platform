# WP02-08 concentrated review

One task-level independent read-only review covered the complete additive Outbox V2 implementation and the two narrow legacy entry changes, against the fixed wave6 card and original PLT-DEL sections 5/6/8. The reviewer did not execute PostgreSQL or certify a production Owner. No unrelated style/refactor requests were added.

| Finding | Resolution | Actual verification |
|---|---|---|
| P1: Serializable snapshot predating advisory-lock wait can retain a stale Owner hold/grant | Write registration requires actual stable Owner authority-row descriptors; library takes FOR UPDATE under Owner advisory roots before RouteControl. Changed rows force native40001; missing fence/row denies. Host must enumerate every actual dependency/writer. | Both hold/cleanup lock orderings and missing-authority-fence regression; full PG review-06 38/38. |
| P1: Conditional/column-filtered guard can look installed while allowing bypass | Validate tgqual/tgattr, full event mask, enabled state, body, language, volatility and fixed search path; require actual unique indexes. | WHEN(FALSE) guard and removed unique-index tests reject before DML and work after restoration. |
| P1: Binding/root token mapping can be altered or ignored | Validate exact entity tables/columns, all required CLR properties, xmin/xid generated concurrency tokens and native byte converter expressions. | Full PG tests for altered token/root mapping; follow-up ignored Binding.Version/root.Version cases and valid claim, mapping-final09 5/5. |

The targeted rereview closed all three findings and found no related new blocker. Its only follow-up was an omitted-property case within the same mapping finding, fixed and tested as above. The strict expiry helper extraction was separately checked by before/equal/after unit cases and one real database expiry regression; no unrelated full review restart.

Final implementation scope: namespace OutboxV2, legacy Outbox store/retention fences, task-private unit/PG tests, migration asset generator and documentation/evidence. Existing dispatcher, InboxV1, shared contracts/models, root projects/dependencies/workflows and WP02-25 telemetry are unchanged. Actual host authority inventory, cutover/migrations, database role governance and broker/business acceptance remain UNPROVEN/NOT_RUN.
