package com.beacon.itsmbridge;

import jakarta.validation.constraints.NotNull;

public record UpdateTicketStatusRequest(@NotNull(message = "status is required") TicketStatus status) {
}
