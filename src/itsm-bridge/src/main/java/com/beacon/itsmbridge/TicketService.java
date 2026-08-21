package com.beacon.itsmbridge;

import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

import org.springframework.stereotype.Service;

/**
 * In-memory store standing in for the legacy system's real database — itsm-bridge exists to
 * demonstrate the service-boundary/protocol-translation pattern (Beacon.Infrastructure's
 * ItsmBridgeClient, on the .NET side), not to actually persist tickets across restarts. A real
 * deployment of a bridge like this would own a real datastore or, more likely, just be a thin
 * client against the real legacy system's own API.
 */
@Service
public class TicketService {

    private final Map<String, Ticket> ticketsById = new ConcurrentHashMap<>();
    // incidentId -> ticketId, so createForIncident can be idempotent: Beacon's own Polly retry
    // policy (Table B) means the same "declare a ticket for incident X" call can genuinely arrive
    // twice, and a legacy ticketing system silently creating a duplicate ticket per retry is
    // exactly the kind of bug that erodes trust in an integration like this.
    private final Map<String, String> ticketIdByIncidentId = new ConcurrentHashMap<>();

    public Ticket createForIncident(String incidentId, String title, String severity) {
        var existingTicketId = ticketIdByIncidentId.get(incidentId);
        if (existingTicketId != null) {
            return ticketsById.get(existingTicketId);
        }

        var now = Instant.now();
        var ticket = new Ticket(
                "TCK-" + UUID.randomUUID().toString().substring(0, 8).toUpperCase(),
                incidentId,
                title,
                severity,
                TicketStatus.OPEN,
                now,
                now);

        ticketsById.put(ticket.ticketId(), ticket);
        ticketIdByIncidentId.put(incidentId, ticket.ticketId());
        return ticket;
    }

    public Ticket updateStatus(String ticketId, TicketStatus status) {
        var current = ticketsById.get(ticketId);
        if (current == null) {
            throw new TicketNotFoundException(ticketId);
        }

        var updated = current.withStatus(status, Instant.now());
        ticketsById.put(ticketId, updated);
        return updated;
    }

    /**
     * Beacon never learns itsm-bridge's own ticketId after creation (it doesn't need to — the
     * whole point of a bridge is that the calling system keeps using its own identifiers), so the
     * resolve-sync path has to look a ticket up by the identifier Beacon actually has: its own
     * incidentId. A real legacy ITSM system's API would very likely offer the same kind of
     * lookup-by-external-reference for exactly this reason.
     */
    public Ticket updateStatusByIncidentId(String incidentId, TicketStatus status) {
        var ticketId = ticketIdByIncidentId.get(incidentId);
        if (ticketId == null) {
            throw new TicketNotFoundException("for incidentId " + incidentId);
        }
        return updateStatus(ticketId, status);
    }

    public Optional<Ticket> findById(String ticketId) {
        return Optional.ofNullable(ticketsById.get(ticketId));
    }

    public List<Ticket> findAll() {
        return ticketsById.values().stream()
                .sorted((a, b) -> b.createdAt().compareTo(a.createdAt()))
                .toList();
    }
}
