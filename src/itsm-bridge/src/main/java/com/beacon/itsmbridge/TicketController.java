package com.beacon.itsmbridge;

import jakarta.validation.Valid;
import java.util.List;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/tickets")
public class TicketController {

    private final TicketService ticketService;

    public TicketController(TicketService ticketService) {
        this.ticketService = ticketService;
    }

    @PostMapping
    public ResponseEntity<Ticket> create(@Valid @RequestBody CreateTicketRequest request) {
        var ticket = ticketService.createForIncident(request.incidentId(), request.title(), request.severity());
        return ResponseEntity.status(HttpStatus.CREATED).body(ticket);
    }

    @GetMapping("/{ticketId}")
    public ResponseEntity<Ticket> getById(@PathVariable String ticketId) {
        return ticketService.findById(ticketId)
                .map(ResponseEntity::ok)
                .orElseThrow(() -> new TicketNotFoundException(ticketId));
    }

    @GetMapping
    public List<Ticket> getAll() {
        return ticketService.findAll();
    }

    @PutMapping("/{ticketId}/status")
    public ResponseEntity<Ticket> updateStatus(
            @PathVariable String ticketId, @Valid @RequestBody UpdateTicketStatusRequest request) {
        var updated = ticketService.updateStatus(ticketId, request.status());
        return ResponseEntity.ok(updated);
    }

    @PutMapping("/by-incident/{incidentId}/status")
    public ResponseEntity<Ticket> updateStatusByIncidentId(
            @PathVariable String incidentId, @Valid @RequestBody UpdateTicketStatusRequest request) {
        var updated = ticketService.updateStatusByIncidentId(incidentId, request.status());
        return ResponseEntity.ok(updated);
    }
}
