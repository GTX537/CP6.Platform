"""Bind original AC subassertions to actual final TRX cases, never to business acceptance."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

# Selectors name concrete test methods. Their scope remains the partial assertion in ac-scope.json.
SELECTORS = {
    "HTTP01-A1": ["ProblemWriter_ProducesSafeRfc9457Profile", "RegisteredTextOnly_TraceAndCorrelationAreSafe_AndInputNeverEchoes"],
    "HTTP01-A2": ["HiddenFieldIncludingEmptyMessagesFallsBackWithoutEcho", "ArbitraryExceptionOrUserTextCannotBecomeRegisteredMessage", "CountCharacterAndSerializedUtf8LimitsProduceWholeGenericJson", "ForeignOrUnregisteredPolicyCannotBeBorrowedByEndpoint"],
    "HTTP01-A3": ["RegisteredTextOnly_TraceAndCorrelationAreSafe_AndInputNeverEchoes", "MultipleCorrelationValues_AreReplaced"],
    "HTTP01-A4": ["StartedAndAbortedResponsesNeverAppendSecondBody"],
    "HTTP01-A5": ["ProblemWriter_ProducesSafeRfc9457Profile", "AuthenticationChallenge_UsesTheSameSafeProblemProfile"],
    "HTTP01-A6": ["UnknownQueriesOriginalBeforeNewWritePermission_AndReturnsSameResult"],
    "HTTP01-A7": ["ConflictingIdentityAndActualTransportInputsAreRejectedBeforeSend", "VerifiedHistoricalQuerySurvivesDisabledOrZeroRetries"],
    "HTTP01-A8": ["UnknownQueriesOriginalBeforeNewWritePermission_AndReturnsSameResult", "ResponseBodyIoFailureQueriesOriginalAndKeepsUnknownWhenUnavailable"],
    "HTTP02-A1": ["GatewayProfile_RejectsInsecureProductionDestination", "GatewayProfile_RejectsUnknownCluster", "GatewayProfile_RejectsUnsafePathOrLimit"],
    "HTTP02-A2": ["Proxy_StripsForgedIdentityMetadata_AndBuildsForwardedMetadata", "HeaderAndSelfDescribedDtoWithoutAuthorityCannotConferProof"],
    "HTTP02-A3": [],
    "HTTP02-A4": ["CorrelationMiddleware_PropagatesOnlySafeValues", "MultipleCorrelationValues_AreReplaced"],
    "HTTP02-A5": ["RouteRateLimit_ReturnsSafeProblemDetailsBeforeDestinationCall"],
    "HTTP02-A6": [],
    "HTTP02-A7": ["ConflictingIdentityAndActualTransportInputsAreRejectedBeforeSend", "RetriesKeepExactBodyKeyTenantVersionDigestAndCredentialSnapshot"],
    "HTTP02-A8": ["RetriesKeepExactBodyKeyTenantVersionDigestAndCredentialSnapshot", "ArbitraryExceptionOrUserTextCannotBecomeRegisteredMessage", "OptInClientDoesNotFollowRedirectOrAcquireCookieCredentials"],
    "HTTP03-A1": ["DefaultsPreserveBounds_AndRequireExplicitEnablement", "Profile_RejectsValuesOutsideApprovedBounds", "InvalidRangesFail"],
    "HTTP03-A2": ["ChildNeverResetsBudget_AndUtcRollbackDoesNotExtendIt", "FullResponseBodyIsInsideAttemptBudget_EvenHeadersReadMode", "ParseAndQueueTimeCounts_EqualityAllowsFullAttempt_AndUtcChangesDoNotMoveEarliest", "RealResponseHeadersDoNotEscapeTotalBodyDeadline"],
    "HTTP03-A3": ["ExplicitDeadlineSource_DistinguishesEqualDurations", "UnrelatedOperationCancellationIsUnknownSource_NotAttemptTimeout", "HttpRequestExceptionRetries_UnknownPollyTimeoutDoesNot"],
    "HTTP03-A4": ["ParentBudgetWaitHonorsThisCallersCancellation", "CancelledWaitNeverAuthorizesAnotherAttempt", "CallerCancellation_PropagatesWithoutRetryOrTranslation", "RealCallerCancellationDuringServerWaitHasNoLaterSend", "CompletedAuthorityCallbacksCannotOverrideCallerCancellation", "InheritedCallerCancellationStaysCallerCancellationAcrossSendWaitAndQuery", "InheritedTimerExpiryRemainsTotalTimeoutRatherThanCallerCancellation"],
    "HTTP03-A5": ["UnknownQueriesOriginalBeforeNewWritePermission_AndReturnsSameResult", "HttpsFixtureOnly_OwnerQueryPrecedesReplay_AndWireBytesAndKeyStayIdentical"],
    "HTTP03-A6": ["UnknownStaysUnknownWhenRetryHitsOpenCircuit", "CircuitCountsActualTransientAttempts_AndRejectsNextNewCallAsNotSent"],
    "HTTP03-A7": ["ChildNeverResetsBudget_AndUtcRollbackDoesNotExtendIt", "ParseAndQueueTimeCounts_EqualityAllowsFullAttempt_AndUtcChangesDoNotMoveEarliest"],
    "HTTP03-A8": ["VerifiedHistoricalQuerySurvivesDisabledOrZeroRetries", "UnavailableOrNotFoundQueryPreservesUnknownWithoutAnotherSend", "ProvenNotAppliedStillNeedsNewEffectPermission", "QueryUsesRemainingParentBudget_AndCannotCreateAnotherSend", "OriginalResultBodyCannotEscapeQueryDeadline", "CompletedQueryBeyondTotalCannotReturnAppliedOrDisabled503", "NoncooperativeCompletedQueryBodyCannotEscapeChildOrTotal", "OriginalQueryBodyIoFailureRemainsUnknownAndDisposesBody"],
    "HTTP04-A1": ["DisabledReadIsOneAttempt", "NoProofWriteAndNonIdempotentAreNeverRetried", "MethodClassMismatchStopsBeforeNetwork", "ConflictingIdentityAndActualTransportInputsAreRejectedBeforeSend"],
    "HTTP04-A2": ["NoProofWriteAndNonIdempotentAreNeverRetried", "OneTimeCredentialExchangeCannotEnableReplay", "RealReadRetriesAreBounded_AndNoProofWriteHasZeroRetries"],
    "HTTP04-A3": ["ApprovedTransientStatuses_AreRetried", "UnapprovedStatuses_AreNotRetried", "ApprovedStatusSetHasBoundedAttempts_AndNoBusinessRejectionsRetry", "HttpRequestExceptionRetries_UnknownPollyTimeoutDoesNot"],
    "HTTP04-A4": ["FrozenExamples_UseMaxServerLocalAndReserveFullAttempt", "EarlyWake_KeepsSameSample_AndLateWakeRechecksBudget", "JitterOnlyChangesLocalWait", "SingleHttpValue_UsesInvariantThreeDateFormsAndOws", "InvalidValues_AreNotServerWaits", "DuplicateFields_AreInvalid", "HugeLegalInteger_StopsInsteadOfFallingBack", "CancelledWaitNeverAuthorizesAnotherAttempt", "RealServerWaitAboveCapStopsImmediately"],
    "HTTP04-A5": ["RetriesKeepExactBodyKeyTenantVersionDigestAndCredentialSnapshot", "ConflictingIdentityAndActualTransportInputsAreRejectedBeforeSend", "SnapshotCopiesCallerBytesAndFactoryIdentityIsPartOfRegistration", "HttpsFixtureOnly_OwnerQueryPrecedesReplay_AndWireBytesAndKeyStayIdentical"],
    "HTTP04-A6": ["StreamOrOversizeCannotBecomeReplayableByBuffering"],
    "HTTP04-A7": ["UnknownQueriesOriginalBeforeNewWritePermission_AndReturnsSameResult", "UnavailableOrNotFoundQueryPreservesUnknownWithoutAnotherSend"],
    "HTTP04-A8": ["UnknownStaysUnknownWhenRetryHitsOpenCircuit", "CircuitCountsActualTransientAttempts_AndRejectsNextNewCallAsNotSent", "Circuit_Opens_HalfOpens_AndRecoversWithManualTime"],
    "HTTP04-A9": ["SameRegistrationIsIdempotent_DifferentProfileOrClientFails", "LegacyAndV2CannotStackInEitherOrder", "OtherRetryPipelineAndCompetingClientTimeoutFail", "ApprovedStatusSetHasBoundedAttempts_AndNoBusinessRejectionsRetry"],
    "HTTP04-A10": ["ProvenNotAppliedStillNeedsNewEffectPermission", "VerifiedHistoricalQuerySurvivesDisabledOrZeroRetries"],
    "HTTP05-A1": [],
    "HTTP05-A2": ["OneTimeCredentialExchangeCannotEnableReplay", "NoProofWriteAndNonIdempotentAreNeverRetried"],
    "HTTP05-A3": ["AuthenticationChallenge_UsesTheSameSafeProblemProfile", "UserAndServiceAudiences_AcceptTheirOwnTokens_AndMutuallyReject", "ForeignOrUnregisteredPolicyCannotBeBorrowedByEndpoint"],
    "HTTP05-A4": ["ProblemWriter_ProducesSafeRfc9457Profile", "RegisteredTextOnly_TraceAndCorrelationAreSafe_AndInputNeverEchoes"],
    "HTTP05-A5": ["VerifiedHistoricalQuerySurvivesDisabledOrZeroRetries", "ProvenNotAppliedStillNeedsNewEffectPermission"],
    "HTTP05-A6": ["EnabledWriteWithoutTrustedRegistrationFails", "ForeignOrUnregisteredPolicyCannotBeBorrowedByEndpoint"],
    "HTTP05-A7": ["ConflictingIdentityAndActualTransportInputsAreRejectedBeforeSend"],
    "HTTP05-A8": [],
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trx", nargs="+", type=Path, help="Final successful runs only, relative to repository root")
    args = parser.parse_args()
    results = []
    for path in args.trx:
        tree = ET.parse(ROOT / path)
        counters = tree.find(".//t:Counters", NS).attrib
        if int(counters["total"]) == 0 or counters["passed"] != counters["total"]:
            raise ValueError(f"Final run is empty or not entirely passed: {path}")
        for result in tree.findall(".//t:UnitTestResult", NS):
            results.append({"run": path.as_posix(), "test_name": result.attrib["testName"], "outcome": result.attrib["outcome"]})
    scope = json.loads((HERE / "ac-scope.json").read_text(encoding="utf-8-sig"))
    lines = ["# WP02-11 原始 42 AC 的本次证据范围", "",
             "原始 AC 内容、身份与 NOT_RUN 状态不变。`PARTIAL_PASS` 仅指下表库子断言在指定本地 TRX 中有通过证据，绝不表示完整业务 AC 通过。无运行子断言的条目标为 `NOT_RUN`；所有真实宿主、Owner、UI、数据库与生产采用继续待验收。准确用例名称和运行路径见 ac-scope.json。", "",
             "| 原 AC | 库子断言状态 | 本任务范围 | 仍需真实宿主 / Owner 负责 |", "|---|---|---|---|" ]
    for row in scope["cases"]:
        case_id = row["original"]["source_case_id"]
        evidence = []
        for selector in SELECTORS[case_id]:
            selected = [r for r in results if f".{selector}(" in r["test_name"] or r["test_name"].endswith("." + selector)]
            if not selected:
                raise ValueError(f"No executed final test matches {case_id}: {selector}")
            evidence.extend(r for r in selected if r not in evidence)
        row["library_evidence"] = evidence
        row["library_verification"] = "PARTIAL_PASS" if evidence else "NOT_RUN"
        row["business_acceptance"] = "NOT_RUN"
        lines.append(f"| {case_id} | {row['library_verification']} | {row['library_subassertion']} | {row['host_owner_responsibility']} |")
    (HERE / "ac-scope.json").write_text(json.dumps(scope, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    (HERE / "AC-SCOPE.md").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print("Bound 42 original AC entries to actual final evidence; business acceptance remains NOT_RUN.")


if __name__ == "__main__":
    main()
