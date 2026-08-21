/*
 * Plain C test + micro-benchmark for log_parser.c, run standalone (not via .NET) as the fast
 * inner-loop check during native development, and as the throughput evidence for why this parsing
 * step is written in C at all rather than "just use C# here too."
 */
#include "log_parser.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include <time.h>

static void test_well_formed_line(void) {
    ParsedLogLine result;
    parse_log_line("Sev1_Critical|prometheus|HighErrorRate|payments-api-3|Error rate exceeded 5% threshold", &result);

    assert(result.success == 1);
    assert(strcmp(result.severity, "Sev1_Critical") == 0);
    assert(strcmp(result.source, "prometheus") == 0);
    assert(strcmp(result.alert_name, "HighErrorRate") == 0);
    assert(strcmp(result.instance, "payments-api-3") == 0);
    assert(strcmp(result.message, "Error rate exceeded 5% threshold") == 0);
    printf("PASS: test_well_formed_line\n");
}

static void test_message_may_contain_pipe_characters(void) {
    ParsedLogLine result;
    parse_log_line("Sev2_High|datadog|LatencySpike|checkout-api-1|p99 was 800ms | threshold is 500ms", &result);

    assert(result.success == 1);
    assert(strcmp(result.message, "p99 was 800ms | threshold is 500ms") == 0);
    printf("PASS: test_message_may_contain_pipe_characters\n");
}

static void test_too_few_fields_fails_cleanly(void) {
    ParsedLogLine result;
    parse_log_line("Sev1_Critical|prometheus|HighErrorRate", &result);

    assert(result.success == 0);
    printf("PASS: test_too_few_fields_fails_cleanly\n");
}

static void test_null_input_fails_cleanly(void) {
    ParsedLogLine result;
    parse_log_line(NULL, &result);

    assert(result.success == 0);
    printf("PASS: test_null_input_fails_cleanly\n");
}

static void test_field_longer_than_buffer_is_truncated_not_overrun(void) {
    char long_line[2000];
    char long_message[1700];
    memset(long_message, 'x', sizeof(long_message) - 1);
    long_message[sizeof(long_message) - 1] = '\0';
    snprintf(long_line, sizeof(long_line), "Sev1_Critical|src|alert|inst|%s", long_message);

    ParsedLogLine result;
    parse_log_line(long_line, &result);

    assert(result.success == 1);
    assert(strlen(result.message) == FIELD_MAX - 1); /* truncated, not a buffer overrun */
    printf("PASS: test_field_longer_than_buffer_is_truncated_not_overrun\n");
}

static void benchmark(void) {
    const char* line = "Sev1_Critical|prometheus|HighErrorRate|payments-api-3|Error rate exceeded 5% threshold in the last 5 minutes";
    const long iterations = 5000000;
    ParsedLogLine result;

    clock_t start = clock();
    for (long i = 0; i < iterations; i++) {
        parse_log_line(line, &result);
    }
    clock_t end = clock();

    double seconds = (double)(end - start) / CLOCKS_PER_SEC;
    double lines_per_sec = (double)iterations / seconds;
    printf("Benchmark: %ld lines in %.3fs (%.0f lines/sec)\n", iterations, seconds, lines_per_sec);
}

int main(void) {
    test_well_formed_line();
    test_message_may_contain_pipe_characters();
    test_too_few_fields_fails_cleanly();
    test_null_input_fails_cleanly();
    test_field_longer_than_buffer_is_truncated_not_overrun();
    benchmark();
    return 0;
}
