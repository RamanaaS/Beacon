#ifndef BEACON_LOG_PARSER_H
#define BEACON_LOG_PARSER_H

#ifdef _WIN32
#define LOGPARSER_API __declspec(dllexport)
#else
#define LOGPARSER_API
#endif

#define FIELD_MAX 256

/*
 * Fixed-size, ByValTStr-marshallable fields (rather than returned char* pointers) deliberately —
 * the caller owns this struct's memory entirely, so there is no cross-boundary free() to get
 * wrong. A pointer-returning API would be marginally faster (no copy) but hands the .NET side a
 * lifetime problem with no clean answer: who calls free(), and when, across a P/Invoke boundary
 * with two different allocators (the C runtime's and .NET's). Not worth it for log lines this size.
 */
typedef struct {
    char severity[FIELD_MAX];
    char source[FIELD_MAX];
    char alert_name[FIELD_MAX];
    char instance[FIELD_MAX];
    char message[FIELD_MAX];
    int success; /* 0 = malformed line (wrong field count), 1 = parsed */
} ParsedLogLine;

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Parses one pipe-delimited raw alert line:
 *   {severity}|{source}|{alert_name}|{instance}|{message}
 * into `out`. Single pass, no allocation, no regex — the entire point of doing this in C instead
 * of C# is avoiding .NET's Regex/string-split allocation overhead on a line-rate ingestion path
 * (the same reason tools like Fluent Bit and Vector keep their hot parsing path native).
 */
LOGPARSER_API void parse_log_line(const char* line, ParsedLogLine* out);

#ifdef __cplusplus
}
#endif

#endif
