package com.unigrid.dataAccess.entities;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "WorkSchedule", schema = "dbo")
public class WorkSchedule {
    @Id
    @Column(name = "workspaceId", nullable = false)
    private UUID id;

    @MapsId
    @OneToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "workspaceId", nullable = false)
    private Workspace workspace;

    @Column(name = "startTime")
    private Instant startTime;

    @Column(name = "endTime")
    private Instant endTime;


}