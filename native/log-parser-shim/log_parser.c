#include "log_parser.h"
#include <string.h>

static void copy_field(char* dest, const char* start, size_t len) {
    if (len >= FIELD_MAX) {
        len = FIELD_MAX - 1;
    }
    memcpy(dest, start, len);
    dest[len] = '\0';
}

void parse_log_line(const char* line, ParsedLogLine* out) {
    memset(out, 0, sizeof(ParsedLogLine));

    if (line == NULL) {
        return;
    }

    const char* field_starts[5];
    size_t field_lens[5];
    int field_count = 0;

    const char* cursor = line;
    const char* field_start = line;

    while (*cursor != '\0' && field_count < 4) {
        if (*cursor == '|') {
            field_starts[field_count] = field_start;
            field_lens[field_count] = (size_t)(cursor - field_start);
            field_count++;
            field_start = cursor + 1;
        }
        cursor++;
    }

    /* Only the first 4 fields are delimiter-terminated above; the 5th (message) runs to the end
       of the line and is allowed to contain '|' itself without being split further. Note this
       can't reuse `cursor` for the length: the loop above exits the instant field_count hits 4,
       which happens right as cursor lands on the first character of the message field itself
       (cursor == field_start at that point) - using (cursor - field_start) here would always
       compute zero. strlen is correct regardless of why/where the loop stopped. */
    if (field_count != 4) {
        out->success = 0;
        return;
    }

    field_starts[4] = field_start;
    field_lens[4] = strlen(field_start);

    copy_field(out->severity, field_starts[0], field_lens[0]);
    copy_field(out->source, field_starts[1], field_lens[1]);
    copy_field(out->alert_name, field_starts[2], field_lens[2]);
    copy_field(out->instance, field_starts[3], field_lens[3]);
    copy_field(out->message, field_starts[4], field_lens[4]);
    out->success = 1;
}
