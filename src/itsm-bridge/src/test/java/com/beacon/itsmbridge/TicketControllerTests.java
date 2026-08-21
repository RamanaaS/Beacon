package com.beacon.itsmbridge;

import static org.hamcrest.Matchers.startsWith;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import tools.jackson.databind.ObjectMapper;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

@SpringBootTest
@AutoConfigureMockMvc
class TicketControllerTests {

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private ObjectMapper objectMapper;

    @Test
    void creating_a_ticket_returns_201_with_an_open_ticket() throws Exception {
        var request = new CreateTicketRequest(UUID.randomUUID().toString(), "Payments API returning 500s", "Sev1_Critical");

        mockMvc.perform(post("/tickets")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(objectMapper.writeValueAsString(request)))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.status").value("OPEN"))
                .andExpect(jsonPath("$.incidentId").value(request.incidentId()))
                .andExpect(jsonPath("$.ticketId", startsWith("TCK-")));
    }

    @Test
    void creating_a_ticket_twice_for_the_same_incident_is_idempotent() throws Exception {
        var incidentId = UUID.randomUUID().toString();
        var request = new CreateTicketRequest(incidentId, "Duplicate-create check", "Sev2_High");
        var body = objectMapper.writeValueAsString(request);

        var firstResult = mockMvc.perform(post("/tickets").contentType(MediaType.APPLICATION_JSON).content(body))
                .andExpect(status().isCreated())
                .andReturn();
        var firstTicket = objectMapper.readValue(firstResult.getResponse().getContentAsString(), Ticket.class);

        mockMvc.perform(post("/tickets").contentType(MediaType.APPLICATION_JSON).content(body))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.ticketId").value(firstTicket.ticketId()));
    }

    @Test
    void updating_status_to_closed_is_reflected_on_read() throws Exception {
        var createRequest = new CreateTicketRequest(UUID.randomUUID().toString(), "Resolved incident sync", "Sev3_Medium");
        var createResult = mockMvc.perform(post("/tickets")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(objectMapper.writeValueAsString(createRequest)))
                .andReturn();
        var created = objectMapper.readValue(createResult.getResponse().getContentAsString(), Ticket.class);

        mockMvc.perform(put("/tickets/" + created.ticketId() + "/status")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(objectMapper.writeValueAsString(new UpdateTicketStatusRequest(TicketStatus.CLOSED))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("CLOSED"));

        mockMvc.perform(get("/tickets/" + created.ticketId()))
                .andExpect(jsonPath("$.status").value("CLOSED"));
    }

    @Test
    void updating_an_unknown_ticket_returns_404() throws Exception {
        mockMvc.perform(put("/tickets/TCK-DOESNOTEXIST/status")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(objectMapper.writeValueAsString(new UpdateTicketStatusRequest(TicketStatus.CLOSED))))
                .andExpect(status().isNotFound());
    }

    @Test
    void updating_status_by_incident_id_finds_the_right_ticket_without_the_caller_knowing_the_ticket_id() throws Exception {
        var incidentId = UUID.randomUUID().toString();
        mockMvc.perform(post("/tickets")
                .contentType(MediaType.APPLICATION_JSON)
                .content(objectMapper.writeValueAsString(
                        new CreateTicketRequest(incidentId, "Lookup-by-incident check", "Sev1_Critical"))));

        mockMvc.perform(put("/tickets/by-incident/" + incidentId + "/status")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(objectMapper.writeValueAsString(new UpdateTicketStatusRequest(TicketStatus.CLOSED))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("CLOSED"))
                .andExpect(jsonPath("$.incidentId").value(incidentId));
    }

    @Test
    void updating_status_for_an_unknown_incident_id_returns_404() throws Exception {
        mockMvc.perform(put("/tickets/by-incident/no-such-incident/status")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(objectMapper.writeValueAsString(new UpdateTicketStatusRequest(TicketStatus.CLOSED))))
                .andExpect(status().isNotFound());
    }

    @Test
    void creating_a_ticket_with_a_blank_title_returns_400_with_field_errors() throws Exception {
        var request = new CreateTicketRequest(UUID.randomUUID().toString(), "", "Sev1_Critical");

        mockMvc.perform(post("/tickets")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(objectMapper.writeValueAsString(request)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.errors.title").exists());
    }
}
