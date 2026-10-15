# WP02-12 library test evidence

Baseline: `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`. These are local library/unit results, not broker, database durability, business adoption, production, or release proof. The in-memory authority/transport/quarantine implementations in the unit tests exercise contract behavior only; actual authentication and committed durability require the separate Host fixture.

All commands used `C:/Users/tt/.dotnet/dotnet.exe` from the task worktree. Initial successful restore used only the existing local package cache:

```powershell
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.WP0212.Tests/CP6.Platform.WP0212.Tests.csproj -c Release -p:NuGetAudit=false -p:RestoreSources=C:/Users/tt/.nuget/packages --logger 'trx;LogFileName=<name>.trx' --results-directory <run-directory>
```

Subsequent WP0212 commands replaced restore properties with `--no-restore`. Each directory preserves stdout/stderr in `run.log`, the actual exit code, and TRX when tests ran. No tests were skipped. No Actions were invoked. Package trust/source configuration was not modified; vulnerability auditing was disabled for these offline local runs, so these runs make no vulnerability-scan claim.

| UTC directory | TRX / operation | Actual result |
|---|---|---|
| 20261015T002438853Z | Initial command used unsupported `dotnet test --ignore-failed-sources --source ...` switches | MSB1001, no tests executed; retained command error |
| 20261015T002448939Z | red.trx | 1 failing assertion: explicit transport boundary absent |
| 20261015T002728914Z | red-boundary.trx | 31 failing behavioral tests against not-implemented boundary stubs; 1 passed |
| 20261015T002948562Z | green-boundary.trx | 32 passed |
| 20261015T003041589Z | bridge stub compile | CS9113 unused stub constructor argument; no tests executed |
| 20261015T003110743Z | red-quarantine-bridge.trx | 5 failed, 32 passed: missing unknown/malformed quarantine capabilities and unimplemented bridge |
| 20261015T003214855Z | green-quarantine-bridge.trx | 37 passed |
| 20261015T003345191Z | exact-version.trx | 40 passed: additional version isolation, external-ref denial and conflicting route checks |
| 20261015T003413218Z | legacy-transport-regression.trx | 101 passed, affected legacy contract tests |
| 20261015T003447093Z | `git -c core.longpaths=true diff --check` | Exit 0; snapshot before final NativeFacts patch |
| 20261015T003704908Z | red-native-quarantine.trx | 2 failed, 42 passed: missing durable native facts incorrectly permitted DROP; oversized bridge payload incorrectly accepted |
| 20261015T003737829Z | green-native-quarantine.trx | 44 passed after both fixes |

Legacy regression command:

```powershell
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj -c Release -p:NuGetAudit=false -p:RestoreSources=C:/Users/tt/.nuget/packages --filter 'FullyQualifiedName~CloudEventContractTests|FullyQualifiedName~DaprKafkaContractTests|FullyQualifiedName~NativeProfileContractTests|FullyQualifiedName~TraceContextContractTests' --logger 'trx;LogFileName=legacy-transport-regression.trx' --results-directory docs/development/evidence/WP02-12/tests/library/20261015T003413218Z
```

The final NativeFacts and bridge-bound changes are confined to TransportV1; the 101 legacy results are reused with their original scope/time, not represented as newly rerun. Task-level architecture validation and one integrated review are owned by the parent task.

`bundle-inputs/` retains all generated local test-only version bundles and forbidden-reference input, including unsuccessful load inputs. Their versions, schemas and examples are test material, not authoritative published contract bundles. `source-hashes.json` records the library/test inputs after the final 44-test pass.

Limitations: `ITransportAuthorityV1`, `IMessageTransportV1` and `ITransportQuarantineStoreV1` are trusted Host seams. A dishonest implementation cannot become genuine evidence merely by implementing them. The Host must select route/profile/policy/store through immutable trusted composition, authenticate real producer entitlement and native metadata, and independently read committed durable quarantine bytes and native facts. Qualification does not prove Inbox admission, Owner transaction commit, business application, or ACK. The EF bridge relies on the existing OutboxV2 dispatcher and its committed send-intent/budget gates; it creates no new Owner authority. Unknown transport outcomes preserve the original identity without automatic fallback.

## Targeted integrated-review corrections

Native delivery reference is now part of `ReadCommittedAsync` lookup identity, so the same original body/id/hash arriving at two broker offsets can read back its own durable quarantine record. Exact native facts still must match within the selected delivery reference. Authenticated messages with a bounded but incorrect native key now receive only a quarantine capability: their actual wrong key is retained, they remain unqualified, and they can never be published through that capability. Producer/topic authentication failure still provides no capability.

| UTC directory | TRX | Actual result |
|---|---|---|
| 20261015T004811372Z | red-native-reference-readback.trx | 1 failed, 11 passed; second native offset incorrectly returned Retry |
| 20261015T004841988Z | red-native-key-quarantine.trx | 3 failed, 11 passed; two wrong-key cases lacked quarantine capability, plus the offset failure |
| 20261015T004920812Z | green-native-reference-key-quarantine.trx | 27 affected tests passed |

All three use the existing test command with `--no-restore`. First two filters: `FullyQualifiedName~Quarantine|FullyQualifiedName~RepeatedInvalidBody`. Green filter additionally includes `|FullyQualifiedName~IndependentFacts` to verify producer/topic/tenant/region and send-key failure behavior. Source hashes after these corrections are recorded in `source-hashes-review-native-reference.json`; the previous source snapshot remains unchanged. Unaffected prior 44-test and 101-test results retain their real original scope and were not rerun. The parent task owns PostgreSQL fixture adaptation and integrated-review disposition.
