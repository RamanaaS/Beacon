package com.beacon.itsmbridge;

import jakarta.validation.constraints.NotBlank;

public record CreateTicketRequest(
        @NotBlank(message = "incidentId is required") String incidentId,
        @NotBlank(message = "title is required") String title,
        @NotBlank(message = "severity is required") String severity) {
}
