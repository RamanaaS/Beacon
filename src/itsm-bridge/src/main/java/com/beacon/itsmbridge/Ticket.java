package com.beacon.itsmbridge;

import java.time.Instant;

/**
 * The "legacy" ticket record itsm-bridge stands in for. Deliberately its own shape, not a copy of
 * Beacon's Incident — a real legacy ITSM system has its own field names, its own status
 * vocabulary (OPEN/CLOSED, not Beacon's Declared/Acknowledged/Resolved), and its own identity
 * space (ticketId, generated here, distinct from Beacon's incidentId). Translating between the
 * two is exactly the job of a bridge service.
 */
public record Ticket(
        String ticketId,
        String incidentId,
        String title,
        String severity,
        TicketStatus status,
        Instant createdAt,
        Instant updatedAt) {

    public Ticket withStatus(TicketStatus newStatus, Instant now) {
        return new Ticket(ticketId, incidentId, title, severity, newStatus, createdAt, now);
    }
}
