"""Map reviewed local case portions to exact final TRX test names, never original AC acceptance."""
import argparse
import xml.etree.ElementTree as ET
import evidence

parser = argparse.ArgumentParser(__doc__)
parser.add_argument('--gate-label', required=True)
parser.add_argument('--label', required=True)
args = parser.parse_args()
records = []
for suite in ('unit', 'host'):
    path = evidence.RUN / args.gate_label / suite / f'wp02-25-{suite}.trx'
    for result in ET.parse(path).findall('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult'):
        records.append(dict(trx=path.relative_to(evidence.ROOT).as_posix(), test_name=result.attrib['testName'], outcome=result.attrib['outcome']))
bindings = {}


def bind(case, scope, remaining, *methods, regression=False):
    proofs = []
    for method in methods:
        matches = [r for r in records if f'.{method}' in r['test_name']]
        assert matches and all(r['outcome'] == 'Passed' for r in matches), method
        proofs.extend({k: r[k] for k in ('trx', 'test_name')} for r in matches)
    bindings[f'PLT-OBS-01::{case}'] = dict(result='REGRESSION_PASS' if regression else 'LOCAL_PASS',
        scope=scope, remaining=remaining, evidence=proofs)


bind('OBS01-A1', 'Legacy names plus new closed finite value registry.', 'Real consumer registration is external.',
     'SourcesAndMeters_UseTheExactApprovedNames', 'Operations_UseTheExactApprovedNames', 'PolicyCopiesFiniteRegistriesAndRejectsArbitraryValuesUnderAllowedKeys')
bind('OBS01-A2', 'Actual SDK finite-series/numeric measurement export, no high-cardinality dimensions.', 'Existing EF emitters are unchanged; real Owner traffic is not measured.',
     'MetricsAreSanitizedBeforeSdkAggregationAndSeriesAndRecordBudgetsAreEnforced')
bind('OBS01-A3', 'Versioned technical measurement definitions preserve pre-commit/transport semantics.', 'Actual Owner commit facts and business counts require original receipts.',
     'ContractPreservesTechnicalMetricSemanticsAndOwnerLatencyHasSeparateBusinessMeaning', 'ExistingTechnicalMetricsHaveVersionedDefinitions')
bind('OBS01-A4', 'Trace-only propagation, invalid trace handling and actual loopback SDK output.', 'Production consumer propagation adoption is external.',
     'Inject_EmitsOnlyW3cTraceFieldsWhenInnerPropagatorEmitsLegacyBaggage', 'InvalidAndDuplicateTraceHeaders_CreateFreshTraceRoots', 'ActualSdkFinalTraceMetricAndLogExportsContainOnlyClosedSafeFields')
bind('OBS01-A5', 'Automatic inbound/outbound final SDK capture plus event/link/baggage/log-state privacy negatives.', 'Guarantee is limited to this configured pipeline, not other providers.',
     'ActualSdkFinalTraceMetricAndLogExportsContainOnlyClosedSafeFields', 'EventLinkBaggageStatusDisplayNameAndCaseVariantSeedsNeverEnterFinalSink', 'LoggerNeverInvokesCallerFormatterStateScopeOrExceptionFormatting', 'NullAndOversizedMetricDimensionsCannotThrowFromActualCounterOrChangeRequestResult')
bind('OBS01-A6', 'Blocked/throwing sink and queue overflow leave local HTTP/simulated Owner result intact; incomplete observation is explicit.', 'No actual Owner transaction or production exporter was exercised.',
     'BlockingAndThrowingSinkCannotDelayOwnerResultAndQueueDropsAreExplicit', 'LocalQueueBoundsBatchLifetimeRecordCountRetentionSizeAndPostDisposeWrites', 'ActualLoggerCallsRacingRuntimeDisposalNeverThrowBackToCaller')
bind('OBS01-A7', 'Multiple loopback hosts, registration drift, process-global replacement and lifecycle cleanup.', 'Production registration ordering and third-party providers remain host responsibilities.',
     'MultipleHostsUseSeparateResourcesAndRestoreOnlyOwnedGlobalPropagators', 'RepeatedLifecycleRestoresGlobalsAndOtelReplacementDisablesExportWithoutClobberingReplacement', 'DuplicateAndDriftedRegistrationAreRejected')
bind('OBS01-A8', 'Reset/aggregation definitions and separate synthetic receipt/clock rules; no real population denominator.', 'Real receipt authenticity, deduplication, sampling and fleet population remain external.',
     'ContractPreservesTechnicalMetricSemanticsAndOwnerLatencyHasSeparateBusinessMeaning', 'OwnerLatencyRequiresDistinctCommittedReceiptsAndClockProvenance')
bind('OBS02-A1', 'Unchanged legacy parser/wire regression only.', 'No new SLO qualification or production evidence acceptance.',
     'Parse_RejectsDuplicateJsonPropertiesWithoutEchoingValues', 'ReleaseIdentity_SerializesOnlyTheApprovedWireShape', 'ValidExamples_ParseAndMatchEvaluator', regression=True)
bind('OBS02-A2', 'Unchanged legacy evaluator comparison and declared-result regression only.', 'Source verification and calculation qualification belong to WP02-26/Owner.',
     'Evaluate_UsesFailClosedMatrix', 'Parse_RejectsClaimedPassWhenSourceDigestDrifts', regression=True)
bind('OBS02-A5', 'Legacy zero-sample/coverage-gap fail-closed regression, not real coverage measurement.', 'Real population and source coverage qualification remain external.',
     'Evaluate_ReturnsIndeterminateForZeroSamplesOrCoverageGap', regression=True)
bind('OBS02-A6', 'Local observation cannot promote candidate flags or package-looking references to production acceptance.', 'No actual trusted package publication or production SLO qualification performed.',
     'ObservationNeverPromotesCandidateOrClaimsProduction', 'CandidateFlagsAndValidLookingPackageLockCannotPromoteLocalEnvelope')
bind('OBS03-A1', 'Legacy identity format regressions and versioned binding validation.', 'Format validity remains distinct from runtime/source/package trust.',
     'Candidate_RequiresImmutableIdentity', 'Candidate_RejectsMutableOrNonCanonicalIdentity', 'NonCandidate_AllowsExplicitLocalIdentityWithoutDigests', 'BindingRejectsNonCanonicalAndNonUtcValues')
bind('OBS03-A2', 'Owned loaded-byte observation and fail-closed missing/self-report boundaries.', 'Only adapter-owned managed modules are observed; no whole-process/package trust.',
     'OwnedBytesMatchOnlyExactPinnedInventoryAndFreshProcessBinding', 'MalformedDuplicateEmptyAndExceptionPathsAreSafeAndCannotBecomeSelfReportMatch')
bind('OBS03-A3', 'Independent expected-binding negatives plus legacy actual request-spoof regression.', 'Host config is declared; actual Main/CRM trusted registration is external.',
     'WrongExpectedBindingCannotMatchOwnedRunningModules', 'SpoofedIdentityAndBaggageNeverBecomeTelemetryData')
bind('OBS03-A4', 'Typed unsupported artifact domains stay unproven and exact module inventory is compared.', 'Container/package/config provenance is not inferred from module bytes.',
     'FileSourceMetadataMvidOrOpaquePackageReferencesNeverProveOtherArtifactDomains', 'LoadedModuleNameVersionDigestAndCompleteInventoryAreComparedIndependently')
bind('OBS03-A5', 'Legacy release endpoint compatibility and safe new identity/health outputs.', 'HTTP success is not production qualification.',
     'ReleaseEndpoint_RejectsInvalidIdentityWithoutEchoingIt', 'ReleaseEndpoint_ReturnsCompleteCandidateIdentity', 'IdentityFreshnessGatesRelevantEffectsWhileAuthorizedReadRemainsAvailable')
bind('OBS03-A6', 'Real local route/module isolation and freshness with conservative aggregate.', 'Actual fleet rollout/cutover remains WP02-28/29 and deployment Owner.',
     'ModuleBMismatchOnlyBlocksBUnlessCommonHostBindingAlsoMismatches', 'IdentityFreshnessGatesRelevantEffectsWhileAuthorizedReadRemainsAvailable')
bind('OBS03-A7', 'Defensive immutable binding/owned-byte snapshots only.', 'No real immutable deployment history or rollback executed.',
     'OwnedBytesMatchOnlyExactPinnedInventoryAndFreshProcessBinding')
bind('OBS04-A1', 'Legacy idempotent mapping plus new early/late route collisions and inventory validation.', 'Actual consumer route-table adoption is external.',
     'MappingEqualProfileTwice_IsIdempotent', 'MappingDifferentProfile_FailsBeforeStartup', 'LaterRouteCollisionFailsAtHostStart', 'MissingEmptyDuplicateUnknownAndWrongPurposeRouteCheckInventoriesFailClosed')
bind('OBS04-A2', 'Live remains constant while actual owned loopback dependency stops/restarts; required registration fails closed.', 'No PostgreSQL or actual application dependency was exercised.',
     'RealKestrelDependencyStopRestartAffectsReadinessButNotLiveAndNeverGrantsNewEffects', 'RegistrationFailsClosed')
bind('OBS04-A3', 'Library path-conflict rejection only; no CRM edits.', 'Actual CRM ready compatibility/adoption belongs to WP02-29.',
     'LaterRouteCollisionFailsAtHostStart')
bind('OBS04-A4', 'Safe HTTP output hides component details without removing aggregate failure.', 'Production operational visibility policies are external.',
     'StaleCacheRefreshesAndHiddenFailureStillChangesAggregate', 'UnpublishedUnsafeCheck_AffectsAggregateButIsOmitted')
bind('OBS04-A5', 'Explicit policy binding exercised in isolated authenticated host.', 'No production probe identity/network/deployment authorization qualification.',
     'IdentityFreshnessGatesRelevantEffectsWhileAuthorizedReadRemainsAvailable')
bind('OBS04-A6', 'Bounded probes, cancellation, exception and stale-cache failures.', 'Production probe rates and actual dependency impact are external.',
     'BlockedProbeIsBoundedAndNeverHealthyIncludingCancellation', 'ExceptionsAndCancelledCheckResultsAreSafeAndNeverHealthy', 'StaleCacheRefreshesAndHiddenFailureStillChangesAggregate', 'HealthCacheExpiresByMonotonicTimeEvenWhenUtcMovesBackwards')
bind('OBS04-A7', 'Per-route check/module availability preserves independent reads and effects while aggregate fails.', 'These observations grant no business write permission or fleet routing action.',
     'WriteOnlyDependencyFailurePreservesReadRouteAndHiddenFailureKeepsOverall503', 'ModuleBMismatchOnlyBlocksBUnlessCommonHostBindingAlsoMismatches')
bind('OBS04-A8', 'Local health GET uses read-only loopback probes, keeps live independent and cannot promote acceptance.', 'Consumer IHealthCheck side effects and real Owner use require separate review/adoption.',
     'LiveEndpoint_DoesNotRunRegisteredChecks', 'RealKestrelDependencyStopRestartAffectsReadinessButNotLiveAndNeverGrantsNewEffects', 'ObservationNeverPromotesCandidateOrClaimsProduction')
evidence.save(evidence.RUN / f'bindings-{args.label}.json', bindings)
print(f'Bound {len(bindings)} scoped local portions; remaining cases stay NOT_RUN')
