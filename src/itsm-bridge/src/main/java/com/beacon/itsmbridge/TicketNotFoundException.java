package com.beacon.itsmbridge;

public class TicketNotFoundException extends RuntimeException {
    public TicketNotFoundException(String ticketId) {
        super("No ticket with id " + ticketId);
    }
}
