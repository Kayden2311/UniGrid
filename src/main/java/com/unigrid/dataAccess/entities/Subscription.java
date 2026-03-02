package com.unigrid.dataAccess.entities;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "Subscription", schema = "dbo")
public class Subscription {
    @Id
    @Column(name = "subscriptionId", nullable = false)
    private UUID id;

    @Column(name = "workspaceId", nullable = false)
    private UUID workspaceId;

    @Column(name = "planId", nullable = false)
    private UUID planId;

    @Column(name = "startDate")
    private Instant startDate;

    @Column(name = "endDate")
    private Instant endDate;

    @Nationalized
    @Column(name = "status", length = 30)
    private String status;

    @Column(name = "renew")
    private Boolean renew;


}